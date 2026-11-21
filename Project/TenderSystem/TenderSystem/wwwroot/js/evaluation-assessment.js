(() => {
    const assistant = document.querySelector('.ev-ai-disclosure');
    if (assistant && window.matchMedia('(max-width: 991px)').matches) assistant.open = false;
    const dialog = document.getElementById('ev-leave-form');
    let form, initial, submitting = false, destination = null, loading = false;
    const snapshot = () => form ? JSON.stringify(Array.from(new FormData(form).entries())) : '';
    const resetForm = () => {
        form = document.querySelector('[data-evaluation-form]');
        initial = snapshot();
        submitting = false;
    };
    resetForm();
    const dirty = () => form?.dataset.editable === 'true' && !submitting && snapshot() !== initial;
    const status = document.createElement('p');
    status.className = 'ev-member-load-status';
    status.setAttribute('role', 'status');
    status.setAttribute('aria-live', 'polite');
    document.querySelector('[data-assessment-panel]')?.prepend(status);

    async function navigate(target) {
        if (!target.member) { submitting = true; window.location.assign(target.url); return; }
        if (loading) return;
        const panel = document.querySelector('[data-assessment-panel]');
        if (!panel) { window.location.assign(target.url); return; }
        loading = true;
        panel.setAttribute('aria-busy', 'true');
        status.textContent = 'Loading member assessment…';
        try {
            // Request the existing authorized GET; replace only its assessment panel, not the page or AI context.
            const response = await fetch(target.url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok) throw new Error('Member assessment could not be loaded.');
            const html = new DOMParser().parseFromString(await response.text(), 'text/html');
            const next = html.querySelector('[data-assessment-panel]');
            if (!next) { submitting = true; window.location.assign(response.url || target.url); return; }
            const y = window.scrollY;
            panel.replaceWith(document.importNode(next, true));
            document.querySelector('[data-assessment-panel]')?.prepend(status);
            history.replaceState(null, '', target.url);
            resetForm();
            status.textContent = 'Member assessment loaded.';
            window.scrollTo({ top: y, behavior: 'instant' });
            document.querySelector('[data-assessment-panel] .ev-switcher-person h2')?.setAttribute('tabindex', '-1');
            document.querySelector('[data-assessment-panel] .ev-switcher-person h2')?.focus({ preventScroll: true });
        } catch {
            panel.removeAttribute('aria-busy');
            status.textContent = 'Unable to load the assessment. Your form is unchanged. Please try again.';
        } finally { loading = false; }
    }
    document.addEventListener('submit', event => { if (event.target === form) submitting = true; });
    window.addEventListener('beforeunload', event => {
        if (dirty()) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || link.target === '_blank' || link.hasAttribute('download') || link.getAttribute('href').startsWith('#')) return;
        const target = { url: link.href, member: link.hasAttribute('data-member-nav') };
        if (!dirty() && !target.member) return;
        if (dirty() && (!dialog || typeof dialog.showModal !== 'function')) return;
        event.preventDefault();
        if (loading) return;
        if (dirty()) {
            destination = target;
            dialog.showModal();
            dialog.querySelector('[data-ev-stay]').focus();
        } else navigate(target);
    });
    dialog?.querySelector('[data-ev-stay]').addEventListener('click', () => dialog.close());
    dialog?.querySelector('[data-ev-leave]').addEventListener('click', () => {
        const target = destination;
        dialog.close();
        if (target) navigate(target);
    });
    dialog?.addEventListener('close', () => { destination = null; });
})();
