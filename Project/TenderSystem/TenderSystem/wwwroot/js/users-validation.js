(function () {
    'use strict';
    document.addEventListener('invalid', event => {
        const field = event.target;
        if (!field.closest('.users-create-panel')) return;
        const message = field.parentElement.querySelector('[data-valmsg-for="' + field.name + '"]');
        if (!message) return;
        message.textContent = field.validity.valueMissing ? 'This field is required.' : field.validationMessage;
        field.classList.add('input-validation-error');
    }, true);
    document.addEventListener('input', event => {
        const field = event.target;
        if (!field.matches('.users-create-panel input, .users-create-panel select') || !field.validity.valid) return;
        const message = field.parentElement.querySelector('[data-valmsg-for="' + field.name + '"]');
        if (message) message.textContent = '';
        field.classList.remove('input-validation-error');
    });
})();
