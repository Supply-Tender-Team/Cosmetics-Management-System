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

function setPublishConfirmation(visible) {
    publishConfirmation.hidden = !visible;
    publishConfirm.disabled = !visible;
    publish.setAttribute('aria-expanded', String(visible));
}

publish?.addEventListener('click', () => {
    setPublishConfirmation(true);
    publishCancel.focus();
});
publishCancel?.addEventListener('click', () => {
    setPublishConfirmation(false);
    publish.focus();
});
let current = 0;

const questionRows = [...setup.querySelectorAll('[data-tender-question]')];
const addQuestion = setup.querySelector('[data-tender-add-question]');
const weightTotal = setup.querySelector('[data-tender-weight-total]');

function updateQuestionWeights() {
    const weights = questionRows.filter(row => !row.hidden)
        .map(row => row.querySelector('[data-tender-weight]'));
    const cents = input => Math.round((input.valueAsNumber || 0) * 100);
    const total = weights.reduce((sum, input) => sum + cents(input), 0);

    for (const input of weights) {
        input.max = (8000 - total + cents(input)) / 100;
    }

    addQuestion.textContent = weights.length === questionRows.length ? 'Next' : 'Add';
    weightTotal.textContent = total > 8000
        ? 'Technical weights cannot exceed 80%.'
        : `${(total / 100).toFixed(2)}% of 80% allocated`;
    return total;
}

if (addQuestion) {
    for (const row of questionRows) {
        row.querySelectorAll('input').forEach(input => { input.disabled = row.hidden; });
        row.querySelector('[data-tender-weight]').addEventListener('input', updateQuestionWeights);
    }

    addQuestion.addEventListener('click', () => {
        const inputs = setup.querySelectorAll('[data-tender-question]:not([hidden]) input');
        for (const input of inputs) {
            if (!input.reportValidity()) return;
        }

        const row = questionRows.find(question => question.hidden);
        if (!row) {
            next.click();
            return;
        }

        const remaining = 8000 - updateQuestionWeights();
        if (remaining <= 0) {
            weightTotal.textContent = 'Lower an existing weight to make room for the next question.';
            return;
        }

        const weight = row.querySelector('[data-tender-weight]');
        if (Math.round(weight.valueAsNumber * 100) > remaining) weight.value = '';
        row.hidden = false;
        row.querySelectorAll('input').forEach(input => { input.disabled = false; });
        updateQuestionWeights();
        row.querySelector('input').focus();
    });

    updateQuestionWeights();
}


function showTenderStep(index) {
    current = index;
    steps.forEach((step, position) => { step.hidden = position !== index; });
    label.textContent = `Step ${index + 1} of 5 · ${steps[index].dataset.tenderStep}`;
    if (navigation) {
        navigation.hidden = index === 0;
        navigation.classList.toggle("d-flex", index !== 0);
        setPublishConfirmation(false);
        publish.hidden = index !== steps.length - 1;
        next.hidden = index === steps.length - 1;
        save.hidden = index !== steps.length - 1;
    }
    setup.querySelector('.modal-body').scrollTop = 0;
}

back?.addEventListener('click', () => showTenderStep(current - 1));
next?.addEventListener('click', () => {
    for (const input of steps[current].querySelectorAll('input, select, textarea')) {
        if (!input.disabled && !input.reportValidity()) return;
    }
    if (steps[current].contains(addQuestion)) {
        const total = updateQuestionWeights();
        if (questionRows.some(row => row.hidden) || total !== 8000) {
            weightTotal.textContent = 'Add all six questions and allocate exactly 80% before continuing.';
            return;
        }
    }
    showTenderStep(current + 1);
});

save?.form.addEventListener('invalid', event => {
    const index = steps.indexOf(event.target.closest('[data-tender-step]'));
    if (index >= 0 && steps[current] !== steps[index]) showTenderStep(index);
}, true);

showTenderStep(steps.length > 1 ? 1 : 0);
if (setup.dataset.resume === 'true') bootstrap.Modal.getOrCreateInstance(setup).show();
