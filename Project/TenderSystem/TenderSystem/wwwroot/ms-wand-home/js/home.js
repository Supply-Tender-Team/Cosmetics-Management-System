/*
 * MS. WAND home: motion and interaction.
 * Sections (in nav order): hero · how it works · supplier opportunities · about · film.
 * Every block below is independent; each one guards for missing markup.
 */
(function () {
  'use strict';

  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const finePointer = matchMedia('(hover: hover) and (pointer: fine)').matches;
  const hasGsap = !!(window.gsap && window.ScrollTrigger);
  const puff = (x, y, o) => window.Powder && window.Powder.burst(x, y, o);
  const centerOf = (el) => { const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, r }; };

  document.documentElement.classList.add('js');
  const body = document.body;

  /* ───────── Shared UI that works without GSAP ───────── */

  // Contact form (opens a mail draft once a recipient is configured)
  const form = $('#contact-form');
  if (form) {
    const status = $('#contact-status');
    const recipient = (form.dataset.recipient || '').trim();
    if (recipient) {
      const fc = $('#footer-contact');
      fc.textContent = recipient;
      fc.href = 'mailto:' + recipient;
    }
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      if (!form.checkValidity()) {
        const bad = form.querySelector(':invalid');
        status.textContent = bad.type === 'email' && bad.value
          ? 'Enter an email address like name@company.com.'
          : 'Fill in your ' + bad.name + ' to send the message.';
        bad.focus();
        return;
      }
      const btn = form.querySelector('[type=submit]');
      const c = centerOf(btn);
      puff(c.x, c.y, { count: 120, power: 8, colors: 'berry' });
      if (!recipient) {
        status.textContent = 'Your message is kept in the form. Email sending is not set up yet.';
        return;
      }
      const f = new FormData(form);
      const text = ['Name: ' + f.get('name'), 'Email: ' + f.get('email'), '', f.get('message')].join('\r\n');
      window.location.href = 'mailto:' + recipient + '?subject=' + encodeURIComponent('Sourcing enquiry') + '&body=' + encodeURIComponent(text);
      status.textContent = 'A draft opened in your email app. Send it from there.';
    });
  }

  // Walkthrough video dialog
  const film = $('#film');
  if (film) {
    const video = $('video', film);
    $$('[data-open-film]').forEach((b) => b.addEventListener('click', () => { film.showModal(); video.play().catch(() => {}); }));
    $$('[data-close-film]').forEach((b) => b.addEventListener('click', () => film.close()));
    film.addEventListener('click', (e) => { if (e.target === film) film.close(); });
    film.addEventListener('close', () => video.pause());
  }

  // Button highlight follows the pointer; every button puffs a little when pressed
  $$('.btn').forEach((b) => {
    b.addEventListener('pointermove', (e) => {
      const r = b.getBoundingClientRect();
      b.style.setProperty('--mx', ((e.clientX - r.left) / r.width) * 100 + '%');
      b.style.setProperty('--my', ((e.clientY - r.top) / r.height) * 100 + '%');
    });
    b.addEventListener('click', (e) => {
      if (b.type === 'submit') return;
      puff(e.clientX, e.clientY, { count: 40, power: 5, cloud: 0.4 });
    });
  });

  // How it works: step data and the bottle that fills
  const STEPS = [
    ['Identify need', 'Production defines what is needed, why, and by when. A new serum launch needs 30 mL glass bottles that fit the existing filling line.', 'Result: a confirmed production need'],
    ['Create purchase request', 'The requester records quantity, bottle dimensions, quality requirements and the target delivery date, so procurement reviews the same specification.', 'Result: a documented purchase request'],
    ['Create tender', 'Procurement turns the approved request into a tender with eligibility criteria, the evaluation method, a deadline and instructions for samples and quotes.', 'Result: a tender open to suppliers'],
    ['Receive bids and offers', 'Suppliers submit quotes, lead times and supporting documents. Procurement checks each offer is complete and records it for review.', 'Result: offers ready for evaluation'],
    ['Technical and financial evaluation', 'Quality tests samples and documents against the specification. Procurement compares eligible offers on total cost, terms and delivery.', 'Result: a scored recommendation'],
    ['Final decision and award', 'The decision maker reviews the recommendation, records the reason for the award and notifies the selected supplier.', 'Result: a recorded award and a notified supplier'],
  ];
  const stepButtons = $$('.how__steps button');
  const howTitle = $('#how-step-title');
  const howText = $('#how-step-text');
  const howOut = $('#how-step-out');
  const howNum = $('#how-num');
  const liquid = $('.bottle__liquid');
  const drop = $('.bottle__drop');
  const seal = $('.seal');
  const stepSwatch = $('.how__swatch');
  let current = -1;

  function setStep(i) {
    if (i === current) return;
    const prev = current;
    current = i;
    stepButtons.forEach((b, n) => b.setAttribute('aria-pressed', String(n === i)));
    howNum.textContent = i + 1;
    [howTitle.textContent, howText.textContent, howOut.textContent] = STEPS[i];
    stepSwatch.style.setProperty('--p', (i + 1) / 6);
    const levelY = 470 - ((i + 1) / 6) * 262; // empty ≈ 470, full ≈ 208 in bottle units
    liquid.style.setProperty('--level', levelY + 'px');

    if (!hasGsap || reduced) {
      gsapless();
      return;
    }
    const gsap = window.gsap;
    gsap.fromTo([howTitle, howText, howOut], { y: 16, opacity: 0 }, { y: 0, opacity: 1, duration: 0.6, stagger: 0.06, ease: 'power3.out', overwrite: true });
    gsap.fromTo(howNum, { yPercent: 60, opacity: 0 }, { yPercent: 0, opacity: 1, duration: 0.6, ease: 'expo.out', overwrite: true });
    if (i > prev) {
      gsap.fromTo(drop, { y: 0, opacity: 1, scaleY: 1 }, {
        y: levelY - 190, scaleY: 1.4, duration: 0.55, ease: 'power2.in', overwrite: true,
        onComplete: () => {
          gsap.set(drop, { opacity: 0 });
          const b = drop.getBoundingClientRect();
          puff(b.left + b.width / 2, b.top, { count: 26, power: 3.5, cloud: 0.2, colors: 'blush', angle: -Math.PI / 2, spread: Math.PI * 0.9 });
        },
      });
    }
    if (i === 5) {
      gsap.to(seal, {
        scale: 1, opacity: 1, duration: 0.5, delay: 0.35, ease: 'back.out(2.2)', overwrite: true,
        onComplete: () => { const c = centerOf(seal); puff(c.x, c.y, { count: 140, power: 9, colors: 'berry', cloud: 0.45 }); },
      });
    } else {
      gsap.to(seal, { scale: 2.4, opacity: 0, duration: 0.3, overwrite: true });
    }

    function gsapless() {
      if (seal) { seal.style.opacity = i === 5 ? '1' : '0'; seal.style.transform = i === 5 ? 'scale(1)' : ''; }
    }
  }
  setStep(0);

  // Step buttons: on desktop they scroll the pinned section to that step; elsewhere they just switch
  let howTrigger = null;
  stepButtons.forEach((b) => b.addEventListener('click', () => {
    const i = Number(b.dataset.step);
    if (howTrigger) {
      const y = howTrigger.start + ((i + 0.5) / 6) * (howTrigger.end - howTrigger.start);
      if (window.__lenis) window.__lenis.scrollTo(y, { duration: 1.2 }); else window.scrollTo({ top: y, behavior: 'smooth' });
    } else setStep(i);
  }));

  // About: wrap mission words so they can light up
  const mission = $('[data-words]');
  if (mission) {
    const parts = mission.textContent.trim().split(/\s+/);
    mission.textContent = '';
    parts.forEach((word, i) => {
      const s = document.createElement('span');
      s.className = 'w';
      s.textContent = word;
      mission.append(s, i < parts.length - 1 ? ' ' : '');
    });
  }

  /* ───────── Without GSAP (CDN blocked) or with reduced motion: show everything, stop ───────── */
  if (!hasGsap || reduced) {
    body.classList.remove('is-loading');
    $$('.about__mission .w').forEach((w) => w.classList.add('is-lit'));
    return;
  }

  const gsap = window.gsap;
  const ScrollTrigger = window.ScrollTrigger;
  gsap.registerPlugin(ScrollTrigger);

  /* ───────── Smooth scrolling ───────── */
  if (window.Lenis) {
    const lenis = new window.Lenis({ lerp: 0.09, smoothWheel: true });
    window.__lenis = lenis;
    lenis.on('scroll', ScrollTrigger.update);
    gsap.ticker.add((t) => lenis.raf(t * 1000));
    gsap.ticker.lagSmoothing(0);
    film && film.addEventListener('close', () => lenis.start());
    $$('[data-open-film]').forEach((b) => b.addEventListener('click', () => lenis.stop()));
  }
  $$('a[href^="#"]').forEach((a) => a.addEventListener('click', (e) => {
    const id = a.getAttribute('href');
    const target = id.length > 1 && document.querySelector(id);
    if (!target) return;
    e.preventDefault();
    if (window.__lenis) window.__lenis.scrollTo(target, { duration: 1.4 });
    else target.scrollIntoView({ behavior: 'smooth' });
    history.replaceState(null, '', id);
  }));

  /* ───────── 1 · Hero: the compact opens and the powder bursts ───────── */
  const hero = $('.hero');
  const compactWrap = $('.compact-wrap');
  const lid = $('.compact__lid');
  const pan = $('.compact__pan');
  const prods = $$('.prod');
  const silk = $$('.silk');
  const titleLines = $$('.hero__title .line > span');
  const intro = $('.hero__intro');

  body.classList.remove('is-loading');
  silk.forEach((p) => {
    const len = p.getTotalLength();
    gsap.set(p, { strokeDasharray: len, strokeDashoffset: len });
  });
  gsap.set(lid, { z: 4, rotationX: 0, transformOrigin: '50% 0%' });
  gsap.set(titleLines, { y: 0, yPercent: 110 });
  gsap.set(['.hero__lead', '.hero__ctas'], { opacity: 0, y: 24 });
  gsap.set(compactWrap, { scale: 0.6, opacity: 0, y: 60 });
  gsap.set(prods, { opacity: 0 });

  const tl = gsap.timeline({ delay: 0.2, onComplete: afterIntro });
  tl.fromTo(intro, { opacity: 0, scale: 0.92, filter: 'blur(10px)' }, { opacity: 1, scale: 1, filter: 'blur(0px)', duration: 1, ease: 'power3.out' })
    .to(compactWrap, { scale: 1, opacity: 1, y: 0, duration: 1.2, ease: 'expo.out' }, 0.15)
    .to(intro, { opacity: 0, yPercent: -60, filter: 'blur(8px)', duration: 0.7, ease: 'power2.in' }, 1.25)
    .to(compactWrap, { rotation: -6, duration: 0.15, yoyo: true, repeat: 3, ease: 'sine.inOut' }, 1.2) // a little shake before it pops
    .to(lid, { rotationX: 118, duration: 1.1, ease: 'back.out(1.6)' }, 1.75)
    .add(() => {
      const c = centerOf(pan);
      puff(c.x, c.y, { count: 300, power: 12, cloud: 0.55, colors: 'blush', glitter: 70 });
      puff(c.x, c.y, { count: 80, power: 8, cloud: 0.2, colors: 'berry', glitter: 0 });
    }, 1.95)
    .add(flyProducts(), 2.0)
    .to(silk, { strokeDashoffset: 0, opacity: (i) => [0.28, 0.55, 0.45][i], duration: 2.2, ease: 'power2.inOut', stagger: 0.12 }, 2.0)
    .to(titleLines, { yPercent: 0, duration: 1.1, ease: 'expo.out', stagger: 0.09 }, 2.35)
    .to(['.hero__lead', '.hero__ctas'], { opacity: 1, y: 0, duration: 0.9, ease: 'power3.out', stagger: 0.12 }, 2.8)
    .to('.hero__hint', { opacity: 1, duration: 0.6 }, 3.4);

  function flyProducts() {
    const sub = gsap.timeline();
    const c = centerOf(compactWrap);
    prods.forEach((p, i) => {
      const pc = centerOf(p);
      sub.fromTo(p,
        { x: c.x - pc.x, y: c.y - pc.y, scale: 0.15, rotation: gsap.utils.random(-220, 220), opacity: 0 },
        { x: 0, y: 0, scale: 1, rotation: 0, opacity: 1, duration: 1.6, ease: 'expo.out' }, i * 0.07);
    });
    return sub;
  }

  function afterIntro() {
    window.Powder.setAmbient(true, () => hero.getBoundingClientRect());
    ScrollTrigger.create({ trigger: hero, start: 'top top', end: 'bottom top', onToggle: (s) => window.Powder.setAmbient(s.isActive) });

    // Scrolling away: products scatter, the lid closes halfway, the copy lifts
    const stage = centerOf($('.stage'));
    const scrollTl = gsap.timeline({ scrollTrigger: { trigger: hero, start: 'top top', end: 'bottom top', scrub: 0.6 } });
    prods.forEach((p) => {
      const pc = centerOf(p);
      const dx = pc.x - stage.x;
      const dy = pc.y - stage.y;
      scrollTl.to($('.prod__drift', p), { x: dx * 0.9, y: dy * 0.9 - 120, rotation: gsap.utils.random(-90, 90), ease: 'none' }, 0);
    });
    scrollTl
      .to(lid, { rotationX: 40, ease: 'none' }, 0)
      .to(compactWrap, { y: 140, scale: 0.85, ease: 'none' }, 0)
      .to('.hero__copy', { y: -90, opacity: 0.15, ease: 'none' }, 0)
      .to('.hero__silk', { yPercent: -18, ease: 'none' }, 0);

    // Pointer: depth parallax, light follows, a powder trail
    if (finePointer) {
      const movers = $$('.prod__par').map((el) => ({
        d: parseFloat(el.dataset.depth || 1),
        x: gsap.quickTo(el, 'x', { duration: 0.9, ease: 'power3.out' }),
        y: gsap.quickTo(el, 'y', { duration: 0.9, ease: 'power3.out' }),
      }));
      const cx = gsap.quickTo(compactWrap, 'rotationY', { duration: 1, ease: 'power3.out' });
      const glow = $('.hero__glow');
      let lastX = 0, lastY = 0;
      hero.addEventListener('pointermove', (e) => {
        const r = hero.getBoundingClientRect();
        const nx = (e.clientX - r.left) / r.width - 0.5;
        const ny = (e.clientY - r.top) / r.height - 0.5;
        movers.forEach((m) => { m.x(nx * 60 * m.d); m.y(ny * 50 * m.d); });
        cx(nx * 14);
        glow.style.setProperty('--gx', (nx + 0.5) * 100 + '%');
        glow.style.setProperty('--gy', (ny + 0.5) * 100 + '%');
        if (Math.hypot(e.clientX - lastX, e.clientY - lastY) > 14) {
          window.Powder.trail(e.clientX, e.clientY);
          lastX = e.clientX; lastY = e.clientY;
        }
      });
    }

    // Tap anywhere in the hero (not on a link) for a puff; tap a product to spin it
    hero.addEventListener('click', (e) => {
      if (e.target.closest('a, button')) return;
      const prod = e.target.closest('.prod');
      if (prod) {
        gsap.fromTo($('.prod__art', prod), { rotation: 0 }, { rotation: 360, duration: 1, ease: 'expo.out' });
        puff(e.clientX, e.clientY, { count: 110, power: 9, colors: pickPalette(prod.dataset.prod), cloud: 0.4 });
        return;
      }
      puff(e.clientX, e.clientY, { count: 90, power: 7.5, cloud: 0.5 });
      gsap.to('.hero__hint', { opacity: 0, duration: 0.4 });
    });
  }
  const pickPalette = (name) => ({ lipstick: 'berry', polish: 'berry', brush: 'bronze', serum: 'blush', puff: 'blush' }[name] || 'blush');

  /* ───────── Section headings rise with a small puff ───────── */
  $$('.how .h2, .suppliers .h2, .about .h2, .intro-film .h2').forEach((h) => {
    gsap.from(h, {
      y: 60, opacity: 0, rotationX: -35, transformOrigin: '0 100%', duration: 1.1, ease: 'expo.out',
      scrollTrigger: {
        trigger: h, start: 'top 85%', once: true,
        onEnter: () => { const r = h.getBoundingClientRect(); setTimeout(() => puff(r.left + 20, r.top + r.height * 0.6, { count: 50, power: 5, cloud: 0.5, spread: Math.PI * 1.2 }), 350); },
      },
    });
  });

  /* ───────── 2 · How it works: pinned on desktop, scroll drives the steps ───────── */
  const mm = gsap.matchMedia();
  mm.add('(min-width: 1081px) and (min-height: 640px)', () => {
    howTrigger = ScrollTrigger.create({
      trigger: '.how__pin', start: 'top top', end: () => '+=' + window.innerHeight * 3.2, pin: true, scrub: true,
      onUpdate: (s) => setStep(Math.min(5, Math.floor(s.progress * 6))),
    });
    return () => { howTrigger = null; };
  });
  mm.add('(max-width: 1080px), (max-height: 639px)', () => {
    // Without the pin, the bottle fills as the steps scroll past
    ScrollTrigger.create({ trigger: '.how__steps', start: 'top 70%', end: 'bottom 40%', onUpdate: (s) => setStep(Math.min(5, Math.floor(s.progress * 6))) });
  });
  gsap.from('.how__detail', { y: 80, opacity: 0, rotation: 2, duration: 1.2, ease: 'expo.out', scrollTrigger: { trigger: '.how', start: 'top 70%', once: true } });
  gsap.from('.bottle', { y: 120, opacity: 0, rotation: -10, duration: 1.4, ease: 'expo.out', scrollTrigger: { trigger: '.how', start: 'top 65%', once: true } });
  gsap.from('.how__steps li', { x: -30, opacity: 0, stagger: 0.06, duration: 0.8, ease: 'power3.out', scrollTrigger: { trigger: '.how__steps', start: 'top 85%', once: true } });

  /* ───────── 3 · Supplier opportunities: the palette ───────── */
  gsap.from('.palette', {
    y: 100, rotationX: 25, opacity: 0, transformPerspective: 1200, transformOrigin: '50% 100%', duration: 1.4, ease: 'expo.out',
    scrollTrigger: { trigger: '.palette', start: 'top 85%', once: true },
  });
  gsap.from('.pan', {
    scale: 0.6, opacity: 0, stagger: 0.12, duration: 1, ease: 'back.out(1.8)', delay: 0.3,
    scrollTrigger: {
      trigger: '.palette', start: 'top 80%', once: true,
      onEnter: () => $$('.pan__disc').forEach((d, i) => setTimeout(() => {
        const c = centerOf(d);
        puff(c.x, c.r.top + 10, { count: 60, power: 6, cloud: 0.5, colors: [getComputedStyle(d.parentElement).getPropertyValue('--shade').trim(), '#FAF5F0'], angle: -Math.PI / 2, spread: Math.PI * 0.8 });
      }, 650 + i * 160)),
    },
  });
  $$('.pan').forEach((p) => {
    let cool = 0;
    const lift = () => {
      const now = performance.now();
      if (now - cool < 700) return;
      cool = now;
      const d = $('.pan__disc', p);
      const c = centerOf(d);
      const shade = getComputedStyle(p).getPropertyValue('--shade').trim();
      const deep = getComputedStyle(p).getPropertyValue('--shade-deep').trim();
      puff(c.x, c.r.top + c.r.height * 0.4, { count: 70, power: 5.5, cloud: 0.55, colors: [shade, deep, '#FAF5F0'], angle: -Math.PI / 2, spread: Math.PI * 0.7, gravity: 0.02 });
    };
    p.addEventListener('pointerenter', lift);
    p.addEventListener('focus', lift);
    p.addEventListener('click', lift);
  });
  gsap.from('.ticker', { opacity: 0, y: 30, duration: 1, scrollTrigger: { trigger: '.ticker', start: 'top 90%', once: true } });
  gsap.from('.route li', { y: 40, opacity: 0, stagger: 0.12, duration: 0.9, ease: 'power3.out', scrollTrigger: { trigger: '.route', start: 'top 85%', once: true } });

  // Counters pop with a puff when they finish
  $$('.stats dd').forEach((dd) => {
    const end = Number(dd.dataset.count);
    const obj = { v: 0 };
    gsap.to(obj, {
      v: end, duration: 2, ease: 'power3.out',
      scrollTrigger: { trigger: '.stats', start: 'top 80%', once: true },
      onUpdate: () => { dd.textContent = Math.round(obj.v).toLocaleString('en-US'); },
      onComplete: () => { const c = centerOf(dd); puff(c.x, c.y, { count: 60, power: 6, colors: 'blush', cloud: 0.4 }); },
    });
  });
  gsap.from('.stats', { y: 60, opacity: 0, duration: 1.1, ease: 'expo.out', scrollTrigger: { trigger: '.stats', start: 'top 88%', once: true } });

  /* ───────── 4 · About: words light up like blush ───────── */
  const words = $$('.about__mission .w');
  ScrollTrigger.create({
    trigger: '.about__mission', start: 'top 80%', end: 'bottom 45%', scrub: true,
    onUpdate: (s) => { const lit = Math.round(s.progress * words.length); words.forEach((w, i) => w.classList.toggle('is-lit', i < lit)); },
  });
  gsap.from('.about__values li', {
    x: 40, opacity: 0, stagger: 0.12, duration: 0.9, ease: 'power3.out',
    scrollTrigger: { trigger: '.about__values', start: 'top 85%', once: true },
  });

  /* ───────── 5 · Contact: drifting lipstick and puff ───────── */
  gsap.from('.form', { y: 80, opacity: 0, duration: 1.2, ease: 'expo.out', scrollTrigger: { trigger: '.form', start: 'top 88%', once: true } });

  /* ───────── Footer: the wordmark rises out of the floor ───────── */
  gsap.fromTo('.footer__giant', { yPercent: 60, opacity: 0 }, { yPercent: 12, opacity: 1, ease: 'none', scrollTrigger: { trigger: '.footer', start: 'top bottom', end: 'bottom bottom', scrub: true } });

  window.addEventListener('load', () => ScrollTrigger.refresh());
})();


