(() => {
    const stage = document.querySelector('[data-recommendation-stage]');
    if (!stage || !stage.dataset.statusUrl) return;
    let checking = false;
    window.setInterval(async () => {
        if (checking || document.hidden || document.querySelector("dialog[open]") || document.querySelector('#recommended-offer')?.value) return;
        checking = true;
        try {
            const response = await fetch(stage.dataset.statusUrl, { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (response.ok && response.headers.get('content-type')?.includes('application/json')) {
                const state = await response.json();
                if (state.stageKey !== stage.dataset.stageKey) window.location.reload();
            }
        } catch { /* Keep the current page when the connection is unavailable. */ }
        finally { checking = false; }
    }, 10000);
})();
