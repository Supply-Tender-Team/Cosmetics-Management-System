(function () {
    function initializeSidebar() {
        var sidebar = document.getElementById("staff-sidebar");

        if (!sidebar) {
            return;
        }

        // Prevent duplicate event handlers.
        if (sidebar.dataset.controlsReady === "true") {
            return;
        }

        sidebar.dataset.controlsReady = "true";

        var body = document.body;

        var collapse = document.getElementById("sb-collapse");
        var toggle = document.getElementById("sidebarToggle");
        var close = document.getElementById("sb-close");
        var overlay = document.getElementById("sb-overlay");
        var content = document.getElementById("dashboard-content");

        var mobile = window.matchMedia(
            "(max-width: 860px)"
        );

        var storageKey = "mswand.sidebar.compact";

        // Restore the desktop preference.
        try {
            if (localStorage.getItem(storageKey) === "true") {
                body.classList.add("sidebar-compact");
            }
        } catch (error) {
            // The menu works even when storage is blocked.
        }

        function syncControls() {
            var compact = body.classList.contains(
                "sidebar-compact"
            );

            var open = body.classList.contains(
                "sidebar-open"
            );

            if (collapse) {
                collapse.setAttribute(
                    "aria-expanded",
                    compact ? "false" : "true"
                );

                collapse.setAttribute(
                    "aria-label",
                    compact ? "Expand sidebar" : "Collapse sidebar"
                );

                collapse.title = compact
                    ? "Expand sidebar"
                    : "Collapse sidebar";
            }

            if (toggle) {
                var expanded = mobile.matches ? open : !compact;

                toggle.setAttribute(
                    "aria-expanded",
                    expanded ? "true" : "false"
                );

                toggle.setAttribute(
                    "aria-label",
                    mobile.matches
                        ? (open ? "Close navigation" : "Open navigation")
                        : (compact ? "Expand sidebar" : "Collapse sidebar")
                );
            }

            sidebar.inert = mobile.matches && !open;

            if (mobile.matches) {
                sidebar.setAttribute(
                    "aria-hidden",
                    open ? "false" : "true"
                );
            } else {
                sidebar.removeAttribute("aria-hidden");
            }
        }

        // Desktop: expanded sidebar <-> icon rail.
        function changeCompact() {
            var compact = body.classList.toggle(
                "sidebar-compact"
            );

            try {
                localStorage.setItem(
                    storageKey,
                    compact ? "true" : "false"
                );
            } catch (error) {
                // Saving the preference is optional.
            }

            syncControls();
        }

        function focusableItems() {
            var items = sidebar.querySelectorAll(
                "a[href], button:not([disabled])"
            );

            return Array.from(items).filter(function (item) {
                return item.getClientRects().length > 0;
            });
        }

        function closeDrawer(returnFocus) {
            if (content) {
                content.inert = false;
            }

            if (returnFocus && toggle) {
                toggle.focus();
            }

            body.classList.remove("sidebar-open");

            if (overlay) {
                overlay.hidden = true;
            }

            syncControls();
        }

        function openDrawer() {
            body.classList.add("sidebar-open");

            if (overlay) {
                overlay.hidden = false;
            }

            syncControls();

            var target = close || focusableItems()[0];

            if (target) {
                target.focus();
            }

            if (content) {
                content.inert = true;
            }
        }

        // Sidebar arrow.
        if (collapse) {
            collapse.addEventListener(
                "click",
                changeCompact
            );
        }

        // Top-bar menu button.
        if (toggle) {
            toggle.addEventListener("click", function () {
                if (!mobile.matches) {
                    changeCompact();
                } else if (
                    body.classList.contains("sidebar-open")
                ) {
                    closeDrawer(true);
                } else {
                    openDrawer();
                }
            });
        }

        if (close) {
            close.addEventListener("click", function () {
                closeDrawer(true);
            });
        }

        if (overlay) {
            overlay.addEventListener("click", function () {
                closeDrawer(true);
            });
        }

        // Mobile keyboard controls.
        document.addEventListener("keydown", function (event) {
            if (
                !mobile.matches ||
                !body.classList.contains("sidebar-open")
            ) {
                return;
            }

            if (event.key === "Escape") {
                event.preventDefault();
                closeDrawer(true);
                return;
            }

            if (event.key !== "Tab") {
                return;
            }

            var items = focusableItems();

            if (items.length === 0) {
                return;
            }

            var first = items[0];
            var last = items[items.length - 1];

            if (
                event.shiftKey &&
                document.activeElement === first
            ) {
                event.preventDefault();
                last.focus();
            } else if (
                !event.shiftKey &&
                document.activeElement === last
            ) {
                event.preventDefault();
                first.focus();
            }
        });

        // Reset the drawer when changing screen size.
        mobile.addEventListener("change", function () {
            var focusInside = sidebar.contains(
                document.activeElement
            );

            var wasOpen = body.classList.contains(
                "sidebar-open"
            );

            closeDrawer(
                wasOpen || (mobile.matches && focusInside)
            );
        });

        // Highlight the current page.
        function normalize(path) {
            return path
                .toLowerCase()
                .replace(/\/+$/, "")
                .replace(/\/index$/, "")
                .replace(/\/dashboard$/, "") || "/";
        }

        var current = normalize(location.pathname);

        var best = null;
        var bestLength = -1;

        sidebar.querySelectorAll(".nav-link").forEach(
            function (link) {
                var label = link.querySelector("span");

                if (label) {
                    link.title = label.textContent.trim();
                }

                link.classList.remove("active", "is-active");
                link.removeAttribute("aria-current");

                var url = new URL(
                    link.href,
                    location.origin
                );

                if (url.origin !== location.origin) {
                    return;
                }

                var path = normalize(url.pathname);

                var matches =
                    current === path ||
                    (
                        path !== "/" &&
                        current.startsWith(path + "/")
                    );

                if (matches && path.length > bestLength) {
                    best = link;
                    bestLength = path.length;
                }
            }
        );

        if (best) {
            best.classList.add("is-active");
            best.setAttribute("aria-current", "page");
        }

        closeDrawer(false);
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeSidebar,
            { once: true }
        );
    } else {
        initializeSidebar();
    }
})();