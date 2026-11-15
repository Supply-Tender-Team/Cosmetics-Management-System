/* AI summary card (Views/Shared/_AiTenderSummary.cshtml). Asks the server what this user may see, and shows nothing if "denied".
   Every text from the server is set as text (never as HTML). */
(function () {
  'use strict';
  if (window.MsWandAiSummary) return;          // the partial may be on a page twice: set the script up only once
  window.MsWandAiSummary = true;

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  function list(items) {
    var ul = el('ul', 'ai-sum-list');
    (items || []).forEach(function (t) { ul.appendChild(el('li', null, t)); });
    return ul;
  }

  function init(root) {
    var tender = parseInt(root.getAttribute('data-tender'), 10);
    var getUrl = root.getAttribute('data-get-url');
    var genUrl = root.getAttribute('data-generate-url');
    var btn = root.querySelector('[data-ai-sum-generate]');
    var stateEl = root.querySelector('[data-ai-sum-state]');
    var body = root.querySelector('[data-ai-sum-body]');
    var tokenEl = root.querySelector('input[name="__RequestVerificationToken"]');
    var busy = false;

    function render(v) {
      if (!v || v.state === 'denied') { root.hidden = true; return; }
      root.hidden = false;
      stateEl.textContent = v.message || '';
      stateEl.className = 'ai-sum-state' + (v.message && v.state === 'ok' ? ' is-note' : '');
      body.textContent = '';
      btn.hidden = v.state !== 'ok';
      btn.disabled = busy || !v.canGenerate;
      btn.textContent = v.summary ? 'Write a new summary' : 'Write summary';

      if (v.state === 'waiting' || v.state === 'off') { root.classList.add('is-quiet'); return; }
      root.classList.remove('is-quiet');
      if (v.factsLine) body.appendChild(el('p', 'ai-sum-facts', 'What the AI was given: ' + v.factsLine));
      if (!v.summary) { body.appendChild(el('p', 'ai-sum-empty', 'No summary has been written yet.')); return; }

      var s = v.summary;
      var meta = el('p', 'ai-sum-meta', 'Written ' + new Date(v.createdAt).toLocaleString() + (v.model ? ' by ' + v.model : '') + ' · ');
      if (v.outdated) meta.appendChild(el('span', 'ai-sum-outdated', 'Outdated: the tender data changed since. Write a new summary.'));
      else meta.appendChild(document.createTextNode('Based on the data as it is now.'));
      body.appendChild(meta);

      body.appendChild(el('p', 'ai-sum-overview', s.overview));

      var wrap = el('div', 'ai-sum-offers');
      (s.offers || []).forEach(function (o) {
        var card = el('article', 'ai-sum-offer');
        card.appendChild(el('h3', null, o.supplier));
        if (o.strengths && o.strengths.length) { card.appendChild(el('h4', null, 'Strengths')); card.appendChild(list(o.strengths)); }
        if (o.weaknesses && o.weaknesses.length) { card.appendChild(el('h4', null, 'Weaknesses')); card.appendChild(list(o.weaknesses)); }
        if (o.mainRisk) { var r = el('p', 'ai-sum-risk'); r.appendChild(el('b', null, 'Main risk: ')); r.appendChild(document.createTextNode(o.mainRisk)); card.appendChild(r); }
        wrap.appendChild(card);
      });
      body.appendChild(wrap);

      if (s.pointsToCheck && s.pointsToCheck.length) {
        body.appendChild(el('h4', 'ai-sum-sub', 'Points to check before deciding'));
        body.appendChild(list(s.pointsToCheck));
      }

      var lean = el('div', 'ai-sum-lean');
      lean.appendChild(el('b', null, 'AI leaning (a suggestion to check, not a decision): '));
      lean.appendChild(document.createTextNode(s.leaningSupplier ? s.leaningSupplier + '. ' + (s.leaningReason || '') : 'No clear best offer. ' + (s.leaningReason || '')));
      body.appendChild(lean);
    }

    async function call(url, options) {
      var res = await fetch(url, options);
      var data = null;
      try { data = await res.json(); } catch (e) { /* not JSON */ }
      if (!data) throw new Error('The server did not answer as expected (' + res.status + '). Please reload the page.');
      return data;
    }

    async function load() {
      try { render(await call(getUrl + '?tenderId=' + tender, { credentials: 'same-origin' })); }
      catch (e) { root.hidden = true; }
    }

    btn.addEventListener('click', async function () {
      if (busy) return;
      busy = true;
      btn.disabled = true;
      root.classList.add('is-busy');
      stateEl.className = 'ai-sum-state';
      stateEl.textContent = 'Writing the summary. A local model can take a few minutes, you can keep this page open...';
      try {
        render(await call(genUrl, {
          method: 'POST', credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': tokenEl ? tokenEl.value : '' },
          body: JSON.stringify({ tenderId: tender })
        }));
      } catch (e) {
        stateEl.className = 'ai-sum-state is-error';
        stateEl.textContent = e.message || 'Could not reach the server.';
      } finally {
        busy = false;
        root.classList.remove('is-busy');
        btn.disabled = false;
      }
    });

    load();
  }

  document.querySelectorAll('[data-ai-summary]').forEach(init);
})();
