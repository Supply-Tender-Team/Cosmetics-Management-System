(() => {
    const countdownElements = document.querySelectorAll("[data-expiration-countdown]");

    function updateCountdowns() {
        const now = Date.now();

        countdownElements.forEach((element) => {
            const deadline = Date.parse(element.dataset.deadlineUtc || "");
            if (Number.isNaN(deadline)) return;

            const remainingMs = deadline - now;
            if (remainingMs <= 0) {
                element.textContent = "Expired";
                element.classList.add("is-expired");
                return;
            }

            const totalMinutes = Math.floor(remainingMs / 60000);
            const days = Math.floor(totalMinutes / 1440);
            const hours = Math.floor((totalMinutes % 1440) / 60);
            const minutes = totalMinutes % 60;

            element.textContent = days > 0
                ? `${days}d ${hours}h left`
                : `${hours}h ${minutes}m left`;
        });
    }

    updateCountdowns();
    window.setInterval(updateCountdowns, 60000);
})();
