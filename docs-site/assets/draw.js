// Small helpers shared by the Schema and ER pages for drawing connector lines.
window.Draw = (function () {
  const NS = 'http://www.w3.org/2000/svg';

  function relRect(el, cont) {
    const a = el.getBoundingClientRect(), c = cont.getBoundingClientRect();
    return { x: a.left - c.left, y: a.top - c.top, w: a.width, h: a.height };
  }

  // Connect two rects edge-to-edge with a smooth curve.
  function connect(a, b) {
    const ca = { x: a.x + a.w / 2, y: a.y + a.h / 2 };
    const cb = { x: b.x + b.w / 2, y: b.y + b.h / 2 };
    const dx = cb.x - ca.x, dy = cb.y - ca.y;
    let p1, p2, c1, c2;
    if (Math.abs(dx) * 0.55 > Math.abs(dy)) {            // mostly sideways
      const s = dx > 0 ? 1 : -1;
      p1 = { x: s > 0 ? a.x + a.w : a.x, y: ca.y };
      p2 = { x: s > 0 ? b.x : b.x + b.w, y: cb.y };
      const k = Math.max(40, Math.abs(p2.x - p1.x) / 2);
      c1 = { x: p1.x + s * k, y: p1.y }; c2 = { x: p2.x - s * k, y: p2.y };
    } else {                                             // mostly vertical
      const s = dy > 0 ? 1 : -1;
      p1 = { x: ca.x, y: s > 0 ? a.y + a.h : a.y };
      p2 = { x: cb.x, y: s > 0 ? b.y : b.y + b.h };
      const k = Math.max(40, Math.abs(p2.y - p1.y) / 2);
      c1 = { x: p1.x, y: p1.y + s * k }; c2 = { x: p2.x, y: p2.y - s * k };
    }
    const d = `M${p1.x},${p1.y} C${c1.x},${c1.y} ${c2.x},${c2.y} ${p2.x},${p2.y}`;
    // point at t=.5 of the cubic
    const mid = {
      x: (p1.x + 3 * c1.x + 3 * c2.x + p2.x) / 8,
      y: (p1.y + 3 * c1.y + 3 * c2.y + p2.y) / 8,
    };
    return { d, p1, p2, mid };
  }

  function el(tag, attrs, parent) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }

  function text(parent, x, y, str, opts = {}) {
    const t = el('text', {
      x, y, 'text-anchor': 'middle', 'dominant-baseline': 'central',
      'font-size': opts.size || 15, 'font-weight': 700, fill: opts.fill || '#0f172a',
    }, parent);
    t.textContent = str;
    return t;
  }

  // small round badge with a label (cardinality "1" / "N")
  function badge(parent, x, y, str, color) {
    el('circle', { cx: x, cy: y, r: 13, fill: '#fff', stroke: color, 'stroke-width': 2.5 }, parent);
    text(parent, x, y, str, { fill: color, size: 14 });
  }

  function nudge(p, toward, dist) {
    const dx = toward.x - p.x, dy = toward.y - p.y, l = Math.hypot(dx, dy) || 1;
    return { x: p.x + dx / l * dist, y: p.y + dy / l * dist };
  }

  return { relRect, connect, el, text, badge, nudge };
})();