(() => {
  const bottle = document.querySelector('.travel-polish');
  if (!bottle) return;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const chapters = [...document.querySelectorAll('main > section')];
  const chapterLabel = bottle.querySelector('.travel-polish__chapter strong');
  const chapterNames = { home: 'Home', 'how-it-works': 'How It Works', suppliers: 'Supplier Opportunities', about: 'About Us', 'site-film': 'Watch the Film' };
  const focus = () => innerHeight * .38;
  let frame = 0;

  const render = () => {
    frame = 0;
    if (reduced.matches) return;
    const index = chapters.findIndex(section => section.getBoundingClientRect().bottom > focus());
    const chapter = index < 0 ? chapters.length - 1 : index;
    if (bottle.dataset.chapter === String(chapter)) return;
    bottle.dataset.chapter = String(chapter);
    bottle.classList.toggle('has-long-label', chapters[chapter].id === 'suppliers');
    chapterLabel.textContent = chapterNames[chapters[chapter].id] ?? '';

    bottle.classList.remove('is-pouring');
    void bottle.offsetWidth;
    bottle.classList.add('is-pouring');
  };

  const queue = () => { if (!frame) frame = requestAnimationFrame(render); };
  addEventListener('scroll', queue, { passive: true });
  addEventListener('resize', queue);
  reduced.addEventListener('change', queue);
  queue();
})();




