/* =========================
   Mobile navbar
   ========================= */

(function () {
    function initializeNavbar() {
        var navbar = document.querySelector(".main-navbar");
        var toggle = document.getElementById("navbar-toggle");
        var links = document.getElementById("main-navbar-links");

        if (!navbar || !toggle || !links) {
            return;
        }

        if (navbar.dataset.menuReady === "true") {
            return;
        }

        navbar.dataset.menuReady = "true";

        var mobileQuery = window.matchMedia(
            "(max-width: 860px)"
        );

        function setMenu(open) {
            navbar.classList.toggle("menu-open", open);

            toggle.setAttribute(
                "aria-expanded",
                open ? "true" : "false"
            );

            toggle.setAttribute(
                "aria-label",
                open ? "Close navigation" : "Open navigation"
            );
        }

        toggle.addEventListener("click", function () {
            var isOpen = navbar.classList.contains("menu-open");
            setMenu(!isOpen);
        });

        links.addEventListener("click", function (event) {
            if (event.target.closest("a")) {
                setMenu(false);
            }
        });

        document.addEventListener("pointerdown", function (event) {
            if (!navbar.contains(event.target)) {
                setMenu(false);
            }
        });

        document.addEventListener("keydown", function (event) {
            if (
                event.key === "Escape" &&
                navbar.classList.contains("menu-open")
            ) {
                setMenu(false);
                toggle.focus();
            }
        });

        mobileQuery.addEventListener("change", function () {
            var focusWasInside = links.contains(
                document.activeElement
            );

            setMenu(false);

            if (mobileQuery.matches && focusWasInside) {
                toggle.focus();
            }
        });

        setMenu(false);
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeNavbar,
            { once: true }
        );
    } else {
        initializeNavbar();
    }
})();


/* =========================
   Home page MS. WAND magic cursor
   ========================= */

