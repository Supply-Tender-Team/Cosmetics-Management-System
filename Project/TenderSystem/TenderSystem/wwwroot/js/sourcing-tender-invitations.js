const invitationForm = document.getElementById('tenderInvitationForm');
if (invitationForm) {
    const search = document.getElementById('tenderSupplierSearch');
    const send = document.getElementById('tenderInviteButton');
    const options = [...invitationForm.querySelectorAll('[data-supplier-option]')];
    const noMatch = document.getElementById('tenderSupplierNoMatch');
    const dialog = document.getElementById('confirmTenderInvitations');
    const modal = bootstrap.Modal.getOrCreateInstance(dialog);
    const countLabel = document.getElementById('tenderSupplierCount');
    const selectAll = document.getElementById('tenderSelectAll');
    const clear = document.getElementById('tenderClearSelection');
    const selected = () => invitationForm.querySelectorAll('input:checked').length;

    function updateSelection() {
        const checked = options.filter(option => option.querySelector('input').checked);
        countLabel.textContent = checked.length + ' selected';
        send.disabled = clear.disabled = checked.length === 0;
    }

    search.addEventListener('input', () => {
        const term = search.value.trim().toLowerCase();
        options.forEach(option => { option.hidden = !option.textContent.toLowerCase().includes(term); });
        noMatch.hidden = options.some(option => !option.hidden);
        selectAll.disabled = !noMatch.hidden;
        selectAll.textContent = term ? 'Select all matching' : 'Select all suppliers';
    });

    selectAll.addEventListener('click', () => {
        options.filter(option => !option.hidden).forEach(option => { option.querySelector('input').checked = true; });
        updateSelection();
    });
    clear.addEventListener('click', () => {
        options.forEach(option => { option.querySelector('input').checked = false; });
        updateSelection();
    });
    invitationForm.addEventListener('change', updateSelection);
    updateSelection();

    send.addEventListener('click', () => {
        dialog.querySelector('[data-invitation-count]').textContent = selected();
        modal.show(send);
    });
    dialog.addEventListener('hidden.bs.modal', () => send.focus());
    invitationForm.addEventListener('submit', event => {
        if (event.submitter?.id !== 'confirmInvitationSend' || !selected()) {
            event.preventDefault();
            return;
        }
        event.submitter.disabled = true;
        event.submitter.textContent = 'Sending…';
    });
}

const categorySelect = document.getElementById('invitationCategory');
categorySelect?.addEventListener('change', () => {
    categorySelect.form.requestSubmit();
});

const historyToggle = document.getElementById('invitationHistoryToggle');
historyToggle?.addEventListener('click', () => {
    const table = document.getElementById('invitationHistoryTable');
    table.hidden = !table.hidden;
    historyToggle.textContent = table.hidden ? 'Show table' : 'Hide table';
    historyToggle.setAttribute('aria-expanded', String(!table.hidden));
});
