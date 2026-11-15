const team = document.querySelector('.wand-team');
const initials = [...team.querySelectorAll('.wand-team__initials button')];
const cards = [...team.querySelectorAll('.wand-team__card')];

const stage = team.querySelector(".wand-team__cards");
const burst = team.querySelector(".wand-team__burst");
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

initials.forEach(button => {
    button.addEventListener('click', () => {
        if (button.getAttribute('aria-expanded') === 'true') return;
        const origin = button.getBoundingClientRect();
        const destination = stage.getBoundingClientRect();
        stage.style.setProperty("--throw-x", (origin.left + origin.width / 2 - destination.left - destination.width / 2) + "px");
        stage.style.setProperty("--throw-y", (origin.top + origin.height / 2 - destination.top) + "px");
        burst.classList.remove("is-flaring");
        if (!reducedMotion.matches) {
            void burst.offsetWidth;
            burst.classList.add("is-flaring");
        }
        initials.forEach(initial => initial.setAttribute('aria-expanded', String(initial === button)));
        cards.forEach(card => { card.hidden = card.id !== button.getAttribute('aria-controls'); });
    });
});

const mentors = team.querySelector('.wand-mentors');
const mentorBurst = mentors.querySelector('.wand-mentors__burst');
const motionToggle = mentors.querySelector('.wand-mentors__motion');
let clearBurst;

function syncMentorMotion() {
    motionToggle.hidden = reducedMotion.matches;
    if (reducedMotion.matches) mentorBurst.replaceChildren();
}

motionToggle.addEventListener('click', () => {
    const paused = mentors.classList.toggle('is-motion-paused');
    motionToggle.setAttribute('aria-pressed', String(paused));
    motionToggle.textContent = paused ? 'Resume card motion' : 'Pause card motion';
});
reducedMotion.addEventListener('change', syncMentorMotion);
syncMentorMotion();

const mentorOverlay = mentors.querySelector('dialog');
mentors.querySelector('.wand-mentors__trigger').addEventListener('click', () => {
    mentorOverlay.showModal();
    clearTimeout(clearBurst);
    mentorBurst.replaceChildren();
    if (reducedMotion.matches) return;

    const burstWidth = Math.min(690, mentorOverlay.clientWidth * .5);
    const particles = Array.from({ length: 110 }, (_, index) => {
        const grain = document.createElement('i');
        const angle = index * 2.39996;
        const distance = .3 + Math.random() * .7;
        grain.style.setProperty('--x', Math.cos(angle) * burstWidth * distance + 'px');
        grain.style.setProperty('--y', Math.sin(angle) * 255 * distance + 'px');
        grain.style.setProperty('--grain', 3 + Math.random() * 26 + 'px');
        return grain;
    });
    mentorBurst.append(...particles);
    clearBurst = setTimeout(() => mentorBurst.replaceChildren(), 1150);
});
