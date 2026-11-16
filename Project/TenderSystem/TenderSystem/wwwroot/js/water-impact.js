(() => {
  const root = document.querySelector('.water-impact');
  const cv = document.createElement('canvas');
  root.appendChild(cv);
  const ctx = cv.getContext('2d');

  const W = 320, H = 200;      // إحداثيات منطقية
  const WL = 152;              // خط سطح الماء
  const CX = 160;              // مركز الارتطام
  const K = .15;               // انضغاط المنظور للموجات (دوائر -> قطوع ناقصة)
  const G = 480;               // الجاذبية
  const CYCLE = 4.8;           // مدة الدورة بالثواني
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const rnd = (a, b) => a + Math.random() * (b - a);
  let scale = 1;

  function resize() {
    const r = root.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    cv.width = Math.round(r.width * dpr);
    cv.height = Math.round(r.height * dpr);
    scale = cv.width / W;
  }

  /* ---------- الحالة ---------- */
  let drop, parts = [], ripples = [], flash = null, cycle = 0;

  function startDrop() {
    drop = { y: 64, vy: 0, t: 0, a: 0, on: true, hit: false };
  }

  const DROP_W = 40, DROP_H = 56;

  function addRipple(x, y, R, life, amp, w, t0 = 0) {
    ripples.push({ x, y, R, life, amp, w, t: t0 });
  }

  /* ---------- الارتطام: قطرات فقط ---------- */
  function splash(v) {
    const power = Math.min(1, v / 300);

    // قطرة الماء المركزية (ترتفع عمودياً ثم تنزل مكانها)
    parts.push({ x: CX, y: WL - 2, vx: rnd(-4, 4), vy: -(170 + 60 * power), r: 3.8,
                 ly: WL, d: .05, b: 0 });

    // قطرات رئيسية بأحجام مختلفة: كثير صغير، قليل كبير
    for (let i = 0; i < 16; i++) {
      const side = Math.random() < .5 ? -1 : 1;
      const th = side * (.12 + Math.pow(Math.random(), .8) * 1.1);
      const sp = rnd(150, 300) * (1 - Math.abs(th) * .18) * (.85 + .25 * power);
      const r  = 1.1 + Math.pow(Math.random(), 2.4) * 3;
      parts.push({
        x: CX + Math.sin(th) * rnd(8, 20), y: WL - 2,
        vx: Math.sin(th) * sp * .9, vy: -Math.cos(th) * sp,
        r, ly: WL + rnd(-3, 6), d: rnd(0, .05), b: 0
      });
    }

    // رذاذ ناعم
    for (let i = 0; i < 14; i++) {
      const th = rnd(-1.3, 1.3), sp = rnd(220, 380);
      parts.push({
        x: CX + Math.sin(th) * rnd(10, 24), y: WL - 3,
        vx: Math.sin(th) * sp, vy: -Math.cos(th) * sp * rnd(.6, 1),
        r: rnd(.5, 1.0), ly: WL + rnd(-2, 8), d: rnd(0, .06), b: 1
      });
    }

    // موجات متتابعة
    for (let i = 0; i < 4; i++) addRipple(CX, WL, 128, 2.7, 1 - i * .16, 1.7 - i * .25, -i * .15);
    flash = { t: 0 };
  }

  /* ---------- التحديث ---------- */
  function update(dt) {
    cycle += dt;
    if (cycle > CYCLE) { cycle = 0; startDrop(); }

    if (drop && drop.on) {
      drop.t += dt;
      drop.a = Math.min(1, drop.t / .2);
      drop.vy += G * dt;
      drop.y += drop.vy * dt;
      if (!drop.hit && drop.y >= WL) { drop.hit = true; splash(drop.vy); }
      if (drop.y - DROP_H > WL + 4) drop.on = false;
    }

    for (const p of parts) {
      if (p.d > 0) { p.d -= dt; continue; }
      p.vy += G * dt;
      p.x += p.vx * dt;
      p.y += p.vy * dt;
      if (p.vy > 0 && p.y >= p.ly) {
        addRipple(p.x, p.ly, p.b ? 4 : 6 + p.r * 4, p.b ? .5 : .85, p.b ? .3 : .6, .8);
        if (p.r > 3 && !p.b2) {            // القطرات الكبيرة ترتد ارتداد خفيف
          p.b2 = 1; p.vy = -Math.abs(p.vy) * .22; p.vx *= .5; p.r *= .6; p.y = p.ly - .5;
        } else p.dead = true;
      }
    }
    parts = parts.filter(p => !p.dead);

    for (const r of ripples) r.t += dt;
    ripples = ripples.filter(r => r.t < r.life);

    if (flash) { flash.t += dt; if (flash.t > .3) flash = null; }
  }

  /* ---------- الرسم ---------- */
  function dropPath(x, yb, w, h) {
    const r = w / 2, yc = yb - r, tip = yb - h;
    ctx.beginPath();
    ctx.moveTo(x, tip);
    ctx.bezierCurveTo(x - r * .12, tip + h * .22, x - r, yc - r * .75, x - r, yc);
    ctx.arc(x, yc, r, Math.PI, 0, true);
    ctx.bezierCurveTo(x + r, yc - r * .75, x + r * .12, tip + h * .22, x, tip);
    ctx.closePath();
  }

  function drawPool() {
    ctx.save();
    ctx.translate(CX, WL + 2); ctx.scale(1, .15);
    let g = ctx.createRadialGradient(0, 0, 0, 0, 0, 145);
    g.addColorStop(0, 'rgba(246,220,215,.95)');
    g.addColorStop(.6, 'rgba(230,182,178,.55)');
    g.addColorStop(1, 'rgba(216,193,174,0)');
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(0, 0, 145, 0, 7); ctx.fill();
    ctx.restore();

    ctx.save();
    ctx.translate(CX, WL - 1); ctx.scale(1, .07);
    let s = ctx.createRadialGradient(0, 0, 0, 0, 0, 115);
    s.addColorStop(0, 'rgba(255,253,251,.4)');
    s.addColorStop(1, 'rgba(255,253,251,0)');
    ctx.fillStyle = s; ctx.beginPath(); ctx.arc(0, 0, 115, 0, 7); ctx.fill();
    ctx.restore();
  }

  function drawRipple(rp) {
    if (rp.t <= 0) return;
    const u = rp.t / rp.life;
    const R = rp.R * (1 - Math.pow(1 - u, 2.2));
    if (R < 1) return;
    const a = rp.amp * Math.pow(1 - u, 1.6);
    for (let i = 0; i < 3; i++) {                 // حلقة رئيسية + حلقات خلفها
      const rr = R * (1 - i * .13); if (rr < 1) continue;
      const aa = a * (1 - i * .3), lw = Math.max(.35, rp.w * (1 - u * .6) * (1 - i * .22));
      ctx.lineWidth = lw;
      ctx.strokeStyle = `rgba(255,253,251,${aa * .95})`;          // حافة مضيئة
      ctx.beginPath(); ctx.ellipse(rp.x, rp.y - .9, rr, rr * K, 0, 0, 7); ctx.stroke();
      ctx.strokeStyle = `rgba(176,120,118,${aa * .5})`;           // ظل بجانبها
      ctx.beginPath(); ctx.ellipse(rp.x, rp.y + .9, rr, rr * K, 0, 0, 7); ctx.stroke();
    }
  }

  function drawDrop(a) {
    const vr = Math.min(1, drop.vy / 330);
    const wob = 1 + .05 * Math.sin(drop.t * 34) * Math.exp(-drop.t * 7);   // اهتزاز عند الانفصال
    const w = DROP_W * (1 - .1 * vr) * wob, h = DROP_H * (1 + .2 * vr) / wob;
    const yb = drop.y, r = w / 2, yc = yb - r;

    ctx.save();
    ctx.beginPath(); ctx.rect(0, 0, W, WL); ctx.clip();      // تغوص تحت خط الماء
    ctx.globalAlpha = a;

    dropPath(CX, yb, w, h);
    const g = ctx.createRadialGradient(CX - r * .3, yc - r * .25, 0, CX - r * .3, yc - r * .25, r * 1.2);
    g.addColorStop(0, 'rgba(255,253,251,.28)');
    g.addColorStop(.55, 'rgba(243,211,207,.42)');
    g.addColorStop(.85, 'rgba(217,163,162,.75)');
    g.addColorStop(1, 'rgba(184,123,126,.92)');
    ctx.fillStyle = g; ctx.fill();
    ctx.lineWidth = .9; ctx.strokeStyle = 'rgba(128,91,82,.5)'; ctx.stroke();

    // ضوء متكسّر في أسفل القطرة (عدسة)
    const c = ctx.createRadialGradient(CX + r * .1, yc + r * .55, 0, CX + r * .1, yc + r * .55, r * .6);
    c.addColorStop(0, 'rgba(255,244,240,.6)'); c.addColorStop(1, 'rgba(255,244,240,0)');
    ctx.save(); dropPath(CX, yb, w, h); ctx.clip(); ctx.fillStyle = c; ctx.fillRect(CX - r, yc - r, w, w * 1.3); ctx.restore();

    ctx.strokeStyle = 'rgba(255,253,251,.55)'; ctx.lineWidth = 1.6; ctx.lineCap = 'round';
    ctx.beginPath(); ctx.arc(CX, yc + r * .08, r * .66, Math.PI * .22, Math.PI * .78); ctx.stroke();

    // لمعة رئيسية
    ctx.fillStyle = 'rgba(255,255,255,.9)';
    ctx.beginPath(); ctx.ellipse(CX - r * .42, yc - r * .42, r * .15, r * .4, .45, 0, 7); ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,.95)'; ctx.lineWidth = 2.4;
    ctx.beginPath(); ctx.moveTo(CX - r * .5, yc - r * .05); ctx.quadraticCurveTo(CX - r * .42, yc - r * .5, CX - r * .12, yc - r * .9); ctx.stroke();
    ctx.fillStyle = 'rgba(255,255,255,.6)';
    ctx.beginPath(); ctx.arc(CX + r * .5, yc + r * .15, r * .09, 0, 7); ctx.fill();
    ctx.restore();

    // انعكاس القطرة على الماء
    if (drop.y < WL) {
      ctx.save();
      ctx.beginPath(); ctx.rect(0, WL, W, H - WL); ctx.clip();
      ctx.translate(0, 2 * WL); ctx.scale(1, -1);
      ctx.globalAlpha = a * .16 * Math.min(1, (drop.y - 20) / (WL - 20));
      dropPath(CX, yb, w * .9, h * .9);
      ctx.fillStyle = '#d9a3a2'; ctx.fill();
      ctx.restore();
    }
  }

  function drawBead(p) {
    if (p.d > 0) return;
    const r = p.r, sp = Math.hypot(p.vx, p.vy), s = 1 + Math.min(sp / 700, .4);
    ctx.save();
    ctx.translate(p.x, p.y); ctx.rotate(Math.atan2(p.vy, p.vx)); ctx.scale(s, 1 / Math.sqrt(s));
    ctx.beginPath(); ctx.arc(0, 0, r, 0, 7);              // شكل مطاول باتجاه الحركة
    ctx.restore();

    const g = ctx.createRadialGradient(p.x - r * .3, p.y - r * .3, 0, p.x, p.y, r * 1.1);
    g.addColorStop(0, 'rgba(255,253,251,.5)');
    g.addColorStop(.6, 'rgba(239,201,197,.5)');
    g.addColorStop(1, 'rgba(190,135,137,.92)');
    ctx.fillStyle = g; ctx.fill();
    ctx.lineWidth = Math.max(.35, r * .18); ctx.strokeStyle = 'rgba(255,253,251,.65)'; ctx.stroke();

    ctx.fillStyle = 'rgba(255,255,255,.95)';
    ctx.beginPath(); ctx.arc(p.x - r * .35, p.y - r * .38, Math.max(.3, r * .27), 0, 7); ctx.fill();
    if (r > 1.6) {
      ctx.fillStyle = 'rgba(255,255,255,.5)';
      ctx.beginPath(); ctx.arc(p.x + r * .38, p.y + r * .42, r * .14, 0, 7); ctx.fill();
    }
  }

  function draw() {
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    ctx.clearRect(0, 0, W, H);
    drawPool();
    for (const r of ripples) drawRipple(r);
    if (flash) {
      const u = flash.t / .3;
      ctx.fillStyle = `rgba(255,253,251,${.6 * (1 - u)})`;
      ctx.beginPath(); ctx.ellipse(CX, WL, 4 + 24 * u, (4 + 24 * u) * K * 1.4, 0, 0, 7); ctx.fill();
    }
    if (drop && drop.on) drawDrop(drop.a);
    for (const p of parts) drawBead(p);
  }

  /* ---------- تشغيل ---------- */
  resize();
  new ResizeObserver(resize).observe(root);

  if (reduced) {                         // لمن يفضّل بدون حركة: لقطة ثابتة
    startDrop(); drop.y = 120; drop.a = 1; drop.t = 1; draw();
    return;
  }

  startDrop();
  let last = performance.now(), visible = true;
  new IntersectionObserver(e => visible = e[0].isIntersecting).observe(root);

  (function frame(now) {
    const dt = Math.min(.033, (now - last) / 1000); last = now;
    if (visible) { update(dt); draw(); }
    requestAnimationFrame(frame);
  })(last);
})();
