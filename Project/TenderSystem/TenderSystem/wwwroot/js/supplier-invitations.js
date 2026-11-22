document.addEventListener("DOMContentLoaded", () => {
    const cards = document.querySelectorAll(".supplier-invitation-card[data-expiry-utc]");

    const updateCountdown = (card) => {
        const deadline = Date.parse(card.dataset.expiryUtc || "");
        const countdown = card.querySelector("[data-countdown]");
        if (!Number.isFinite(deadline) || !countdown) return false;

        const remaining = deadline - Date.now();
        if (remaining <= 0) {
            countdown.textContent = "Expired";
            const status = card.querySelector("[data-invitation-status] span");
            if (status) status.textContent = "Expired";
            card.dataset.status = "Expired";
            card.querySelector("[data-invitation-status]")?.classList.add("is-expired");
            const actions = card.querySelector("[data-response-actions]");
            if (actions) actions.hidden = true;
            return false;
        }

        const totalMinutes = Math.floor(remaining / 60000);
        const days = Math.floor(totalMinutes / 1440);
        const hours = Math.floor((totalMinutes % 1440) / 60);
        const minutes = totalMinutes % 60;
        countdown.textContent = days > 0
            ? `${days}d ${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m left`
            : `${hours}h ${String(minutes).padStart(2, "0")}m left`;
        return true;
    };

    cards.forEach((card) => {
        if (!card.dataset.expiryUtc) return;
        updateCountdown(card);
        const timer = window.setInterval(() => {
            if (!updateCountdown(card)) window.clearInterval(timer);
        }, 30000);
    });
});
