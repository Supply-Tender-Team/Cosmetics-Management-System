document.addEventListener("DOMContentLoaded", function () {
    const dashboard = document.querySelector(".manager-dashboard");
    if (!dashboard) return;

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const counters = dashboard.querySelectorAll(".manager-counter");

    function animateCounter(counter, delay) {
        const target = Number(counter.dataset.count);
        if (!Number.isFinite(target)) return;
        if (reduceMotion) {
            counter.textContent = String(target);
            return;
        }

        window.setTimeout(function () {
            const startTime = performance.now();
            const duration = 1000;

            function update(now) {
                const progress = Math.min((now - startTime) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 3);
                counter.textContent = String(Math.round(target * eased));
                if (progress < 1) requestAnimationFrame(update);
            }

            counter.textContent = "0";
            requestAnimationFrame(update);
        }, delay);
    }

    counters.forEach(function (counter, index) {
        animateCounter(counter, index * 110);
    });

    const revealItems = dashboard.querySelectorAll(".manager-reveal");
    if (reduceMotion || !("IntersectionObserver" in window)) return;

    revealItems.forEach(function (item, index) {
        item.classList.add("manager-reveal-pending");
        item.style.transitionDelay = Math.min(index * 100, 250) + "ms";
    });

    const observer = new IntersectionObserver(function (entries, currentObserver) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;
            entry.target.classList.add("manager-reveal-visible");
            entry.target.style.transitionDelay = "0ms";
            currentObserver.unobserve(entry.target);
        });
    }, { threshold: 0.08 });

    revealItems.forEach(function (item) {
        observer.observe(item);
    });
});
