const motion = matchMedia('(prefers-reduced-motion: reduce)');
const counters = document.querySelectorAll('[data-wand-count]');

if (!motion.matches && 'IntersectionObserver' in window) {
    const observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            const counter = entry.target;
            observer.unobserve(counter);
            const total = Number(counter.dataset.wandCount);
            const start = performance.now();
            function update(now) {
                const progress = motion.matches ? 1 : Math.min((now - start) / 650, 1);
                counter.textContent = Math.round(total * progress);
                if (progress < 1) requestAnimationFrame(update);
            }
            requestAnimationFrame(update);
        }
    }, { threshold: .5 });
    counters.forEach(counter => observer.observe(counter));
}


const hero = document.querySelector('.wand-hero-scroll');
const sound = hero.querySelector('audio');
const button = hero.querySelector('.wand-sound');
button.hidden = false;
button.addEventListener('click', async () => {
    if (!sound.paused) {
        sound.pause();
        return;
    }
    try { await sound.play(); }
    catch { button.textContent = 'Retry soundtrack'; }
});
function syncSound() {
    button.textContent = sound.paused ? 'Play soundtrack' : 'Pause soundtrack';
    button.setAttribute('aria-pressed', String(!sound.paused));
}
sound.addEventListener('play', syncSound);
sound.addEventListener('pause', syncSound);


const stage = hero.querySelector('.wand-vision');
let frame = 0;
function queueHero() {
    if (frame || motion.matches) return;
    frame = requestAnimationFrame(() => {
        frame = 0;
        const distance = Math.max(1, hero.offsetHeight - stage.offsetHeight);
        const p = Math.max(0, Math.min(1, -hero.getBoundingClientRect().top / distance));
        stage.style.setProperty('--hero-pan', (p * 56 - 12) + 'px');
        stage.style.setProperty('--hero-turn', (p * 34 - 15) + 'deg');
        stage.style.setProperty('--hero-zoom', String(1 + p * .15));
        stage.style.setProperty('--hero-caption', (-p * 45) + 'px');
    });
}
addEventListener('scroll', queueHero, { passive: true });
addEventListener('resize', queueHero);
motion.addEventListener('change', queueHero);
queueHero();

const reveal = new IntersectionObserver((entries) => {
    entries.forEach(({target, isIntersecting}) => {
        if (!isIntersecting) return;
        target.classList.add('wand-revealed');
        reveal.unobserve(target);
    });
}, { threshold: .12 });
document.querySelectorAll('.wand-story .container, .wand-grid, .wand-impact__grid, .wand-counts, .wand-finale .container')
    .forEach(element => reveal.observe(element));

const detailsFilm = document.querySelector('.wand-impact__film video');
if (detailsFilm && 'IntersectionObserver' in window) {
    const startDetails = new IntersectionObserver(([entry]) => {
        if (!entry.isIntersecting) return;
        startDetails.disconnect();
        if (!motion.matches) detailsFilm.play().catch(() => {});
    }, { threshold: .45 });
    startDetails.observe(detailsFilm);
}
