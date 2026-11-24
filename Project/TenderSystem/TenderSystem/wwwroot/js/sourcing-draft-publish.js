{
    const form = document.querySelector('#publishDraft form');
    const selectAll = form?.querySelector('[data-draft-select-all]');

    if (selectAll) {
        const suppliers = [...form.querySelectorAll('input[name="supplierIds"]')];

        function updateSelection() {
            const count = suppliers.filter(supplier => supplier.checked).length;
            selectAll.checked = count === suppliers.length;
            selectAll.indeterminate = count > 0 && !selectAll.checked;
        }

        form.addEventListener('change', event => {
            if (event.target === selectAll) {
                suppliers.forEach(supplier => { supplier.checked = selectAll.checked; });
            }
            updateSelection();
        });

        updateSelection();
    }
}
