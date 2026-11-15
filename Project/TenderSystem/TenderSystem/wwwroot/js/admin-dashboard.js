// Admin Dashboard (Dev 3): count-up animation for the statistic numbers.
// The real value is already printed by the server, so the page is correct even without JavaScript.
// This script only animates from 0 to that value and always finishes on the exact number.
(function () {
    // Only the counters of the Admin dashboard (the Sourcing dashboard shares some class names).
    const counters = document.querySelectorAll('.ad-admin [data-count-to]');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (counters.length === 0 || reduceMotion) return;

    const duration = 1200; // milliseconds
    const cardDelay = 70;  // same stagger as the card entrance in admin-dashboard.css (--i * 70ms)
    const format = value => value.toLocaleString('en-US');
    const easeOutCubic = t => 1 - Math.pow(1 - t, 3);

    counters.forEach(element => {
        const target = parseInt(element.dataset.countTo, 10);
        if (!Number.isFinite(target) || target <= 0) return;

        // Start counting when the card starts to fade in (its --i index), so the
        // final server value is never shown first and then reset to 0.
        const card = element.closest('.ad-kpi');
        const index = card ? parseInt(getComputedStyle(card).getPropertyValue('--i'), 10) || 0 : 0;
        const delay = index * cardDelay;

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
        setTimeout(() => requestAnimationFrame(step), delay);

        // Safety net: if frames are paused (for example the tab is in the background),
        // still show the exact value once the animation time is over.
        setTimeout(() => {
            finished = true;
            element.textContent = format(target);
        }, delay + duration + 200);
    });
})();
