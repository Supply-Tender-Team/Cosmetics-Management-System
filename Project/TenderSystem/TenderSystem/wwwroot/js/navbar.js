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
   Global MS. WAND magic cursor
   ========================= */

(function () {
    function initializeMagicCursor() {
        var root = document.documentElement;

        if (root.dataset.msWandMagicCursorReady === "true") {
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
        initializeWandScrollbars();

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

        function initializeWandScrollbars() {
            function attachScrollbar(track, thumb, readMetrics, writeScroll, scrollEventTarget, wheelTarget) {
                var dragging = false;
                var pointerId = null;
                var startY = 0;
                var startScroll = 0;
                var currentMetrics = null;

                function update() {
                    currentMetrics = readMetrics();
                    var trackHeight = track.clientHeight;
                    var maxScroll = currentMetrics.total - currentMetrics.visible;

                    if (maxScroll <= 1 || trackHeight <= 0) {
                        track.hidden = true;
                        return;
                    }

                    track.hidden = false;
                    var thumbHeight = Math.min(trackHeight, Math.max(38, trackHeight * currentMetrics.visible / currentMetrics.total));
                    var travel = trackHeight - thumbHeight;
                    var top = maxScroll > 0 ? currentMetrics.position / maxScroll * travel : 0;

                    thumb.style.height = thumbHeight + "px";
                    thumb.style.transform = "translateY(" + top + "px)";
                    thumb.setAttribute("aria-valuemax", String(Math.round(maxScroll)));
                    thumb.setAttribute("aria-valuenow", String(Math.round(currentMetrics.position)));
                    thumb.setAttribute("aria-valuetext", Math.round(currentMetrics.position) + " of " + Math.round(maxScroll) + " pixels scrolled");
                }

                function scrollToTrackPosition(clientY, centerThumb) {
                    currentMetrics = readMetrics();
                    var bounds = track.getBoundingClientRect();
                    var trackHeight = track.clientHeight;
                    var maxScroll = Math.max(0, currentMetrics.total - currentMetrics.visible);
                    var thumbHeight = parseFloat(thumb.style.height) || Math.max(38, trackHeight * currentMetrics.visible / currentMetrics.total);
                    var travel = Math.max(1, trackHeight - thumbHeight);
                    var offset = clientY - bounds.top - (centerThumb ? thumbHeight / 2 : thumbHeight / 2);
                    writeScroll(Math.max(0, Math.min(maxScroll, offset / travel * maxScroll)));
                }

                track.addEventListener("pointerdown", function (event) {
                    if (event.button !== 0) return;
                    event.preventDefault();
                    if (event.target !== thumb) scrollToTrackPosition(event.clientY, true);
                    currentMetrics = readMetrics();
                    startY = event.clientY;
                    startScroll = currentMetrics.position;
                    dragging = true;
                    pointerId = event.pointerId;
                    track.classList.add("is-dragging");
                    track.setPointerCapture(pointerId);
                    update();
                });

                track.addEventListener("pointermove", function (event) {
                    if (!dragging || event.pointerId !== pointerId) return;
                    var trackHeight = track.clientHeight;
                    var thumbHeight = parseFloat(thumb.style.height) || 38;
                    var travel = Math.max(1, trackHeight - thumbHeight);
                    var maxScroll = Math.max(0, currentMetrics.total - currentMetrics.visible);
                    writeScroll(Math.max(0, Math.min(maxScroll, startScroll + (event.clientY - startY) / travel * maxScroll)));
                });

                function finishDrag(event) {
                    if (!dragging || (event && event.pointerId !== pointerId)) return;
                    dragging = false;
                    pointerId = null;
                    track.classList.remove("is-dragging");
                }

                track.addEventListener("pointerup", finishDrag);
                track.addEventListener("pointercancel", finishDrag);
                track.addEventListener("lostpointercapture", finishDrag);
                track.addEventListener("wheel", function (event) {
                    if (wheelTarget) {
                        event.preventDefault();
                        wheelTarget.scrollTop += event.deltaY;
                    }
                }, { passive: false });

                thumb.addEventListener("keydown", function (event) {
                    var metrics = readMetrics();
                    var step = Math.max(48, metrics.visible * .12);
                    var next = metrics.position;
                    if (event.key === "ArrowDown" || event.key === "PageDown") next += event.key === "PageDown" ? metrics.visible : step;
                    else if (event.key === "ArrowUp" || event.key === "PageUp") next -= event.key === "PageUp" ? metrics.visible : step;
                    else if (event.key === "Home") next = 0;
                    else if (event.key === "End") next = metrics.total;
                    else return;
                    event.preventDefault();
                    writeScroll(next);
                });

                scrollEventTarget.addEventListener("scroll", update, { passive: true });
                window.addEventListener("resize", update, { passive: true });
                update();
                return update;
            }

            var sidebarNav = document.querySelector(".sb-navigation");
            if (sidebarNav && sidebarNav.parentElement) {
                var area = document.createElement("div");
                area.className = "wand-scroll-area";
                sidebarNav.parentElement.insertBefore(area, sidebarNav);
                area.appendChild(sidebarNav);
                sidebarNav.classList.add("wand-scroll-content");

                var sideTrack = document.createElement("div");
                sideTrack.className = "wand-scroll-track wand-scroll-track-sidebar";
                sideTrack.setAttribute("aria-label", "Scroll sidebar");
                var sideThumb = document.createElement("div");
                sideThumb.className = "wand-scroll-thumb";
                sideThumb.setAttribute("role", "scrollbar");
                sideThumb.setAttribute("aria-label", "Sidebar scroll position");
                sideThumb.setAttribute("aria-controls", sidebarNav.id || "sidebar-navigation");
                sideThumb.setAttribute("aria-orientation", "vertical");
                sideThumb.tabIndex = 0;
                sideTrack.appendChild(sideThumb);
                area.appendChild(sideTrack);

                attachScrollbar(
                    sideTrack,
                    sideThumb,
                    function () { return { total: sidebarNav.scrollHeight, visible: sidebarNav.clientHeight, position: sidebarNav.scrollTop }; },
                    function (position) { sidebarNav.scrollTop = position; },
                    sidebarNav,
                    sidebarNav
                );

                if (window.ResizeObserver) {
                    var sideObserver = new ResizeObserver(function () {
                        sideTrack.style.height = area.clientHeight + "px";
                    });
                    sideObserver.observe(area);
                    sideObserver.observe(sidebarNav);
                }
            }

            var useNativePageScrollbar = document.querySelector(
                ".contact-page, .wand-about, .wand-team, #home.hero, .page-placeholder, #staff-sidebar a[href*=\"/Sourcing/\"]"
            );
            if (!useNativePageScrollbar) {
                var pageTrack = document.createElement("div");
                pageTrack.className = "wand-scroll-track wand-scroll-track-page";
                pageTrack.setAttribute("aria-label", "Scroll page");
                var pageThumb = document.createElement("div");
                pageThumb.className = "wand-scroll-thumb";
                pageThumb.setAttribute("role", "scrollbar");
                pageThumb.setAttribute("aria-label", "Page scroll position");
                pageThumb.setAttribute("aria-controls", "mainContent");
                pageThumb.setAttribute("aria-orientation", "vertical");
                pageThumb.tabIndex = 0;
                pageTrack.appendChild(pageThumb);
                document.body.appendChild(pageTrack);
                document.documentElement.classList.add("wand-page-scroll-active");

                var updatePageScrollbar = attachScrollbar(
                    pageTrack,
                    pageThumb,
                    function () { return { total: document.documentElement.scrollHeight, visible: window.innerHeight, position: window.scrollY }; },
                    function (position) { window.scrollTo(0, position); },
                    window,
                    null
                );

                if (window.ResizeObserver) {
                    var pageObserver = new ResizeObserver(updatePageScrollbar);
                    pageObserver.observe(document.documentElement);
                    pageObserver.observe(document.body);
                }
            }
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
