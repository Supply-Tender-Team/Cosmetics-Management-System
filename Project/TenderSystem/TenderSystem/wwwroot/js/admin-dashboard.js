// Admin Dashboard (Dev 3): count-up animation for the statistic numbers.
// The real value is already printed by the server, so the page is correct even without JavaScript.
// This script only animates from 0 to that value and always finishes on the exact number.
(function () {
    const counters = document.querySelectorAll('[data-count-to]');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (counters.length === 0 || reduceMotion) return;

    const duration = 1500; // milliseconds
    const format = value => value.toLocaleString('en-US');
    const easeOutCubic = t => 1 - Math.pow(1 - t, 3);

    counters.forEach(element => {
        const target = parseInt(element.dataset.countTo, 10);
        if (!Number.isFinite(target) || target <= 0) return;

        // The start time is taken on the first frame. If the browser never draws a frame
        // (for example a hidden tab), the real server value simply stays on screen.
        let start = null;
        let finished = false;

        function step(now) {
            if (finished) return;
            if (start === null) start = now;
            const progress = Math.min((now - start) / duration, 1);
            element.textContent = format(Math.round(target * easeOutCubic(progress)));
            if (progress < 1) requestAnimationFrame(step);
            else element.textContent = format(target); // exact final value
        }
        requestAnimationFrame(step);

        // Safety net: if frames are paused (for example the tab is in the background),
        // still show the exact value once the animation time is over.
        setTimeout(() => {
            finished = true;
            element.textContent = format(target);
        }, duration + 200);
    });
})();