(() => {
  const frame = document.querySelector('.intro-film__frame');
  const video = frame?.querySelector('video');
  if (!video || !window.gsap || !window.ScrollTrigger) return;

  gsap.registerPlugin(ScrollTrigger);
  gsap.matchMedia().add('(prefers-reduced-motion: no-preference)', () => {
    const timeline = gsap.timeline({
      scrollTrigger: {
        trigger: frame,
        start: 'top 98%',
        end: 'bottom 12%',
        scrub: .45,
        invalidateOnRefresh: true
      }
    });
    timeline
      .fromTo(frame, { scale: .86, y: 12 }, { scale: 1, y: 0, duration: .42, ease: 'sine.out' })
      .to(frame, { scale: 1, y: 0, duration: .22 })
      .to(frame, { scale: .92, y: -8, duration: .36, ease: 'sine.inOut' });
  });

})();

(() => {
  const palette = document.querySelector('.route-palette');
  if (!palette) return;
  const shades = [...palette.querySelectorAll('[data-shade]')];
  const status = palette.querySelector('[role="status"]');
  let shade;
  for (const pan of shades) {
    pan.style.setProperty('--shade', 'var(--' + pan.dataset.shade + ')');
    pan.addEventListener('click', () => {
      shade = pan;
      if (!matchMedia('(prefers-reduced-motion: reduce)').matches) {
        pan.querySelector('.route-powder')?.remove();
        const cloud = document.createElement('span');
        cloud.className = 'route-powder';
        cloud.setAttribute('aria-hidden', 'true');
        for (let i = 0; i < 24; i++) {
          const grain = document.createElement('i');
          const angle = Math.random() * Math.PI * 2;
          const distance = 12 + Math.random() * 32;
          grain.style.setProperty('--dx', Math.cos(angle) * distance + 'px');
          grain.style.setProperty('--dy', (Math.sin(angle) * distance - 9) + 'px');
          grain.style.setProperty('--size', (2 + Math.random() * 4) + 'px');
          cloud.append(grain);
        }
        pan.append(cloud);
        setTimeout(() => cloud.remove(), 600);
      }
      for (const option of shades) option.setAttribute('aria-pressed', String(option === pan));
      status.textContent = pan.getAttribute('aria-label') + ' selected. Choose a card.';
    });
  }
  for (const card of palette.parentElement.querySelectorAll('.route__card')) {
    const canvas = document.createElement('canvas');
    canvas.className = 'route__pigment';
    canvas.setAttribute('aria-hidden', 'true');
    card.prepend(canvas);
    const ctx = canvas.getContext('2d');
    let pointer = null;
    let previous = null;
    let color;
    let distance = 0;
    let completed = false;
    const result = card.querySelector('.route__reveal');

    function reveal() {
      if (completed) return;
      completed = true;
      card.style.setProperty('--card-shade', color);
      card.classList.remove('is-brushed', 'has-reveal');
      void card.offsetWidth;
      card.classList.add('is-brushed');
      result.hidden = false;
      card.classList.add('has-reveal');
      status.textContent = 'Shade applied to ' + card.querySelector('strong').textContent + '.';
    }

    function paint(x, y, ink) {
      const gradient = ctx.createRadialGradient(x, y, 0, x, y, 24);
      gradient.addColorStop(0, ink);
      gradient.addColorStop(1, 'transparent');
      ctx.fillStyle = gradient;
      ctx.globalAlpha = .13;
      ctx.fillRect(x - 24, y - 24, 48, 48);
    }

    function start(event) {
      if (!shade) {
        status.textContent = 'Pick a shade from the palette first.';
        return false;
      }
      if (!canvas.dataset.sized) {
        canvas.width = card.clientWidth;
        canvas.height = card.clientHeight;
        canvas.dataset.sized = 'true';
      }
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      distance = 0;
      completed = false;
      color = getComputedStyle(card).getPropertyValue('--' + shade.dataset.shade).trim();
      if (event) {
        pointer = event.pointerId;
        card.setPointerCapture(pointer);
      }
      return true;
    }

    card.addEventListener('pointerdown', event => {
      if (event.button === 0 && pointer === null) start(event);
    });
    card.addEventListener('pointermove', event => {
      if (event.pointerId !== pointer || completed) return;
      const bounds = canvas.getBoundingClientRect();
      const x = (event.clientX - bounds.left) * canvas.width / bounds.width;
      const y = (event.clientY - bounds.top) * canvas.height / bounds.height;
      if (previous) {
        distance += Math.hypot(x - previous.x, y - previous.y);
        if (distance >= 18) { reveal(); return; }
        const steps = Math.max(1, Math.ceil(Math.hypot(x - previous.x, y - previous.y) / 5));
        for (let i = 1; i <= steps; i++) {
          paint(previous.x + (x - previous.x) * i / steps, previous.y + (y - previous.y) * i / steps, color);
        }
      } else paint(x, y, color);
      previous = { x, y };
    });
    card.addEventListener('lostpointercapture', () => {
      pointer = null;
      previous = null;
    });
    card.addEventListener('click', event => {
      if (event.detail !== 0 || !start()) return;
      paint(canvas.width / 2, canvas.height / 2, color);
      reveal();
    });
  }
})();
(() => {
  const video = document.querySelector('.about__backdrop video');
  if (!video || !('IntersectionObserver' in window)) return;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let visible = false;
  function sync() {
    if (!visible || reduced.matches || document.hidden) {
      video.pause();
      return;
    }
    if (!video.hasAttribute('src')) video.src = video.dataset.src;
    video.play().catch(() => {});
  }
  video.addEventListener('playing', () => video.classList.add('is-playing'));
  new IntersectionObserver(([entry]) => {
    visible = entry.isIntersecting;
    sync();
  }).observe(video.closest('.about'));
  reduced.addEventListener('change', sync);
  document.addEventListener('visibilitychange', sync);
})();
