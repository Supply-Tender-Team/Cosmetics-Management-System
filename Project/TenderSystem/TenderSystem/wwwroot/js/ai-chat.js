/* Floating AI assistant (Views/Shared/_AiChat.cshtml). One round button, one window, and (for Admins) two tabs inside it. Each tab is its own conversation:
   - Ask: the normal chat. Its conversation lives in this browser tab only (sessionStorage, one key per user) and is sent to /AiChat/Ask with each question.
   - Command: the Command center (Admin only). Kept only on this page and never stored: the assistant PREPARES an action and nothing is
     sent until the Admin approves it with a fresh fingerprint or face check. */
(function () {
  'use strict';

  var root = document.getElementById('aiChat');
  if (!root) return;

  var url = root.getAttribute('data-url');
  var actionsBase = root.getAttribute('data-actions') || '';
  var storeKey = 'aiChat:' + (root.getAttribute('data-user') || '');
  var reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

  // ---- showing text safely ----
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

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;       // every text from the server is set as text, never as HTML
    return n;
  }

  function token() {
    var t = root.querySelector('input[name="__RequestVerificationToken"]');
    return t ? t.value : '';
  }

  // ---- the window: header, optional tabs, and one pane per conversation ----
  var win = document.getElementById('aiPanel');
  var toggle = document.getElementById('aiToggle');
  var wideBtn = win.querySelector('[data-ai-win="wide"]');
  var clearBtn = win.querySelector('[data-ai-win="clear"]');
  var closeBtn = win.querySelector('[data-ai-win="close"]');
  var headTitle = win.querySelector('[data-ai-head-title]');
  var headSub = win.querySelector('[data-ai-head-sub]');
  var headIcon = win.querySelector('[data-ai-head-icon]');
  var tabBtns = Array.prototype.slice.call(win.querySelectorAll('[data-ai-tab]'));
  var active = null;          // the pane that is showing
  var closeTimer = null;

  // ---- one conversation (a pane) ----
  function Pane(paneEl) {
    function part(name) { return paneEl.querySelector('[data-ai-part="' + name + '"]'); }
    var p = {
      el: paneEl, name: paneEl.getAttribute('data-ai-pane'),
      list: part('messages'), suggestions: part('suggestions'), form: part('form'), input: part('input'), sendBtn: part('send'),
      busy: false, wide: false, onClear: null
    };

    p.addMessage = function (kind, text, table) {
      var div = document.createElement('div');
      div.className = 'ai-msg ai-msg-' + kind + (table ? ' ai-msg-wide' : '');
      div.setAttribute('dir', 'auto');
      if (kind === 'bot') div.innerHTML = format(text); else div.textContent = text;
      if (table) div.appendChild(buildTable(table));
      p.list.appendChild(div);
      p.list.scrollTop = p.list.scrollHeight;
      return div;
    };

    p.addTyping = function () {
      var div = document.createElement('div');
      div.className = 'ai-msg ai-msg-bot';
      div.innerHTML = '<span class="ai-typing" aria-label="The assistant is typing"><i></i><i></i><i></i></span>';
      p.list.appendChild(div);
      p.list.scrollTop = p.list.scrollHeight;
      return div;
    };

    // tables and approval cards need room, so the window widens by itself (and the user can toggle it); each pane remembers its own width
    p.setWide = function (on) {
      p.wide = on;
      if (active === p) applyWide();
    };

    p.setBusy = function (value) {
      p.busy = value;
      p.sendBtn.disabled = value;
      p.input.disabled = value;
      if (active === p) win.classList.toggle('is-busy', value);
      if (!value && active === p) p.input.focus();
    };

    p.autosize = function () {
      p.input.style.height = 'auto';
      p.input.style.height = Math.min(p.input.scrollHeight, 110) + 'px';
      p.input.style.overflowY = p.input.scrollHeight > 110 ? 'auto' : 'hidden';
    };
    p.input.addEventListener('input', p.autosize);

    return p;
  }

  var askPane = Pane(win.querySelector('[data-ai-pane="ask"]'));
  var cmdPaneEl = win.querySelector('[data-ai-pane="command"]');
  var cmdPane = (cmdPaneEl && actionsBase) ? Pane(cmdPaneEl) : null;
  active = askPane;

  function applyWide() {
    var on = active.wide;
    win.classList.toggle('ai-panel-wide', on);
    wideBtn.setAttribute('aria-pressed', on ? 'true' : 'false');
    wideBtn.querySelector('i').className = on ? 'bi bi-arrows-angle-contract' : 'bi bi-arrows-angle-expand';
    wideBtn.title = on ? 'Make the chat narrower' : 'Make the chat wider';
    active.list.scrollTop = active.list.scrollHeight;
  }
  wideBtn.addEventListener('click', function () { active.setWide(!active.wide); });

  // switch between the tabs: the header changes with it, and each tab keeps its own messages
  function showPane(p, focus) {
    active = p;
    [askPane, cmdPane].forEach(function (other) { if (other) other.el.hidden = other !== p; });
    tabBtns.forEach(function (b) {
      var on = b.getAttribute('data-ai-tab') === p.name;
      b.classList.toggle('is-active', on);
      b.setAttribute('aria-selected', on ? 'true' : 'false');
    });
    headTitle.textContent = p.el.getAttribute('data-title');
    headSub.textContent = p.el.getAttribute('data-sub');
    headIcon.className = 'bi ' + p.el.getAttribute('data-icon');
    clearBtn.title = p.el.getAttribute('data-clear');
    clearBtn.setAttribute('aria-label', p.el.getAttribute('data-clear'));
    win.classList.toggle('is-command', p === cmdPane);
    win.classList.toggle('is-busy', p.busy);
    applyWide();
    if (focus !== false && !win.hidden) p.input.focus();
    try { sessionStorage.setItem(storeKey + ':tab', p.name); } catch (e) { /* ignore */ }
  }
  tabBtns.forEach(function (b) {
    b.addEventListener('click', function () {
      var next = b.getAttribute('data-ai-tab') === 'command' ? cmdPane : askPane;
      if (next) showPane(next);
    });
  });

  clearBtn.addEventListener('click', function () { if (!active.busy && active.onClear) active.onClear(); });

  // ---- open / close: the window grows out of the button and shrinks back into it (no animation when the user asked for less motion) ----
  function isOpen() { return !win.hidden && !win.classList.contains('is-closing'); }

  function openWin(focus, remember) {
    if (closeTimer) { clearTimeout(closeTimer); closeTimer = null; }
    win.classList.remove('is-closing');
    win.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    toggle.classList.add('is-open');
    if (reduceMotion) win.classList.add('is-open');
    else window.requestAnimationFrame(function () { window.requestAnimationFrame(function () { win.classList.add('is-open'); }); });
    active.list.scrollTop = active.list.scrollHeight;
    if (focus !== false) active.input.focus();
    if (remember !== false) { try { sessionStorage.setItem(storeKey + ':open', '1'); } catch (e) { /* ignore */ } }
  }

  function closeWin() {
    toggle.setAttribute('aria-expanded', 'false');
    toggle.classList.remove('is-open');
    try { sessionStorage.setItem(storeKey + ':open', '0'); } catch (e) { /* ignore */ }
    if (win.hidden) return;
    if (reduceMotion) { win.classList.remove('is-open'); win.hidden = true; return; }
    win.classList.remove('is-open');
    win.classList.add('is-closing');
    closeTimer = setTimeout(function () { win.classList.remove('is-closing'); win.hidden = true; closeTimer = null; }, 220);
  }

  toggle.addEventListener('click', function () { if (isOpen()) closeWin(); else openWin(); });
  closeBtn.addEventListener('click', function () { closeWin(); toggle.focus(); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && isOpen()) { closeWin(); toggle.focus(); }
  });

  // =============================== Ask: the normal chat ===============================
  var history = [];          // [{ role: 'user' | 'assistant', text: '...' }]

  // storage may be blocked in private windows, so never rely on it
  function load() {
    try { history = JSON.parse(sessionStorage.getItem(storeKey) || '[]') || []; } catch (e) { history = []; }
  }
  function save() {
    try { sessionStorage.setItem(storeKey, JSON.stringify(history.slice(-20))); } catch (e) { /* ignore */ }
  }

  function render() {
    // keep the welcome bubble and chips, rebuild the rest
    Array.prototype.slice.call(askPane.list.querySelectorAll('.ai-msg:not([data-ai-welcome])')).forEach(function (n) { n.remove(); });
    history.forEach(function (m) { askPane.addMessage(m.role === 'user' ? 'user' : 'bot', m.text, m.table).style.animation = 'none'; });   // old messages just appear, only new ones animate
    askPane.setWide(history.some(function (m) { return m.table; }));
    askPane.suggestions.hidden = history.length > 0;
  }

  askPane.onClear = function () {
    history = [];
    save();
    render();
  };

  async function ask(question) {
    question = (question || '').trim();
    if (!question || askPane.busy) return;

    askPane.suggestions.hidden = true;
    askPane.addMessage('user', question);
    var earlier = history.map(function (m) { return { role: m.role, text: m.text }; });   // what the assistant should remember (text only, no tables)
    history.push({ role: 'user', text: question });
    askPane.input.value = '';
    askPane.autosize();
    askPane.setBusy(true);
    var typing = askPane.addTyping();

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
        askPane.addMessage('bot', data.reply, data.table);
        if (data.table) askPane.setWide(true);
      } else {
        history.pop();                          // the question was not answered, so do not remember it
        var failed = askPane.addMessage('error', (data && data.message) || 'The assistant is not available right now. Please reload the page and try again.');
        failed.setAttribute('role', 'alert');
        askPane.input.value = question;         // let the user send it again
      }
    } catch (e) {
      typing.remove();
      history.pop();
      askPane.addMessage('error', 'Could not reach the server. Check your connection and try again.').setAttribute('role', 'alert');
      askPane.input.value = question;
    }
    save();
    askPane.setBusy(false);
  }

  askPane.form.addEventListener('submit', function (e) { e.preventDefault(); ask(askPane.input.value); });
  askPane.input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); ask(askPane.input.value); }
  });
  askPane.suggestions.addEventListener('click', function (e) {
    var chip = e.target.closest('[data-ai-suggestion]');
    if (chip) ask(chip.getAttribute('data-ai-suggestion'));
  });

  // =============================== Command tab (Admin only) ===============================
  // The assistant only PREPARES an action. Nothing is sent until the Admin approves it with a fresh fingerprint or face check.
  // Text and recipients are fixed on the server; this page only shows them and carries the proof.
  if (cmdPane) {
    var cmd = cmdPane;

    var postAction = async function (path, body) {
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
    };

    var cards = [];     // stop functions of the cards on screen, so clearing the tab stops their timers

    // ---- AI-09: the Excel file panel (template picker, download link, file) ----
    var attach = cmd.el.querySelector('[data-ai-part="attach"]');
    var kindSel = attach.querySelector('[data-ai-part="kind"]');
    var channelSel = attach.querySelector('[data-ai-part="channel"]');
    var channelWrap = attach.querySelector('[data-ai-part="channelWrap"]');
    var templateLink = attach.querySelector('[data-ai-part="template"]');
    var fileInput = attach.querySelector('[data-ai-part="file"]');
    var helpEl = attach.querySelector('[data-ai-part="help"]');
    var attachToggle = cmd.form.querySelector('[data-ai-part="attachToggle"]');

    function updateAttach() {
      var accounts = kindSel.value === 'accounts';
      channelWrap.hidden = accounts;
      templateLink.href = actionsBase + '/Template?kind=' + encodeURIComponent(kindSel.value);
      helpEl.textContent = accounts
        ? '1. Download the template and fill one person per row. 2. Attach it here and press send. 3. Check the preview and approve it. Each person gets a temporary password by email.'
        : '1. Download the template and list the employees (Subject and Message are optional). 2. Attach it here. Rows without their own text use the text you type in the box. 3. Check the preview and approve it.';
    }
    function setAttach(open, kind) {
      if (kind) kindSel.value = kind;
      attach.hidden = !open;
      attachToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
      updateAttach();
    }
    kindSel.addEventListener('change', updateAttach);
    attachToggle.addEventListener('click', function () { setAttach(attach.hidden); });
    updateAttach();

    var showProposal = function (data, question, typing) {
      typing.remove();
      if (data.ok && data.card) {
        cmd.addMessage('bot', data.message);
        cmd.list.appendChild(buildActionCard(data.card));
        cmd.setWide(true);
        cmd.list.scrollTop = cmd.list.scrollHeight;
        return true;
      }
      cmd.addMessage('error', data.message || 'I could not prepare that.').setAttribute('role', 'alert');
      if (question) cmd.input.value = question;
      return false;
    };

    var proposeFile = async function (question) {
      var file = fileInput.files && fileInput.files[0];
      if (!file) return;
      var kindText = kindSel.options[kindSel.selectedIndex].text;
      cmd.suggestions.hidden = true;
      cmd.addMessage('user', '[Excel] ' + file.name + ' (' + kindText + ')' + (question ? '\n' + question : ''));
      cmd.input.value = '';
      cmd.autosize();
      cmd.setBusy(true);
      var typing = cmd.addTyping();
      try {
        var form = new FormData();
        form.append('kind', kindSel.value);
        form.append('channel', channelSel.value);
        form.append('request', question || '');
        form.append('file', file);
        var res = await fetch(actionsBase + '/ProposeFile', {
          method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': token() }, body: form
        });
        var data = null;
        try { data = await res.json(); } catch (e) { /* not JSON */ }
        if (!data) throw new Error('The server did not answer as expected (' + res.status + '). Please reload the page.');
        if (showProposal(data, null, typing)) { fileInput.value = ''; setAttach(false); }
      } catch (e) {
        typing.remove();
        cmd.addMessage('error', e.message || 'Could not reach the server.').setAttribute('role', 'alert');
      }
      cmd.setBusy(false);
    };

    var propose = async function (question) {
      cmd.suggestions.hidden = true;
      cmd.addMessage('user', question);
      cmd.input.value = '';
      cmd.autosize();
      cmd.setBusy(true);
      var typing = cmd.addTyping();
      try {
        var data = await postAction('/Propose', { request: question });
        showProposal(data, question, typing);
      } catch (e) {
        typing.remove();
        cmd.addMessage('error', e.message || 'Could not reach the server.').setAttribute('role', 'alert');
        cmd.input.value = question;
      }
      cmd.setBusy(false);
    };

    var buildActionCard = function (card) {
      var wrap = el('div', 'ai-msg ai-msg-bot ai-msg-wide ai-action');
      wrap.setAttribute('dir', 'auto');
      wrap.appendChild(el('div', 'ai-action-head', card.heading));

      var to = el('div', 'ai-action-row');
      to.appendChild(el('b', null, (card.previewRows ? 'From: ' : 'To: ')));
      to.appendChild(document.createTextNode(card.audience + ' (' + card.recipientCount + ')'));
      wrap.appendChild(to);
      if (card.sampleNames && card.sampleNames.length) {
        var more = card.recipientCount - card.sampleNames.length;
        wrap.appendChild(el('div', 'ai-action-names', card.sampleNames.join(', ') + (more > 0 ? ' and ' + more + ' more' : '')));
      }

      // AI-09: an Excel file. The rows were read and checked by the server; this is only a preview of what it will do.
      if (card.previewRows) {
        var shown = card.previewRows.length;
        wrap.appendChild(buildTable({ title: shown < card.recipientCount ? 'First ' + shown + ' of ' + card.recipientCount + ' valid rows' : 'Valid rows', columns: card.previewColumns, rows: card.previewRows }));
      }
      if (card.skipped && card.skipped.length) {
        var sk = el('div', 'ai-action-skipped');
        sk.appendChild(el('div', null, card.skippedCount + ' row' + (card.skippedCount === 1 ? '' : 's') + ' will be skipped:'));
        card.skipped.forEach(function (line) { sk.appendChild(el('div', null, line)); });
        wrap.appendChild(sk);
      }

      if (card.subject || !card.previewRows) {
        var box = el('div', 'ai-action-box');
        if (card.previewRows) box.appendChild(el('div', 'ai-action-names', 'Common text for the rows without their own text:'));
        box.appendChild(el('div', 'ai-action-subject', card.subject));
        box.appendChild(el('div', 'ai-action-body', card.body));
        wrap.appendChild(box);
      }

      if (card.note) wrap.appendChild(el('div', 'ai-action-note', card.note));      // for example: email is not connected, so nothing will really be sent
      var expire = el('div', 'ai-action-expire');
      var status = el('div', 'ai-action-status');
      status.setAttribute('role', 'status');
      var buttons = el('div', 'ai-action-buttons');
      var finished = false, timer = null;

      function say(text, kind) { status.textContent = text || ''; status.className = 'ai-action-status' + (kind ? ' is-' + kind : ''); }
      function stopTimer() { if (timer) { clearInterval(timer); timer = null; } }
      function lockAll() {
        buttons.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
        stopTimer();
      }
      function showRows(result) {
        if (result.rows && result.rows.length && !wrap.querySelector('[data-ai-result]')) {
          var t = buildTable({ title: 'Result', columns: ['Person', 'Result'], rows: result.rows });
          t.setAttribute('data-ai-result', '');
          wrap.appendChild(t);
        }
      }
      function conclude(result) {
        if (result.ok) { finished = true; say(result.message, 'ok'); expire.textContent = ''; lockAll(); wrap.classList.add('is-done'); showRows(result); return; }
        say(result.message || 'It did not work. Please try again.', 'error');
        if (result.state === 'Cancelled' || result.state === 'Failed') { finished = true; expire.textContent = ''; lockAll(); showRows(result); }
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
      cards.push(stopTimer);
      return wrap;
    };

    // "Clear" only cleans the tab. A card that is still waiting simply expires on the server after a few minutes.
    cmd.onClear = function () {
      cards.forEach(function (stop) { stop(); });
      cards = [];
      Array.prototype.slice.call(cmd.list.querySelectorAll('.ai-msg:not([data-ai-welcome])')).forEach(function (n) { n.remove(); });
      cmd.setWide(false);
      cmd.suggestions.hidden = false;
    };

    function submitCommand() {
      var q = (cmd.input.value || '').trim();
      if (cmd.busy) return;
      if (fileInput.files && fileInput.files.length) proposeFile(q);      // an attached Excel file: the text (if any) is a note for the common message
      else if (q) propose(q);
    }
    cmd.form.addEventListener('submit', function (e) { e.preventDefault(); submitCommand(); });
    cmd.input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); submitCommand(); }
    });
    // an example only fills the box: a command is never sent by one click
    cmd.suggestions.addEventListener('click', function (e) {
      var attachChip = e.target.closest('[data-ai-attach]');
      if (attachChip) { setAttach(true, attachChip.getAttribute('data-ai-attach')); fileInput.focus(); return; }
      var chip = e.target.closest('[data-ai-suggestion]');
      if (!chip) return;
      cmd.input.value = chip.getAttribute('data-ai-suggestion');
      cmd.autosize();
      cmd.input.focus();
    });
  }

  load();
  render();
  var savedTab = 'ask';
  try { savedTab = sessionStorage.getItem(storeKey + ':tab') || 'ask'; } catch (e) { /* ignore */ }
  showPane(savedTab === 'command' && cmdPane ? cmdPane : askPane, false);
  var wasOpen = false;
  try { wasOpen = sessionStorage.getItem(storeKey + ':open') === '1'; } catch (e) { /* ignore */ }
  if (wasOpen) {                            // do not steal focus, and do not animate, when a new page loads
    win.hidden = false;
    win.classList.add('is-open');
    toggle.setAttribute('aria-expanded', 'true');
    toggle.classList.add('is-open');
    active.list.scrollTop = active.list.scrollHeight;
  }
})();
