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
        if (choices) return;
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
        form.setAttribute('aria-busy', 'true');
        message('Checking report availability...');
        var controller = new AbortController();
        var timer = setTimeout(function () { controller.abort(); }, 15000);
        try {
            var response = await fetch(form.action, { method: 'POST', body: new FormData(form), signal: controller.signal, headers: { 'Accept': 'application/json' } });
            if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error();
            var result = await response.json();
            // No generated-report success path until the dedicated service and snapshot export are connected.
            message(result.message || 'AI report generation is not connected yet. Regular reports remain available.');
        } catch (error) { message(error.name === 'AbortError' ? 'The request timed out. Please try again.' : 'The request could not be completed. Please retry or refresh your session.'); }
        finally { clearTimeout(timer); busy = false; generate.disabled = false; form.removeAttribute('aria-busy'); }
    });
})();
