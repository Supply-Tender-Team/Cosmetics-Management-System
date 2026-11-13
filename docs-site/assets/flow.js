(function () {
  const { actors, cycles } = FLOW;
  const $ = id => document.getElementById(id);
  const COL = 210, ROW = 100, TOP = 40;
  let cy = cycles[0], cur = 0, timer = null;

  // ---- cycle chips ----
  for (const c of cycles) {
    const b = document.createElement('button');
    b.className = 'chip'; b.textContent = c.title; b.dataset.id = c.id;
    b.onclick = () => choose(c.id);
    $('cycles').appendChild(b);
  }

  function choose(id) {
    stop();
    cy = cycles.find(c => c.id === id) || cycles[0];
    cur = 0;
    history.replaceState(null, '', '#' + cy.id);
    document.querySelectorAll('#cycles .chip').forEach(b => {
      const on = b.dataset.id === cy.id;
      b.classList.toggle('on', on);
      b.style.background = on ? '#be185d' : '#fff';
      b.style.color = on ? '#fff' : '#0f172a';
    });
    $('story').innerHTML = '<b>Story:</b> ' + cy.story;
    buildFrame();
    render();
    $('scroller').scrollTop = 0;
  }

  // ---- fixed part: actor boxes + lifelines ----
  function buildFrame() {
    const n = cy.actors.length;
    $('actors').style.gridTemplateColumns = `repeat(${n}, 1fr)`;
    $('actors').innerHTML = cy.actors.map(k =>
      `<div class="actor" style="border-color:${actors[k].color};color:${actors[k].color}">${actors[k].name}</div>`).join('');
  }

  const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const x = k => (cy.actors.indexOf(k) + .5) * COL;

  function render() {
    const n = cy.actors.length, W = n * COL, H = TOP + cy.steps.length * ROW + 30;
    const svg = $('svg');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    let h = '';

    // lifelines
    cy.actors.forEach(k => {
      h += `<line x1="${x(k)}" y1="0" x2="${x(k)}" y2="${H}" stroke="${actors[k].color}" stroke-opacity=".35" stroke-width="3" stroke-dasharray="3 9" stroke-linecap="round"/>`;
    });

    cy.steps.forEach((st, i) => {
      const y = TOP + i * ROW + ROW / 2;
      const dir = x(st.to) > x(st.from) ? 1 : -1;
      const x1 = x(st.from) + dir * 6, x2 = x(st.to) - dir * 6;
      const col = actors[st.from].color;
      const active = i === cur, shown = i <= cur;
      const op = shown ? 1 : .13;
      const sw = active ? 6 : 3.5;
      const stroke = active ? '#be185d' : (shown ? col : '#94a3b8');
      const mid = (x1 + x2) / 2;

      h += `<g class="step" data-i="${i}" style="cursor:pointer" opacity="${op}">`;
      h += `<rect x="${Math.min(x1, x2) - 40}" y="${y - ROW / 2 + 6}" width="${Math.abs(x2 - x1) + 80}" height="${ROW - 12}" fill="${active ? '#fdf2f8' : 'transparent'}" rx="14"/>`;
      h += `<line x1="${x1}" y1="${y}" x2="${x2 - dir * 12}" y2="${y}" stroke="${stroke}" stroke-width="${sw}" stroke-linecap="round" ${shown ? '' : 'stroke-dasharray="6 8"'}/>`;
      h += `<polygon points="${x2},${y} ${x2 - dir * 20},${y - 11} ${x2 - dir * 20},${y + 11}" fill="${stroke}"/>`;
      h += `<circle cx="${x1}" cy="${y}" r="16" fill="${stroke}"/><text x="${x1}" y="${y}" text-anchor="middle" dominant-baseline="central" font-size="17" font-weight="800" fill="#fff">${i + 1}</text>`;
      h += `<text x="${mid}" y="${y - 20}" text-anchor="middle" font-size="${active ? 19 : 17}" font-weight="${active ? 800 : 700}" fill="${active ? '#be185d' : '#0f172a'}" stroke="#fff" stroke-width="7" paint-order="stroke" font-family="'JetBrains Mono', Consolas, monospace">${esc(st.label)}</text>`;
      if (active) {
        h += `<circle r="10" fill="#be185d"><animateMotion dur="1.8s" repeatCount="indefinite" path="M${x1},${y} L${x2},${y}"/></circle>`;
      }
      h += '</g>';
    });
    svg.innerHTML = h;
    svg.querySelectorAll('.step').forEach(g => g.onclick = () => go(+g.dataset.i));

    renderPanel();
    $('count').textContent = `Step ${cur + 1} of ${cy.steps.length}`;
    $('prev').disabled = cur === 0;
    $('next').disabled = cur === cy.steps.length - 1;

    // keep the active arrow in view
    const sc = $('scroller'), scale = sc.clientWidth / W;
    const yy = (TOP + cur * ROW + ROW / 2) * scale + 60;
    sc.scrollTo({ top: Math.max(0, yy - sc.clientHeight / 2), behavior: 'smooth' });
  }

  function renderPanel() {
    const st = cy.steps[cur], a = actors[st.from], b = actors[st.to];
    const pretty = Object.keys(st.payload).length ? JSON.stringify(st.payload, null, 2) : '(no body)';
    $('panel').style.borderColor = '#be185d';
    $('panel').innerHTML = `
      <div class="stepno">Step ${cur + 1} / ${cy.steps.length}</div>
      <h2>${esc(st.label)}</h2>
      <div class="route">
        <span class="pill" style="background:${a.color}">${esc(a.name)}</span>
        <b>&rarr;</b>
        <span class="pill" style="background:${b.color}">${esc(b.name)}</span>
      </div>
      <div class="lab">What happens</div>
      <p>${esc(st.what)}</p>
      <div class="lab">What is inside the request</div>
      <pre class="json">${esc(pretty)}</pre>
      ${st.db ? `<div class="lab">Database</div><div class="dbline">${esc(st.db)}</div>` : ''}
      ${st.status ? `<div class="lab">Status change</div><div class="status">${esc(st.status)}</div>` : ''}
      <div class="dots">${cy.steps.map((_, i) => `<button data-i="${i}" class="${i === cur ? 'cur' : i < cur ? 'done' : ''}">${i + 1}</button>`).join('')}</div>`;
    $('panel').querySelectorAll('.dots button').forEach(btn => btn.onclick = () => go(+btn.dataset.i));
  }

  function go(i) { cur = Math.max(0, Math.min(cy.steps.length - 1, i)); render(); }
  function stop() { if (timer) { clearInterval(timer); timer = null; } $('play').innerHTML = '&#9654; Auto-play'; }

  $('next').onclick = () => { stop(); go(cur + 1); };
  $('prev').onclick = () => { stop(); go(cur - 1); };
  $('reset').onclick = () => { stop(); go(0); };
  $('play').onclick = () => {
    if (timer) return stop();
    if (cur === cy.steps.length - 1) go(0);
    $('play').innerHTML = '&#10074;&#10074; Pause';
    timer = setInterval(() => { if (cur >= cy.steps.length - 1) return stop(); go(cur + 1); }, 4200);
  };
  document.addEventListener('keydown', e => {
    if (e.key === 'ArrowRight') { stop(); go(cur + 1); }
    if (e.key === 'ArrowLeft') { stop(); go(cur - 1); }
  });
  window.addEventListener('resize', () => render());

  choose(location.hash.slice(1) || cycles[0].id);
})();
