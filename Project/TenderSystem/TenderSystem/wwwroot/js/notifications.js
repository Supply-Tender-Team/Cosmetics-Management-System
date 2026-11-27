(() => {
    const root = document.getElementById("notificationPopoverRoot");
    const toggle = document.getElementById("notificationsToggle");
    const panel = document.getElementById("notificationPopover");
    const closeButton = document.getElementById("notificationPopoverClose");
    const content = document.getElementById("notificationsPopoverContent");
    const badge = document.getElementById("notifBadge");
    const antiForgeryToken = document.querySelector(
        "#notificationAntiForgeryToken input[name='__RequestVerificationToken']");

    if (!root || !toggle || !panel || !content) return;

    const countUrl = toggle.getAttribute("data-notification-count-url");
    const latestUrl = toggle.getAttribute("data-notification-latest-url");

    const setMessage = (message) => {
        content.replaceChildren();
        const note = document.createElement("p");
        note.className = "notifications-popover-note";
        note.textContent = message;
        content.append(note);
    };

    const renderNotifications = (items) => {
        content.replaceChildren();

        if (!items.length) {
            setMessage("No notifications yet.");
            return;
        }

        for (const item of items) {
            const card = document.createElement("article");
            card.className = "notifications-popover-item";
            if (!item.isRead) card.classList.add("is-unread");

            const headingRow = document.createElement("div");
            headingRow.className = "notifications-popover-item-heading";

            const title = document.createElement("h3");
            title.textContent = item.title || "Notification";
            headingRow.append(title);

            if (!item.isRead) {
                const unread = document.createElement("span");
                unread.className = "notifications-popover-new";
                unread.textContent = "New";
                headingRow.append(unread);
            }

            const message = document.createElement("p");
            message.textContent = item.message || "";

            const footer = document.createElement("div");
            footer.className = "notifications-popover-item-footer";

            const date = document.createElement("time");
            date.textContent = item.createdAt || "";
            footer.append(date);

            if (item.link) {
                try {
                    const detailsUrl = new URL(item.link, window.location.origin);

                    if (
                        ["http:", "https:"].includes(detailsUrl.protocol) &&
                        detailsUrl.origin === window.location.origin
                    ) {
                        const details = document.createElement("a");
                        details.href = detailsUrl.href;
                        details.className = "notifications-popover-details";
                        details.textContent = "View details";
                        footer.append(details);
                    }
                } catch {
                    // Ignore invalid notification links.
                }
            }

            if (!item.isRead && Number.isInteger(Number(item.id))) {
                const markRead = document.createElement("button");
                markRead.type = "button";
                markRead.className = "notifications-popover-details notifications-popover-mark-read";
                markRead.textContent = "Mark as read";
                markRead.dataset.notificationMarkRead = String(item.id);
                footer.append(markRead);
            }

            card.append(headingRow, message, footer);
            content.append(card);
        }
    };

    const loadLatest = async () => {
        if (!latestUrl) return;

        content.setAttribute("aria-busy", "true");
        setMessage("Loading notifications.");

        try {
            const response = await fetch(latestUrl, {
                credentials: "same-origin",
                headers: { Accept: "application/json" },
                cache: "no-store"
            });

            if (!response.ok) {
                setMessage("Notifications could not be loaded.");
                return;
            }

            const result = await response.json();
            renderNotifications(Array.isArray(result.items) ? result.items : []);
        } catch {
            setMessage("Notifications could not be loaded.");
        } finally {
            content.setAttribute("aria-busy", "false");
        }
    };

    const refreshUnreadCount = async () => {
        if (!countUrl || document.visibilityState !== "visible") return;

        try {
            const response = await fetch(countUrl, {
                credentials: "same-origin",
                headers: { Accept: "application/json" },
                cache: "no-store"
            });

            if (!response.ok) return;

            const result = await response.json();
            const count = Number(result.count);
            if (!Number.isInteger(count) || count < 0) return;

            if (badge) {
                badge.textContent = count > 99 ? "99+" : String(count);
                badge.hidden = count === 0;
            }

            toggle.setAttribute(
                "aria-label",
                count === 0 ? "Notifications" : "Notifications, " + count + " unread");

            if (!panel.hidden) await loadLatest();
        } catch {
            // Keep the last displayed count during temporary connection issues.
        }
    };

    toggle.addEventListener("click", () => {
        const opening = panel.hidden;
        panel.hidden = !opening;
        toggle.setAttribute("aria-expanded", String(opening));
        if (opening) void loadLatest();
    });

    content.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-notification-mark-read]");
        if (!button || !antiForgeryToken || button.disabled) return;

        const markReadUrl = toggle.getAttribute("data-notification-mark-read-url");
        if (!markReadUrl) return;

        button.disabled = true;
        const body = new URLSearchParams({ id: button.dataset.notificationMarkRead });

        try {
            const response = await fetch(markReadUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
                    "RequestVerificationToken": antiForgeryToken.value,
                    "X-Requested-With": "XMLHttpRequest",
                    "Accept": "application/json"
                },
                body,
                cache: "no-store"
            });

            if (!response.ok) throw new Error("Could not mark notification as read.");

            const card = button.closest(".notifications-popover-item");
            card?.classList.remove("is-unread");
            card?.querySelector(".notifications-popover-new")?.remove();
            button.remove();
            content.querySelector(".notifications-popover-error")?.remove();
            await refreshUnreadCount();
        } catch {
            button.disabled = false;
            let errorNote = content.querySelector(".notifications-popover-error");
            if (!errorNote) {
                errorNote = document.createElement("p");
                errorNote.className = "notifications-popover-note notifications-popover-error";
                content.prepend(errorNote);
            }
            errorNote.textContent = "The notification could not be marked as read. Please try again.";
        }
    });

    closeButton?.addEventListener("click", () => {
        panel.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
        toggle.focus();
    });

    document.addEventListener("click", (event) => {
        if (!panel.hidden && !root.contains(event.target)) {
            panel.hidden = true;
            toggle.setAttribute("aria-expanded", "false");
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && !panel.hidden) {
            panel.hidden = true;
            toggle.setAttribute("aria-expanded", "false");
            toggle.focus();
        }
    });

    void refreshUnreadCount();
    window.setInterval(refreshUnreadCount, 30000);
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") void refreshUnreadCount();
    });
})();

(() => {
    const list = document.querySelector("[data-notifications-list]");
    const filters = document.querySelectorAll("[data-notification-filter]");
    const emptyMessage = document.querySelector(".notifications-filter-empty");

    if (!list || !filters.length) return;

    for (const filter of filters) {
        filter.addEventListener("click", () => {
            const showUnreadOnly = filter.dataset.notificationFilter === "unread";
            let visibleCount = 0;

            for (const item of list.querySelectorAll("[data-notification-item]")) {
                const visible = !showUnreadOnly || item.dataset.isRead !== "true";
                item.hidden = !visible;
                if (visible) visibleCount += 1;
            }

            for (const button of filters) {
                const active = button === filter;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-pressed", String(active));
            }

            if (emptyMessage) emptyMessage.hidden = visibleCount > 0;
        });
    }
})();