(function () {
    function initializeMagicCursor() {
        var root = document.documentElement;

        if (!document.querySelector("[data-home-magic-cursor]") || root.dataset.msWandMagicCursorReady === "true") {
            return;
        }

        var motionQuery = window.matchMedia(
            "(prefers-reduced-motion: reduce)"
        );

        var wand = document.createElement("div");

        wand.className = "nav-magic-wand";
        wand.setAttribute("aria-hidden", "true");

        wand.innerHTML = `
            <svg viewBox="0 0 40 40" fill="none">
                <defs>
                    <linearGradient id="wand-shaft" x1="10" y1="10" x2="34" y2="34" gradientUnits="userSpaceOnUse">
                        <stop stop-color="#C98F91" />
                        <stop offset=".52" stop-color="#722F37" />
                        <stop offset="1" stop-color="#4A302C" />
                    </linearGradient>
                    <linearGradient id="wand-star" x1="4" y1="3" x2="17" y2="17" gradientUnits="userSpaceOnUse">
                        <stop stop-color="#FFF9F2" />
                        <stop offset=".55" stop-color="#E6B6B2" />
                        <stop offset="1" stop-color="#C98F91" />
                    </linearGradient>
                </defs>
                <path d="M10 10L30 30" stroke="#FAF5F0" stroke-width="7" stroke-linecap="round" />
                <path d="M10 10L30 30" stroke="url(#wand-shaft)" stroke-width="4.6" stroke-linecap="round" />
                <path d="M13 13L29 29" stroke="#F4D6CE" stroke-opacity=".78" stroke-width=".9" stroke-linecap="round" />
                <path d="M27.3 27.3L32.7 27.3L37 31.6L31.6 37L27.3 32.7Z" fill="#D8C1AE" stroke="#4A302C" stroke-width="1" />
                <path d="M9 1.8L11 6.6L15.8 8.6L11 10.6L9 15.4L7 10.6L2.2 8.6L7 6.6L9 1.8Z" fill="url(#wand-star)" stroke="#722F37" stroke-width=".9" />
                <circle cx="9" cy="8.6" r="1.15" fill="#FFF9F2" />
                <path d="M20.8 8.2L21.8 10.8L24.4 11.8L21.8 12.8L20.8 15.4L19.8 12.8L17.2 11.8L19.8 10.8L20.8 8.2Z" fill="#D8C1AE" />
                <circle cx="4.5" cy="20" r="1.2" fill="#E6B6B2" />
            </svg>
        `;

        document.body.appendChild(wand);

        // Keep the normal cursor if the wand CSS is missing.
        if (getComputedStyle(wand).position !== "fixed") {
            wand.remove();

            console.warn(
                "MS. WAND: magic cursor styles are missing. Check navbar.css."
            );

            return;
        }

        root.dataset.msWandMagicCursorReady = "true";

        var pointerX = 0;
        var pointerY = 0;

        var tapTimer = null;
        var lastSparkleTime = 0;

        function showWand(x, y) {
            pointerX = x;
            pointerY = y;

            // Put the crystal tip exactly on the pointer hotspot.
            wand.style.left = (x - 8) + "px";
            wand.style.top = (y - 8) + "px";

            wand.classList.add("is-visible");
        }

        function sparkle(count) {
            // Reduced motion stops particles, not the wand cursor.
            if (motionQuery.matches) {
                return;
            }

            for (var i = 0; i < count; i++) {
                var star = document.createElement("span");
                var size = 3 + Math.random() * 4;

                star.className = "nav-sparkle";
                star.setAttribute("aria-hidden", "true");

                star.style.left = pointerX + "px";
                star.style.top = pointerY + "px";

                star.style.width = size + "px";
                star.style.height = size + "px";

                star.style.backgroundColor = ["#E6B6B2", "#D8C1AE", "#C98F91"][i % 3];

                star.style.setProperty(
                    "--spray-x",
                    (Math.random() * 28 - 14) + "px"
                );

                star.style.setProperty(
                    "--spray-y",
                    (Math.random() * 28 - 14) + "px"
                );

                document.body.appendChild(star);

                setTimeout(function (particle) {
                    particle.remove();
                }, 750, star);
            }
        }

        function stopWand() {
            clearTimeout(tapTimer);

            tapTimer = null;

            wand.classList.remove("is-visible");
            root.classList.remove("site-magic-cursor-active");
        }

        function sparkleOnMove() {
            var now = performance.now();
            if (motionQuery.matches || now - lastSparkleTime < 85) {
                return;
            }

            lastSparkleTime = now;
            sparkle(1);
        }

        // Mouse: show the wand across the whole document.
        document.addEventListener("pointermove", function (event) {
            if (event.pointerType !== "mouse") {
                return;
            }

            clearTimeout(tapTimer);
            tapTimer = null;

            showWand(event.clientX, event.clientY);

            // Hide the native cursor for the whole page while the wand is active.
            root.classList.add("site-magic-cursor-active");

            sparkleOnMove();
        }, { passive: true, capture: true });

        // Touch and pen: brief effect without blocking the tap.
        document.addEventListener("pointerdown", function (event) {
            if (
                event.pointerType !== "touch" &&
                event.pointerType !== "pen"
            ) {
                return;
            }

            stopWand();

            showWand(event.clientX, event.clientY);
            sparkle(5);

            tapTimer = setTimeout(stopWand, 550);
        }, { passive: true });

        // Hide the wand when the mouse leaves the document.
        document.addEventListener("pointerout", function (event) {
            if (
                event.pointerType === "mouse" &&
                event.relatedTarget === null
            ) {
                stopWand();
            }
        });

        // Restore the normal cursor during keyboard navigation.
        document.addEventListener("keydown", function (event) {
            if (
                event.key === "Tab" ||
                event.key === "Escape"
            ) {
                stopWand();
            }
        });

        document.addEventListener("visibilitychange", function () {
            if (document.hidden) {
                stopWand();
            }
        });

        window.addEventListener("blur", stopWand);
        window.addEventListener("pagehide", stopWand);
        window.addEventListener("resize", stopWand);

        motionQuery.addEventListener("change", function () {
            document.querySelectorAll(".nav-sparkle").forEach(
                function (dot) {
                    dot.remove();
                }
            );
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeMagicCursor,
            { once: true }
        );
    } else {
        initializeMagicCursor();
    }
})();
