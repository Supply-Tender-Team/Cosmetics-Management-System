document.addEventListener("DOMContentLoaded", function () {
    const form = document.getElementById("purchaseRequestForm");
    const items = document.getElementById("requestItems");
    const addButton = document.getElementById("addRequestItem");

    if (!form || !items || !addButton) return;

    function updateRows() {
        const rows = items.querySelectorAll("[data-item-row]");

        rows.forEach(function (row, index) {
            const number = row.querySelector("[data-item-number]");
            if (number) number.textContent = index + 1;

            row.querySelectorAll("[name]").forEach(function (field) {
                field.name = field.name.replace(/Items\[\d+\]/g, "Items[" + index + "]");
            });

            row.querySelectorAll("[id], [for]").forEach(function (field) {
                if (field.id) field.id = field.id.replace(/Items_\d+__/g, "Items_" + index + "__");
                if (field.htmlFor) field.htmlFor = field.htmlFor.replace(/Items_\d+__/g, "Items_" + index + "__");
            });

            row.querySelectorAll("[data-valmsg-for]").forEach(function (message) {
                message.dataset.valmsgFor = message.dataset.valmsgFor.replace(/Items\[\d+\]/g, "Items[" + index + "]");
                message.textContent = "";
                message.classList.remove("field-validation-error");
                message.classList.add("field-validation-valid");
            });

            const removeButton = row.querySelector("[data-remove-item]");
            if (removeButton) removeButton.disabled = rows.length === 1;
        });
    }

    function refreshValidation() {
        if (!window.jQuery || !window.jQuery.validator || !window.jQuery.validator.unobtrusive) return;

        const jqueryForm = window.jQuery(form);
        jqueryForm.removeData("validator");
        jqueryForm.removeData("unobtrusiveValidation");
        window.jQuery.validator.unobtrusive.parse(jqueryForm);
    }

    addButton.addEventListener("click", function () {
        const firstRow = items.querySelector("[data-item-row]");
        if (!firstRow) return;

        const newRow = firstRow.cloneNode(true);
        newRow.querySelectorAll("input, textarea, select").forEach(function (field) {
            field.value = "";
            field.classList.remove("input-validation-error");
        });
        items.appendChild(newRow);
        updateRows();
        refreshValidation();

        const firstInput = newRow.querySelector("input");
        if (firstInput) firstInput.focus();
    });

    items.addEventListener("click", function (event) {
        const removeButton = event.target.closest("[data-remove-item]");
        if (!removeButton || items.querySelectorAll("[data-item-row]").length === 1) return;

        removeButton.closest("[data-item-row]").remove();
        updateRows();
        refreshValidation();
        addButton.focus();
    });

    updateRows();
});
