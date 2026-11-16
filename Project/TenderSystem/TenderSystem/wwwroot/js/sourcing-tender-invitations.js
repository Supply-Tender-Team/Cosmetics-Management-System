const category = document.getElementById('invitationCategory');
const invitationForm = document.getElementById('tenderInvitationForm');
const emptySuppliers = document.getElementById('tenderSupplierEmpty');
if (category && (invitationForm || emptySuppliers)) {
    const confirmedCategory = category.value;
    category.addEventListener('change', () => {
        if (invitationForm) invitationForm.hidden = category.value !== confirmedCategory;
        if (emptySuppliers) emptySuppliers.hidden = category.value !== confirmedCategory;
    });
}
if (invitationForm) {
    const suppliers = [...invitationForm.querySelectorAll('input[name="supplierIds"]')];
    const picker = document.getElementById('tenderSupplierPicker');
    const selectAll = document.getElementById('tenderSelectAll');
    const countLabel = document.getElementById('tenderSupplierCount');
    const reviewButton = document.getElementById('tenderReviewButton');
    const review = document.getElementById('tenderInvitationReview');
    const reviewTitle = document.getElementById('tenderReviewTitle');
    const reviewList = document.getElementById('tenderSelectedSuppliers');
    const send = document.getElementById('tenderInviteButton');
    const dialog = document.getElementById('confirmTenderInvitations');
    const modal = bootstrap.Modal.getOrCreateInstance(dialog);
    const selected = () => suppliers.filter(supplier => supplier.checked);

    function updateSelection() {
        const count = selected().length;
        countLabel.textContent = count ? '(' + count + ' selected)' : '';
        selectAll.checked = count === suppliers.length;
        selectAll.indeterminate = count > 0 && !selectAll.checked;
        reviewButton.disabled = count === 0;
        review.hidden = true;
    }

    selectAll.addEventListener('change', () => {
        suppliers.forEach(supplier => { supplier.checked = selectAll.checked; });
    });
    invitationForm.addEventListener('change', updateSelection);
    updateSelection();

    reviewButton.addEventListener('click', () => {
        reviewList.replaceChildren(...selected().map(supplier => {
            const item = document.createElement('li');
            item.textContent = supplier.closest('label').textContent.trim();
            return item;
        }));
        bootstrap.Dropdown.getInstance(picker)?.hide();
        review.hidden = false;
        reviewTitle.focus();
    });
    send.addEventListener('click', () => {
        const count = selected().length;
        dialog.querySelector('[data-invitation-count]').textContent = count + (count === 1 ? ' supplier' : ' suppliers');
        modal.show(send);
    });
    dialog.addEventListener('hidden.bs.modal', () => send.focus());
    invitationForm.addEventListener('submit', event => {
        if (event.submitter?.id !== 'confirmInvitationSend' || review.hidden || !selected().length) {
            event.preventDefault();
            return;
        }
        event.submitter.disabled = true;
        event.submitter.textContent = 'Sending…';
    });
}
const historyToggle = document.getElementById('invitationHistoryToggle');
historyToggle?.addEventListener('click', () => {
    const table = document.getElementById('invitationHistoryTable');
    table.hidden = !table.hidden;
    historyToggle.textContent = table.hidden ? 'Show table' : 'Hide table';
    historyToggle.setAttribute('aria-expanded', String(!table.hidden));
});
