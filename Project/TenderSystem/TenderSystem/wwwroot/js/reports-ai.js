(function () {
    var box = document.querySelector('[data-ai-report]');
    if (!box) return;
    var form = box.querySelector('[data-ai-form]');
    var focusPanel = box.querySelector('[data-ai-focus]');
    var focus = box.querySelector('#ai-focus');
    var choice = box.querySelector('#ai-choice');
    var choiceWrap = box.querySelector('[data-ai-choice-wrap]');
    var status = box.querySelector('[data-ai-status]');
    var generate = box.querySelector('[data-ai-generate]');
    var choices;
    var busy = false;
    var active = null;
    var revision = 0;
    var exportUrl = null;
    var exportButton = box.querySelector('[data-ai-export]');
    var cancelButton = box.querySelector('[data-ai-cancel]');
    var output = box.querySelector('[data-ai-output]');
    function invalidateResult() {
        revision++;
        if (active) active.abort();
        exportUrl = null;
        exportButton.disabled = true;
        if (output.childNodes.length) {
            output.classList.add('reports-ai-stale');
            message('The scope changed. Generate a new report before exporting.');
        }
    }
    exportButton.addEventListener('click', async function () {
        if (!exportUrl || busy) return;
        var requested = exportUrl, version = revision;
        exportButton.disabled = true;
        try {
            var response = await fetch(requested, { cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/pdf' } });
            if (version !== revision || requested !== exportUrl) return;
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/pdf')) {
                exportUrl = null;
                message('This generated report is no longer available. Generate it again before exporting.');
                return;
            }
            var blob = await response.blob();
            if (version !== revision || requested !== exportUrl) return;
            var objectUrl = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = objectUrl; link.download = 'MS-WAND-AI-Report.pdf'; document.body.append(link); link.click(); link.remove();
            setTimeout(function () { URL.revokeObjectURL(objectUrl); }, 1000);
            message('PDF downloaded from the same generated report.');
        } catch (_) { message('The PDF could not be downloaded. Please try again.'); }
        finally { exportButton.disabled = !exportUrl || busy; }
    });
    cancelButton.addEventListener('click', function () { if (active) active.abort(); });
    window.addEventListener('pagehide', function () { if (active) active.abort(); });
    function showFacts(result) {
        output.replaceChildren();
        var title = document.createElement('h3'); title.textContent = result.title || 'Verified figures'; output.append(title);
        var scope = document.createElement('p'); scope.textContent = result.scope || ''; output.append(scope);
        var grid = document.createElement('div'); grid.className = 'reports-ai-figures';
        (result.figures || []).forEach(function (item) {
            var card = document.createElement('article'), label = document.createElement('span'), value = document.createElement('strong');
            label.textContent = item.label; value.textContent = item.value; card.append(label, value); grid.append(card);
        });
        output.append(grid);
        var notes = document.createElement('ul');
        (result.coverageNotes || []).forEach(function (note) { var li = document.createElement('li'); li.textContent = note; notes.append(li); });
        output.append(notes);
    }

    function message(text) { status.textContent = text; status.hidden = !text; }
    function isSpecific() { return form.elements.SpecificTopic.value === 'true'; }
    function topicChanged() {
        var name = { supplier: 'supplier', tender: 'tender', department: 'department', person: 'person', status: 'status' }[focus.value];
        choiceWrap.hidden = !name;
        choice.disabled = !name || !isSpecific();
        choice.required = !!name && isSpecific();
        var label = name ? 'Choose a ' + name : 'Choose a record';
        choiceWrap.querySelector('label').textContent = label;
        choice.replaceChildren(new Option(label, ''));
        if (choices && name) {
            var items = focus.value === 'status' ? (choices.statuses || []).map(function (s) { return { text: s, value: s }; }) : choices[{ supplier: 'suppliers', tender: 'tenders', department: 'departments', person: 'people' }[focus.value]] || [];
            items.forEach(function (item) { choice.add(new Option(item.text, item.value)); });
            if (!items.length) message('No available records for this focus. Choose another topic.');
        }
        box.querySelector('[data-ai-topic-hint]').textContent = focus.value === 'compare-suppliers'
            ? 'Compare suppliers within this report and optional date range. No individual selection needed.'
            : focus.value === 'compare-departments' ? 'Compare departments within this report and optional date range. No individual selection needed.'
            : 'This scope is separate from the regular table filters. Dates use UTC.';
        var from = form.elements.From;
        if (from) {
            from.required = focus.value === 'period' && isSpecific() && !form.elements.To.value;
            form.elements.To.required = false;
        }
    }
    async function loadChoices() {
        focus.disabled = true;
        message('Loading available topics...');
        try {
            var url = new URL(form.dataset.choicesUrl, location.href);
            url.searchParams.set('type', form.elements.Type.value);
            var response = await fetch(url, { headers: { 'Accept': 'application/json' } });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) throw new Error();
            choices = await response.json();
            message('');
            topicChanged();
        } catch (_) { message('Topics could not be loaded. Select Quick summary, then Specific topic to retry.'); }
        finally { focus.disabled = !isSpecific() || !choices; }
    }
    form.addEventListener('change', function (event) {
        invalidateResult();
        if (event.target.name === 'SpecificTopic') {
            var specific = isSpecific();
            focusPanel.hidden = !specific;
            focusPanel.querySelectorAll('input, select').forEach(function (field) { field.disabled = !specific; });
            focus.required = specific;
            message('');
            topicChanged();
            if (specific) loadChoices();
        } else if (event.target === focus) { message(''); topicChanged(); }
        else if (event.target.name === 'To' || event.target.name === 'From') topicChangedDates();
    });
    function topicChangedDates() {
        if (form.elements.From) form.elements.From.required = isSpecific() && focus.value === 'period' && !form.elements.To.value;
    }
    form.addEventListener('submit', async function (event) {
        event.preventDefault();
        if (busy) return;
        var from = form.elements.From, to = form.elements.To;
        if (isSpecific() && (!choices || !focus.value)) { message('Choose an available focus before generating.'); return; }
        if (isSpecific() && from && from.value && to.value && from.value > to.value) { message('From date must not be after To date.'); return; }
        busy = true;
        generate.disabled = true;
        exportButton.disabled = true;
        exportUrl = null;
        cancelButton.hidden = false;
        form.setAttribute('aria-busy', 'true');
        message('Preparing verified figures and analyzing the report. This can take up to a few minutes…');
        var controller = new AbortController();
        active = controller;
        var currentRevision = revision;
        var timedOut = false;
        var timer = setTimeout(function () { timedOut = true; controller.abort(); }, 200000);
        var progress = setInterval(function () { message('Analysis is still running. You can cancel and keep using regular reports.'); }, 20000);
        try {
            var response = await fetch(form.action, { method: 'POST', body: new FormData(form), signal: controller.signal, headers: { 'Accept': 'text/html, application/json' } });
            if (currentRevision !== revision) return;
            var type = response.headers.get('content-type') || '';
            var download = response.headers.get('X-AI-Report-Export');
            if (response.ok && type.includes('text/html') && download) {
                var url = new URL(download, location.href);
                if (url.origin !== location.origin) throw new Error();
                var html = await response.text();
                if (currentRevision !== revision) return;
                // This is our Razor-encoded server partial, never raw HTML from the model.
                output.innerHTML = html;
                output.classList.remove('reports-ai-stale');
                exportUrl = url.href;
                exportButton.disabled = false;
                message('Report generated. Expand shows the rest of this same result. PDF is available for 20 minutes or until a newer report replaces it.');
            } else if (type.includes('application/json')) {
                var result = await response.json();
                if (currentRevision !== revision) return;
                output.classList.remove('reports-ai-stale');
                if (result.figures) showFacts(result);
                message(result.message || 'The analysis could not be completed. Please try again.');
            } else throw new Error();
        } catch (error) {
            if (currentRevision === revision) message(error.name === 'AbortError'
                ? (timedOut ? 'The analysis timed out. Please try again.' : 'Analysis cancelled. No new result is displayed; generate again before exporting.')
                : 'The request could not be completed. Please retry or refresh your session.');
        } finally {
            clearTimeout(timer); clearInterval(progress);
            active = null; busy = false; generate.disabled = false; cancelButton.hidden = true; form.removeAttribute('aria-busy');
        }
    });
})();
