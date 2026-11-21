// Admin Analytics: the two interactive charts, count-up numbers, tooltips and the filter loading state.
// All numbers come from the server (JSON block #anData and the printed markup). Nothing is invented here:
// the count-up always ends on the exact server value and the charts only draw the values they are given.
(function () {
    const page = document.querySelector('[data-an-page]');
    const dataElement = document.getElementById('anData');
    if (!page) return;

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const $ = (selector, root = page) => root.querySelector(selector);
    const esc = text => String(text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const format = value => value.toLocaleString('en-US');
    const chartStyles = getComputedStyle(page);
    const PALETTE = Array.from({ length: 7 }, (_, index) => chartStyles.getPropertyValue(`--an-chart-${index + 1}`).trim());

    if (!reduceMotion) page.classList.add('anim-on');
    // Re-triggers the CSS animations of one chart container.
    function play(element) {
        if (reduceMotion || !element) return;
        element.classList.remove('anim');
        void element.offsetWidth;
        element.classList.add('anim');
    }

    // ---------- count-up (ends on the exact server value) ----------
    if (!reduceMotion) {
        const duration = 1100, cardDelay = 70;
        const easeOutCubic = t => 1 - Math.pow(1 - t, 3);
        page.querySelectorAll('[data-count-to]').forEach(element => {
            const target = parseInt(element.dataset.countTo, 10);
            if (!Number.isFinite(target) || target <= 0) return;
            const card = element.closest('.an-kpi');
            const index = card ? parseInt(getComputedStyle(card).getPropertyValue('--i'), 10) || 0 : 0;
            const delay = index * cardDelay;
            let start = null, finished = false;
            function step(now) {
                if (finished) return;
                if (start === null) start = now;
                const progress = Math.min((now - start) / duration, 1);
                element.textContent = format(Math.round(target * easeOutCubic(progress)));
                if (progress < 1) requestAnimationFrame(step); else element.textContent = format(target);
            }
            setTimeout(() => requestAnimationFrame(step), delay);
            // Safety net for background tabs: still end on the exact value.
            setTimeout(() => { finished = true; element.textContent = format(target); }, delay + duration + 250);
        });
    }

    // ---------- tooltip ----------
    const tip = document.getElementById('anTip');
    function showTip(html, x, y) {
        if (!tip) return;
        tip.innerHTML = html;
        tip.classList.add('on');
        const box = tip.getBoundingClientRect();
        const left = Math.min(Math.max(8, x - box.width / 2), window.innerWidth - box.width - 8);
        const top = y - box.height - 14 < 8 ? y + 18 : y - box.height - 14;
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
    }
    function hideTip() { if (tip) tip.classList.remove('on'); }
    document.addEventListener('keydown', event => { if (event.key === 'Escape') hideTip(); });

    // Simple text tooltips for any element with data-tip (bars, supplier segments).
    page.addEventListener('pointermove', event => {
        const element = event.target.closest('[data-tip]');
        if (element) showTip(esc(element.dataset.tip), event.clientX, event.clientY);
        else if (!event.target.closest('.an-plot')) hideTip();
    });
    page.addEventListener('pointerleave', hideTip);
    page.addEventListener('focusin', event => {
        const element = event.target.closest('[data-tip]');
        if (!element) return;
        const box = element.getBoundingClientRect();
        showTip(esc(element.dataset.tip), box.left + box.width / 2, box.top);
    });
    page.addEventListener('focusout', hideTip);

    // ---------- filter loading state ----------
    const form = $('[data-an-filters]');
    if (form) {
        form.addEventListener('submit', () => {
            page.classList.add('is-loading');
            const button = $('[data-an-apply]', form);
            if (button) button.classList.add('is-loading');
        });
    }
    window.addEventListener('pageshow', () => {
        page.classList.remove('is-loading');
        page.querySelectorAll('.is-loading').forEach(element => element.classList.remove('is-loading'));
    });

    if (!dataElement) return;
    let data;
    try { data = JSON.parse(dataElement.textContent); } catch { return; }

    // ---------- monthly activity chart ----------
    let set = 'requests', mode = 'bars';
    const hidden = new Set();
    const plot = document.getElementById('anTrend');
    let lastSize = '';

    function niceMax(value) {
        if (value <= 5) return 5;
        const power = Math.pow(10, Math.floor(Math.log10(value)));
        return [1, 2, 2.5, 5, 10].find(m => m * power >= value) * power;
    }

    function drawLegend() {
        const config = data.trend[set];
        document.getElementById('anTrendLegend').innerHTML = config.series.map((s, i) =>
            `<button type="button" data-an-series="${esc(s.name)}" aria-pressed="${!hidden.has(s.name)}"><span class="an-dot" style="background:${i === 0 ? PALETTE[0] : PALETTE[2]}"></span>${esc(s.name)}</button>`).join('');
    }

    function drawTrend(animate = true) {
        const config = data.trend[set];
        document.getElementById('anTrendCap').textContent = config.caption;
        const labels = config.series[0].labels;
        const months = labels.map(l => l.split(' ')[0]);
        const colors = config.series.map((s, i) => i === 0 ? PALETTE[0] : PALETTE[2]);
        const visible = config.series.map((s, i) => ({ ...s, color: colors[i] })).filter(s => !hidden.has(s.name));

        const total = config.series.reduce((sum, s) => sum + s.data.reduce((a, b) => a + b, 0), 0);
        const W = plot.clientWidth || 600, H = plot.clientHeight || 268;
        lastSize = W + 'x' + H;

        // Accessible table with the exact values (also used when there is nothing to draw).
        document.getElementById('anTrendTable').innerHTML = `<caption>${esc(config.caption)}</caption><tr><th>Month</th>${config.series.map(s => `<th>${esc(s.name)}</th>`).join('')}</tr>`
            + labels.map((label, i) => `<tr><th>${esc(label)}</th>${config.series.map(s => `<td>${s.data[i]}</td>`).join('')}</tr>`).join('');

        if (total === 0) {
            plot.innerHTML = '<p class="an-empty">No activity in this period for the selected filters.</p>';
            return;
        }

        const m = { l: 32, r: 8, t: 10, b: 24 };
        const iw = W - m.l - m.r, ih = H - m.t - m.b;
        const max = niceMax(Math.max(...config.series.flatMap(s => s.data)));
        const y = v => m.t + ih - (v / max) * ih;
        const band = iw / months.length;

        let grid = '';
        for (let i = 0; i <= 4; i++) {
            const v = max / 4 * i, yy = y(v);
            grid += `<line class="an-gl" x1="${m.l}" x2="${W - m.r}" y1="${yy}" y2="${yy}"/><text x="${m.l - 6}" y="${yy + 4}" text-anchor="end">${Math.round(v * 10) / 10}</text>`;
        }
        months.forEach((month, i) => { grid += `<text x="${m.l + band * i + band / 2}" y="${H - 6}" text-anchor="middle">${esc(month)}</text>`; });

        let body = '';
        if (mode === 'bars') {
            const n = Math.max(1, visible.length), bw = Math.min(26, band * .7 / n), gap = 4, group = bw * n + gap * (n - 1);
            visible.forEach((s, si) => s.data.forEach((v, i) => {
                const x = m.l + band * i + (band - group) / 2 + si * (bw + gap), h = Math.max(2, ih * v / max);
                body += `<rect class="an-bar-r" data-i="${i}" style="--d:${i}" x="${x}" y="${m.t + ih - h}" width="${bw}" height="${h}" rx="5" fill="${s.color}"/>`;
            }));
        } else {
            visible.forEach(s => {
                const points = s.data.map((v, i) => [m.l + band * i + band / 2, y(v)]);
                const path = points.map((p, i) => (i ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1)).join(' ');
                body += `<path class="an-area" d="${path} L${points[points.length - 1][0]} ${m.t + ih} L${points[0][0]} ${m.t + ih}Z" fill="${s.color}"/>`;
                body += `<path class="an-line" pathLength="1" d="${path}" stroke="${s.color}"/>`;
                points.forEach(p => { body += `<circle class="an-pt" cx="${p[0]}" cy="${p[1]}" r="5" fill="${s.color}"/>`; });
            });
        }

        let hits = `<line class="an-guide" id="anGuide" y1="${m.t}" y2="${m.t + ih}"/>`;
        labels.forEach((label, i) => {
            hits += `<rect class="an-hit" tabindex="0" data-i="${i}" x="${m.l + band * i}" y="${m.t}" width="${band}" height="${ih}" role="img" aria-label="${esc(label)}: ${config.series.map(s => esc(s.name) + ' ' + s.data[i]).join(', ')}"/>`;
        });
        plot.innerHTML = `<svg viewBox="0 0 ${W} ${H}" role="group" aria-label="Monthly activity chart"><g class="an-axis">${grid}</g>${body}${hits}</svg>`;
        if (animate) play(plot); else plot.classList.remove('anim');

        const svg = plot.firstElementChild, guide = $('#anGuide', svg);
        svg.querySelectorAll('.an-hit').forEach(hit => {
            const i = +hit.dataset.i;
            const on = (x, yPos) => {
                const gx = m.l + band * i + band / 2;
                guide.setAttribute('x1', gx); guide.setAttribute('x2', gx); guide.style.opacity = 1;
                svg.querySelectorAll('.an-bar-r').forEach(bar => { bar.style.opacity = +bar.dataset.i === i ? 1 : .45; });
                showTip(`<b>${esc(labels[i])}</b>` + config.series.map((s, k) => `<div class="r"><span><i style="background:${colors[k]}"></i>${esc(s.name)}</span><strong>${s.data[i]}</strong></div>`).join(''), x, yPos);
            };
            const off = () => { guide.style.opacity = 0; svg.querySelectorAll('.an-bar-r').forEach(bar => { bar.style.opacity = 1; }); hideTip(); };
            hit.addEventListener('pointermove', e => on(e.clientX, e.clientY));
            hit.addEventListener('pointerleave', off);
            hit.addEventListener('focus', () => { const r = hit.getBoundingClientRect(); on(r.left + r.width / 2, r.top + 30); });
            hit.addEventListener('blur', off);
        });
    }

    // ---------- status donut ----------
    let mix = 'requests';
    function drawDonut() {
        const config = data.mix[mix];
        document.getElementById('anMixCap').textContent = config.caption;
        const donut = document.getElementById('anDonut'), legend = document.getElementById('anDonutLegend');
        const total = config.items.reduce((sum, item) => sum + item.count, 0);
        if (total === 0) {
            donut.innerHTML = '';
            legend.innerHTML = '<p class="an-empty">No ' + esc(config.unit) + ' match these filters.</p>';
            return;
        }
        const R = 66, C = 2 * Math.PI * R, gap = config.items.length > 1 ? 2 : 0;
        let offset = 0, segments = '';
        config.items.forEach((item, i) => {
            const length = Math.max(0, item.count / total * C - gap);
            segments += `<circle class="an-seg-c" data-i="${i}" style="--d:${i}" cx="88" cy="88" r="${R}" stroke="${PALETTE[i % PALETTE.length]}" stroke-dasharray="${length} ${C - length}" stroke-dashoffset="${-offset}"/>`;
            offset += item.count / total * C;
        });
        donut.innerHTML = `<svg viewBox="0 0 176 176" role="img" aria-label="${esc(config.caption)}: ${config.items.map(x => esc(x.label) + ' ' + x.count).join(', ')}">${segments}</svg><div class="an-dc"><strong id="anDcv">${format(total)}</strong><span id="anDcl">${esc(config.unit)}</span></div>`;
        legend.innerHTML = config.items.map((item, i) =>
            `<a href="${esc(item.href)}" data-i="${i}" aria-label="${esc(item.label)}: ${item.count} ${esc(config.unit)}; open report"><i class="an-dot" style="background:${PALETTE[i % PALETTE.length]}"></i><span>${esc(item.label)}</span><b>${format(item.count)}</b><em>${Math.round(item.count / total * 100)}%</em></a>`).join('');
        play(donut);

        const focusOn = i => {
            donut.classList.add('hov');
            donut.querySelectorAll('.an-seg-c').forEach(s => s.classList.toggle('on', +s.dataset.i === i));
            legend.querySelectorAll('a').forEach(a => a.classList.toggle('on', +a.dataset.i === i));
            const item = config.items[i];
            document.getElementById('anDcv').textContent = format(item.count);
            document.getElementById('anDcl').textContent = item.label + ' · ' + Math.round(item.count / total * 100) + '%';
        };
        const focusOff = () => {
            donut.classList.remove('hov');
            donut.querySelectorAll('.an-seg-c').forEach(s => s.classList.remove('on'));
            legend.querySelectorAll('a').forEach(a => a.classList.remove('on'));
            document.getElementById('anDcv').textContent = format(total);
            document.getElementById('anDcl').textContent = config.unit;
        };
        donut.querySelectorAll('.an-seg-c').forEach(s => { s.addEventListener('pointerenter', () => focusOn(+s.dataset.i)); s.addEventListener('pointerleave', focusOff); });
        legend.querySelectorAll('a').forEach(a => {
            const i = +a.dataset.i;
            a.addEventListener('pointerenter', () => focusOn(i)); a.addEventListener('pointerleave', focusOff);
            a.addEventListener('focus', () => focusOn(i)); a.addEventListener('blur', focusOff);
        });
    }

    // ---------- controls ----------
    page.addEventListener('click', event => {
        const button = event.target.closest('button');
        if (!button) return;
        if (button.dataset.anSet) {
            set = button.dataset.anSet; hidden.clear();
            page.querySelectorAll('[data-an-set]').forEach(b => b.setAttribute('aria-pressed', b === button));
            drawLegend(); drawTrend();
        } else if (button.dataset.anMode) {
            mode = button.dataset.anMode;
            page.querySelectorAll('[data-an-mode]').forEach(b => b.setAttribute('aria-pressed', b === button));
            drawTrend();
        } else if (button.dataset.anSeries) {
            const name = button.dataset.anSeries;
            if (hidden.has(name)) hidden.delete(name);
            else if (hidden.size < data.trend[set].series.length - 1) hidden.add(name); // keep at least one series visible
            drawLegend(); drawTrend();
        } else if (button.dataset.anMix) {
            mix = button.dataset.anMix;
            page.querySelectorAll('[data-an-mix]').forEach(b => b.setAttribute('aria-pressed', b === button));
            drawDonut();
        }
    });

    // Resizing redraws the chart to the new size without replaying the animation.
    let resizeTimer;
    new ResizeObserver(() => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => { if (plot.clientWidth + 'x' + plot.clientHeight !== lastSize) drawTrend(false); }, 120);
    }).observe(plot);

    drawLegend(); drawTrend(); drawDonut();
})();
