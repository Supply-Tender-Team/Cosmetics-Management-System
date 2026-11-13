// Shared UI helpers: sidebar toggle + highlight of the current menu item.
(function () {
  const shell = document.getElementById('dashShell');
  const toggle = document.getElementById('sidebarToggle');
  if (shell && toggle) {
    toggle.addEventListener('click', function () {
      const small = window.matchMedia('(max-width: 991px)').matches;
      shell.classList.toggle(small ? 'sidebar-open' : 'sidebar-collapsed');
    });
  }

  // Highlight only the best (longest) matching menu link. "/Admin" is the dashboard, so it needs special care.
  const strip = p => (p || '').toLowerCase().replace(/\/index$/, '').replace(/\/dashboard$/, '') || '/';
  const path = strip(location.pathname);
  let best = null, bestLen = -1;
  document.querySelectorAll('.dash-sidebar .nav-link').forEach(function (a) {
    const href = strip(a.getAttribute('href'));
    if ((path === href || path.startsWith(href + '/')) && href.length > bestLen) { best = a; bestLen = href.length; }
  });
  if (best) best.classList.add('active');
})();
