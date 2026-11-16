(() => {
    const video = document.querySelector('.contact-background');
    const sound = document.querySelector('.contact-sound');
    const motion = matchMedia('(prefers-reduced-motion: reduce)');

    function syncSound() {
        sound.textContent = video.muted ? 'Play sound' : 'Mute sound';
        sound.setAttribute('aria-pressed', String(!video.muted));
    }

    function syncPlayback() {
        if (document.hidden || motion.matches) video.pause();
        else video.play().catch(() => {});
    }

    sound.hidden = false;
    sound.addEventListener('click', async () => {
        video.muted = !video.muted;
        if (!video.muted) {
            try { await video.play(); }
            catch { video.muted = true; }
        }
        syncSound();
    });

    document.addEventListener('visibilitychange', syncPlayback);
    motion.addEventListener('change', syncPlayback);
    if (!motion.matches && !document.hidden) {
        video.muted = false;
        video.play().catch(() => {
            video.muted = true;
            syncPlayback();
        }).finally(syncSound);
    }
})();
