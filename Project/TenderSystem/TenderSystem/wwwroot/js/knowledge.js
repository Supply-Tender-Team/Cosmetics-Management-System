/* Admin page "Assistant documents" (Views/Knowledge/Index.cshtml): delete confirmations, an upload note, and the "Try a question" box. */
(function () {
  'use strict';

  // a form with data-confirm asks first
  document.querySelectorAll('form[data-confirm]').forEach(function (form) {
    form.addEventListener('submit', function (e) {
      if (!window.confirm(form.getAttribute('data-confirm'))) e.preventDefault();
    });
  });

  // indexing a long document takes a while: show that something is happening and stop a second click
  var uploadButton = document.getElementById('kbUploadButton');
  if (uploadButton && uploadButton.form) {
    uploadButton.form.addEventListener('submit', function () {
      if (!uploadButton.form.checkValidity()) return;
      document.getElementById('kbUploadNote').hidden = false;
      setTimeout(function () { uploadButton.disabled = true; }, 0);
    });
  }

  var box = document.getElementById('kbSearch');
  if (!box) return;
  var results = document.getElementById('kbResults');

  function line(text, className) {
    var p = document.createElement('p');
    p.className = className || 'mb-1';
    p.textContent = text;
    return p;
  }

  async function search() {
    var question = document.getElementById('kbQuestion').value.trim();
    if (!question) return;
    results.textContent = '';
    results.appendChild(line('Searching…', 'text-muted'));
    try {
      var res = await fetch(box.getAttribute('data-url'), {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': box.querySelector('input[name="__RequestVerificationToken"]').value },
        body: JSON.stringify({ question: question, role: document.getElementById('kbSearchRole').value })
      });
      var data = await res.json();
      results.textContent = '';
      if (!res.ok) { results.appendChild(line(data.message || 'The search failed.', 'text-danger')); return; }
      if (!data.hits.length) {
        results.appendChild(line('Nothing found for this role. The assistant would say it did not find the answer in the company documents.', 'text-muted'));
        return;
      }
      data.hits.forEach(function (h) {
        var card = document.createElement('div');
        card.className = 'border rounded p-2 mb-2';
        var head = document.createElement('strong');
        head.textContent = h.title + ' · passage ' + h.position + ' · score ' + h.score.toFixed(2);
        card.appendChild(head);
        card.appendChild(line(h.text, 'mb-0 small'));
        results.appendChild(card);
      });
    } catch (e) {
      results.textContent = '';
      results.appendChild(line('Could not reach the server.', 'text-danger'));
    }
  }

  document.getElementById('kbSearchButton').addEventListener('click', search);
  document.getElementById('kbQuestion').addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); search(); } });
})();
