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
    var out = '', listType = '';
    function closeList() { if (listType) { out += '</' + listType + '>'; listType = ''; } }
    lines.forEach(function (line) {
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
    return out.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  }

  function addMessage(kind, text) {
    var div = document.createElement('div');
    div.className = 'ai-msg ai-msg-' + kind;
    div.setAttribute('dir', 'auto');
    if (kind === 'bot') div.innerHTML = format(text); else div.textContent = text;
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
    history.forEach(function (m) { addMessage(m.role === 'user' ? 'user' : 'bot', m.text); });
    suggestions.hidden = history.length > 0;
  }

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

    suggestions.hidden = true;
    addMessage('user', question);
    var earlier = history.slice();            // what the assistant should remember (without this question)
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
        history.push({ role: 'assistant', text: data.reply });
        addMessage('bot', data.reply);
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
  var wasOpen = false;
  try { wasOpen = sessionStorage.getItem(storeKey + ':open') === '1'; } catch (e) { /* ignore */ }
  if (wasOpen) { setOpen(true, false); input.blur(); }   // do not steal focus from the new page
})();
