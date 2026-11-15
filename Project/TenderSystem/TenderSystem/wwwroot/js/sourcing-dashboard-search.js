(() => {
    const form = document.querySelector('.sourcing-dashboard .sourcing-search');
    const results = document.getElementById('recentRequestResults');
    if (!form || !results) return;

    const toggle = document.getElementById("recentRequestsToggle");
    toggle?.addEventListener("click", () => {
        results.hidden = !results.hidden;
        toggle.textContent = results.hidden ? "Show table" : "Hide table";
        toggle.setAttribute("aria-expanded", String(!results.hidden));
    });

    let timer;
    let pending;

    async function update() {
        pending = new AbortController();
        const { signal } = pending;
        const url = new URL(form.action);
        const term = form.elements.namedItem('search').value.trim();
        if (term) url.searchParams.set('search', term);

        try {
            const response = await fetch(url, { signal });
            if (!response.ok || response.redirected) return;
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const next = page.getElementById(results.id);
            if (signal.aborted || !next) return;
            results.replaceChildren(...next.childNodes);
            history.replaceState(history.state, '', url);
        } catch {
            // The Search button can still submit the form directly.
        }
    }

    form.addEventListener('input', () => {
        pending?.abort();
        clearTimeout(timer);
        timer = setTimeout(update, 250);
    });
    form.addEventListener('submit', () => {
        clearTimeout(timer);
        pending?.abort();
    });
})();
