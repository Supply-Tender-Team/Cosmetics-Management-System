/* Full-screen assistant page (Views/Assistant/Index.cshtml).
   The server keeps every chat (private to the user); this file only shows them. A question is sent to /Assistant/Send and the answer
   comes back as lines of JSON ("application/x-ndjson"), so the text can be shown while the model is still writing.
   The Stop button simply closes that request: the server saves the part written so far. */
(function () {
  'use strict';

  var root = document.getElementById('asst');
  if (!root) return;

  var $ = function (id) { return document.getElementById(id); };
  var el = {
    list: $('asstList'), thread: $('asstThread'), scroll: $('asstScroll'), welcome: $('asstWelcome'), title: $('asstTitle'),
    form: $('asstForm'), input: $('asstInput'), send: $('asstSend'), stop: $('asstStop'),
    newChat: $('asstNew'), deleteAll: $('asstDeleteAll'), toggle: $('asstToggle'), collapse: $('asstCollapse'), backdrop: $('asstBackdrop'),
    web: $('asstWeb'), webNote: $('asstWebNote'),           // the Web switch exists only when web mode is available
    mic: $('asstMic'), lang: $('asstLang'), voiceNote: $('asstVoiceNote')
  };
  var url = {
    send: root.dataset.sendUrl, sessions: root.dataset.sessionsUrl, chat: root.dataset.chatUrl, rename: root.dataset.renameUrl,
    del: root.dataset.deleteUrl, delAll: root.dataset.deleteAllUrl, exportTable: root.dataset.exportUrl, rate: root.dataset.rateUrl
  };

  var webOn = false;          // web mode for the NEXT question only; it switches itself off after the question is sent
  var currentId = null;       // the open chat, null = a new chat that is not saved yet
  var busy = false;
  var controller = null;      // aborts the running answer
  var DEFAULT_TITLE = 'MS. WAND assistant';
  var phone = window.matchMedia('(max-width: 860px)');

  function token() {
    var t = root.querySelector('input[name="__RequestVerificationToken"]');
    return t ? t.value : '';
  }

  async function post(address, body) {
    return fetch(address, {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify(body || {})
    });
  }

  // ---- showing text safely: everything is escaped first, then **bold**, lists and tables are drawn ----
  function escapeHtml(s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }

  function format(text) {
    var lines = escapeHtml(text).split(/\r?\n/);
    var out = '', listType = '', rows = [];
    function closeList() { if (listType) { out += '</' + listType + '>'; listType = ''; } }
    function closeTable() {
      if (!rows.length) return;
      out += '<div class="cg-table-wrap"><table class="cg-table">';
      rows.forEach(function (cells, i) {
        var tag = i === 0 ? 'th' : 'td';
        out += '<tr>' + cells.map(function (c) { return '<' + tag + '>' + c + '</' + tag + '>'; }).join('') + '</tr>';
      });
      out += '</table></div>';
      rows = [];
    }
    lines.forEach(function (line) {
      if (/^\s*\|.*\|\s*$/.test(line)) {
        closeList();
        if (!/^\s*\|[\s:|-]+\|\s*$/.test(line)) rows.push(line.trim().replace(/^\||\|$/g, '').split('|').map(function (c) { return c.trim(); }));
        return;
      }
      closeTable();
      var bullet = /^\s*[-*•]\s+(.*)$/.exec(line), number = /^\s*\d+[.)]\s+(.*)$/.exec(line), item = bullet || number;
      if (item) {
        var type = bullet ? 'ul' : 'ol';
        if (listType !== type) { closeList(); out += '<' + type + '>'; listType = type; }
        out += '<li>' + item[1] + '</li>';
      } else if (line.trim() === '') {
        closeList();
      } else {
        closeList();
        out += '<p>' + line + '</p>';
      }
    });
    closeList(); closeTable();
    return out.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  }

  // ---- small helpers ----
  function nearBottom() { return el.scroll.scrollHeight - el.scroll.scrollTop - el.scroll.clientHeight < 80; }
  function toBottom() { el.scroll.scrollTop = el.scroll.scrollHeight; }

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {
      var area = document.createElement('textarea');
      area.value = text; area.style.position = 'fixed'; area.style.opacity = '0';
      document.body.appendChild(area); area.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { /* ignore */ }
      area.remove();
      ok ? resolve() : reject(new Error('copy failed'));
    });
  }

  function button(className, icon, label, onClick) {
    var b = document.createElement('button');
    b.type = 'button'; b.className = className; b.title = label; b.setAttribute('aria-label', label);
    b.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i>';
    b.addEventListener('click', function (e) { e.stopPropagation(); onClick(b); });
    return b;
  }
  function toolButton(icon, label, onClick) { return button('cg-tool', icon, label, onClick); }

  function flash(btn, icon) {
    var i = btn.querySelector('i'), old = i.className;
    i.className = 'bi ' + icon;
    setTimeout(function () { i.className = old; }, 1300);
  }

  // The rows come from the database through the server; every cell is set with textContent, so nothing can inject HTML.
  function buildTable(t) {
    var wrap = document.createElement('div');
    wrap.className = 'cg-table-wrap';
    var head = document.createElement('div');
    head.className = 'cg-table-title';
    var name = document.createElement('span');
    name.textContent = t.title || '';
    head.appendChild(name);
    var tools = document.createElement('span');
    tools.className = 'cg-tools';
    tools.appendChild(toolButton('bi-clipboard', 'Copy the table', function (b) {
      var text = [t.columns || []].concat(t.rows || []).map(function (r) { return r.join('\t'); }).join('\n');
      copyText(text).then(function () { flash(b, 'bi-check2'); }).catch(function () { flash(b, 'bi-x-lg'); });
    }));
    tools.appendChild(toolButton('bi-file-earmark-excel', 'Export to Excel', async function (b) {
      try {
        var res = await post(url.exportTable, { title: t.title, columns: t.columns, rows: t.rows });
        if (!res.ok) throw new Error('export failed');
        var blob = await res.blob();
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob); a.download = 'assistant-table.xlsx';
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(function () { URL.revokeObjectURL(a.href); }, 4000);
        flash(b, 'bi-check2');
      } catch (e) { flash(b, 'bi-x-lg'); }
    }));
    head.appendChild(tools);
    wrap.appendChild(head);

    var table = document.createElement('table');
    table.className = 'cg-table';
    var tr = document.createElement('tr');
    (t.columns || []).forEach(function (c) { var th = document.createElement('th'); th.textContent = c; tr.appendChild(th); });
    table.appendChild(tr);
    (t.rows || []).forEach(function (r) {
      var row = document.createElement('tr');
      r.forEach(function (c) { var td = document.createElement('td'); td.textContent = c; row.appendChild(td); });
      table.appendChild(row);
    });
    wrap.appendChild(table);
    return wrap;
  }

  // ---- messages on the screen ----
  function addUser(text) {
    var m = document.createElement('div');
    m.className = 'cg-msg is-user';
    var b = document.createElement('div');
    b.className = 'cg-bubble'; b.dir = 'auto'; b.textContent = text;
    m.appendChild(b);
    el.thread.appendChild(m);
    return m;
  }

  function addBot() {
    var m = document.createElement('div');
    m.className = 'cg-msg is-bot';
    var badge = document.createElement('span');
    badge.className = 'cg-badge'; badge.setAttribute('aria-hidden', 'true');
    badge.innerHTML = '<i class="bi bi-stars"></i>';
    var body = document.createElement('div');
    body.className = 'cg-bot-body';
    var b = document.createElement('div');
    b.className = 'cg-bubble'; b.dir = 'auto';
    body.appendChild(b);
    m.appendChild(badge); m.appendChild(body);
    el.thread.appendChild(m);
    return { box: body, bubble: b, text: '' };
  }

  function setBotText(bot, text) { bot.text = text; bot.bubble.innerHTML = format(text); }

  function addTyping(bot) { bot.bubble.innerHTML = '<span class="cg-typing" aria-label="The assistant is writing"><span></span><span></span><span></span></span>'; }

  // Thumbs up / down under an answer (AI-20). Pressing the same thumb again removes the rating. The rating belongs to the user's own chat.
  function addRating(tools, id, current) {
    if (!id) return;
    var value = current || 0, up, down;
    function paint() {
      up.classList.toggle('is-on', value === 1); down.classList.toggle('is-on', value === -1);
      up.querySelector('i').className = 'bi ' + (value === 1 ? 'bi-hand-thumbs-up-fill' : 'bi-hand-thumbs-up');
      down.querySelector('i').className = 'bi ' + (value === -1 ? 'bi-hand-thumbs-down-fill' : 'bi-hand-thumbs-down');
      up.setAttribute('aria-pressed', value === 1 ? 'true' : 'false'); down.setAttribute('aria-pressed', value === -1 ? 'true' : 'false');
    }
    async function set(wanted) {
      var next = value === wanted ? 0 : wanted;
      try {
        var res = await post(url.rate, { id: id, value: next });
        if (res.ok) { value = next; paint(); }
      } catch (e) { /* the rating is a convenience */ }
    }
    up = toolButton('bi-hand-thumbs-up', 'Good answer', function () { set(1); });
    down = toolButton('bi-hand-thumbs-down', 'Bad answer', function () { set(-1); });
    tools.appendChild(up); tools.appendChild(down);
    paint();
  }

  function finishBot(bot, stopped, id, feedback) {
    if (stopped) {
      var s = document.createElement('div');
      s.className = 'cg-stopped'; s.textContent = 'Stopped.';
      bot.box.appendChild(s);
    }
    var tools = document.createElement('div');
    tools.className = 'cg-tools';
    tools.appendChild(toolButton('bi-clipboard', 'Copy', function (b) {
      copyText(bot.text).then(function () { flash(b, 'bi-check2'); }).catch(function () { flash(b, 'bi-x-lg'); });
    }));
    addRating(tools, id, feedback);
    bot.box.appendChild(tools);
  }

  // The company documents the answer was based on: small chips "[1] Title" right under the answer. Titles are set with textContent.
  function addSources(bot, sources) {
    if (!sources || !sources.length) return;
    var row = document.createElement('div');
    row.className = 'cg-sources';
    var label = document.createElement('span');
    label.className = 'cg-sources-label';
    label.innerHTML = '<i class="bi bi-journal-text" aria-hidden="true"></i> Sources';
    if (sources.every(function (s) { return !!s.url; })) label.innerHTML = '<i class="bi bi-globe2" aria-hidden="true"></i> From the web';
    row.appendChild(label);
    sources.forEach(function (s) {
      var isWeb = !!s.url;                                   // a web page is a link, a company document is plain text
      var chip = document.createElement(isWeb && /^https:\/\//i.test(s.url) ? 'a' : 'span');
      chip.className = 'cg-source' + (isWeb ? ' is-web' : '');
      chip.textContent = (isWeb ? '[W' : '[') + s.n + '] ' + s.title;
      chip.title = isWeb ? s.url : s.title;
      if (chip.tagName === 'A') { chip.href = s.url; chip.target = '_blank'; chip.rel = 'noopener noreferrer'; }
      row.appendChild(chip);
    });
    bot.bubble.after(row);
  }

  function showError(bot, message) {
    if (!bot.text) { bot.bubble.classList.add('is-error'); bot.bubble.textContent = message; return; }
    var e = document.createElement('div');
    e.className = 'cg-bubble is-error'; e.textContent = message;
    bot.box.appendChild(e);
  }

  function renderChat(chat) {
    el.thread.innerHTML = '';
    chat.messages.forEach(function (m) {
      if (m.role === 'user') { addUser(m.content); return; }
      var bot = addBot();
      setBotText(bot, m.content);
      addSources(bot, m.sources);
      if (m.table) bot.box.appendChild(buildTable(m.table));
      finishBot(bot, m.stopped, m.id, m.feedback);
    });
    toBottom();
  }

  function setBusy(on) {
    busy = on;
    el.send.hidden = on; el.stop.hidden = !on;
  }

  function showWelcome(on) {
    if (!el.welcome) return;
    el.welcome.hidden = !on;
    if (on && !el.welcome.parentNode) el.thread.appendChild(el.welcome);
  }

  function setTitle(text) {
    el.title.textContent = text || DEFAULT_TITLE;
    document.title = (text ? text + ' | ' : '') + 'MS. WAND';
  }

  // ---- the list of chats, grouped like ChatGPT: Today, Yesterday, Previous 7 days, Older ----
  function groupOf(iso) {
    var d = new Date(iso.endsWith('Z') ? iso : iso + 'Z');                       // the server stores UTC
    var start = new Date(); start.setHours(0, 0, 0, 0);
    var days = Math.floor((start - new Date(d.getFullYear(), d.getMonth(), d.getDate())) / 86400000);
    return days <= 0 ? 'Today' : days === 1 ? 'Yesterday' : days <= 7 ? 'Previous 7 days' : 'Older';
  }

  async function renameChat(s) {
    var name = window.prompt('Chat name', s.title);
    if (name == null || !name.trim()) return;
    var res = await post(url.rename, { id: s.id, title: name.trim() });
    if (res.ok) { if (s.id === currentId) setTitle(name.trim().slice(0, 120)); loadList(); }
  }

  async function deleteChat(s) {
    if (busy && s.id === currentId) return;
    if (!window.confirm('Delete this chat? This cannot be undone.')) return;
    var res = await post(url.del, { id: s.id });
    if (!res.ok) return;
    if (s.id === currentId) startNew(true); else loadList();
  }

  async function loadList() {
    try {
      var res = await fetch(url.sessions, { credentials: 'same-origin' });
      if (!res.ok) throw new Error();
      var items = await res.json();
      el.list.innerHTML = '';
      if (!items.length) {
        var empty = document.createElement('div');
        empty.className = 'cg-empty'; empty.textContent = 'No chats yet.';
        el.list.appendChild(empty);
        return;
      }
      var last = '';
      items.forEach(function (s) {
        var g = groupOf(s.updatedAt);
        if (g !== last) {
          var h = document.createElement('div');
          h.className = 'cg-group'; h.textContent = g;
          el.list.appendChild(h);
          last = g;
        }
        var row = document.createElement('div');
        row.className = 'cg-item' + (s.id === currentId ? ' is-active' : '');
        var open = document.createElement('button');
        open.type = 'button'; open.className = 'cg-item-open'; open.textContent = s.title; open.title = s.title; open.dir = 'auto';
        open.addEventListener('click', function () { openChat(s.id); });
        var actions = document.createElement('span');
        actions.className = 'cg-item-actions';
        actions.appendChild(button('cg-mini', 'bi-pencil', 'Rename', function () { renameChat(s); }));
        actions.appendChild(button('cg-mini', 'bi-trash3', 'Delete', function () { deleteChat(s); }));
        row.appendChild(open); row.appendChild(actions);
        el.list.appendChild(row);
      });
    } catch (e) { /* the list is a convenience: the chat still works without it */ }
  }

  function remember(id) {
    try {
      var u = new URL(window.location.href);
      if (id == null) u.searchParams.delete('c'); else u.searchParams.set('c', id);
      window.history.replaceState(null, '', u);
    } catch (e) { /* ignore */ }
  }

  function closeSideOnPhone() { if (phone.matches) { root.classList.remove('is-side-open'); el.backdrop.hidden = true; } }

  function startNew(force) {
    if (busy && !force) return;
    currentId = null;
    el.thread.innerHTML = '';
    showWelcome(true);
    setTitle('');
    remember(null); loadList(); closeSideOnPhone();
    el.input.focus();
  }

  async function openChat(id) {
    if (busy) return;
    try {
      var res = await fetch(url.chat + '?id=' + encodeURIComponent(id), { credentials: 'same-origin' });
      if (!res.ok) { startNew(); return; }
      var chat = await res.json();
      currentId = chat.id;
      showWelcome(false);
      renderChat(chat);
      setTitle(chat.title);
      remember(chat.id); loadList(); closeSideOnPhone();
    } catch (e) { startNew(); }
  }

  // ---- asking ----
  async function ask(question) {
    question = (question || '').trim();
    if (!question || busy) return;

    showWelcome(false);
    if (el.welcome && el.welcome.parentNode) el.welcome.parentNode.removeChild(el.welcome);
    addUser(question);
    var bot = addBot();
    addTyping(bot);
    el.input.value = ''; autosize();
    toBottom();
    setBusy(true);

    controller = new AbortController();
    var stopped = false, failed = false, got = false, sentWithWeb = webOn, messageId = 0;
    setWeb(false);                                           // web mode is per question: the next one starts without it
    try {
      var res = await fetch(url.send, {
        method: 'POST', credentials: 'same-origin', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
        body: JSON.stringify({ sessionId: currentId, message: question, web: sentWithWeb })
      });
      if (!res.ok || !res.body) throw new Error('http ' + res.status);

      var reader = res.body.getReader(), decoder = new TextDecoder(), buffer = '', text = '';
      while (true) {
        var chunk = await reader.read();
        if (chunk.done) break;
        buffer += decoder.decode(chunk.value, { stream: true });
        var lines = buffer.split('\n');
        buffer = lines.pop();
        for (var i = 0; i < lines.length; i++) {
          if (!lines[i].trim()) continue;
          var ev = JSON.parse(lines[i]), stick = nearBottom();
          if (ev.type === 'session') {
            currentId = ev.data.id; setTitle(ev.data.title); remember(currentId);
          } else if (ev.type === 'notice') {
            var note = document.createElement('div');
            note.className = 'cg-stopped'; note.textContent = ev.data.message;
            bot.bubble.before(note);
          } else if (ev.type === 'sources') {
            addSources(bot, ev.data);
          } else if (ev.type === 'delta') {
            got = true; text += ev.data.text; setBotText(bot, text);
          } else if (ev.type === 'table') {
            bot.box.appendChild(buildTable(ev.data));
          } else if (ev.type === 'error') {
            failed = true; showError(bot, ev.data.message);
          } else if (ev.type === 'done') {
            stopped = !!ev.data.stopped && !failed; messageId = ev.data.id || 0;
          }
          if (stick) toBottom();
        }
      }
    } catch (e) {
      if (e && e.name === 'AbortError') stopped = true;
      else { failed = true; showError(bot, 'Could not reach the assistant. Please try again.'); }
    }

    if (got) finishBot(bot, stopped, messageId, 0);
    else if (!failed) { if (stopped) { bot.bubble.textContent = 'Stopped.'; } else { showError(bot, 'The assistant did not give an answer. Please try again.'); } }
    controller = null;
    setBusy(false);
    loadList();
    el.input.focus();
  }

  // ---- the box ----
  function autosize() {
    el.input.style.height = 'auto';
    el.input.style.height = Math.min(el.input.scrollHeight, 200) + 'px';
  }

  el.input.addEventListener('input', autosize);
  el.input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); el.form.requestSubmit(); }
  });
  el.form.addEventListener('submit', function (e) { e.preventDefault(); ask(el.input.value); });
  el.stop.addEventListener('click', function () { if (controller) controller.abort(); });

  function setWeb(on) {
    webOn = !!on && !!el.web;
    if (!el.web) return;
    el.web.classList.toggle('is-on', webOn);
    el.web.setAttribute('aria-pressed', webOn ? 'true' : 'false');
    el.webNote.hidden = !webOn;
  }
  if (el.web) el.web.addEventListener('click', function () { if (!busy) setWeb(!webOn); });
  el.newChat.addEventListener('click', function () { startNew(); });
  root.querySelectorAll('[data-ask]').forEach(function (b) { b.addEventListener('click', function () { ask(b.getAttribute('data-ask')); }); });

  // The same buttons open and close the list of chats: on a computer it slides away, on a phone it covers the conversation.
  function toggleSide() {
    if (phone.matches) {
      var open = root.classList.toggle('is-side-open');
      el.backdrop.hidden = !open;
    } else {
      root.classList.toggle('is-collapsed');
    }
  }
  el.toggle.addEventListener('click', toggleSide);
  el.collapse.addEventListener('click', toggleSide);
  el.backdrop.addEventListener('click', closeSideOnPhone);

  el.deleteAll.addEventListener('click', async function () {
    if (busy) return;
    if (!window.confirm('Delete ALL your chats? This cannot be undone.')) return;
    var res = await post(url.delAll, {});
    if (res.ok) startNew(true);
  });

  // ---- voice typing (ai-voice.js): the recognised text goes into the box, the user reads it and presses Send ----
  var voiceTimer = null;
  function voiceMessage(text) {
    if (!el.voiceNote) return;
    el.voiceNote.textContent = text; el.voiceNote.hidden = false;
    clearTimeout(voiceTimer);
    voiceTimer = setTimeout(function () { el.voiceNote.hidden = true; }, 7000);
  }
  if (window.MsWandVoice && el.mic) {
    window.MsWandVoice.attach({
      button: el.mic, langButton: el.lang, input: el.input,
      mode: root.dataset.voice || 'browser', url: root.dataset.voiceUrl, token: token, autosize: autosize, notify: voiceMessage
    });
  }

  // ---- start ----
  var wanted = parseInt(new URLSearchParams(window.location.search).get('c'), 10);
  loadList();
  if (wanted > 0) openChat(wanted);
  el.input.focus();
})();
