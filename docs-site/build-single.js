// Bundles the whole docs site into ONE html file you can email or drop in a chat.
//   node build-single.js        ->  SourcingTender-Docs.html
// Re-run it after editing anything in this folder (for example assets/data.js).
const fs = require('fs');
const path = require('path');

const root = __dirname;
const read = f => fs.readFileSync(path.join(root, f), 'utf8');
const PAGES = [
  ['index', 'Home'],
  ['schema', 'Schema Diagram'],
  ['er', 'ER Diagram'],
  ['sql', 'SQL Script'],
  ['flow', 'Flow Story'],
  ['pages', 'Pages & Roles'],
  ['onboarding', 'Onboarding Data'],
];

const safeScript = s => s.replace(/<\/script/gi, '<\\/script');

function bundlePage(name) {
  let html = read(name + '.html');

  // inline stylesheet
  html = html.replace(/<link rel="stylesheet" href="(assets\/[^"]+)">/g, (_, f) => `<style>${read(f)}</style>`);

  // inline scripts (nav.js is dropped: the shell has its own navigation)
  html = html.replace(/<script src="assets\/nav\.js"><\/script>\s*/g, '');
  html = html.replace(/<script src="(assets\/[^"]+)"><\/script>/g, (_, f) => `<script>${safeScript(read(f))}</script>`);

  // pages must not read/write the real URL (they live inside the shell now)
  html = html.replace(/location\.hash/g, 'window.__SUB.hash')
             .replace(/location\.search/g, 'window.__SUB.search')
             .replace(/history\.replaceState\([^;]*\);?/g, 'void 0;');

  // shell-friendly tweaks + parameters injected at navigation time
  html = html.replace('</head>',
    '<style>.topnav,footer.site{display:none!important}#panel{top:16px!important;max-height:calc(100vh - 32px)!important}</style>' +
    '<!--SUB--></head>');
  return html;
}

const bundles = Object.fromEntries(PAGES.map(([n]) => [n, bundlePage(n)]));
const json = JSON.stringify(bundles).replace(/<\//g, '<\\/');

const shell = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Sourcing & Tender System - Team Docs</title>
<style>
  :root { --brand:#be185d; --ink:#0f172a; --line:#e2e8f0; --soft:#f8fafc; }
  * { box-sizing: border-box; }
  html, body { margin: 0; height: 100%; background: #fff; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; color: var(--ink); display: flex; flex-direction: column; }
  nav { display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; padding: .6rem 1.2rem; border-bottom: 2px solid var(--line); background: #fff; }
  nav b { color: var(--brand); font-size: 1.1rem; margin-right: 1rem; }
  nav a { text-decoration: none; color: var(--ink); font-weight: 600; font-size: .95rem; padding: .35rem .9rem; border-radius: 999px; }
  nav a:hover { background: var(--soft); }
  nav a.on { background: var(--brand); color: #fff; }
  iframe { flex: 1 1 auto; width: 100%; border: 0; min-height: 0; }
</style>
</head>
<body>
<nav id="nav"><b>Sourcing &amp; Tender System</b></nav>
<iframe id="view" title="page" allow="clipboard-write"></iframe>

<script>
const PAGES = ${json};
const TITLES = ${JSON.stringify(Object.fromEntries(PAGES))};
const view = document.getElementById('view');
const nav = document.getElementById('nav');

for (const [id, title] of Object.entries(TITLES)) {
  const a = document.createElement('a');
  a.textContent = title; a.href = '#' + id; a.dataset.id = id;
  nav.appendChild(a);
}

// "#flow/onboarding"  -> page flow, hash onboarding
// "#schema?t=Bids"    -> page schema, search ?t=Bids
function parse() {
  const m = /^#?([a-z]+)(\\?[^/]*)?(?:\\/(.*))?$/.exec(location.hash) || [];
  const id = PAGES[m[1]] ? m[1] : 'index';
  return { id, search: m[2] || '', hash: m[3] || '' };
}

let current = '';
function show() {
  const { id, search, hash } = parse();
  const key = id + '|' + search + '|' + hash;
  if (key === current) return;
  current = key;
  nav.querySelectorAll('a').forEach(a => a.classList.toggle('on', a.dataset.id === id));
  document.title = TITLES[id] + ' - Sourcing & Tender System';
  const sub = '<script>window.__SUB=' + JSON.stringify({ search, hash: hash ? '#' + hash : '' }).replace(/</g, '\\\\u003c') + '<\\/script>';
  view.srcdoc = PAGES[id].replace('<!--SUB-->', sub);
}

// links inside the pages ("flow.html#big", "schema.html?t=Bids") navigate the shell instead
view.addEventListener('load', () => {
  const doc = view.contentDocument;
  if (!doc) return;
  doc.addEventListener('click', e => {
    const a = e.target.closest && e.target.closest('a[href]');
    if (!a) return;
    const m = /^([a-z]+)\\.html(\\?[^#]*)?(?:#(.*))?$/.exec(a.getAttribute('href'));
    if (!m || !PAGES[m[1]]) return;
    e.preventDefault();
    location.hash = '#' + m[1] + (m[2] || '') + (m[3] ? '/' + m[3] : '');
  });
});

window.addEventListener('hashchange', show);
show();
</script>
</body>
</html>
`;

const out = path.join(root, 'SourcingTender-Docs.html');
fs.writeFileSync(out, shell);
console.log('written', out, (fs.statSync(out).size / 1024).toFixed(0) + ' KB');
