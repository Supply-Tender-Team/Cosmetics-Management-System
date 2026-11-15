const departmentModal = document.getElementById('departmentModal');
if (departmentModal) {
    const title = document.getElementById('departmentModalTitle');
    const id = document.getElementById('departmentId');
    const name = document.getElementById('departmentName');
    const code = document.getElementById('departmentCode');
    const form = document.getElementById('departmentForm');

    function clearValidation() {
        form.querySelectorAll('.field-validation-error, .validation-summary-errors').forEach(element => {
            element.textContent = '';
            element.classList.remove('field-validation-error', 'validation-summary-errors');
        });
        form.querySelectorAll('.input-validation-error').forEach(element => element.classList.remove('input-validation-error'));
    }

    document.querySelectorAll('[data-department-add]').forEach(button => button.addEventListener('click', () => {
        clearValidation();
        title.textContent = 'Add Department';
        id.value = '0';
        name.value = '';
        code.value = '';
    }));

    document.querySelectorAll('[data-department-edit]').forEach(button => button.addEventListener('click', () => {
        clearValidation();
        title.textContent = 'Edit Department';
        id.value = button.dataset.id;
        name.value = button.dataset.name;
        code.value = button.dataset.code;
    }));

    if (departmentModal.dataset.openOnLoad === 'true') {
        title.textContent = id.value === '0' ? 'Add Department' : 'Edit Department';
        bootstrap.Modal.getOrCreateInstance(departmentModal).show();
    }
}
