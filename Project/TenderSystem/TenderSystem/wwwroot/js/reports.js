document.addEventListener('click', function (event) {
    if (event.target.closest('[data-report-print]')) window.print();
    var button = event.target.closest('[data-report-details]');
    if (!button) return;
    var row = document.getElementById(button.dataset.reportDetails);
    if (!row) return;
    row.hidden = !row.hidden;
    button.setAttribute('aria-expanded', String(!row.hidden));
    button.textContent = row.hidden ? 'View details' : 'Hide details';
});
