// Admin category choices only narrow the report picker; server authorization still checks the report type.
var reportCategory = document.getElementById('report-category');
var reportType = document.getElementById('report-type');
if (reportCategory && reportType) {
    var reportGroups = Array.from(reportType.querySelectorAll('optgroup')).map(function (group) {
        return { label: group.label, options: Array.from(group.querySelectorAll('option')).map(function (option) {
            return { value: option.value, text: option.textContent };
        }) };
    });
    function showCategoryReports() {
        var previous = reportType.value;
        var group = reportGroups.find(function (item) { return item.label === reportCategory.value; });
        if (!group) return;
        reportType.replaceChildren();
        group.options.forEach(function (item) {
            reportType.add(new Option(item.text, item.value, false, item.value === previous));
        });
    }
    reportCategory.addEventListener('change', showCategoryReports);
    showCategoryReports();
}

document.addEventListener('click', function (event) {
    if (event.target.closest('[data-report-exports] a[aria-disabled="true"]')) { event.preventDefault(); return; }
    if (event.target.closest('[data-report-print]')) window.print();
    var button = event.target.closest('[data-report-details]');
    if (!button) return;
    var row = document.getElementById(button.dataset.reportDetails);
    if (!row) return;
    row.hidden = !row.hidden;
    button.setAttribute('aria-expanded', String(!row.hidden));
    button.textContent = row.hidden ? 'View details' : 'Hide details';
});

// Only the dropdown choices refresh in the background. Results require View results.
var simpleFilters = document.querySelector('[data-simple-report-filters]');
var optionsRequest;
if (simpleFilters) simpleFilters.addEventListener('change', async function (event) {
    var pending = document.querySelector('[data-report-pending]');
    if (pending) pending.hidden = false;
    document.querySelectorAll('[data-report-exports] a').forEach(function (link) {
        link.setAttribute('aria-disabled', 'true'); link.classList.add('disabled');
    });
    var clear = event.target.id === 'report-supplier' ? ['personId', 'tenderNumber', 'status']
        : event.target.id === 'report-person' ? ['tenderNumber', 'status']
        : event.target.id === 'report-department' ? ['personId', 'tenderNumber', 'status']
        : event.target.id === 'report-tender' ? ['status'] : [];
    clear.forEach(function (name) { var field = simpleFilters.querySelector('[name="' + name + '"]'); if (field) field.value = ''; });
    if (optionsRequest) optionsRequest.abort();
    optionsRequest = new AbortController();
    try {
        var query = new URLSearchParams(new FormData(event.target.form));
        var optionsUrl = new URL(simpleFilters.dataset.optionsUrl, window.location.href);
        query.forEach(function (value, key) { optionsUrl.searchParams.set(key, value); });
        var response = await fetch(optionsUrl, { signal: optionsRequest.signal });
        if (!response.ok) return;
        var choices = await response.json();
        function update(name, items, allText) {
            var field = simpleFilters.querySelector('[name="' + name + '"]');
            if (!field) return;
            var selected = field.value;
            var selectedText = field.selectedOptions[0] ? field.selectedOptions[0].textContent : selected;
            field.replaceChildren(new Option(allText, ''));
            items.forEach(function (item) { field.add(new Option(item.text, item.value, false, item.value === selected)); });
            if (selected && !items.some(function (item) { return item.value === selected; }))
                field.add(new Option(selectedText, selected, false, true));
        }
        update('supplierId', choices.suppliers, 'All suppliers');
        update('personId', choices.people, 'All people');
        update('departmentId', choices.departments, 'All departments');
        update('tenderNumber', choices.tenders, 'All tenders');
        update('status', choices.statuses.map(function (status) { return { text: status, value: status }; }), 'All statuses');
        var statusField = simpleFilters.querySelector('[data-report-status-field]');
        if (statusField) statusField.hidden = choices.statuses.length <= 1;
    } catch (error) {
        if (error.name !== 'AbortError' && pending) pending.textContent = 'Click View results to apply your selections.';
    }
});
