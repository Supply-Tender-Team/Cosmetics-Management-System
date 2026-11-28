// Monthly Activity chart on the Admin Dashboard.
// All numbers come from the server (DashboardController). This script only draws them.
//  - Requests / Tenders: sends the filter form again with the other data set (the server returns the right numbers
//    and the right status list). The selected chart type and the other filters stay in the form.
//  - Bars / Lines: only redraws the chart, no reload. The chart type is also written to the hidden field,
//    so it is kept when you press Apply.
(() => {
    const page = document.querySelector('.ad-admin [data-an-page]');
    if (!page) return;

    // ---------- 1. Elements and data ----------
    const data = JSON.parse(page.querySelector('#anData').textContent);
    const series = data.series[0];                       // { name, labels[], data[] }
    const plot = page.querySelector('#anTrend');
    const table = page.querySelector('#anTrendTable');
    const form = page.querySelector('[data-an-filters]');
    const tip = page.querySelector('#anTip');
    const color = getComputedStyle(page).getPropertyValue('--an-chart-1').trim();
    let chartMode = page.querySelector('#anChartMode').value;

    page.querySelector('#anTrendCap').textContent = data.caption;
    table.classList.add('visually-hidden'); // the table (written by the server) stays for screen readers

    // ---------- 2. Small helpers ----------

    // Makes text safe to put inside the SVG markup.
    function escapeHtml(value) {
        const replacements = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
        return String(value).replace(/[&<>"']/g, character => replacements[character]);
    }

    // "1 request", "5 requests"
    function unit(count) {
        const plural = series.name.toLowerCase();
        return count === 1 ? plural.replace(/s$/, '') : plural;
    }

    // Y axis: whole numbers only (you cannot have 2.5 requests). The step is 1, 2, 5, 10, 20, 25, 50 ...
    // chosen so that there are about 4 grid lines.
    function axisStep(maxValue) {
        if (maxValue <= 4) return 1;
        const wanted = maxValue / 4;
        const power = Math.pow(10, Math.floor(Math.log10(wanted)));
        return [1, 2, 2.5, 5, 10].map(multiplier => multiplier * power)
            .find(step => Number.isInteger(step) && step >= wanted);
    }

    // Sizes and the function that turns a count into a vertical position.
    function calculateLayout() {
        const width = Math.max(300, plot.clientWidth);
        const height = Math.max(230, plot.clientHeight);
        const margin = { left: 38, right: 8, top: 12, bottom: 26 };
        const chartWidth = width - margin.left - margin.right;
        const chartHeight = height - margin.top - margin.bottom;
        const highest = Math.max(...series.data);
        const step = axisStep(highest);
        const axisTop = Math.ceil(highest / step) * step;   // the top of the axis is a multiple of the step
        return {
            width, height, margin, chartHeight, step, axisTop,
            band: chartWidth / series.labels.length,         // the width reserved for one month
            y: value => margin.top + chartHeight - value / axisTop * chartHeight
        };
    }

    // ---------- 3. Drawing ----------

    function showEmptyMessage() {
        plot.innerHTML = '<p class="an-empty"><i class="bi bi-bar-chart-line" aria-hidden="true"></i>'
            + `<strong>No ${escapeHtml(unit(2))} match these filters.</strong>`
            + '<span>Try a wider date range or another department or status, or press Reset.</span></p>';
    }

    // Grid lines, Y labels (0, step, 2*step ... top) and X labels (the months).
    function buildAxis(layout) {
        let svg = '';
        for (let value = 0; value <= layout.axisTop; value += layout.step) {
            const y = layout.y(value);
            svg += `<line class="an-gl" x1="${layout.margin.left}" x2="${layout.width - layout.margin.right}" y1="${y}" y2="${y}"/>`;
            svg += `<text x="${layout.margin.left - 6}" y="${y + 4}" text-anchor="end">${value}</text>`;
        }

        // X labels: "Jun". Add the year ("Jun 26") when the chart covers more than one year.
        // When the months are too narrow to read, every second label is skipped.
        const years = new Set(series.labels.map(label => label.split(' ')[1]));
        const showYear = years.size > 1;
        const labelEvery = layout.band < 44 ? 2 : 1;
        series.labels.forEach((label, index) => {
            if (index % labelEvery !== 0) return;
            const [month, year] = label.split(' ');
            const text = showYear ? `${month} ${year.slice(2)}` : month;
            const x = layout.margin.left + layout.band * (index + .5);
            svg += `<text x="${x}" y="${layout.height - 7}" text-anchor="middle">${escapeHtml(text)}</text>`;
        });
        return svg;
    }

    function buildBars(layout) {
        let svg = '';
        series.data.forEach((value, index) => {
            const barWidth = Math.min(32, layout.band * .55);
            const barHeight = layout.chartHeight * value / layout.axisTop;
            const x = layout.margin.left + layout.band * (index + .5) - barWidth / 2;
            svg += `<rect class="an-bar-r" x="${x}" y="${layout.y(value)}" width="${barWidth}" height="${barHeight}" rx="5" fill="${color}"/>`;
        });
        return svg;
    }

    function buildLine(layout) {
        const points = series.data.map((value, index) => [layout.margin.left + layout.band * (index + .5), layout.y(value)]);
        const path = points.map((point, index) => `${index ? 'L' : 'M'}${point[0]} ${point[1]}`).join(' ');
        let svg = `<path class="an-line" d="${path}" stroke="${color}"/>`;
        points.forEach(point => { svg += `<circle class="an-pt" cx="${point[0]}" cy="${point[1]}" r="5" fill="${color}"/>`; });
        return svg;
    }

    // One invisible hover / keyboard area per month.
    function buildHitAreas(layout) {
        return series.labels.map((label, index) => {
            const count = series.data[index];
            const x = layout.margin.left + layout.band * index;
            return `<rect class="an-hit" tabindex="0" data-index="${index}" x="${x}" y="${layout.margin.top}" width="${layout.band}" height="${layout.chartHeight}"`
                + ` role="img" aria-label="${escapeHtml(label)}: ${count} ${unit(count)}"/>`;
        }).join('');
    }

    // Shows the exact month and count when the pointer or the keyboard is on a month.
    function addTooltips(layout) {
        plot.querySelectorAll('.an-hit').forEach(hit => {
            function show() {
                const index = Number(hit.dataset.index);
                const count = series.data[index];
                const box = hit.getBoundingClientRect();
                const pointTop = box.top + (layout.y(count) - layout.margin.top); // screen position of the bar top / point
                tip.innerHTML = `<b>${escapeHtml(series.labels[index])}</b>${count} ${unit(count)}`;
                tip.style.left = `${Math.min(box.left + box.width / 2, window.innerWidth - 140)}px`;
                tip.style.top = `${Math.max(8, pointTop - 62)}px`;
                tip.classList.add('on');
            }
            function hide() { tip.classList.remove('on'); }
            hit.addEventListener('pointerenter', show);
            hit.addEventListener('pointerleave', hide);
            hit.addEventListener('focus', show);
            hit.addEventListener('blur', hide);
        });
    }

    function drawChart() {
        const hasNoRecords = series.data.every(count => count === 0);
        if (hasNoRecords) {
            showEmptyMessage();
            return;
        }

        const layout = calculateLayout();
        const marks = chartMode === 'bars' ? buildBars(layout) : buildLine(layout);
        plot.innerHTML = `<svg viewBox="0 0 ${layout.width} ${layout.height}" role="group" aria-label="Monthly activity chart">`
            + `<g class="an-axis">${buildAxis(layout)}</g>${marks}${buildHitAreas(layout)}</svg>`;
        addTooltips(layout);
    }

    // ---------- 4. Buttons and filters ----------

    // Requests <-> Tenders: the status list is different for each, so the status is cleared and the form is sent again.
    page.querySelectorAll('[data-an-set]').forEach(button => {
        button.addEventListener('click', () => {
            const activity = page.querySelector('#anActivity');
            if (button.dataset.anSet === activity.value) return;
            activity.value = button.dataset.anSet;
            page.querySelector('#anStatus').value = '';
            form.requestSubmit();
        });
    });

    // Bars <-> Lines: redraw only.
    page.querySelectorAll('[data-an-mode]').forEach(button => {
        button.addEventListener('click', () => {
            chartMode = button.dataset.anMode;
            page.querySelector('#anChartMode').value = chartMode;
            page.querySelectorAll('[data-an-mode]').forEach(option => option.setAttribute('aria-pressed', String(option === button)));
            drawChart();
        });
    });

    // The "To" date cannot be before the "From" date (the server checks this too).
    const fromInput = page.querySelector('#anFrom');
    const toInput = page.querySelector('#anTo');
    function limitDates() {
        toInput.min = fromInput.value;
        fromInput.max = toInput.value;
    }
    fromInput.addEventListener('change', limitDates);
    toInput.addEventListener('change', limitDates);
    limitDates();

    // Redraw when the card changes size (for example when the window is resized).
    new ResizeObserver(drawChart).observe(plot);
    drawChart();
})();
