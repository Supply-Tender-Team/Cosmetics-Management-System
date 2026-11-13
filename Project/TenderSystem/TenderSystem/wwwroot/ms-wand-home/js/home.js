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

  // Mobile menu
  const header = $('#site-header');
  const menu = $('.menu-toggle');
  const nav = $('#navigation');
  const setMenu = (open) => {
    menu.setAttribute('aria-expanded', String(open));
    nav.classList.toggle('is-open', open);
  };
  menu.addEventListener('click', () => setMenu(menu.getAttribute('aria-expanded') !== 'true'));
  nav.addEventListener('click', (e) => { if (e.target.closest('a')) setMenu(false); });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && menu.getAttribute('aria-expanded') === 'true') { setMenu(false); menu.focus(); }
  });
  matchMedia('(max-width: 920px)').addEventListener('change', () => setMenu(false));

  // Glass header
  const updateHeader = () => header.classList.toggle('is-glass', window.scrollY > 24);
  window.addEventListener('scroll', updateHeader, { passive: true });
  updateHeader();

  // Scroll spy: the lipstick swatch slides under the current section's link
  const swatch = $('.nav__swatch');
  const spyLinks = $$('[data-spy]');
  const moveSwatch = (link) => {
    spyLinks.forEach((a) => a.classList.toggle('is-active', a === link));
    if (!link || !swatch) return;
    swatch.style.setProperty('--x', link.offsetLeft + 'px');
    swatch.style.width = link.offsetWidth + 'px';
    swatch.style.setProperty('--o', '1');
  };
  const spy = new IntersectionObserver((entries) => {
    entries.forEach((en) => {
      if (en.isIntersecting) moveSwatch(spyLinks.find((a) => a.dataset.spy === en.target.id));
    });
  }, { rootMargin: '-45% 0px -50% 0px' });
  spyLinks.forEach((a) => { const s = document.getElementById(a.dataset.spy); if (s) spy.observe(s); });
  window.addEventListener('resize', () => moveSwatch($('[data-spy].is-active')));

  // Year
  const year = $('#year');
  if (year) year.textContent = new Date().getFullYear();

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

  /* ───────── 4 · About: words light up like blush, the mirror glows ───────── */
  const words = $$('.about__mission .w');
  ScrollTrigger.create({
    trigger: '.about__mission', start: 'top 80%', end: 'bottom 45%', scrub: true,
    onUpdate: (s) => { const lit = Math.round(s.progress * words.length); words.forEach((w, i) => w.classList.toggle('is-lit', i < lit)); },
  });
  gsap.from('.mirror', { scale: 0.7, opacity: 0, rotation: -12, duration: 1.4, ease: 'expo.out', scrollTrigger: { trigger: '.mirror', start: 'top 85%', once: true } });
  gsap.fromTo('.mirror__glass img', { yPercent: -8 }, { yPercent: 8, ease: 'none', scrollTrigger: { trigger: '.about', start: 'top bottom', end: 'bottom top', scrub: true } });
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
  const poses = [[.55, -16], [.30, 18], [.62, -12], [.38, 16], [.67, -9]];
  let frame = 0;

  const render = () => {
    frame = 0;
    if (reduced.matches) return;
    const focus = innerHeight * .48;
    const stops = chapters.map(section => {
      const rect = section.getBoundingClientRect();
      return rect.top + Math.min(rect.height * .5, focus);
    });
    const next = stops.findIndex(stop => stop > focus);
    const right = next < 0 ? stops.length - 1 : next;
    const left = next < 0 ? right : Math.max(0, right - 1);
    const t = left === right ? 0 : (focus - stops[left]) / (stops[right] - stops[left]);
    const [fromY, fromAngle] = poses[left];
    const [toY, toAngle] = poses[right];
    bottle.style.setProperty('--bottle-y', (fromY + (toY - fromY) * t) * innerHeight + 'px');
    bottle.style.setProperty('--bottle-angle', fromAngle + (toAngle - fromAngle) * t + 'deg');
    bottle.classList.add('is-ready');
    const index = chapters.findIndex(section => section.getBoundingClientRect().bottom > focus);
    const chapter = index < 0 ? chapters.length - 1 : index;
    if (bottle.dataset.chapter === String(chapter)) return;
    bottle.dataset.chapter = String(chapter);
    const shades = ['burgundy', 'dusty-rose', 'mocha', 'soft-pink', 'burgundy'];
    bottle.style.setProperty('--pour-color', 'var(--' + shades[chapter] + ')');
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
  if (!frame || !window.gsap || !window.ScrollTrigger) return;
  gsap.registerPlugin(ScrollTrigger);
  gsap.matchMedia().add('(prefers-reduced-motion: no-preference)', () => {
    gsap.timeline({ scrollTrigger: { trigger: frame, start: 'top 98%', end: 'bottom 8%', scrub: .55 } })
      .fromTo(frame, { scale: .79, y: 24 }, { scale: 1, y: 0, duration: .55, ease: 'power2.out' })
      .to(frame, { scale: .88, y: -18, duration: .45, ease: 'power1.inOut' });
  });
})();
