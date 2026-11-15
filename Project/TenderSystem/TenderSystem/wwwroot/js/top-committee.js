document.addEventListener("DOMContentLoaded", function () {
    const page = document.querySelector(".top-committee-page");
    if (!page) return;

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    page.querySelectorAll(".committee-counter").forEach(function (counter, index) {
        const target = Number(counter.dataset.count);
        if (!Number.isFinite(target)) return;
        if (reduceMotion) {
            counter.textContent = String(target);
            return;
        }

        window.setTimeout(function () {
            const startedAt = performance.now();
            const duration = 900;

            function animate(now) {
                const progress = Math.min((now - startedAt) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 3);
                counter.textContent = String(Math.round(target * eased));
                if (progress < 1) requestAnimationFrame(animate);
            }

            counter.textContent = "0";
            requestAnimationFrame(animate);
        }, index * 100);
    });

    const items = page.querySelectorAll(".committee-reveal");
    if (reduceMotion || !("IntersectionObserver" in window)) return;

    items.forEach(function (item, index) {
        item.classList.add("is-pending");
        item.style.transitionDelay = Math.min(index * 70, 210) + "ms";
    });

    const observer = new IntersectionObserver(function (entries, currentObserver) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;
            entry.target.classList.add("is-visible");
            entry.target.style.transitionDelay = "0ms";
            currentObserver.unobserve(entry.target);
        });
    }, { threshold: 0.08 });

    items.forEach(function (item) {
        observer.observe(item);
    });
});
