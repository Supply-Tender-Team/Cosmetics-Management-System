/* Floating AI assistant (Views/Shared/_AiChat.cshtml).
   The conversation lives in this tab only (sessionStorage, one key per user) and is sent to /AiChat/Ask with each question. */
(function () {
  'use strict';

  var root = document.getElementById('aiChat');
  if (!root) return;

  var panel = document.getElementById('aiPanel');
  var toggle = document.getElementById('aiToggle');
  var closeBtn = document.getElementById('aiClose');
  var clearBtn = document.getElementById('aiClear');
  var wideBtn = document.getElementById('aiWide');
  var modeBtns = Array.prototype.slice.call(root.querySelectorAll('[data-ai-mode]'));   // only rendered for Admins when AI_ACTIONS=true
  var modeHint = document.getElementById('aiModeHint');
  var noteEl = root.querySelector('.ai-note');
  var actionsBase = root.getAttribute('data-actions') || '';
  var mode = 'ask';
  var list = document.getElementById('aiMessages');
  var suggestions = document.getElementById('aiSuggestions');
  var form = document.getElementById('aiForm');
  var input = document.getElementById('aiInput');
  var sendBtn = document.getElementById('aiSend');

  var url = root.getAttribute('data-url');
  var storeKey = 'aiChat:' + (root.getAttribute('data-user') || '');
  var history = [];          // [{ role: 'user' | 'assistant', text: '...' }]
  var busy = false;

  // ---- storage (may be blocked in private windows, so never rely on it) ----
  function load() {
    try { history = JSON.parse(sessionStorage.getItem(storeKey) || '[]') || []; } catch (e) { history = []; }
  }
  function save() {
    try { sessionStorage.setItem(storeKey, JSON.stringify(history.slice(-20))); } catch (e) { /* ignore */ }
  }

  // ---- showing messages ----
  function escapeHtml(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // Tiny safe formatter for the answers: **bold**, "- item" / "1. item" lists, paragraphs. Everything is escaped first.
  function format(text) {
    var lines = escapeHtml(text).split(/\r?\n/);
    var out = '', listType = '', tableRows = [];
    function closeList() { if (listType) { out += '</' + listType + '>'; listType = ''; } }
    function closeTable() {
      if (!tableRows.length) return;
      out += '<div class="ai-table-wrap"><table class="ai-table">';
      tableRows.forEach(function (cells, i) {
        var tag = i === 0 ? 'th' : 'td';
        out += '<tr>' + cells.map(function (c) { return '<' + tag + '>' + c + '</' + tag + '>'; }).join('') + '</tr>';
      });
      out += '</table></div>';
      tableRows = [];
    }
    lines.forEach(function (line) {
      if (/^\s*\|.*\|\s*$/.test(line)) {                 // | a | b |  (a table written by the model)
        closeList();
        if (!/^\s*\|[\s:|-]+\|\s*$/.test(line))          // skip the |---|---| line
          tableRows.push(line.trim().replace(/^\||\|$/g, '').split('|').map(function (c) { return c.trim(); }));
        return;
      }
      closeTable();
      var bullet = /^\s*[-*•]\s+(.*)$/.exec(line);
      var number = /^\s*\d+[.)]\s+(.*)$/.exec(line);
      var item = bullet || number;
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
    closeList();
    closeTable();
    return out.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  }

  // The rows come straight from the database through the server; every cell is set with textContent, so nothing can inject HTML.
  function buildTable(t) {
    var wrap = document.createElement('div');
    wrap.className = 'ai-table-wrap';
    var title = document.createElement('div');
    title.className = 'ai-table-title';
    title.textContent = t.title || '';
    wrap.appendChild(title);
    var table = document.createElement('table');
    table.className = 'ai-table';
    var head = table.insertRow();
    (t.columns || []).forEach(function (c) { var th = document.createElement('th'); th.textContent = c; head.appendChild(th); });
    (t.rows || []).forEach(function (r) {
      var tr = table.insertRow();
      r.forEach(function (c) { var td = tr.insertCell(); td.textContent = c; });
    });
    wrap.appendChild(table);
    return wrap;
  }

  function addMessage(kind, text, table) {
    var div = document.createElement('div');
    div.className = 'ai-msg ai-msg-' + kind + (table ? ' ai-msg-wide' : '');
    div.setAttribute('dir', 'auto');
    if (kind === 'bot') div.innerHTML = format(text); else div.textContent = text;
    if (table) div.appendChild(buildTable(table));
    list.appendChild(div);
    list.scrollTop = list.scrollHeight;
    return div;
  }

  function addTyping() {
    var div = document.createElement('div');
    div.className = 'ai-msg ai-msg-bot';
    div.innerHTML = '<span class="ai-typing" aria-label="The assistant is typing"><i></i><i></i><i></i></span>';
    list.appendChild(div);
    list.scrollTop = list.scrollHeight;
    return div;
  }

  function render() {
    // keep the welcome bubble and chips, rebuild the rest
    Array.prototype.slice.call(list.querySelectorAll('.ai-msg:not(#aiWelcome)')).forEach(function (n) { n.remove(); });
    history.forEach(function (m) { addMessage(m.role === 'user' ? 'user' : 'bot', m.text, m.table); });
    setWide(history.some(function (m) { return m.table; }));
    suggestions.hidden = history.length > 0;
  }

  // ---- wide mode: tables need room, so the panel widens by itself when one is shown (and the user can toggle it) ----
  function setWide(on) {
    panel.classList.toggle('ai-panel-wide', on);
    wideBtn.setAttribute('aria-pressed', on ? 'true' : 'false');
    wideBtn.querySelector('i').className = on ? 'bi bi-arrows-angle-contract' : 'bi bi-arrows-angle-expand';
    wideBtn.title = on ? 'Make the chat narrower' : 'Make the chat wider';
    list.scrollTop = list.scrollHeight;
  }
  wideBtn.addEventListener('click', function () { setWide(!panel.classList.contains('ai-panel-wide')); });

  // ---- open / close ----
  function setOpen(open, remember) {
    panel.hidden = !open;
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) { list.scrollTop = list.scrollHeight; input.focus(); }
    if (remember !== false) {                 // stay open (or closed) when the user moves to another page
      try { sessionStorage.setItem(storeKey + ':open', open ? '1' : '0'); } catch (e) { /* ignore */ }
    }
  }

  toggle.addEventListener('click', function () { setOpen(panel.hidden); });
  closeBtn.addEventListener('click', function () { setOpen(false); toggle.focus(); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !panel.hidden) { setOpen(false); toggle.focus(); }
  });

  clearBtn.addEventListener('click', function () {
    if (busy) return;
    history = [];
    save();
    render();
  });

  // ---- asking ----
  function token() {
    var el = root.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
  }

  function setBusy(value) {
    busy = value;
    sendBtn.disabled = value;
    input.disabled = value;
    if (!value) input.focus();
  }

  async function ask(question) {
    question = (question || '').trim();
    if (!question || busy) return;
    if (mode === 'command') return propose(question);

    suggestions.hidden = true;
    addMessage('user', question);
    var earlier = history.map(function (m) { return { role: m.role, text: m.text }; });   // what the assistant should remember (text only, no tables)
    history.push({ role: 'user', text: question });
    input.value = '';
    autosize();
    setBusy(true);
    var typing = addTyping();

    try {
      var res = await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
        body: JSON.stringify({ message: question, page: location.pathname, history: earlier })
      });
      var data = null;
      try { data = await res.json(); } catch (e) { /* not JSON: probably a redirect to the sign-in page */ }
      typing.remove();

      if (data && data.ok) {
        history.push({ role: 'assistant', text: data.reply, table: data.table || null });
        addMessage('bot', data.reply, data.table);
        if (data.table) setWide(true);
      } else {
        history.pop();                          // the question was not answered, so do not remember it
        var failed = addMessage('error', (data && data.message) || 'The assistant is not available right now. Please reload the page and try again.');
        failed.setAttribute('role', 'alert');
        input.value = question;                 // let the user send it again
      }
    } catch (e) {
      typing.remove();
      history.pop();
      addMessage('error', 'Could not reach the server. Check your connection and try again.').setAttribute('role', 'alert');
      input.value = question;
    }
    save();
    setBusy(false);
  }

  // ---- command mode (Admin only) ----------------------------------------------------------------------------------------
  // The assistant only PREPARES an action. Nothing is sent until the Admin approves it with a fresh fingerprint or face check.
  // Text and recipients are fixed on the server; this page only shows them and carries the proof.
  var NOTE_ASK = noteEl ? noteEl.textContent : '';
  var NOTE_COMMAND = 'Command mode: nothing is sent until you approve it with your fingerprint or face. The assistant only prepares it.';

  function setMode(next) {
    if (!actionsBase) next = 'ask';
    mode = next;
    modeBtns.forEach(function (b) {
      var on = b.getAttribute('data-ai-mode') === next;
      b.classList.toggle('is-active', on);
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
    input.placeholder = next === 'command' ? 'Tell me what to send and to whom...' : 'Type your question...';
    if (modeHint) modeHint.hidden = next !== 'command';
    if (noteEl) noteEl.textContent = next === 'command' ? NOTE_COMMAND : NOTE_ASK;
    suggestions.hidden = next === 'command' || history.length > 0;
    try { sessionStorage.setItem(storeKey + ':mode', next); } catch (e) { /* ignore */ }
  }
  modeBtns.forEach(function (b) { b.addEventListener('click', function () { setMode(b.getAttribute('data-ai-mode')); input.focus(); }); });

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;       // every text from the server is set as text, never as HTML
    return n;
  }

  async function postAction(path, body) {
    var res = await fetch(actionsBase + path, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify(body || {})
    });
    var data = null;
    try { data = await res.json(); } catch (e) { /* not JSON */ }
    if (!data) throw new Error('The server did not answer as expected (' + res.status + '). Please reload the page.');
    return data;
  }

  async function propose(question) {
    suggestions.hidden = true;
    addMessage('user', question);
    input.value = '';
    autosize();
    setBusy(true);
    var typing = addTyping();
    try {
      var data = await postAction('/Propose', { request: question });
      typing.remove();
      if (data.ok && data.card) {
        addMessage('bot', data.message);
        list.appendChild(buildActionCard(data.card));
        setWide(true);
        list.scrollTop = list.scrollHeight;
      } else {
        addMessage('error', data.message || 'I could not prepare that.').setAttribute('role', 'alert');
        input.value = question;
      }
    } catch (e) {
      typing.remove();
      addMessage('error', e.message || 'Could not reach the server.').setAttribute('role', 'alert');
      input.value = question;
    }
    setBusy(false);
  }

  function buildActionCard(card) {
    var wrap = el('div', 'ai-msg ai-msg-bot ai-msg-wide ai-action');
    wrap.setAttribute('dir', 'auto');
    wrap.appendChild(el('div', 'ai-action-head', card.heading));

    var to = el('div', 'ai-action-row');
    to.appendChild(el('b', null, 'To: '));
    to.appendChild(document.createTextNode(card.audience + ' (' + card.recipientCount + ')'));
    wrap.appendChild(to);
    var more = card.recipientCount - card.sampleNames.length;
    wrap.appendChild(el('div', 'ai-action-names', card.sampleNames.join(', ') + (more > 0 ? ' and ' + more + ' more' : '')));

    var box = el('div', 'ai-action-box');
    box.appendChild(el('div', 'ai-action-subject', card.subject));
    box.appendChild(el('div', 'ai-action-body', card.body));
    wrap.appendChild(box);

    var expire = el('div', 'ai-action-expire');
    var status = el('div', 'ai-action-status');
    status.setAttribute('role', 'status');
    var buttons = el('div', 'ai-action-buttons');
    var finished = false, timer = null;

    function say(text, kind) { status.textContent = text || ''; status.className = 'ai-action-status' + (kind ? ' is-' + kind : ''); }
    function lockAll() {
      buttons.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
      if (timer) { clearInterval(timer); timer = null; }
    }
    function conclude(result) {
      if (result.ok) { finished = true; say(result.message, 'ok'); expire.textContent = ''; lockAll(); return; }
      say(result.message || 'It did not work. Please try again.', 'error');
      if (result.state === 'Cancelled' || result.state === 'Failed') { finished = true; expire.textContent = ''; lockAll(); }
    }

    if (card.canFingerprint && window.MsWandPasskey) {
      var fp = el('button', 'btn btn-brand ai-action-btn');
      fp.type = 'button';
      fp.innerHTML = '<i class="bi bi-fingerprint" aria-hidden="true"></i> Approve with fingerprint';
      fp.addEventListener('click', async function () {
        fp.disabled = true;
        say('Follow the prompt on your device...');
        try {
          conclude(await window.MsWandPasskey.approve({ options: actionsBase + '/PasskeyOptions', approve: actionsBase + '/PasskeyApprove' }, card.actionId, token(), say));
        } catch (err) {
          say(window.MsWandPasskey.explain(err), 'error');
        } finally {
          if (!finished) fp.disabled = false;
        }
      });
      buttons.appendChild(fp);
    }

    if (card.canFace && window.MsWandFace) {
      var face = document.createElement('div');
      face.className = 'face-widget ai-action-face';
      face.setAttribute('data-face-capture', '');
      face.setAttribute('data-mode', 'approve');
      face.setAttribute('data-challenge-url', actionsBase + '/FaceChallenge');
      face.setAttribute('data-submit-url', actionsBase + '/FaceApprove');
      face.setAttribute('data-action-id', card.actionId);
      face.innerHTML = '<input type="hidden" name="__RequestVerificationToken" value="">' +
        '<div class="face-stage" hidden><video playsinline muted autoplay></video><div class="face-prompt"></div></div>' +
        '<div class="face-status" role="status"></div>' +
        '<button type="button" class="btn btn-outline-secondary ai-action-btn face-start"><i class="bi bi-person-bounding-box" aria-hidden="true"></i> Approve with face</button>';
      face.querySelector('input').value = token();
      face.addEventListener('face:approved', function (e) { conclude(e.detail); });
      buttons.appendChild(face);
      window.MsWandFace.init(face);
    }

    var cancel = el('button', 'btn btn-link ai-action-cancel', 'Cancel');
    cancel.type = 'button';
    cancel.addEventListener('click', async function () {
      cancel.disabled = true;
      try { conclude(await postAction('/Cancel', { actionId: card.actionId })); }
      catch (err) { say(err.message, 'error'); cancel.disabled = false; }
    });
    buttons.appendChild(cancel);

    wrap.appendChild(expire);
    wrap.appendChild(buttons);
    wrap.appendChild(status);

    // countdown: after this the server refuses the action anyway
    var deadline = Date.parse(card.expiresUtc);
    function tick() {
      if (finished) return;
      var left = Math.floor((deadline - Date.now()) / 1000);
      if (left <= 0) {
        finished = true;
        expire.textContent = '';
        say('This action expired. Please ask again.', 'error');
        lockAll();
        return;
      }
      expire.textContent = 'Waiting for your approval: ' + Math.floor(left / 60) + ':' + ('0' + (left % 60)).slice(-2);
    }
    tick();
    timer = setInterval(tick, 1000);
    return wrap;
  }

  form.addEventListener('submit', function (e) { e.preventDefault(); ask(input.value); });
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); ask(input.value); }
  });

  function autosize() {
    input.style.height = 'auto';
    input.style.height = Math.min(input.scrollHeight, 110) + 'px';
    input.style.overflowY = input.scrollHeight > 110 ? 'auto' : 'hidden';
  }
  input.addEventListener('input', autosize);

  suggestions.addEventListener('click', function (e) {
    var chip = e.target.closest('[data-ai-suggestion]');
    if (chip) ask(chip.getAttribute('data-ai-suggestion'));
  });

  load();
  render();
  var savedMode = 'ask';
  try { savedMode = sessionStorage.getItem(storeKey + ':mode') || 'ask'; } catch (e) { /* ignore */ }
  setMode(savedMode);
  var wasOpen = false;
  try { wasOpen = sessionStorage.getItem(storeKey + ':open') === '1'; } catch (e) { /* ignore */ }
  if (wasOpen) { setOpen(true, false); input.blur(); }   // do not steal focus from the new page
})();
