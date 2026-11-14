(() => {
  const $ = selector => document.querySelector(selector);
  const $$ = selector => [...document.querySelectorAll(selector)];
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

})();
