const motion = matchMedia('(prefers-reduced-motion: reduce)');
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
    button.textContent = sound.paused ? 'Play soundtrack' : 'Stop soundtrack';
    button.setAttribute('aria-pressed', String(!sound.paused));
    document.querySelector('.wand-lipstick-trail')?.classList.toggle('has-soundtrack', !sound.paused);
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
document.querySelectorAll('.wand-story .container, .wand-grid, .wand-impact__grid, .wand-finale .container')
    .forEach(element => reveal.observe(element));

const detailsFilm = document.querySelector('.wand-impact__film video');
if (detailsFilm) {
    const playDetails = detailsFilm.parentElement.querySelector('.wand-details-play');
    let detailsVisible = false;

    function syncDetailsFilm() {
        if (!detailsVisible || document.hidden) {
            detailsFilm.pause();
            playDetails.hidden = true;
            return;
        }

        if (motion.matches) {
            playDetails.hidden = !detailsFilm.paused;
            return;
        }

        detailsFilm.play().catch(error => {
            if (detailsVisible && !document.hidden && error.name === 'NotAllowedError') {
                playDetails.hidden = false;
            }
        });
    }

    playDetails.addEventListener('click', () => {
        detailsFilm.play().catch(() => {});
    });
    detailsFilm.addEventListener('playing', () => { playDetails.hidden = true; });

    new IntersectionObserver(([entry]) => {
        detailsVisible = entry.intersectionRatio >= .45;
        syncDetailsFilm();
    }, { threshold: .45 }).observe(detailsFilm);
    document.addEventListener('visibilitychange', syncDetailsFilm);
    motion.addEventListener('change', syncDetailsFilm);
}

const storyChapters = document.querySelector('.wand-chapters');
if (storyChapters) {
    const chapters = [...storyChapters.querySelectorAll('details')];
    let activeChapter = chapters.findIndex(chapter => chapter.open);
    let storyVisible = false;
    let storySelected = false;
    let storyTimer;

    function syncStoryRotation() {
        clearInterval(storyTimer);
        if (storySelected || !storyVisible || document.hidden || motion.matches) return;

        storyTimer = setInterval(() => {
            activeChapter = (activeChapter + 1) % chapters.length;
            chapters[activeChapter].open = true;
        }, 3500);
    }

    chapters.forEach(chapter => {
        chapter.querySelector('summary').addEventListener('click', event => {
            event.preventDefault();
            storySelected = true;
            chapter.open = true;
            syncStoryRotation();
        });
    });

    new IntersectionObserver(([entry]) => {
        storyVisible = entry.isIntersecting;
        syncStoryRotation();
    }, { threshold: .2 }).observe(storyChapters);
    motion.addEventListener('change', syncStoryRotation);
    document.addEventListener('visibilitychange', syncStoryRotation);
}

const lipstickPage = document.querySelector('.wand-about');
const lipstickTrail = lipstickPage.querySelector('.wand-lipstick-trail');
const lipstick = lipstickTrail.querySelector(':scope > img');
const caption = lipstickTrail.querySelector('.wand-lipstick-caption');
const swatchTemplate = lipstickTrail.querySelector('template');
const variations = [[-12, 1, 0], [18, .71, 9], [-23, .86, 12], [8, 1.13, 2], [-5, .76, 17], [24, .95, 10]];
const sections = [...lipstickPage.querySelectorAll(':scope > section')];
let strokes = [];
let lipstickFrame = 0;
let drawnTo;

function layoutLipstick() {
    if (motion.matches) return;
    strokes.forEach(({ swatch }) => swatch.remove());
    strokes = [];
    const page = lipstickPage.getBoundingClientRect();
    const width = Math.min(104, Math.max(30, page.width * .052));
    for (const section of sections) {
        const bounds = section.getBoundingClientRect();
        const heading = document.getElementById(section.getAttribute('aria-labelledby'));
        const name = (section.querySelector('.wand-kicker') ?? heading)?.textContent.trim();
        const count = Math.max(1, Math.ceil(bounds.height / 320));
        const span = bounds.height / count;
        for (let i = 0; i < count; i++) {
            const [tilt, scale, inset] = variations[strokes.length % variations.length];
            const top = bounds.top - page.top + i * span;
            const left = page.width - width - 8 - inset;
            const height = span * (.88 + scale * .18);
            const swatch = swatchTemplate.content.firstElementChild.cloneNode();
            swatch.style.cssText = `left:${left}px;top:${top}px;width:${width}px;height:${height}px;clip-path:inset(0 0 100%);transform-origin:50% 0;rotate:${tilt}deg;`;
            lipstickTrail.prepend(swatch);
            strokes.push({ swatch, name, top, left, width, height, tilt });
        }
    }
    queueLipstick();
}

function drawLipstick() {
    lipstickFrame = 0;
    if (motion.matches || !strokes.length) return;
    const page = lipstickPage.getBoundingClientRect();
    const target = Math.max(0, Math.min(page.height - 24, innerHeight * .53 - page.top));
    const previous = drawnTo ?? target;
    drawnTo = Math.abs(target - previous) < .5
        ? target
        : Math.max(target - 180, Math.min(target + 180, previous + (target - previous) * .04));

    let active = strokes[0];
    for (const stroke of strokes) {
        const progress = Math.max(0, Math.min(1, (drawnTo - stroke.top) / stroke.height));
        stroke.swatch.style.clipPath = `inset(0 0 ${(1 - progress) * 100}%)`;
        if (drawnTo >= stroke.top) active = stroke;
    }

    const { name, top, left, width, height, tilt } = active;
    const progress = Math.max(0, Math.min(1, (drawnTo - top) / height));
    const wave = Math.sin(progress * Math.PI * 2);
    const offset = height * progress * Math.sin(tilt * Math.PI / 180);
    const x = Math.max(width * .5, Math.min(page.width - 12, left + width * (.53 - wave * .2) - offset));
    lipstick.style.transform = `translate(${x}px,${drawnTo}px) translateX(-63%) rotate(${155 + wave * 33 + tilt}deg)`;
    caption.style.transform = `translateY(${drawnTo}px)`;
    if (drawnTo !== target) queueLipstick();
    if (caption.textContent !== name) {
        caption.textContent = name;
        caption.animate([{ opacity: 0, translate: '10px 0' }, { opacity: 1, translate: '0 0' }], { duration: 420, easing: 'ease-out' });
    }
}

function queueLipstick() {
    if (!lipstickFrame) lipstickFrame = requestAnimationFrame(drawLipstick);
}

function syncLipstickMotion() {
    lipstickTrail.hidden = motion.matches;
    layoutLipstick();
}

new ResizeObserver(layoutLipstick).observe(lipstickPage);
window.addEventListener('scroll', queueLipstick, { passive: true });
motion.addEventListener('change', syncLipstickMotion);
syncLipstickMotion();
