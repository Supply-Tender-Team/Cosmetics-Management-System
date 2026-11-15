const tenderSearch = document.querySelector('[data-tender-search]');
const tenderResults = document.getElementById('tenderResults');
let tableHidden = false;
let timer;
let pending;

async function filterTenders() {
    pending?.abort();
    pending = new AbortController();
    const { signal } = pending;
    const url = new URL(tenderSearch.action);
    for (const [name, value] of new FormData(tenderSearch)) {
        if (value.trim()) url.searchParams.set(name, value.trim());
    }
    try {
        const response = await fetch(url, { signal });
        if (!response.ok || response.redirected) return;
        const page = new DOMParser().parseFromString(await response.text(), 'text/html');
        const results = page.getElementById('tenderResults');
        if (signal.aborted || !results) return;
        tenderResults.replaceChildren(...results.childNodes);
        syncTenderTable();
        history.replaceState(history.state, '', url);
    } catch {
        // The standard Filter form remains available if the request fails.
    }
}

tenderSearch.addEventListener('input', () => {
    pending?.abort();
    clearTimeout(timer);
    timer = setTimeout(filterTenders, 250);
});
tenderSearch.addEventListener('submit', () => {
    clearTimeout(timer);
    pending?.abort();
});

function syncTenderTable() {
    const table = tenderResults.querySelector('#tenderTable');
    const toggle = tenderResults.querySelector('[data-tender-table-toggle]');
    if (!table || !toggle) return;
    table.hidden = tableHidden;
    toggle.textContent = tableHidden ? 'Show table' : 'Hide table';
    toggle.setAttribute('aria-expanded', String(!tableHidden));
}

tenderResults.addEventListener('click', event => {
    if (!event.target.closest('[data-tender-table-toggle]')) return;
    tableHidden = !tableHidden;
    syncTenderTable();
});
