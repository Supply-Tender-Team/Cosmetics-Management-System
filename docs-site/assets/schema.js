(function () {
  const { domains, tables, byName, relations } = DB;
  const $ = id => document.getElementById(id);
  const NS = 'http://www.w3.org/2000/svg';
  const W = 320, HEAD = 38, ROW = 28, COLGAP = 190, VGAP = 50;

  // ---------- layout: columns that follow the business flow ----------
  const COLUMNS = [
    ['Departments', 'AspNetUsers', 'AspNetRoles', 'AspNetUserRoles'],
    ['EmployeeProfiles', 'SupplierProfiles', 'SupplierDocuments', 'BiometricCredentials', 'FaceEnrollments', 'BiometricAuthLogs'],
    ['PurchaseRequisitions', 'PurchaseRequisitionItems', 'Tenders'],
    ['TenderSuppliers', 'Invitations', 'EvaluationCriteria', 'CommitteeMembers'],
    ['Bids', 'BidItems', 'BidDocuments', 'ComplianceChecks'],
    ['TechnicalEvaluations', 'FinancialEvaluations', 'AiBidAnalyses', 'EvaluationReports'],
    ['TenderApprovals', 'TopCommitteeVotes', 'Clarifications', 'ClarificationMessages'],
    ['Notifications', 'AuditLogs', 'DocumentAccessLogs'],
  ];
  const height = t => HEAD + t.cols.length * ROW;
  const pos = {};
  COLUMNS.forEach((col, ci) => {
    let y = 0;
    col.forEach(n => { pos[n] = { x: ci * (W + COLGAP), y }; y += height(byName[n]) + VGAP; });
  });
  const missing = tables.filter(t => !pos[t.name]);
  if (missing.length) console.warn('Tables without a position:', missing.map(t => t.name));

  // ---------- build table boxes ----------
  const world = $('world'), svg = $('lines'), vp = $('viewport');
  const box = {};
  for (const t of tables) {
    const d = domains[t.domain];
    const el = document.createElement('div');
    el.className = 'tbl';
    el.style.borderColor = d.color;
    el.innerHTML = `<header style="background:${d.color}" title="${t.desc.replace(/"/g, '&quot;')}">${t.name}</header>` +
      t.cols.map(c => `<div class="row ${c.pk ? 'pk' : ''} ${c.fk ? 'fk' : ''}"><span class="n">${c.pk ? '<span class="k pk">PK</span>' : ''}${c.fk ? '<span class="k fk">FK</span>' : ''}${c.uq && !c.pk ? '<span class="k uq">UQ</span>' : ''}${c.name}${c.nullable ? '<span style="color:#94a3b8">?</span>' : ''}</span><span class="t">${c.type}</span></div>`).join('');
    world.appendChild(el);
    box[t.name] = el;
  }

  // ---------- legend ----------
  $('legend').innerHTML = Object.values(domains).map(d => `<span><i style="background:${d.color}"></i>${d.label}</span>`).join('');

  // ---------- state ----------
  let s = 1, tx = 0, ty = 0, selected = null;

  function applyView() {
    world.style.transform = `translate(${tx}px,${ty}px) scale(${s})`;
    $('zoomHint').textContent = Math.round(s * 100) + '%  zoom';
  }
  function place() { for (const t of tables) { box[t.name].style.left = pos[t.name].x + 'px'; box[t.name].style.top = pos[t.name].y + 'px'; } }

  // ---------- lines with crow's foot ----------
  function el(tag, attrs, parent) { const e = document.createElementNS(NS, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); parent.appendChild(e); return e; }

  function anchors(r) {
    const child = byName[r.child], parent = byName[r.parent];
    const ci = child.cols.findIndex(c => c.name === r.childCol);
    const pi = Math.max(0, parent.cols.findIndex(c => c.name === r.parentCol));
    const cp = pos[r.child], pp = pos[r.parent];
    const cy = cp.y + HEAD + ci * ROW + ROW / 2, py = pp.y + HEAD + pi * ROW + ROW / 2;
    let p1, p2, d1, d2;
    if (cp.x + W + 20 < pp.x) { p1 = { x: cp.x + W, y: cy }; d1 = 1; p2 = { x: pp.x, y: py }; d2 = -1; }
    else if (pp.x + W + 20 < cp.x) { p1 = { x: cp.x, y: cy }; d1 = -1; p2 = { x: pp.x + W, y: py }; d2 = 1; }
    else { p1 = { x: cp.x + W, y: cy }; d1 = 1; p2 = { x: pp.x + W, y: py }; d2 = 1; }  // same column: loop on the right
    return { p1, p2, d1, d2 };
  }

  function draw() {
    svg.innerHTML = '';
    const hideUsers = $('noUsers').checked;
    const sel = selected;
    const selColor = sel ? domains[byName[sel].domain].color : null;
    const list = relations.filter(r => !(hideUsers && r.parent === 'AspNetUsers' && !(sel && (r.child === sel || r.parent === sel))));

    // faint lines first, highlighted ones on top
    const sorted = [...list].sort((a, b) => hl(a) - hl(b));
    function hl(r) { return sel && (r.child === sel || r.parent === sel) ? 1 : 0; }

    for (const r of sorted) {
      const on = hl(r) === 1;
      const { p1, p2, d1, d2 } = anchors(r);
      const k = Math.max(60, Math.abs(p2.x - p1.x) / 2);
      const color = on ? selColor : (sel ? '#cbd5e1' : domains[byName[r.child].domain].color);
      const g = el('g', { opacity: sel ? (on ? 1 : .35) : .75 }, svg);
      el('path', {
        d: `M${p1.x},${p1.y} C${p1.x + d1 * k},${p1.y} ${p2.x + d2 * k},${p2.y} ${p2.x},${p2.y}`,
        fill: 'none', stroke: color, 'stroke-width': on ? 3.2 : 1.7, 'stroke-linecap': 'round',
      }, g);
      // child end: crow's foot (many) or double bar (one-to-one)
      const sw = on ? 3 : 1.8;
      if (r.oneToOne) bars(g, p1, d1, color, sw); else foot(g, p1, d1, color, sw);
      // parent end: double bar (exactly one)
      bars(g, p2, d2, color, sw);
    }
  }
  function foot(g, p, d, color, sw) {
    const a = { x: p.x + d * 16, y: p.y };
    for (const dy of [-8, 0, 8]) el('line', { x1: a.x, y1: a.y, x2: p.x, y2: p.y + dy, stroke: color, 'stroke-width': sw, 'stroke-linecap': 'round' }, g);
  }
  function bars(g, p, d, color, sw) {
    for (const off of [8, 15]) el('line', { x1: p.x + d * off, y1: p.y - 8, x2: p.x + d * off, y2: p.y + 8, stroke: color, 'stroke-width': sw, 'stroke-linecap': 'round' }, g);
  }

  // ---------- selection ----------
  function related(name) {
    return { out: relations.filter(r => r.child === name), inn: relations.filter(r => r.parent === name) };
  }
  function select(name, center) {
    selected = name;
    const names = new Set(name ? [name] : []);
    if (name) { const { out, inn } = related(name); out.forEach(r => names.add(r.parent)); inn.forEach(r => names.add(r.child)); }
    for (const t of tables) {
      box[t.name].classList.toggle('sel', t.name === name);
      box[t.name].classList.toggle('dim', !!name && !names.has(t.name));
    }
    renderInfo();
    draw();
    if (center && name) focusOn([...names]);
  }

  function renderInfo() {
    const p = $('info');
    if (!selected) { p.hidden = true; return; }
    const t = byName[selected], d = domains[t.domain], { out, inn } = related(selected);
    p.hidden = false; p.style.borderColor = d.color;
    const btn = (n, a, b) => `<button class="rel" data-t="${n}" style="border-left:7px solid ${domains[byName[n].domain].color}"><b>${n}</b><span>${a}</span><span>${b}</span></button>`;
    p.innerHTML = `
      <span class="badge" style="background:${d.color}">${d.label}</span>
      <h2 style="color:${d.color}">${t.name}</h2>
      <p>${t.desc}</p>
      <h3>Depends on (${out.length})</h3>
      ${out.length ? out.map(r => btn(r.parent, `via <b>${r.childCol}</b> &middot; many ${t.name} &rarr; one ${r.parent}`, r.oneToOne ? 'one-to-one' : (r.nullable ? 'optional link' : 'required link'))).join('') : '<p>Nothing &mdash; root table.</p>'}
      <h3>Used by (${inn.length})</h3>
      ${inn.length ? inn.map(r => btn(r.child, `via <b>${r.child}.${r.childCol}</b>`, `one ${t.name} &rarr; ${r.oneToOne ? 'one' : 'many'} ${r.child}`)).join('') : '<p>No table points here.</p>'}`;
    p.querySelectorAll('.rel').forEach(b => b.onclick = () => select(b.dataset.t, true));
  }

  // ---------- pan / zoom ----------
  function bounds(names) {
    let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
    for (const n of names) { const p = pos[n], h = height(byName[n]); x0 = Math.min(x0, p.x); y0 = Math.min(y0, p.y); x1 = Math.max(x1, p.x + W); y1 = Math.max(y1, p.y + h); }
    return { x0, y0, x1, y1 };
  }
  function focusOn(names) {
    const b = bounds(names), pad = 60;
    const availW = vp.clientWidth - (selected ? 390 : 0);
    s = Math.min(1.1, (availW - pad) / (b.x1 - b.x0), (vp.clientHeight - pad) / (b.y1 - b.y0));
    tx = (availW - (b.x1 - b.x0) * s) / 2 - b.x0 * s;
    ty = (vp.clientHeight - (b.y1 - b.y0) * s) / 2 - b.y0 * s;
    applyView();
  }
  function fit() { selected = null; select(null); focusOn(tables.map(t => t.name)); }
  function zoomAt(f, cx, cy) {
    const ns = Math.max(.1, Math.min(2, s * f));
    tx = cx - (cx - tx) * (ns / s); ty = cy - (cy - ty) * (ns / s); s = ns; applyView();
  }

  vp.addEventListener('wheel', e => {
    e.preventDefault();
    const r = vp.getBoundingClientRect();
    zoomAt(e.deltaY < 0 ? 1.12 : 1 / 1.12, e.clientX - r.left, e.clientY - r.top);
  }, { passive: false });

  // drag: background = pan, header = move table, click = select
  let drag = null;
  vp.addEventListener('pointerdown', e => {
    if (e.target.closest('#info')) return;
    const hdr = e.target.closest('.tbl header');
    const tbl = e.target.closest('.tbl');
    drag = { x: e.clientX, y: e.clientY, tx, ty, moved: false, hdr: hdr ? hdr.parentNode : null, tbl, ox: 0, oy: 0 };
    if (hdr) { const n = tables.find(t => box[t.name] === hdr.parentNode).name; drag.name = n; drag.ox = pos[n].x; drag.oy = pos[n].y; }
    vp.setPointerCapture(e.pointerId);
    if (!hdr) vp.classList.add('panning');
  });
  vp.addEventListener('pointermove', e => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 4) drag.moved = true;
    if (!drag.moved) return;
    if (drag.name) { pos[drag.name].x = drag.ox + dx / s; pos[drag.name].y = drag.oy + dy / s; place(); draw(); }
    else { tx = drag.tx + dx; ty = drag.ty + dy; applyView(); }
  });
  vp.addEventListener('pointerup', () => {
    if (!drag) return;
    vp.classList.remove('panning');
    if (!drag.moved) {
      const n = drag.tbl ? tables.find(t => box[t.name] === drag.tbl).name : null;
      select(n && n !== selected ? n : null);
    }
    drag = null;
  });

  // ---------- toolbar ----------
  $('fit').onclick = fit;
  $('zin').onclick = () => zoomAt(1.25, vp.clientWidth / 2, vp.clientHeight / 2);
  $('zout').onclick = () => zoomAt(1 / 1.25, vp.clientWidth / 2, vp.clientHeight / 2);
  $('clear').onclick = () => select(null);
  $('noUsers').onchange = draw;
  $('search').oninput = e => {
    const q = e.target.value.trim().toLowerCase();
    if (!q) return;
    const t = tables.find(t => t.name.toLowerCase().startsWith(q)) || tables.find(t => t.name.toLowerCase().includes(q));
    if (t) select(t.name, true);
  };
  window.addEventListener('resize', () => { if (!selected) focusOn(tables.map(t => t.name)); });

  place(); draw(); applyView();
  const initial = new URLSearchParams(location.search).get('t');
  requestAnimationFrame(() => { fit(); if (byName[initial]) select(initial, true); });
})();
