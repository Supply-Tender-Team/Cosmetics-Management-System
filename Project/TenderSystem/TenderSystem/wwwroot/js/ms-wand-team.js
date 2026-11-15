const team = document.querySelector('.wand-team');
const initials = [...team.querySelectorAll('.wand-team__initials button')];
const cards = [...team.querySelectorAll('.wand-team__card')];

const stage = team.querySelector(".wand-team__cards");
const burst = team.querySelector(".wand-team__burst");
const powder = team.querySelector(".wand-team__powder");
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
            const particles = Array.from({ length: 64 }, (_, index) => {
                const speck = document.createElement('i');
                const angle = index * 2.4;
                const distance = 2 + (index % 11) * .62;
                const progress = (index % 16 + .5) / 16;
                const side = Math.floor(index / 16);
                const x = side === 0 || side === 2 ? progress : side === 1 ? 1 : 0;
                const y = side === 1 || side === 3 ? progress : side === 2 ? 1 : 0;
                speck.style.left = (x * 100) + '%';
                speck.style.top = (y * 100) + '%';
                speck.style.setProperty('--scatter-x', Math.cos(angle) * distance);
                speck.style.setProperty('--scatter-y', Math.sin(angle) * distance);
                speck.style.setProperty('--grain', (2 + index % 5) + 'px');
                speck.style.setProperty('--delay', (index % 7) * 18 + 'ms');
                return speck;
            });
            powder.replaceChildren(...particles);
        }
        stage.dataset.shade = String(initials.indexOf(button) % 3);
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
