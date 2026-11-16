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

const technicalWeightLimit = 8000;
const weightInCents = input => Math.round((input.valueAsNumber || 0) * 100);

function updateQuestionWeights() {
    const weights = [...questions.querySelectorAll('[data-tender-weight]')];
    const total = weights.reduce((sum, input) => sum + weightInCents(input), 0);

    weightTotal.textContent = total > technicalWeightLimit
        ? `${((total - technicalWeightLimit) / 100).toFixed(2)}% over the limit`
        : `${((technicalWeightLimit - total) / 100).toFixed(2)}% remaining · ${(total / 100).toFixed(2)}% used`;
    weightError.textContent = total > technicalWeightLimit ? 'Technical weights cannot exceed 80%.' : '';
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
    questions.addEventListener('input', event => {
        if (!event.target.matches('[data-tender-weight]')) return;
        delete event.target.dataset.autoWeight;
        updateQuestionWeights();
    });

    setCount.addEventListener('click', () => {
        const count = questionCount.valueAsNumber;
        if (!Number.isInteger(count) || count < 1 || count > Number(questionCount.max)) {
            weightError.textContent = 'Enter a valid number of questions.';
            questionCount.focus();
            return;
        }

        while (questions.children.length > count) questions.lastElementChild.remove();
        for (let index = questions.children.length; index < count; index++) {
            const row = questionTemplate.content.cloneNode(true);
            const name = row.querySelector('[data-question-name]');
            name.value = questionExamples[index] || '';
            name.placeholder = 'Ask for specific evidence relevant to this offer';
            row.querySelector('[data-tender-weight]').dataset.autoWeight = '';
            questions.append(row);
        }

        const weights = [...questions.querySelectorAll('[data-tender-weight]')];
        const automatic = weights.filter(input => input.hasAttribute('data-auto-weight'));
        const manualTotal = weights.filter(input => !input.hasAttribute('data-auto-weight'))
            .reduce((sum, input) => sum + weightInCents(input), 0);
        const available = Math.max(0, technicalWeightLimit - manualTotal);
        if (automatic.length) {
            const share = Math.floor(available / automatic.length);
            const remainder = available % automatic.length;
            automatic.forEach((input, index) => {
                const cents = share + (index < remainder ? 1 : 0);
                input.value = cents > 0 ? (cents / 100).toFixed(2) : '';
            });
        }

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
    if (questions && steps[current].contains(questions) && updateQuestionWeights() > technicalWeightLimit) return;
    showTenderStep(current + 1);
});

save?.form.addEventListener('invalid', event => {
    const index = steps.indexOf(event.target.closest('[data-tender-step]'));
    if (index >= 0 && steps[current] !== steps[index]) showTenderStep(index);
}, true);

showTenderStep(steps.length > 1 ? 1 : 0);
if (setup.dataset.resume === 'true') bootstrap.Modal.getOrCreateInstance(setup).show();
