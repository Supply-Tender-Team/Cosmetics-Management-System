const setup = document.getElementById('tenderSetup');
const steps = [...setup.querySelectorAll('[data-tender-step]')];
const label = document.getElementById('tenderStepLabel');
const navigation = setup.querySelector('[data-tender-navigation]');
const back = setup.querySelector('[data-tender-back]');
const next = setup.querySelector('[data-tender-next]');
const save = setup.querySelector('[data-tender-save]');
const publish = setup.querySelector('[data-tender-publish]');
const publishConfirmation = document.getElementById('tenderPublishConfirmation');
const publishCancel = setup.querySelector('[data-tender-publish-cancel]');
const publishConfirm = setup.querySelector('[data-tender-publish-confirm]');

publish?.addEventListener('click', () => {
    publishConfirmation.showModal();
    publishConfirm.disabled = false;
    publish.setAttribute('aria-expanded', 'true');
});
publishCancel?.addEventListener('click', () => publishConfirmation.close());
publishConfirmation?.addEventListener('keydown', event => {
    if (event.key === 'Escape') event.stopPropagation();
});
publishConfirmation?.addEventListener('close', () => {
    publishConfirm.disabled = true;
    publish.setAttribute('aria-expanded', 'false');
});

let current = 0;

const questionGroups = [...setup.querySelectorAll('[data-question-prefix]')];
const weightInCents = input => Math.round((input.valueAsNumber || 0) * 100);

function updateQuestionWeights(group) {
    const total = [...group.querySelectorAll('[data-tender-questions] [data-tender-weight]')]
        .reduce((sum, input) => sum + weightInCents(input), 0);
    const limit = Math.round(Number(group.dataset.questionLimit) * 100);
    group.querySelector('[data-tender-weight-total]').textContent =
        (total / 100).toFixed(2) + '% used of ' + (limit / 100) + '%';
    group.querySelector('[data-tender-weight-error]').textContent =
        total > limit ? 'Question weights exceed the department allocation.' : '';
    return total === limit;
}

function numberQuestions(group) {
    const prefix = group.dataset.questionPrefix;
    const rows = group.querySelector('[data-tender-questions]').children;
    [...rows].forEach((row, index) => {
        const name = row.querySelector('[data-question-name]');
        const weight = row.querySelector('[data-tender-weight]');
        name.name = prefix + '[' + index + '].Name';
        weight.name = prefix + '[' + index + '].Weight';
        name.id = prefix + '-question-' + index;
        weight.id = prefix + '-weight-' + index;
        const questionLabel = row.querySelector('[data-question-label]');
        questionLabel.htmlFor = name.id;
        questionLabel.textContent = 'Question ' + (index + 1);
        row.querySelector('[data-weight-label]').htmlFor = weight.id;
    });
    updateQuestionWeights(group);
}

questionGroups.forEach(group => {
    const questions = group.querySelector('[data-tender-questions]');
    const countInput = group.querySelector('input[id$="QuestionCount"]');
    const error = group.querySelector('[data-tender-weight-error]');
    numberQuestions(group);
    questions.addEventListener('input', event => {
        if (!event.target.matches('[data-tender-weight]')) return;
        delete event.target.dataset.autoWeight;
        updateQuestionWeights(group);
    });
    group.querySelector('[data-tender-set-count]').addEventListener('click', () => {
        const count = countInput.valueAsNumber;
        if (!Number.isInteger(count) || count < 1 || count > Number(countInput.max)) {
            error.textContent = 'Enter a valid number of questions.';
            countInput.focus();
            return;
        }
        while (questions.children.length > count) questions.lastElementChild.remove();
        for (let index = questions.children.length; index < count; index++) {
            const row = group.querySelector('template').content.cloneNode(true);
            row.querySelector('[data-tender-weight]').dataset.autoWeight = '';
            questions.append(row);
        }
        const weights = [...questions.querySelectorAll('[data-tender-weight]')];
        const automatic = weights.filter(input => input.hasAttribute('data-auto-weight'));
        const manualTotal = weights.filter(input => !input.hasAttribute('data-auto-weight'))
            .reduce((sum, input) => sum + weightInCents(input), 0);
        const available = Math.max(0, Math.round(Number(group.dataset.questionLimit) * 100) - manualTotal);
        if (automatic.length) {
            const share = Math.floor(available / automatic.length);
            const remainder = available % automatic.length;
            automatic.forEach((input, index) => {
                const cents = share + (index < remainder ? 1 : 0);
                input.value = cents > 0 ? (cents / 100).toFixed(2) : '';
            });
        }
        numberQuestions(group);
        questions.querySelector('[data-question-name]').focus();
    });
});

