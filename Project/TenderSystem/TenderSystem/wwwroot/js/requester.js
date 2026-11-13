document.addEventListener("DOMContentLoaded", function () {
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const requestFilter = document.querySelector("[data-request-filter]");

    if (requestFilter) {
        const searchInput = requestFilter.querySelector("[data-request-search]");
        const statusInput = requestFilter.querySelector("[data-request-status]");
        const requestRows = document.querySelectorAll("[data-request-row]");
        const countLabel = document.querySelector("[data-request-count]");
        const noResults = document.querySelector("[data-request-no-results]");

        function filterRequests() {
            const searchText = (searchInput.value || "").trim().toLocaleLowerCase();
            const selectedStatus = statusInput.value;
            let visibleCount = 0;

            requestRows.forEach(function (row) {
                const matchesSearch = row.dataset.searchText.includes(searchText);
                const matchesStatus = !selectedStatus || row.dataset.status === selectedStatus;
                const isVisible = matchesSearch && matchesStatus;
                row.classList.toggle("d-none", !isVisible);
                if (isVisible) visibleCount += 1;
            });

            if (countLabel) {
                countLabel.textContent = visibleCount + " request" + (visibleCount === 1 ? "" : "s") + " found";
            }
            if (noResults) noResults.classList.toggle("d-none", visibleCount !== 0);
        }

        searchInput.addEventListener("input", filterRequests);
        statusInput.addEventListener("change", filterRequests);
        requestFilter.addEventListener("submit", function (event) {
            event.preventDefault();
            filterRequests();
        });
        filterRequests();
    }

    const counters = document.querySelectorAll(".requester-counter");

    function animateCount(element, target, suffix) {
        if (reduceMotion) {
            element.textContent = target + suffix;
            return;
        }

        const startTime = performance.now();
        const duration = 1000;

        function update(now) {
            const progress = Math.min((now - startTime) / duration, 1);
            const eased = 1 - Math.pow(1 - progress, 3);
            element.textContent = Math.round(target * eased) + suffix;

            if (progress < 1) requestAnimationFrame(update);
        }

        element.textContent = "0" + suffix;
        requestAnimationFrame(update);
    }

    counters.forEach(function (counter, index) {
        const target = Number(counter.dataset.count);
        if (!Number.isFinite(target)) return;
        window.setTimeout(function () {
            animateCount(counter, target, "");
        }, index * 100);
    });

    const approvalCircle = document.querySelector(".requester-approval-circle");
    const approvalCount = document.querySelector(".requester-approval-count");
    let approvalStarted = false;

    function animateApproval() {
        if (!approvalCircle || !approvalCount || approvalStarted) return;
        approvalStarted = true;

        const target = Math.max(0, Math.min(100, Number(approvalCount.dataset.count) || 0));
        if (reduceMotion) {
            approvalCircle.style.setProperty("--approval-percent", target);
            approvalCount.textContent = target + "%";
            return;
        }

        const startTime = performance.now();
        const duration = 1200;
        approvalCircle.style.setProperty("--approval-percent", 0);

        function update(now) {
            const progress = Math.min((now - startTime) / duration, 1);
            const eased = 1 - Math.pow(1 - progress, 3);
            const value = target * eased;
            approvalCircle.style.setProperty("--approval-percent", value);
            approvalCount.textContent = Math.round(value) + "%";

            if (progress < 1) requestAnimationFrame(update);
        }

        approvalCount.textContent = "0%";
        requestAnimationFrame(update);
    }

    const progressBars = document.querySelectorAll(".requester-progress-fill");
    progressBars.forEach(function (bar) {
        bar.dataset.targetWidth = bar.style.width;
        if (!reduceMotion) bar.style.width = "0%";
    });

    function animateProgressBar(bar, delay) {
        const targetWidth = Math.max(0, Math.min(100, Number.parseFloat(bar.dataset.targetWidth) || 0));
        if (reduceMotion) {
            bar.style.width = targetWidth + "%";
            return;
        }

        window.setTimeout(function () {
            let startTime = null;
            const duration = 1700;

            function update(now) {
                if (startTime === null) startTime = now;
                const progress = Math.min((now - startTime) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 3);
                bar.style.width = targetWidth * eased + "%";

                if (progress < 1) requestAnimationFrame(update);
            }

            requestAnimationFrame(update);
        }, delay);
    }

    const revealSections = document.querySelectorAll(".requester-reveal");
    if (reduceMotion || !("IntersectionObserver" in window)) {
        progressBars.forEach(function (bar) {
            bar.style.width = bar.dataset.targetWidth;
        });
        animateApproval();
        return;
    }

    revealSections.forEach(function (section, index) {
        section.classList.add("requester-reveal-pending");
        section.style.transitionDelay = Math.min(index * 100, 250) + "ms";
    });

    const revealObserver = new IntersectionObserver(function (entries, observer) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;

            entry.target.classList.add("requester-reveal-visible");
            entry.target.style.transitionDelay = "0ms";
            if (entry.target.contains(approvalCircle)) animateApproval();
            observer.unobserve(entry.target);
        });
    }, { threshold: 0.08 });

    revealSections.forEach(function (section) {
        revealObserver.observe(section);
    });

    let visibleProgressBarCount = 0;
    const progressObserver = new IntersectionObserver(function (entries, observer) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;

            animateProgressBar(entry.target, visibleProgressBarCount * 180);
            visibleProgressBarCount += 1;
            observer.unobserve(entry.target);
        });
    }, { threshold: 0.2 });

    progressBars.forEach(function (bar) {
        progressObserver.observe(bar);
    });
});
