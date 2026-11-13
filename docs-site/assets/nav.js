// Injects the same top navigation + footer on every page.
(function () {
  const pages = [
    ['index.html', 'Home'],
    ['schema.html', 'Schema Diagram'],
    ['er.html', 'ER Diagram'],
    ['sql.html', 'SQL Script'],
    ['flow.html', 'Flow Story'],
    ['pages.html', 'Pages & Roles'],
    ['onboarding.html', 'Onboarding Data'],
  ];
  const here = location.pathname.split('/').pop() || 'index.html';
  const nav = document.createElement('nav');
  nav.className = 'topnav';
  nav.innerHTML =
    '<a class="brand" href="index.html">Sourcing &amp; Tender System</a>' +
    pages.map(([h, t]) => `<a class="link ${h === here ? 'active' : ''}" href="${h}">${t}</a>`).join('');
  document.body.prepend(nav);

  const f = document.createElement('footer');
  f.className = 'site';
  f.textContent = 'Team documentation · ASP.NET Core MVC + SQL Server + Ollama + WebAuthn';
  document.body.append(f);
})();
