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

const questions = setup.querySelector('[data-tender-questions]');
const questionTemplate = setup.querySelector('[data-tender-question-template]');
const questionCount = document.getElementById('technicalQuestionCount');
const setCount = setup.querySelector('[data-tender-set-count]');
const weightTotal = setup.querySelector('[data-tender-weight-total]');
const weightError = setup.querySelector('[data-tender-weight-error]');

function updateQuestionWeights() {
    const weights = [...questions.querySelectorAll('[data-tender-weight]')];
    const cents = input => Math.max(0, Math.round((input.valueAsNumber || 0) * 100));
    const total = weights.reduce((sum, input) => sum + cents(input), 0);
    for (const input of weights) {
        input.max = Math.max(0, 8000 - total + cents(input)) / 100;
    }

    weightTotal.textContent = total > 8000
        ? `${((total - 8000) / 100).toFixed(2)}% over the limit`
        : `${((8000 - total) / 100).toFixed(2)}% remaining · ${(total / 100).toFixed(2)}% used`;
    weightError.textContent = total > 8000 ? 'Technical weights cannot exceed 80%.' : '';
    return total;
}

function numberQuestions() {
    [...questions.children].forEach((row, index) => {
        const name = row.querySelector('[data-question-name]');
        const weight = row.querySelector('[data-tender-weight]');
        name.name = `Questions[${index}].Name`;
        weight.name = `Questions[${index}].Weight`;
        name.id = `technical-question-${index}`;
        weight.id = `technical-weight-${index}`;
        const label = row.querySelector('[data-question-label]');
        label.htmlFor = name.id;
        label.textContent = `Question ${index + 1}`;
        row.querySelector('[data-weight-label]').htmlFor = weight.id;
    });
    updateQuestionWeights();
}

const questionExamples = [
    'How many comparable clients have you served in the last three years, split by for-profit and nonprofit clients? Give two relevant examples.',
    'If relevant to this tender, how many young people did comparable projects reach, which age groups, and what outcomes can you verify?',
    'How does your offer meet each required specification and quantity? List any deviations and attach evidence that supports your claims.',
    'How do you approve samples, verify batch quality, and trace defects? Share your inspection criteria and a corrective action example.',
    'What production capacity and lead times support your delivery dates, and how would you handle an interruption without missing milestones?',
    'What acceptance, replacement, and after-sales commitments do you offer, and how quickly will you resolve defects?',
    'How do you manage material compatibility and consistency across production lots? Provide test results or relevant certifications.',
    'Describe a comparable delivery that missed a target. What triggered it, how did you respond, and what measurable improvement followed?'
];

if (questions) {
    numberQuestions();
    questions.addEventListener('input', updateQuestionWeights);
    setCount.addEventListener('click', () => {
        const count = questionCount.valueAsNumber;
        if (!Number.isInteger(count) || count < 1 || count > Number(questionCount.max)) {
            weightError.textContent = 'Enter a valid number of questions.';
            questionCount.focus();
            return;
        }
        const firstNewIndex = questions.children.length;
        const addedCount = count - firstNewIndex;
        if (addedCount > 0) {
            const remaining = Math.max(0, 8000 - updateQuestionWeights());
            const share = Math.floor(remaining / addedCount);
            const extra = remaining % addedCount;

            for (let offset = 0; offset < addedCount; offset++) {
                const row = questionTemplate.content.cloneNode(true);
                const name = row.querySelector('[data-question-name]');
                const weight = row.querySelector('[data-tender-weight]');
                name.value = questionExamples[firstNewIndex + offset] || '';
                name.placeholder = 'Ask for specific evidence relevant to this offer';
                const cents = share + (offset < extra ? 1 : 0);
                if (cents > 0) weight.value = (cents / 100).toFixed(2);
                weight.placeholder = 'Enter weight';
                questions.append(row);
            }
        }
        while (questions.children.length > count) questions.lastElementChild.remove();
        numberQuestions();
        questions.querySelector('[data-question-name]').focus();
    });
}

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
    setup.querySelector('.modal-body').scrollTop = 0;
}

back?.addEventListener('click', () => showTenderStep(current - 1));
next?.addEventListener('click', () => {
    if (questions && steps[current].contains(questions) &&
        (!questions.children.length || questions.children.length !== questionCount.valueAsNumber)) {
        weightError.textContent = 'Set the number of questions before continuing.';
        questionCount.focus();
        return;
    }
    for (const input of steps[current].querySelectorAll('input, select, textarea')) {
        if (!input.disabled && !input.reportValidity()) return;
    }
    if (questions && steps[current].contains(questions) && updateQuestionWeights() > 8000) return;
    showTenderStep(current + 1);
});

save?.form.addEventListener('invalid', event => {
    const index = steps.indexOf(event.target.closest('[data-tender-step]'));
    if (index >= 0 && steps[current] !== steps[index]) showTenderStep(index);
}, true);

showTenderStep(steps.length > 1 ? 1 : 0);
if (setup.dataset.resume === 'true') bootstrap.Modal.getOrCreateInstance(setup).show();
