(() => {
    const dashboard = document.querySelector('.sourcing-dashboard');
    const form = dashboard?.querySelector('.sourcing-search');
    if (!form) return;

    const input = form.elements.namedItem('search');
    const status = dashboard.querySelector('[data-search-status]');
    let timer;
    let request;

    async function update() {
        const pending = new AbortController();
        request = pending;
        const url = new URL(form.action);
        const term = input.value.trim();
        if (term) url.searchParams.set('search', term);

        try {
            const response = await fetch(url, { signal: pending.signal });
            if (!response.ok) throw new Error('Search request failed.');
            const documentResult = new DOMParser().parseFromString(await response.text(), 'text/html');
            if (pending.signal.aborted) return;
            const queues = documentResult.querySelectorAll('.sourcing-dashboard .sourcing-queue');
            if (queues.length !== 3) throw new Error('Search results unavailable.');

            for (const next of queues) {
                const current = dashboard.querySelector('#' + next.id);
                next.querySelector('details').open = Boolean(term) || current.querySelector('details').open;
                current.replaceWith(next);
            }
            const counts = documentResult.querySelectorAll('.sourcing-kpis [href^="#"] .ad-kpi-value');
            dashboard.querySelectorAll('.sourcing-kpis [href^="#"] .ad-kpi-value').forEach((count, index) => {
                count.textContent = counts[index].textContent;
            });
            const address = new URL(location.href);
            address.search = url.search;
            history.replaceState(history.state, '', address);
            status.textContent = term ? 'Results updated for ' + term + '.' : 'Showing all records.';
        } catch (error) {
            if (!pending.signal.aborted) status.textContent = 'Live search unavailable. Try the Search button.';
        }
    }

    input.addEventListener('input', () => {
        request?.abort();
        clearTimeout(timer);
        timer = setTimeout(update, 180);
    });
})();
