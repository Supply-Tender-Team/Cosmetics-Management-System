/* Floating AI assistant (Views/Shared/_AiChat.cshtml). Two separate windows:
   - the normal chat (questions). Its conversation lives in this tab only (sessionStorage, one key per user) and is sent to /AiChat/Ask with each question.
   - the Command center (Admin only). Its own conversation, kept only on this page and never stored: the assistant PREPARES an action and nothing is
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

  // ---- one window (used for both the normal chat and the Command center) ----
  function Panel(panelEl, toggleEl) {
    function part(name) { return panelEl.querySelector('[data-ai-part="' + name + '"]'); }
    var p = {
      el: panelEl, toggle: toggleEl,
      list: part('messages'), suggestions: part('suggestions'), form: part('form'), input: part('input'),
      sendBtn: part('send'), wideBtn: part('wide'), closeBtn: part('close'), clearBtn: part('clear'),
      busy: false, closeTimer: null
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

    p.setWide = function (on) {
      panelEl.classList.toggle('ai-panel-wide', on);
      if (p.wideBtn) {
        p.wideBtn.setAttribute('aria-pressed', on ? 'true' : 'false');
        p.wideBtn.querySelector('i').className = on ? 'bi bi-arrows-angle-contract' : 'bi bi-arrows-angle-expand';
        p.wideBtn.title = on ? 'Make the chat narrower' : 'Make the chat wider';
      }
      p.list.scrollTop = p.list.scrollHeight;
    };
    if (p.wideBtn) p.wideBtn.addEventListener('click', function () { p.setWide(!panelEl.classList.contains('ai-panel-wide')); });

    p.isOpen = function () { return !panelEl.hidden && !panelEl.classList.contains('is-closing'); };

    // The window grows out of its button and shrinks back into it (no animation when the user asked for less motion).
    p.open = function (focus) {
      if (p.closeTimer) { clearTimeout(p.closeTimer); p.closeTimer = null; }
      panelEl.classList.remove('is-closing');
      panelEl.hidden = false;
      toggleEl.setAttribute('aria-expanded', 'true');
      toggleEl.classList.add('is-open');
      if (reduceMotion) panelEl.classList.add('is-open');
      else window.requestAnimationFrame(function () { window.requestAnimationFrame(function () { panelEl.classList.add('is-open'); }); });
      p.list.scrollTop = p.list.scrollHeight;
      if (focus !== false) p.input.focus();
    };

    p.close = function () {
      toggleEl.setAttribute('aria-expanded', 'false');
      toggleEl.classList.remove('is-open');
      if (panelEl.hidden) return;
      if (reduceMotion) { panelEl.classList.remove('is-open'); panelEl.hidden = true; return; }
      panelEl.classList.remove('is-open');
      panelEl.classList.add('is-closing');
      p.closeTimer = setTimeout(function () { panelEl.classList.remove('is-closing'); panelEl.hidden = true; p.closeTimer = null; }, 220);
    };

    p.setBusy = function (value) {
      p.busy = value;
      p.sendBtn.disabled = value;
      p.input.disabled = value;
      panelEl.classList.toggle('is-busy', value);
      if (!value) p.input.focus();
    };

    p.autosize = function () {
      p.input.style.height = 'auto';
      p.input.style.height = Math.min(p.input.scrollHeight, 110) + 'px';
      p.input.style.overflowY = p.input.scrollHeight > 110 ? 'auto' : 'hidden';
    };
    p.input.addEventListener('input', p.autosize);

    return p;
  }

  var askPanel = Panel(document.getElementById('aiPanel'), document.getElementById('aiToggle'));
  var cmdPanelEl = document.getElementById('aiCmdPanel');
  var cmdToggle = document.getElementById('aiCmdToggle');
  var cmdPanel = (cmdPanelEl && cmdToggle && actionsBase) ? Panel(cmdPanelEl, cmdToggle) : null;

  // Only one window is open at a time: opening one closes the other.
  function openPanel(p, remember) {
    [askPanel, cmdPanel].forEach(function (other) { if (other && other !== p && other.isOpen()) other.close(); });
    p.open(remember !== false);
    if (p === askPanel && remember !== false) {      // stay open (or closed) when the user moves to another page
      try { sessionStorage.setItem(storeKey + ':open', '1'); } catch (e) { /* ignore */ }
    }
  }
  function closePanel(p) {
    p.close();
    if (p === askPanel) { try { sessionStorage.setItem(storeKey + ':open', '0'); } catch (e) { /* ignore */ } }
  }
  function wire(p) {
    p.toggle.addEventListener('click', function () { if (p.isOpen()) closePanel(p); else openPanel(p); });
    p.closeBtn.addEventListener('click', function () { closePanel(p); p.toggle.focus(); });
  }
  wire(askPanel);
  if (cmdPanel) wire(cmdPanel);
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    [askPanel, cmdPanel].forEach(function (p) { if (p && p.isOpen()) { closePanel(p); p.toggle.focus(); } });
  });

  // =============================== normal chat ===============================
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
    Array.prototype.slice.call(askPanel.list.querySelectorAll('.ai-msg:not([data-ai-welcome])')).forEach(function (n) { n.remove(); });
    history.forEach(function (m) { askPanel.addMessage(m.role === 'user' ? 'user' : 'bot', m.text, m.table).style.animation = 'none'; });   // old messages just appear, only new ones animate
    askPanel.setWide(history.some(function (m) { return m.table; }));     // tables need room, so the window widens by itself
    askPanel.suggestions.hidden = history.length > 0;
  }

  askPanel.clearBtn.addEventListener('click', function () {
    if (askPanel.busy) return;
    history = [];
    save();
    render();
  });

  async function ask(question) {
    question = (question || '').trim();
    if (!question || askPanel.busy) return;

    askPanel.suggestions.hidden = true;
    askPanel.addMessage('user', question);
    var earlier = history.map(function (m) { return { role: m.role, text: m.text }; });   // what the assistant should remember (text only, no tables)
    history.push({ role: 'user', text: question });
    askPanel.input.value = '';
    askPanel.autosize();
    askPanel.setBusy(true);
    var typing = askPanel.addTyping();

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
        askPanel.addMessage('bot', data.reply, data.table);
        if (data.table) askPanel.setWide(true);
      } else {
        history.pop();                          // the question was not answered, so do not remember it
        var failed = askPanel.addMessage('error', (data && data.message) || 'The assistant is not available right now. Please reload the page and try again.');
        failed.setAttribute('role', 'alert');
        askPanel.input.value = question;        // let the user send it again
      }
    } catch (e) {
      typing.remove();
      history.pop();
      askPanel.addMessage('error', 'Could not reach the server. Check your connection and try again.').setAttribute('role', 'alert');
      askPanel.input.value = question;
    }
    save();
    askPanel.setBusy(false);
  }

  askPanel.form.addEventListener('submit', function (e) { e.preventDefault(); ask(askPanel.input.value); });
  askPanel.input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); ask(askPanel.input.value); }
  });
  askPanel.suggestions.addEventListener('click', function (e) {
    var chip = e.target.closest('[data-ai-suggestion]');
    if (chip) ask(chip.getAttribute('data-ai-suggestion'));
  });

  // =============================== Command center (Admin only) ===============================
  // The assistant only PREPARES an action. Nothing is sent until the Admin approves it with a fresh fingerprint or face check.
  // Text and recipients are fixed on the server; this page only shows them and carries the proof.
  if (cmdPanel) {
    var cmd = cmdPanel;

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

    var cards = [];     // timers of the cards on screen, so clearing the window stops them

    var propose = async function (question) {
      cmd.suggestions.hidden = true;
      cmd.addMessage('user', question);
      cmd.input.value = '';
      cmd.autosize();
      cmd.setBusy(true);
      var typing = cmd.addTyping();
      try {
        var data = await postAction('/Propose', { request: question });
        typing.remove();
        if (data.ok && data.card) {
          cmd.addMessage('bot', data.message);
          cmd.list.appendChild(buildActionCard(data.card));
          cmd.setWide(true);
          cmd.list.scrollTop = cmd.list.scrollHeight;
        } else {
          cmd.addMessage('error', data.message || 'I could not prepare that.').setAttribute('role', 'alert');
          cmd.input.value = question;
        }
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
      to.appendChild(el('b', null, 'To: '));
      to.appendChild(document.createTextNode(card.audience + ' (' + card.recipientCount + ')'));
      wrap.appendChild(to);
      var more = card.recipientCount - card.sampleNames.length;
      wrap.appendChild(el('div', 'ai-action-names', card.sampleNames.join(', ') + (more > 0 ? ' and ' + more + ' more' : '')));

      var box = el('div', 'ai-action-box');
      box.appendChild(el('div', 'ai-action-subject', card.subject));
      box.appendChild(el('div', 'ai-action-body', card.body));
      wrap.appendChild(box);

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
      function conclude(result) {
        if (result.ok) { finished = true; say(result.message, 'ok'); expire.textContent = ''; lockAll(); wrap.classList.add('is-done'); return; }
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
      cards.push(stopTimer);
      return wrap;
    };

    // "Clear" only cleans the window. A card that is still waiting simply expires on the server after a few minutes.
    cmd.clearBtn.addEventListener('click', function () {
      if (cmd.busy) return;
      cards.forEach(function (stop) { stop(); });
      cards = [];
      Array.prototype.slice.call(cmd.list.querySelectorAll('.ai-msg:not([data-ai-welcome])')).forEach(function (n) { n.remove(); });
      cmd.setWide(false);
      cmd.suggestions.hidden = false;
    });

    cmd.form.addEventListener('submit', function (e) { e.preventDefault(); var q = (cmd.input.value || '').trim(); if (q && !cmd.busy) propose(q); });
    cmd.input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); var q = (cmd.input.value || '').trim(); if (q && !cmd.busy) propose(q); }
    });
    // an example only fills the box: a command is never sent by one click
    cmd.suggestions.addEventListener('click', function (e) {
      var chip = e.target.closest('[data-ai-suggestion]');
      if (!chip) return;
      cmd.input.value = chip.getAttribute('data-ai-suggestion');
      cmd.autosize();
      cmd.input.focus();
    });
  }

  load();
  render();
  var wasOpen = false;
  try { wasOpen = sessionStorage.getItem(storeKey + ':open') === '1'; } catch (e) { /* ignore */ }
  if (wasOpen) {                            // do not steal focus, and do not animate, when a new page loads
    askPanel.el.hidden = false;
    askPanel.el.classList.add('is-open');
    askPanel.toggle.setAttribute('aria-expanded', 'true');
    askPanel.toggle.classList.add('is-open');
    askPanel.list.scrollTop = askPanel.list.scrollHeight;
  }
})();
