const tableToggle = document.getElementById('requestTableToggle');
tableToggle?.addEventListener('click', () => {
    const table = document.getElementById('requestTable');
    table.hidden = !table.hidden;
    tableToggle.textContent = table.hidden ? 'Show table' : 'Hide table';
    tableToggle.setAttribute('aria-expanded', String(!table.hidden));
});

const review = document.getElementById('requestReview');
if (review) {
    const modal = bootstrap.Modal.getOrCreateInstance(review);
    const body = document.getElementById('requestReviewBody');
    let pending;
    let opener;
    review.addEventListener('hide.bs.modal', () => pending?.abort());
    review.addEventListener('hidden.bs.modal', () => opener?.focus());
    document.addEventListener('click', async event => {
            const link = event.target.closest('[data-request-review]');
            if (!link) return;
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            opener = link;
            pending?.abort();
            pending = new AbortController();
            const { signal } = pending;
            body.textContent = 'Loading request…';
            modal.show();
            try {
                const url = new URL(link.href);
                url.searchParams.set('popup', 'true');
                const response = await fetch(url, { signal });
                if (!response.ok || response.redirected) throw new Error('Request unavailable');
                const markup = await response.text();
                if (!signal.aborted) body.innerHTML = markup;
            } catch {
                if (!signal.aborted) body.textContent = 'Could not load the request. Close this window and try again.';
            }
    });
}

document.addEventListener('click', event => {
    const trigger = event.target.closest('[data-decision], [data-cancel-decision]');
    if (!trigger) return;
    const panel = trigger.closest('.src-requests').querySelector('#requestDecision');
    if (trigger.hasAttribute('data-cancel-decision')) {
        panel.close();
        return;
    }

    const { decision, description } = trigger.dataset;
    const form = panel.querySelector('form');
    const confirm = form.querySelector('[type="submit"]');
    const note = form.elements.note;
    confirm.value = decision;
    confirm.classList.toggle('btn-danger', decision === 'Reject');
    confirm.classList.toggle('src-primary', decision !== 'Reject');
    note.value = '';
    note.setCustomValidity('');
    const action = decision === 'Return' ? 'return this request for changes' : decision.toLowerCase() + ' this request';
    panel.querySelector('#requestDecisionTitle').textContent = 'Are you sure you want to ' + action + '?';
    panel.querySelector('#requestDecisionHelp').textContent = description;
    panel.onkeydown = event => {
        if (event.key === 'Escape') event.stopPropagation();
    };
    panel.showModal();
});

document.addEventListener('input', event => {
    const note = event.target;
    if (note.id === 'requestNote') {
        note.setCustomValidity(!note.value.trim() ? 'Enter a reason for your decision.' : '');
    }
});

const resultToast = document.getElementById('requestResult');
if (resultToast) bootstrap.Toast.getOrCreateInstance(resultToast).show();
