/*
 * Powder: a small particle engine for loose powder, puffs and glitter.
 * One fixed full-screen canvas; the frame loop runs only while particles are alive.
 *   Powder.burst(x, y, options)  — a puff at viewport coordinates
 *   Powder.trail(x, y)           — a few grains (pointer trail)
 *   Powder.setAmbient(on)        — slow floating motes over the hero
 */
(function () {
  'use strict';

  const canvas = document.querySelector('.powder-canvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');

  const PALETTES = {
    blush: ['#E6B6B2', '#C98F91', '#B5777A', '#D8C1AE', '#FAF5F0', '#A86A6E'],
    berry: ['#722F37', '#C98F91', '#A9505A', '#E6B6B2'],
    bronze: ['#805B52', '#D8C1AE', '#C99A86', '#E2D5CF'],
  };
  const MAX = 1600;

  let dpr = 1, w = 0, h = 0;
  const parts = [];
  let running = false;
  let ambient = false;
  let ambientBox = null;

  // Pre-rendered soft sprites, one per colour, so every frame is just drawImage.
  const spriteCache = new Map();
  function sprite(color, hard) {
    const key = color + (hard ? 'h' : 's');
    if (spriteCache.has(key)) return spriteCache.get(key);
    const s = document.createElement('canvas');
    s.width = s.height = 64;
    const c = s.getContext('2d');
    const g = c.createRadialGradient(32, 32, 0, 32, 32, 32);
    g.addColorStop(0, color);
    g.addColorStop(hard ? 0.55 : 0.25, color + (hard ? 'ee' : '99'));
    g.addColorStop(1, color + '00');
    c.fillStyle = g;
    c.fillRect(0, 0, 64, 64);
    spriteCache.set(key, s);
    return s;
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    w = window.innerWidth;
    h = window.innerHeight;
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  const rand = (a, b) => a + Math.random() * (b - a);
  const pick = (arr) => arr[(Math.random() * arr.length) | 0];

  function add(p) {
    if (parts.length >= MAX) parts.shift();
    parts.push(p);
    if (!running) { running = true; requestAnimationFrame(tick); }
  }

  /**
   * options: count, power, colors (palette name or array), angle, spread,
   *          cloud (0..1 share of big soft puffs), glitter (count), gravity
   */
  function burst(x, y, o = {}) {
    if (reduced.matches) return;
    const colors = Array.isArray(o.colors) ? o.colors : PALETTES[o.colors || 'blush'];
    const count = o.count ?? 90;
    const power = o.power ?? 7;
    const angle = o.angle ?? -Math.PI / 2;
    const spread = o.spread ?? Math.PI * 2;
    const cloud = o.cloud ?? 0.35;
    const gravity = o.gravity ?? 0.035;

    for (let i = 0; i < count; i++) {
      const isCloud = Math.random() < cloud;
      const a = angle + rand(-spread / 2, spread / 2);
      const v = power * (isCloud ? rand(0.15, 0.6) : rand(0.3, 1.25));
      add({
        x: x + rand(-6, 6), y: y + rand(-6, 6),
        vx: Math.cos(a) * v, vy: Math.sin(a) * v,
        r: isCloud ? rand(18, 44) : rand(1.5, 5),
        grow: isCloud ? rand(0.35, 0.9) : 0,
        drag: isCloud ? 0.955 : 0.965,
        g: isCloud ? -0.006 : gravity,
        life: 0, max: isCloud ? rand(90, 170) : rand(60, 130),
        alpha: isCloud ? rand(0.35, 0.65) : rand(0.75, 1),
        img: sprite(pick(colors), !isCloud),
        seed: Math.random() * 1000,
        sparkle: false,
      });
    }
    const glitter = o.glitter ?? Math.round(count / 5);
    for (let i = 0; i < glitter; i++) {
      const a = angle + rand(-spread / 2, spread / 2);
      const v = power * rand(0.5, 1.5);
      add({
        x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, r: rand(0.8, 2), grow: 0,
        drag: 0.96, g: 0.02, life: 0, max: rand(50, 110), alpha: 1,
        img: null, seed: Math.random() * 1000, sparkle: true,
      });
    }
  }

  function trail(x, y) {
    if (reduced.matches) return;
    for (let i = 0; i < 2; i++) {
      add({
        x: x + rand(-4, 4), y: y + rand(-4, 4), vx: rand(-0.4, 0.4), vy: rand(-0.6, 0.1),
        r: rand(1, 3), grow: 0.02, drag: 0.97, g: 0.012, life: 0, max: rand(40, 70),
        alpha: rand(0.4, 0.8), img: sprite(pick(PALETTES.blush), true), seed: Math.random() * 1000, sparkle: Math.random() < 0.15,
      });
    }
  }

  function spawnMote() {
    if (!ambientBox) return;
    const b = ambientBox();
    if (!b || b.bottom < 0 || b.top > h) return;
    add({
      x: rand(b.left, b.right), y: rand(Math.max(b.top, 0), Math.min(b.bottom, h)),
      vx: rand(-0.15, 0.15), vy: rand(-0.35, -0.08), r: rand(1, 2.8), grow: 0.004,
      drag: 1, g: 0, life: 0, max: rand(220, 420), alpha: rand(0.25, 0.6),
      img: sprite(pick(PALETTES.blush), true), seed: Math.random() * 1000, sparkle: Math.random() < 0.25, mote: true,
    });
  }

  function setAmbient(on, boxFn) {
    ambient = on && !reduced.matches;
    if (boxFn) ambientBox = boxFn;
    if (ambient && !running) { running = true; requestAnimationFrame(tick); }
  }

  let frame = 0;
  function tick() {
    frame++;
    ctx.clearRect(0, 0, w, h);
    if (ambient && frame % 6 === 0 && parts.length < MAX * 0.5) spawnMote();

    for (let i = parts.length - 1; i >= 0; i--) {
      const p = parts[i];
      p.life++;
      if (p.life >= p.max) { parts.splice(i, 1); continue; }
      // gentle curl so clouds swirl instead of moving in straight lines
      const n = Math.sin((p.y + p.seed) * 0.012 + frame * 0.02) * 0.06;
      p.vx = (p.vx + n) * p.drag;
      p.vy = (p.vy + p.g) * p.drag;
      p.x += p.vx;
      p.y += p.vy;
      p.r += p.grow;

      const t = p.life / p.max;
      const fade = p.mote ? Math.sin(t * Math.PI) : (1 - t) * (1 - t * 0.4);
      if (p.sparkle) {
        const tw = 0.5 + 0.5 * Math.sin(p.life * 0.5 + p.seed);
        ctx.globalAlpha = p.alpha * fade * tw;
        ctx.fillStyle = '#FFFFFF';
        const s = p.r * (1.5 + tw * 2.5);
        ctx.fillRect(p.x - s, p.y - 0.5, s * 2, 1);
        ctx.fillRect(p.x - 0.5, p.y - s, 1, s * 2);
      } else {
        ctx.globalAlpha = p.alpha * fade;
        const d = p.r * 2;
        ctx.drawImage(p.img, p.x - p.r, p.y - p.r, d, d);
      }
    }
    ctx.globalAlpha = 1;

    if (parts.length || ambient) requestAnimationFrame(tick);
    else { running = false; ctx.clearRect(0, 0, w, h); }
  }

  resize();
  window.addEventListener('resize', resize);
  window.Powder = { burst, trail, setAmbient, palettes: PALETTES };
})();
