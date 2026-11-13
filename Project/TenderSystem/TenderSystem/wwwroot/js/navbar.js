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
   Global brush and blush
   ========================= */

(function () {
    function initializeBrush() {
        var root = document.documentElement;

        if (root.dataset.msWandBrushReady === "true") {
            return;
        }

        var motionQuery = window.matchMedia(
            "(prefers-reduced-motion: reduce)"
        );

        var brush = document.createElement("div");

        brush.className = "nav-makeup-brush";
        brush.setAttribute("aria-hidden", "true");

        brush.innerHTML = `
            <svg viewBox="0 0 40 40" fill="none">

                <path
                    d="M3 3Q13 0 17 12L12 17Q0 13 3 3Z"
                    fill="#E6B6B2"
                    stroke="#805B52"
                    stroke-width="1.4" />

                <path
                    d="M5 5L12 13M9 4L14 11M4 9L11 14"
                    stroke="#C98F91"
                    stroke-width="1.2"
                    stroke-linecap="round" />

                <path
                    d="M12 17L17 12L23 18L18 23Z"
                    fill="#D8C1AE"
                    stroke="#805B52"
                    stroke-width="1.2" />

                <path
                    d="M18 23L23 18L36 31Q39 34 36 37Q34 39 31 36Z"
                    fill="#722F37"
                    stroke="#FAF5F0"
                    stroke-width="1" />

            </svg>
        `;

        document.body.appendChild(brush);

        // Keep the normal cursor if the brush CSS is missing.
        if (getComputedStyle(brush).position !== "fixed") {
            brush.remove();

            console.warn(
                "MS. WAND: brush styles are missing. Check navbar.css."
            );

            return;
        }

        root.dataset.msWandBrushReady = "true";

        var pointerX = 0;
        var pointerY = 0;

        var powderTimer = null;
        var tapTimer = null;

        function showBrush(x, y) {
            pointerX = x;
            pointerY = y;

            brush.style.left = (x - 3) + "px";
            brush.style.top = (y - 3) + "px";

            brush.classList.add("is-visible");
        }

        function spray(count) {
            // Reduced motion stops powder, not the brush itself.
            if (motionQuery.matches) {
                return;
            }

            for (var i = 0; i < count; i++) {
                var dot = document.createElement("span");
                var size = 3 + Math.random() * 5;

                dot.className = "nav-blush";
                dot.setAttribute("aria-hidden", "true");

                dot.style.left = pointerX + "px";
                dot.style.top = pointerY + "px";

                dot.style.width = size + "px";
                dot.style.height = size + "px";

                dot.style.backgroundColor =
                    i % 2 === 0 ? "#E6B6B2" : "#C98F91";

                dot.style.setProperty(
                    "--spray-x",
                    (Math.random() * 40 - 8) + "px"
                );

                dot.style.setProperty(
                    "--spray-y",
                    (Math.random() * 40 - 8) + "px"
                );

                document.body.appendChild(dot);

                setTimeout(function (particle) {
                    particle.remove();
                }, 750, dot);
            }
        }

        function stopPowder() {
            clearInterval(powderTimer);
            powderTimer = null;
        }

        function stopBrush() {
            stopPowder();
            clearTimeout(tapTimer);

            tapTimer = null;

            brush.classList.remove("is-visible");
            root.classList.remove("site-brush-active");
        }

        function startPowder() {
            if (
                motionQuery.matches ||
                powderTimer !== null
            ) {
                return;
            }

            spray(3);

            powderTimer = setInterval(function () {
                spray(2);
            }, 140);
        }

        // Mouse: show the brush across the whole document.
        document.addEventListener("pointermove", function (event) {
            if (event.pointerType !== "mouse") {
                return;
            }

            clearTimeout(tapTimer);
            tapTimer = null;

            showBrush(event.clientX, event.clientY);

            // Hide the normal cursor only after showing the brush.
            root.classList.add("site-brush-active");

            startPowder();
        }, { passive: true });

        // Touch and pen: brief effect without blocking the tap.
        document.addEventListener("pointerdown", function (event) {
            if (
                event.pointerType !== "touch" &&
                event.pointerType !== "pen"
            ) {
                return;
            }

            stopBrush();

            showBrush(event.clientX, event.clientY);
            spray(10);

            tapTimer = setTimeout(stopBrush, 550);
        }, { passive: true });

        // Hide the brush when the mouse leaves the document.
        document.addEventListener("pointerout", function (event) {
            if (
                event.pointerType === "mouse" &&
                event.relatedTarget === null
            ) {
                stopBrush();
            }
        });

        // Restore the normal cursor during keyboard navigation.
        document.addEventListener("keydown", function (event) {
            if (
                event.key === "Tab" ||
                event.key === "Escape"
            ) {
                stopBrush();
            }
        });

        document.addEventListener("visibilitychange", function () {
            if (document.hidden) {
                stopBrush();
            }
        });

        window.addEventListener("blur", stopBrush);
        window.addEventListener("pagehide", stopBrush);
        window.addEventListener("resize", stopBrush);

        motionQuery.addEventListener("change", function () {
            stopPowder();

            document.querySelectorAll(".nav-blush").forEach(
                function (dot) {
                    dot.remove();
                }
            );
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeBrush,
            { once: true }
        );
    } else {
        initializeBrush();
    }
})();