(() => {
    const video = document.querySelector('.contact-background');
    const sound = document.querySelector('.contact-sound');
    const motion = matchMedia('(prefers-reduced-motion: reduce)');

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
        sound.textContent = video.muted ? 'Play sound' : 'Mute sound';
        sound.setAttribute('aria-pressed', String(!video.muted));
    });

    document.addEventListener('visibilitychange', syncPlayback);
    motion.addEventListener('change', syncPlayback);
    syncPlayback();
})();
