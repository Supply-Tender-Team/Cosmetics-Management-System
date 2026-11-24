(() => {
    'use strict';
    const card = document.querySelector('[data-offer-assistant]');
    if (!card) return;
    const button = card.querySelector('[data-advice-generate]'), cancel = card.querySelector('[data-advice-cancel]');
    const status = card.querySelector('[role="status"]'), result = card.querySelector('[data-advice-result]');
    let pending, timer, busy = false, disabled = false;
    async function read(response) {
        if (response.redirected) throw new Error('Your session changed. Reload and sign in again.');
        if (!response.ok) {
            let message = 'Analysis could not be loaded. Please retry or reload.';
            if (response.status === 404 || response.status === 403) message = 'This tender is no longer available for your evaluation. Reload the page.';
            if ((response.headers.get('content-type') || '').includes('application/json')) {
                const error = await response.json();
                message = error.message || message;
                if (error.status === 'disabled') disabled = true;
            }
            throw new Error(message);
        }
        if (response.status === 204) return;
        if (!(response.headers.get('content-type') || '').includes('text/html')) throw new Error('Unexpected response. Please reload.');
        // This is a same-origin server partial. Razor encodes every AI text field.
        result.innerHTML = await response.text();
    }
    async function restore() {
        if (busy) return;
        try {
            const response = await fetch(card.dataset.currentUrl, { cache: 'no-store', credentials: 'same-origin' });
            if (busy) return;
            if (response.status === 204) {
                if (result.childElementCount) { result.replaceChildren(); status.textContent = 'Previous advice expired. Generate a fresh analysis.'; }
                return;
            }
            await read(response);
        }
        catch (error) { status.textContent = error.message; }
    }
    button.addEventListener('click', async () => {
        if (busy) return;
        busy = true; pending = new AbortController(); button.disabled = true; cancel.hidden = false;
        card.setAttribute('aria-busy', 'true');
        status.textContent = 'Comparing eligible offers... This may take a few minutes. Your evaluation is unchanged.';
        let timedOut = false;
        timer = setTimeout(() => { timedOut = true; pending.abort(); }, 1250000);
        try {
            const data = new FormData();
            data.append('__RequestVerificationToken', card.querySelector('[name="__RequestVerificationToken"]').value);
            await read(await fetch(card.dataset.analyzeUrl, { method: 'POST', body: data, credentials: 'same-origin', signal: pending.signal }));
            status.textContent = 'Analysis ready. Review its checks and coverage before deciding.';
        } catch (error) {
            status.textContent = error.name === 'AbortError' ? (timedOut ? 'Analysis took too long. Please retry.' : 'Analysis cancelled. Previous advice is unchanged.') : error.message;
        } finally {
            clearTimeout(timer); busy = false; button.disabled = disabled; cancel.hidden = true; card.removeAttribute('aria-busy');
        }
    });
    cancel.addEventListener('click', () => pending?.abort());
    window.addEventListener('pagehide', () => pending?.abort());
    window.addEventListener('focus', restore);
    restore();
})();
