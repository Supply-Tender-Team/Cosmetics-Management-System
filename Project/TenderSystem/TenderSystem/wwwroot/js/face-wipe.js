var MAX_RETRIES = 3;
var faceAttempts = 0;
var wiping = false;
var box = document.getElementById('faceBox');
var shell = document.querySelector('.aura-login-shell');
window.faceWipePromise = null;
window.resetFaceAttempts = function () { faceAttempts = 0; };

(function () {
    var rnd = function (a, b) { return a + Math.random() * (b - a); };
    var clamp = function (v, a, b) { return Math.max(a, Math.min(b, v)); };
    var easeIO = function (p) { return .5 - .5 * Math.cos(Math.PI * p); };

    var T_FOG = .35, T_WIPE = 1.9, T_FADE = .25, T_GLINT = .45;
    var TOTAL = T_FOG + T_WIPE + T_FADE + T_GLINT;

    /* مسار القماشة (نسب من حجم الإطار): تدخل من اليمين، صفوف متداخلة، تلميع دائري، وتطلع */
    var WAYPOINTS = [
        [1.45, .2], [.95, .22], [.1, .2], [-.14, .3], [-.06, .5], [.5, .5], [1.06, .5],
        [1.14, .72], [.6, .78], [.05, .74], [.3, .58], [.62, .4], [.45, .3], [.5, .5], [1.5, .46]
    ];

    /* ---------- أدوات المسار ---------- */
    function catmull(p0, p1, p2, p3, t) {
        var t2 = t * t, t3 = t2 * t;
        return [0, 1].map(function (i) {
            return .5 * ((2 * p1[i]) + (-p0[i] + p2[i]) * t + (2 * p0[i] - 5 * p1[i] + 4 * p2[i] - p3[i]) * t2 +
                (-p0[i] + 3 * p1[i] - 3 * p2[i] + p3[i]) * t3);
        });
    }
    function buildPath(w, h) {
        var P = [WAYPOINTS[0]].concat(WAYPOINTS, [WAYPOINTS[WAYPOINTS.length - 1]]);
        var pts = [];
        for (var i = 1; i < P.length - 2; i++)
            for (var k = 0; k < 28; k++) {
                var q = catmull(P[i - 1], P[i], P[i + 1], P[i + 2], k / 28);
                pts.push([q[0] * w, q[1] * h]);
            }
        var len = [0];
        for (var j = 1; j < pts.length; j++)
            len[j] = len[j - 1] + Math.hypot(pts[j][0] - pts[j - 1][0], pts[j][1] - pts[j - 1][1]);
        return { pts: pts, len: len, total: len[len.length - 1] };
    }
    function pointAt(path, s) {
        var d = s * path.total, lo = 0, hi = path.len.length - 1;
        while (hi - lo > 1) { var m = (lo + hi) >> 1; if (path.len[m] < d) lo = m; else hi = m; }
        var seg = path.len[hi] - path.len[lo] || 1, f = (d - path.len[lo]) / seg;
        return [path.pts[lo][0] + (path.pts[hi][0] - path.pts[lo][0]) * f,
                path.pts[lo][1] + (path.pts[hi][1] - path.pts[lo][1]) * f];
    }

    /* ---------- الطبقة الضبابية: لقطة مموّهة من الفيديو + بصمات + قطرات + غبار ---------- */
    function drawCover(g, v, w, h) {
        var vw = v.videoWidth, vh = v.videoHeight; if (!vw || !vh) return;
        var s = Math.max(w / vw, h / vh), dw = vw * s, dh = vh * s;
        var m = /matrix\(\s*(-?[\d.]+)/.exec(getComputedStyle(v).transform || '');
        g.save();
        if (m && parseFloat(m[1]) < 0) { g.translate(w, 0); g.scale(-1, 1); }   // الفيديو معكوس (مرآة)
        g.drawImage(v, (w - dw) / 2, (h - dh) / 2, dw, dh);
        g.restore();
    }

    function fingerprint(g, x, y, rad, rot) {
        g.save(); g.translate(x, y); g.rotate(rot);
        var oil = g.createRadialGradient(0, 0, 0, 0, 0, rad * 1.1);
        oil.addColorStop(0, 'rgba(255,255,255,.16)'); oil.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = oil; g.beginPath(); g.ellipse(0, 0, rad * .85, rad * 1.1, 0, 0, 7); g.fill();
        g.lineWidth = 1; g.strokeStyle = 'rgba(255,255,255,.2)';
        for (var i = 1; i <= 9; i++) {
            var a0 = rnd(0, .5) * Math.PI, a1 = a0 + rnd(1.2, 1.9) * Math.PI;
            g.beginPath(); g.ellipse(0, 0, i * rad / 10 * .8, i * rad / 10 * 1.05, 0, a0, a1); g.stroke();
        }
        g.restore();
    }

    function buildFog(w, h, dpr, video) {
        var c = document.createElement('canvas');
        c.width = Math.round(w * dpr); c.height = Math.round(h * dpr);
        var g = c.getContext('2d'); g.scale(dpr, dpr);
        var m = Math.min(w, h);

        if (video && video.readyState >= 2 && 'filter' in g) {
            try { g.filter = 'blur(8px)'; drawCover(g, video, w, h); g.filter = 'none'; } catch (e) {}
        }
        g.fillStyle = 'rgba(255,250,247,.30)'; g.fillRect(0, 0, w, h);

        for (var i = 0; i < 7; i++) {                                   // غيوم ضباب
            var x = rnd(0, w), y = rnd(0, h), r = rnd(.15, .35) * m;
            var cg = g.createRadialGradient(x, y, 0, x, y, r);
            cg.addColorStop(0, 'rgba(255,255,255,.26)'); cg.addColorStop(1, 'rgba(255,255,255,0)');
            g.fillStyle = cg; g.fillRect(x - r, y - r, r * 2, r * 2);
        }
        for (var k = 0; k < 2; k++) fingerprint(g, rnd(.2, .8) * w, rnd(.25, .75) * h, rnd(.07, .11) * m, rnd(-.8, .8));

        for (var d = 0; d < 30; d++) {                                  // غبار
            g.fillStyle = 'rgba(255,255,255,' + rnd(.15, .4).toFixed(2) + ')';
            g.beginPath(); g.arc(rnd(0, w), rnd(0, h), rnd(.4, 1.2), 0, 7); g.fill();
        }

        var drops = [];                                                  // قطرات رطوبة
        for (var n = 0; n < 16; n++) {
            var dx = rnd(.05, .95) * w, dy = rnd(.05, .95) * h, dr = rnd(1.8, 5.5);
            var dg = g.createRadialGradient(dx - dr * .3, dy - dr * .3, 0, dx, dy, dr);
            dg.addColorStop(0, 'rgba(255,255,255,.55)');
            dg.addColorStop(.7, 'rgba(255,255,255,.12)');
            dg.addColorStop(1, 'rgba(80,60,60,.28)');
            g.fillStyle = dg; g.beginPath(); g.arc(dx, dy, dr, 0, 7); g.fill();
            g.strokeStyle = 'rgba(255,255,255,.5)'; g.lineWidth = .6; g.stroke();
            g.fillStyle = 'rgba(255,255,255,.95)';
            g.beginPath(); g.arc(dx - dr * .35, dy - dr * .38, Math.max(.4, dr * .22), 0, 7); g.fill();
            drops.push({ x: dx, y: dy, r: dr, gone: false });
        }
        return { c: c, g: g, drops: drops };
    }

    /* ---------- القماشة ---------- */
    var weave;
    function weavePattern(ctx) {
        if (weave) return weave;
        var p = document.createElement('canvas'); p.width = p.height = 6;
        var g = p.getContext('2d');
        g.strokeStyle = 'rgba(120,80,80,.12)'; g.lineWidth = 1;
        g.beginPath(); g.moveTo(0, 6); g.lineTo(6, 0); g.moveTo(-3, 3); g.lineTo(3, -3); g.moveTo(3, 9); g.lineTo(9, 3); g.stroke();
        g.strokeStyle = 'rgba(255,255,255,.18)';
        g.beginPath(); g.moveTo(0, 0); g.lineTo(6, 6); g.stroke();
        return (weave = ctx.createPattern(p, 'repeat'));
    }

    function drawCloth(ctx, x, y, ang, r, t) {
        var pw = r * 2.5, ph = r * 1.85, rad = r * .55;
        ctx.save();
        ctx.translate(x, y); ctx.rotate(ang);

        ctx.shadowColor = 'rgba(80,50,50,.32)'; ctx.shadowBlur = r * .55; ctx.shadowOffsetY = r * .22;
        var body = ctx.createLinearGradient(-pw / 2, -ph / 2, pw / 2, ph / 2);
        body.addColorStop(0, '#fbf1ec'); body.addColorStop(.5, '#f0d5cf'); body.addColorStop(1, '#dfb9b3');
        ctx.fillStyle = body;
        ctx.beginPath(); ctx.roundRect(-pw / 2, -ph / 2, pw, ph, rad); ctx.fill();
        ctx.shadowColor = 'transparent';

        ctx.save(); ctx.clip();
        ctx.fillStyle = weavePattern(ctx); ctx.fillRect(-pw / 2, -ph / 2, pw, ph);     // نسيج الميكروفايبر

        // طيّات القماش
        var fold = ctx.createLinearGradient(0, -ph / 2, 0, ph / 2);
        fold.addColorStop(0, 'rgba(255,255,255,.35)'); fold.addColorStop(.35, 'rgba(255,255,255,0)');
        fold.addColorStop(.7, 'rgba(120,70,70,.12)'); fold.addColorStop(1, 'rgba(120,70,70,.22)');
        ctx.fillStyle = fold; ctx.fillRect(-pw / 2, -ph / 2, pw, ph);

        ctx.lineWidth = 1.4; ctx.lineCap = 'round';
        ctx.strokeStyle = 'rgba(120,70,70,.16)';
        ctx.beginPath(); ctx.moveTo(-pw * .42, -ph * .1); ctx.quadraticCurveTo(0, ph * .12 + Math.sin(t * 8) * 2, pw * .42, -ph * .06); ctx.stroke();
        ctx.strokeStyle = 'rgba(255,255,255,.45)';
        ctx.beginPath(); ctx.moveTo(-pw * .4, -ph * .17); ctx.quadraticCurveTo(0, ph * .05 + Math.sin(t * 8) * 2, pw * .4, -ph * .13); ctx.stroke();
        ctx.restore();

        ctx.lineWidth = 1.2; ctx.strokeStyle = 'rgba(255,255,255,.7)';                  // حافة مخيّطة
        ctx.beginPath(); ctx.roundRect(-pw / 2 + 3, -ph / 2 + 3, pw - 6, ph - 6, rad - 3); ctx.setLineDash([4, 3]); ctx.stroke();
        ctx.restore();
    }

    /* ---------- لمعة العدسة بعد التنظيف ---------- */
    function drawGlint(ctx, w, h, u) {
        var x = -.3 * w + u * 1.6 * w;
        var g = ctx.createLinearGradient(x - .15 * w, 0, x + .15 * w, h * .5);
        g.addColorStop(0, 'rgba(255,255,255,0)'); g.addColorStop(.5, 'rgba(255,255,255,' + (.5 * Math.sin(u * Math.PI)).toFixed(3) + ')'); g.addColorStop(1, 'rgba(255,255,255,0)');
        ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);

        var s = Math.sin(clamp((u - .25) / .6, 0, 1) * Math.PI), cx = w * .84, cy = h * .18, L = Math.min(w, h) * .09 * s;
        if (L > .5) {
            ctx.save(); ctx.translate(cx, cy); ctx.fillStyle = 'rgba(255,255,255,' + (.9 * s).toFixed(2) + ')';
            ctx.beginPath(); ctx.moveTo(0, -L); ctx.quadraticCurveTo(0, 0, L, 0); ctx.quadraticCurveTo(0, 0, 0, L);
            ctx.quadraticCurveTo(0, 0, -L, 0); ctx.quadraticCurveTo(0, 0, 0, -L); ctx.fill(); ctx.restore();
        }
    }

    /* ---------- التشغيل ---------- */
    window.wipeCamera = function (done) {
        var stage = shell.querySelector('.face-stage');
        if (!stage || wiping) return;
        wiping = true;
        shell.classList.add('aura-face-wiping');

        var finish = function () {
            shell.classList.remove('aura-face-wiping');
            wiping = false;
            if (done) done();
        };
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { setTimeout(finish, 300); return; }

        var rect = stage.getBoundingClientRect(), w = rect.width, h = rect.height;
        var dpr = Math.min(window.devicePixelRatio || 1, 2);
        var cv = document.createElement('canvas');
        cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
        cv.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;z-index:7;pointer-events:none;border-radius:inherit';
        cv.setAttribute('aria-hidden', 'true');
        stage.appendChild(cv);
        var ctx = cv.getContext('2d');

        var fog = buildFog(w, h, dpr, stage.querySelector('video'));
        var path = buildPath(w, h);
        var R = Math.min(w, h) * .22;
        var last = null, smears = [], ang = 0, start = performance.now(), prevT = 0;

        function erase(x, y, ux, uy) {
            var g = fog.g;
            g.save(); g.globalCompositeOperation = 'destination-out';
            var gr = g.createRadialGradient(x, y, R * .35, x, y, R);
            gr.addColorStop(0, 'rgba(0,0,0,.55)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
            g.fillStyle = gr; g.beginPath(); g.arc(x, y, R, 0, 7); g.fill(); g.restore();
            fog.drops.forEach(function (d) {                       // القطرة تتمدد كأثر رطب
                if (!d.gone && Math.hypot(d.x - x, d.y - y) < R * .8) {
                    d.gone = true;
                    smears.push({ x: d.x, y: d.y, dx: ux * rnd(18, 32), dy: uy * rnd(18, 32), r: d.r, t: 0 });
                }
            });
        }

        (function frame(now) {
            var t = (now - start) / 1000, dt = t - prevT; prevT = t;
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, w, h);

            var p = clamp((t - T_FOG) / T_WIPE, 0, 1);
            var pos = null;
            if (t >= T_FOG && p < 1) {
                pos = pointAt(path, easeIO(p));
                if (last) {
                    var vx = pos[0] - last[0], vy = pos[1] - last[1], dist = Math.hypot(vx, vy);
                    if (dist > .01) {
                        var steps = Math.max(1, Math.ceil(dist / (R * .25)));
                        for (var i = 1; i <= steps; i++) erase(last[0] + vx * i / steps, last[1] + vy * i / steps, vx / dist, vy / dist);
                        var tilt = clamp(Math.atan(vy / (vx || .001)), -.6, .6) * .5;
                        ang += (tilt - ang) * .12;
                    }
                }
                last = pos;
            }

            // الطبقة الضبابية (تظهر تدريجياً، وتتلاشى بالكامل بالنهاية)
            var fadeOut = 1 - clamp((t - T_FOG - T_WIPE) / T_FADE, 0, 1);
            ctx.globalAlpha = clamp(t / T_FOG, 0, 1) * fadeOut;
            ctx.drawImage(fog.c, 0, 0, w, h);
            ctx.globalAlpha = 1;

            // آثار القطرات
            smears.forEach(function (s) {
                s.t += dt;
                var a = 1 - s.t / .6; if (a <= 0) return;
                var k = Math.min(1, s.t / .12);
                ctx.strokeStyle = 'rgba(255,255,255,' + (a * .5).toFixed(3) + ')';
                ctx.lineWidth = s.r * 1.2; ctx.lineCap = 'round';
                ctx.beginPath(); ctx.moveTo(s.x, s.y); ctx.lineTo(s.x + s.dx * k, s.y + s.dy * k); ctx.stroke();
            });

            if (pos) drawCloth(ctx, pos[0], pos[1], ang + Math.sin(t * 9) * .035, R, t);

            var gu = (t - T_FOG - T_WIPE - T_FADE * .6) / T_GLINT;
            if (gu > 0 && gu < 1) drawGlint(ctx, w, h, gu);

            if (t < TOTAL) requestAnimationFrame(frame);
            else { cv.remove(); finish(); }
        })(start);
    };

    /* ---------- إعادة المحاولة ---------- */
    function retryFaceScan() {
        if (box.hidden) return;
        var startScan = box.querySelector('[data-face-capture] .face-start');
        if (startScan) startScan.click();
    }
    function onFaceFailed() {
        if (box.hidden) return;
        if (faceAttempts >= MAX_RETRIES) {
            var status = box.querySelector('.face-status');
            if (status) status.textContent = 'Face was not recognized after several scans. Enter your email below or sign in with your password.';
            return;
        }
        faceAttempts++;
        window.faceWipePromise = new Promise(function (resolve) {
            window.wipeCamera(function () {
                resolve();
                window.setTimeout(retryFaceScan, 80);
            });
        });
    }
    document.addEventListener('face:loggedin', function () { faceAttempts = 0; });
    document.addEventListener('face:failed', onFaceFailed);
})();
