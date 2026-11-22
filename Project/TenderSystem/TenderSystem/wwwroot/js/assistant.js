/* Full-page assistant (Views/Assistant/Index.cshtml).
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
    rename: $('asstRename'), del: $('asstDelete'), newChat: $('asstNew'), deleteAll: $('asstDeleteAll'), toggle: $('asstToggle')
  };
  var url = {
    send: root.dataset.sendUrl, sessions: root.dataset.sessionsUrl, chat: root.dataset.chatUrl, rename: root.dataset.renameUrl,
    del: root.dataset.deleteUrl, delAll: root.dataset.deleteAllUrl, exportTable: root.dataset.exportUrl
  };

  var currentId = null;       // the open chat, null = a new chat that is not saved yet
  var busy = false;
  var controller = null;      // aborts the running answer
  var DEFAULT_TITLE = 'MS. WAND assistant';

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
      out += '<div class="asst-table-wrap"><table class="asst-table">';
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

  function toolButton(icon, label, onClick) {
    var b = document.createElement('button');
    b.type = 'button'; b.className = 'asst-tool'; b.title = label; b.setAttribute('aria-label', label);
    b.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i>';
    b.addEventListener('click', function () { onClick(b); });
    return b;
  }

  function flash(btn, icon) {
    var i = btn.querySelector('i'), old = i.className;
    i.className = 'bi ' + icon;
    setTimeout(function () { i.className = old; }, 1300);
  }

  // The rows come from the database through the server; every cell is set with textContent, so nothing can inject HTML.
  function buildTable(t) {
    var wrap = document.createElement('div');
    wrap.className = 'asst-table-wrap';
    var head = document.createElement('div');
    head.className = 'asst-table-title';
    var name = document.createElement('span');
    name.textContent = t.title || '';
    head.appendChild(name);
    var tools = document.createElement('span');
    tools.className = 'asst-tools';
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
    table.className = 'asst-table';
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
    m.className = 'asst-msg is-user';
    var b = document.createElement('div');
    b.className = 'asst-bubble'; b.dir = 'auto'; b.textContent = text;
    m.appendChild(b);
    el.thread.appendChild(m);
    return m;
  }

  function addBot() {
    var m = document.createElement('div');
    m.className = 'asst-msg is-bot';
    var b = document.createElement('div');
    b.className = 'asst-bubble'; b.dir = 'auto';
    m.appendChild(b);
    el.thread.appendChild(m);
    return { box: m, bubble: b, text: '' };
  }

  function setBotText(bot, text) { bot.text = text; bot.bubble.innerHTML = format(text); }

  function addTyping(bot) { bot.bubble.innerHTML = '<span class="asst-typing" aria-label="The assistant is writing"><span></span><span></span><span></span></span>'; }

  function finishBot(bot, stopped) {
    if (stopped) {
      var s = document.createElement('div');
      s.className = 'asst-stopped'; s.textContent = 'Stopped.';
      bot.box.appendChild(s);
    }
    var tools = document.createElement('div');
    tools.className = 'asst-tools';
    tools.appendChild(toolButton('bi-clipboard', 'Copy', function (b) {
      copyText(bot.text).then(function () { flash(b, 'bi-check2'); }).catch(function () { flash(b, 'bi-x-lg'); });
    }));
    bot.box.appendChild(tools);
  }

  function showError(bot, message) {
    if (!bot.text) { bot.bubble.classList.add('is-error'); bot.bubble.textContent = message; return; }
    var e = document.createElement('div');
    e.className = 'asst-bubble is-error'; e.textContent = message;
    bot.box.appendChild(e);
  }

  function renderChat(chat) {
    el.thread.innerHTML = '';
    chat.messages.forEach(function (m) {
      if (m.role === 'user') { addUser(m.content); return; }
      var bot = addBot();
      setBotText(bot, m.content);
      if (m.table) bot.box.appendChild(buildTable(m.table));
      finishBot(bot, m.stopped);
    });
    toBottom();
  }

  function setBusy(on) {
    busy = on;
    el.send.hidden = on; el.stop.hidden = !on;
    el.input.disabled = false;
  }

  function showWelcome(on) { if (el.welcome) el.welcome.hidden = !on; if (on && el.welcome && !el.welcome.parentNode) el.thread.appendChild(el.welcome); }

  // ---- the list of chats ----
  function setHeader() {
    var open = currentId != null;
    el.rename.hidden = !open; el.del.hidden = !open;
  }

  async function loadList() {
    try {
      var res = await fetch(url.sessions, { credentials: 'same-origin' });
      if (!res.ok) throw new Error();
      var items = await res.json();
      el.list.innerHTML = '';
      if (!items.length) {
        var empty = document.createElement('div');
        empty.className = 'asst-empty'; empty.textContent = 'No chats yet.';
        el.list.appendChild(empty);
      }
      items.forEach(function (s) {
        var b = document.createElement('button');
        b.type = 'button'; b.className = 'asst-item' + (s.id === currentId ? ' is-active' : '');
        b.textContent = s.title; b.title = s.title; b.dir = 'auto';
        b.addEventListener('click', function () { openChat(s.id); });
        el.list.appendChild(b);
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

  function startNew() {
    if (busy) return;
    currentId = null;
    el.thread.innerHTML = '';
    showWelcome(true);
    el.title.textContent = DEFAULT_TITLE;
    setHeader(); remember(null); loadList();
    root.classList.remove('is-side-open');
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
      el.title.textContent = chat.title;
      setHeader(); remember(chat.id); loadList();
      root.classList.remove('is-side-open');
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
    var stopped = false, failed = false, got = false;
    try {
      var res = await fetch(url.send, {
        method: 'POST', credentials: 'same-origin', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
        body: JSON.stringify({ sessionId: currentId, message: question })
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
            currentId = ev.data.id; el.title.textContent = ev.data.title; setHeader(); remember(currentId);
          } else if (ev.type === 'delta') {
            got = true; text += ev.data.text; setBotText(bot, text);
          } else if (ev.type === 'table') {
            bot.box.appendChild(buildTable(ev.data));
          } else if (ev.type === 'error') {
            failed = true; showError(bot, ev.data.message);
          } else if (ev.type === 'done') {
            stopped = !!ev.data.stopped && !failed;
          }
          if (stick) toBottom();
        }
      }
    } catch (e) {
      if (e && e.name === 'AbortError') stopped = true;
      else { failed = true; showError(bot, 'Could not reach the assistant. Please try again.'); }
    }

    if (got) finishBot(bot, stopped);
    else if (!failed) { if (stopped) { bot.bubble.textContent = 'Stopped.'; } else { showError(bot, 'The assistant did not give an answer. Please try again.'); } }
    controller = null;
    setBusy(false);
    loadList();
    el.input.focus();
  }

  // ---- the box ----
  function autosize() {
    el.input.style.height = 'auto';
    el.input.style.height = Math.min(el.input.scrollHeight, 180) + 'px';
  }

  el.input.addEventListener('input', autosize);
  el.input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); el.form.requestSubmit(); }
  });
  el.form.addEventListener('submit', function (e) { e.preventDefault(); ask(el.input.value); });
  el.stop.addEventListener('click', function () { if (controller) controller.abort(); });
  el.newChat.addEventListener('click', startNew);
  el.toggle.addEventListener('click', function () { root.classList.toggle('is-side-open'); });
  root.querySelectorAll('[data-ask]').forEach(function (b) { b.addEventListener('click', function () { ask(b.getAttribute('data-ask')); }); });

  el.rename.addEventListener('click', async function () {
    if (currentId == null) return;
    var name = window.prompt('Chat name', el.title.textContent);
    if (name == null || !name.trim()) return;
    var res = await post(url.rename, { id: currentId, title: name.trim() });
    if (res.ok) { el.title.textContent = name.trim().slice(0, 120); loadList(); }
  });

  el.del.addEventListener('click', async function () {
    if (currentId == null || busy) return;
    if (!window.confirm('Delete this chat? This cannot be undone.')) return;
    var res = await post(url.del, { id: currentId });
    if (res.ok) startNew();
  });

  el.deleteAll.addEventListener('click', async function () {
    if (busy) return;
    if (!window.confirm('Delete ALL your chats? This cannot be undone.')) return;
    var res = await post(url.delAll, {});
    if (res.ok) startNew();
  });

  // ---- start ----
  var wanted = parseInt(new URLSearchParams(window.location.search).get('c'), 10);
  loadList();
  if (wanted > 0) openChat(wanted);
  setHeader();
  el.input.focus();
})();
