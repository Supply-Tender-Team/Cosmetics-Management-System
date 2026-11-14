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


const heroVideo = hero.querySelector('.wand-hero-video');
const captions = [...hero.querySelectorAll('[data-film-cue]')];
let activeCue = 0;
function updateCaption() {
    const time = motion.matches ? 0 : heroVideo.currentTime;
    const next = captions.findLastIndex(caption => Number(caption.dataset.filmCue) <= time);
    if (next < 0 || next === activeCue) return;
    captions[activeCue].hidden = true;
    captions[next].hidden = false;
    activeCue = next;
}
heroVideo.addEventListener('timeupdate', updateCaption);
heroVideo.addEventListener('seeked', updateCaption);
motion.addEventListener('change', updateCaption);

let heroVisible = false;
function syncHeroVideo() {
    if (!heroVisible || motion.matches || document.hidden) {
        heroVideo.pause();
        return;
    }
    if (!heroVideo.hasAttribute('src')) heroVideo.src = heroVideo.dataset.src;
    heroVideo.play().catch(() => {});
}
new IntersectionObserver(([entry]) => {
    heroVisible = entry.isIntersecting;
    syncHeroVideo();
}).observe(hero);
motion.addEventListener('change', syncHeroVideo);
document.addEventListener('visibilitychange', syncHeroVideo);

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