function showTenderStep(index) {
    current = index;
    steps.forEach((step, position) => { step.hidden = position !== index; });
    label.textContent = `Step ${index + 1} of ${navigation ? steps.length : 5} · ${steps[index].dataset.tenderStep}`;
    if (navigation) {
        navigation.hidden = index === 0;
        navigation.classList.toggle("d-flex", index !== 0);
        if (publishConfirmation.open) publishConfirmation.close();
        publish.hidden = index !== steps.length - 1;
        next.hidden = index === steps.length - 1;
        save.hidden = index !== steps.length - 1;
    }
    if (steps[index].dataset.tenderStep === 'Review tender') updateReview();
    setup.querySelector('.modal-body').scrollTop = 0;
}

back?.addEventListener('click', () => showTenderStep(current - 1));
next?.addEventListener('click', () => {
    const group = steps[current].matches('[data-question-prefix]') ? steps[current] : null;
    if (group) {
        const count = group.querySelector('input[id$="QuestionCount"]');
        const rows = group.querySelector('[data-tender-questions]').children;
        if (!rows.length || rows.length !== count.valueAsNumber || !updateQuestionWeights(group)) {
            group.querySelector('[data-tender-weight-error]').textContent =
                'Set the question count and distribute exactly ' + group.dataset.questionLimit + '%.';
            return;
        }
    }
    for (const input of steps[current].querySelectorAll('input, select, textarea')) {
        if (!input.disabled && !input.reportValidity()) return;
    }
    showTenderStep(current + 1);
});

save?.form.addEventListener('invalid', event => {
    const index = steps.indexOf(event.target.closest('[data-tender-step]'));
    if (index >= 0 && steps[current] !== steps[index]) showTenderStep(index);
}, true);

const category = setup.querySelector('[data-setup-category]');
const supplierRows = [...setup.querySelectorAll('[data-setup-supplier]')];
const selectAll = setup.querySelector('[data-setup-select-all]');
function updateSuppliers() {
    let visibleCount = 0;
    let selectedCount = 0;
    supplierRows.forEach(row => {
        const input = row.querySelector('input');
        row.hidden = Boolean(category.value && row.dataset.category !== category.value);
        row.classList.toggle('d-flex', !row.hidden);
        if (row.hidden) input.checked = false;
        else {
            visibleCount++;
            if (input.checked) selectedCount++;
        }
    });
    if (selectAll) {
        selectAll.checked = visibleCount > 0 && visibleCount === selectedCount;
        selectAll.indeterminate = selectedCount > 0 && selectedCount < visibleCount;
    }
    const count = setup.querySelector('[data-setup-selected-count]');
    if (count) count.textContent = selectedCount ? '(' + selectedCount + ' selected)' : '';
}
category?.addEventListener('change', updateSuppliers);
selectAll?.addEventListener('change', () => {
    supplierRows.filter(row => !row.hidden).forEach(row => { row.querySelector('input').checked = selectAll.checked; });
    updateSuppliers();
});
supplierRows.forEach(row => row.addEventListener('change', updateSuppliers));
if (category) updateSuppliers();

function updateReview() {
    for (const field of ['Title', 'Description', 'Terms', 'Deadline']) {
        setup.querySelector('[data-review-' + field.toLowerCase() + ']').textContent =
            setup.querySelector('[name="' + field + '"]').value;
    }
    const selectedCount = supplierRows.filter(row => row.querySelector('input').checked).length;
    setup.querySelector('[data-review-supplier-count]').textContent = selectedCount;
    publish.disabled = selectedCount === 0;
    const items = questionGroups.flatMap(group =>
        [...group.querySelector('[data-tender-questions]').children].map(row => {
            const item = document.createElement('li');
            item.textContent = group.dataset.tenderStep + ': ' + row.querySelector('[data-question-name]').value
                + ' (' + row.querySelector('[data-tender-weight]').value + '%)';
            return item;
        }));
    setup.querySelector('[data-review-questions]').replaceChildren(...items);
}

showTenderStep(steps.length > 1 ? 1 : 0);
if (setup.dataset.resume === 'true') bootstrap.Modal.getOrCreateInstance(setup).show();
