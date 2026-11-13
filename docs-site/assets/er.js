(function () {
  const { domains, tables, byName, relations } = DB;
  const $ = id => document.getElementById(id);
  let current = Object.keys(domains)[0];
  let focus = null;
  const els = {};

  const tabs = $('tabs');
  for (const [key, d] of Object.entries(domains)) {
    const b = document.createElement('button');
    b.className = 'chip'; b.textContent = d.label; b.dataset.k = key;
    b.onclick = () => { current = key; focus = null; render(); };
    tabs.appendChild(b);
  }

  function render() {
    tabs.querySelectorAll('.chip').forEach(b => {
      const on = b.dataset.k === current, c = domains[b.dataset.k].color;
      b.classList.toggle('on', on);
      b.style.background = on ? c : '#fff';
      b.style.color = on ? '#fff' : '#0f172a';
    });

    const own = tables.filter(t => t.domain === current).map(t => t.name);
    // Show the area's own tables plus the outside tables they point to (dashed). Tables that merely
    // point INTO this area are left out, otherwise hubs like AspNetUsers drag in half the database.
    const rels = relations.filter(r => own.includes(r.child));
    const ghosts = [...new Set(rels.flatMap(r => [r.child, r.parent]).filter(n => !own.includes(n)))];

    // order: walk relations so linked entities end up next to each other
    const order = [], seen = new Set();
    const visit = n => {
      if (seen.has(n) || !own.includes(n)) return;
      seen.add(n); order.push(n);
      rels.filter(r => r.child === n || r.parent === n).forEach(r => visit(r.child === n ? r.parent : r.child));
    };
    own.forEach(visit);
    const all = [...order, ...ghosts];
    const cols = all.length <= 4 ? 2 : all.length <= 9 ? 3 : 4;

    const grid = $('grid');
    grid.style.gridTemplateColumns = `repeat(${cols}, 280px)`;
    grid.innerHTML = '';
    for (const n of all) {
      const t = byName[n], d = domains[t.domain], ghost = !own.includes(n);
      const keyCols = t.cols.filter(c => c.pk || c.fk);
      const rest = t.cols.filter(c => !(c.pk || c.fk));
      const shown = [...keyCols, ...rest.slice(0, ghost ? 0 : 3)];
      const hidden = t.cols.length - shown.length;
      const e = document.createElement('div');
      e.className = 'ent' + (ghost ? ' ghost' : '');
      e.style.borderColor = d.color;
      e.innerHTML = `<header style="background:${d.color}">${t.name}</header><ul>` +
        shown.map(c => `<li class="${c.pk ? 'pk' : ''}"><span>${c.pk ? '&#128273; ' : ''}${c.fk ? 'FK ' : ''}${c.name}</span><span>${c.type}</span></li>`).join('') +
        (hidden > 0 ? `<li class="more">+ ${hidden} more columns</li>` : '') + '</ul>';
      e.onclick = () => { focus = focus === n ? null : n; applyFocus(); };
      grid.appendChild(e);
      els[n] = e;
    }
    requestAnimationFrame(() => { drawLines(rels); applyFocus(); });
    state.rels = rels; state.names = all;
  }
  const state = {};

  function drawLines(rels) {
    const svg = $('lines'), stage = $('stage');
    svg.innerHTML = '';
    svg.setAttribute('width', stage.scrollWidth);
    svg.setAttribute('height', stage.scrollHeight);
    for (const r of rels) {
      const a = Draw.relRect(els[r.parent], stage), b = Draw.relRect(els[r.child], stage);
      const k = Draw.connect(a, b);
      const color = domains[byName[r.child].domain].color;
      const g = Draw.el('g', { 'data-a': r.parent, 'data-b': r.child }, svg);
      Draw.el('path', { d: k.d, fill: 'none', stroke: color, 'stroke-width': 2.5 }, g);
      Draw.badge(g, ...Object.values(Draw.nudge(k.p1, k.mid, 24)), '1', color);
      Draw.badge(g, ...Object.values(Draw.nudge(k.p2, k.mid, 24)), r.oneToOne ? '1' : 'N', color);
      // diamond with the relationship name
      const w = Math.max(70, r.verb.length * 7.2), h = 30;
      Draw.el('polygon', {
        points: `${k.mid.x - w / 2 - 14},${k.mid.y} ${k.mid.x},${k.mid.y - h / 2 - 6} ${k.mid.x + w / 2 + 14},${k.mid.y} ${k.mid.x},${k.mid.y + h / 2 + 6}`,
        fill: '#fff', stroke: color, 'stroke-width': 2.5,
      }, g);
      Draw.text(g, k.mid.x, k.mid.y, r.verb, { size: 12, fill: color });
    }
  }

  function applyFocus() {
    const rel = new Set(focus ? [focus] : []);
    if (focus) state.rels.forEach(r => { if (r.child === focus) rel.add(r.parent); if (r.parent === focus) rel.add(r.child); });
    for (const n of state.names) els[n].classList.toggle('dim', !!focus && !rel.has(n));
    $('lines').querySelectorAll('g').forEach(g => {
      const on = !focus || g.dataset.a === focus || g.dataset.b === focus;
      g.style.opacity = on ? 1 : .08;
    });
  }

  window.addEventListener('resize', () => state.rels && drawLines(state.rels));
  render();
})();
