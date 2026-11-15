const setup = document.getElementById('tenderSetup');
const steps = [...setup.querySelectorAll('[data-tender-step]')];
const label = document.getElementById('tenderStepLabel');
const navigation = setup.querySelector('[data-tender-navigation]');
const back = setup.querySelector('[data-tender-back]');
const next = setup.querySelector('[data-tender-next]');
const save = setup.querySelector('[data-tender-save]');
const publish = setup.querySelector('[data-tender-publish]');
let current = 0;

function showTenderStep(index) {
    current = index;
    steps.forEach((step, position) => { step.hidden = position !== index; });
    label.textContent = `Step ${index + 1} of 5 · ${steps[index].dataset.tenderStep}`;
    if (navigation) {
        navigation.hidden = index === 0;
        navigation.classList.toggle("d-flex", index !== 0);
        publish.hidden = index !== steps.length - 1;
        next.hidden = index === steps.length - 1;
        save.hidden = index !== steps.length - 1;
    }
    setup.querySelector('.modal-body').scrollTop = 0;
}

back?.addEventListener('click', () => showTenderStep(current - 1));
next?.addEventListener('click', () => {
    for (const input of steps[current].querySelectorAll('input, select, textarea')) {
        if (!input.reportValidity()) return;
    }
    showTenderStep(current + 1);
});

save?.form.addEventListener('submit', event => {
    if (event.submitter === publish && !window.confirm('Publish this tender and lock its evaluation criteria?')) event.preventDefault();
});

save?.form.addEventListener('invalid', event => {
    const index = steps.indexOf(event.target.closest('[data-tender-step]'));
    if (index >= 0 && steps[current] !== steps[index]) showTenderStep(index);
}, true);

showTenderStep(steps.length > 1 ? 1 : 0);
if (setup.dataset.resume === 'true') bootstrap.Modal.getOrCreateInstance(setup).show();
