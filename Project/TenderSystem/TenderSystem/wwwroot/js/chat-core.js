/* The parts that BOTH chats share (the full page assistant.js and the round-button window ai-chat.js): showing text safely, the copy and
   export buttons, and the table under an answer. Each chat passes its own "skin" (its CSS class names), so the look of each chat does not change.
   Load this file BEFORE assistant.js or ai-chat.js. It does nothing by itself. */
(function () {
  'use strict';

  var Core = {};

  // ---- showing text safely: everything is escaped first, then **bold**, lists and tables are drawn ----
  Core.escapeHtml = function (s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); };

  // skin = { wrap: 'class of the table box', table: 'class of the table' }
  Core.format = function (text, skin) {
    var lines = Core.escapeHtml(text).split(/\r?\n/);
    var out = '', listType = '', rows = [];
    function closeList() { if (listType) { out += '</' + listType + '>'; listType = ''; } }
    function closeTable() {
      if (!rows.length) return;
      out += '<div class="' + skin.wrap + '"><table class="' + skin.table + '">';
      rows.forEach(function (cells, i) {
        var tag = i === 0 ? 'th' : 'td';
        out += '<tr>' + cells.map(function (c) { return '<' + tag + '>' + c + '</' + tag + '>'; }).join('') + '</tr>';
      });
      out += '</table></div>';
      rows = [];
    }
    lines.forEach(function (line) {
      if (/^\s*\|.*\|\s*$/.test(line)) {                       // | a | b |  (a table written by the model)
        closeList();
        if (!/^\s*\|[\s:|-]+\|\s*$/.test(line))                // skip the |---|---| line
          rows.push(line.trim().replace(/^\||\|$/g, '').split('|').map(function (c) { return c.trim(); }));
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
  };

  // ---- copy ----
  Core.copyText = function (text) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {          // older browsers, plain http, or a page that is not focused
      var area = document.createElement('textarea');
      area.value = text; area.setAttribute('readonly', ''); area.style.position = 'fixed'; area.style.opacity = '0';
      document.body.appendChild(area); area.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { /* ignore */ }
      area.remove();
      ok ? resolve() : reject(new Error('copy failed'));
    });
  };

  // ---- small buttons ----
  Core.button = function (className, icon, label, onClick) {
    var b = document.createElement('button');
    b.type = 'button'; b.className = className; b.title = label; b.setAttribute('aria-label', label);
    b.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i>';
    b.addEventListener('click', function (e) { e.stopPropagation(); onClick(b); });
    return b;
  };

  // the icon turns into a tick (or a cross) for a moment; doneClass is optional
  Core.flash = function (btn, icon, doneClass) {
    var i = btn.querySelector('i'), old = i.className;
    i.className = 'bi ' + icon;
    if (doneClass) btn.classList.add(doneClass);
    setTimeout(function () { i.className = old; if (doneClass) btn.classList.remove(doneClass); }, 1400);
  };

  Core.copyButton = function (className, getText, doneClass) {
    return Core.button(className, 'bi-clipboard', 'Copy', function (b) {
      Core.copyText(getText()).then(function () { Core.flash(b, 'bi-check2', doneClass); }).catch(function () { Core.flash(b, 'bi-x-lg', doneClass); });
    });
  };

  Core.postJson = function (address, body, token) {
    return fetch(address, {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify(body || {})
    });
  };

  // ---- the table under an answer ----
  // t = { title, columns, rows } from the server. Every cell is set with textContent, so nothing can inject HTML.
  // skin = { wrap, title, tools, table, tool, done } (class names). options = { exportUrl, token }.
  Core.buildTable = function (t, skin, options) {
    var wrap = document.createElement('div');
    wrap.className = skin.wrap;

    var head = document.createElement('div');
    head.className = skin.title;
    var name = document.createElement('span');
    name.textContent = t.title || '';
    head.appendChild(name);
    if (t.rows && t.rows.length) {
      var tools = document.createElement('span');
      tools.className = skin.tools;
      tools.appendChild(Core.copyButton(skin.tool, function () {          // tab separated: pastes into Excel as a grid
        return [t.columns || []].concat(t.rows || []).map(function (r) { return r.join('\t'); }).join('\n');
      }, skin.done));
      tools.appendChild(Core.button(skin.tool, 'bi-file-earmark-excel', 'Export to Excel', function (b) { exportTable(t, b, skin, options); }));
      head.appendChild(tools);
    }
    wrap.appendChild(head);

    var table = document.createElement('table');
    table.className = skin.table;
    var tr = table.insertRow();
    (t.columns || []).forEach(function (c) { var th = document.createElement('th'); th.textContent = c; tr.appendChild(th); });
    (t.rows || []).forEach(function (r) {
      var row = table.insertRow();
      r.forEach(function (c) { row.insertCell().textContent = c; });
    });
    wrap.appendChild(table);
    return wrap;
  };

  async function exportTable(t, btn, skin, options) {
    btn.disabled = true;
    try {
      var res = await Core.postJson(options.exportUrl, { title: t.title, columns: t.columns, rows: t.rows }, options.token);
      if (!res.ok) throw new Error('export failed');
      var blob = await res.blob();
      var a = document.createElement('a');
      a.href = URL.createObjectURL(blob); a.download = 'assistant-table.xlsx';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(function () { URL.revokeObjectURL(a.href); }, 4000);
      Core.flash(btn, 'bi-check2', skin.done);
    } catch (e) { Core.flash(btn, 'bi-x-lg', skin.done); }
    btn.disabled = false;
  }

  window.MsWandChat = Core;
})();
