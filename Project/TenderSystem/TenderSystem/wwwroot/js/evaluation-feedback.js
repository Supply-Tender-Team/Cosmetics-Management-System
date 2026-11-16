(() => {
    const dialog = document.getElementById('ev-confirm');
    let pendingButton = null;
    let approvedButton = null;

    document.addEventListener('submit', event => {
        const button = event.submitter;
        if (!button?.hasAttribute('data-ev-confirm') || button === approvedButton) return;
        const message = button.dataset.evConfirm;
        if (!dialog || typeof dialog.showModal !== 'function') {
            if (!window.confirm(message)) event.preventDefault();
            return;
        }
        event.preventDefault();
        pendingButton = button;
        document.getElementById('ev-confirm-title').textContent = button.dataset.evConfirmTitle;
        document.getElementById('ev-confirm-description').textContent = message;
        document.getElementById('ev-confirm-submit').textContent = button.dataset.evConfirmButton;
        dialog.showModal();
    });

    dialog?.addEventListener('close', () => { pendingButton = null; });
    document.getElementById('ev-confirm-submit')?.addEventListener('click', () => {
        const button = pendingButton;
        if (!button || !dialog.open) return;
        dialog.close();
        approvedButton = button;
        try {
            // Preserve the original submit button, named values, validation and anti-forgery token.
            button.form.requestSubmit(button);
        } finally { approvedButton = null; }
    });

    document.querySelectorAll('[data-ev-close]').forEach(button => {
        button.addEventListener('click', () => button.closest('dialog').close());
    });
    const success = document.querySelector('[data-ev-success]');
    if (success && typeof success.showModal === 'function') success.showModal();
})();
