document.addEventListener("DOMContentLoaded", function () {
    const form = document.getElementById("purchaseRequestForm");
    const itemsContainer = document.getElementById("requestItems");
    const addItemButton = document.getElementById("addRequestItem");

    if (!form || !itemsContainer || !addItemButton) return;

    function refreshItemRows() {
        const itemRows = itemsContainer.querySelectorAll("[data-item-row]");

        itemRows.forEach(function (row, index) {
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
            if (removeButton) removeButton.disabled = itemRows.length === 1;
        });
    }

    function refreshFormValidation() {
        if (!window.jQuery || !window.jQuery.validator || !window.jQuery.validator.unobtrusive) return;

        const jqueryForm = window.jQuery(form);
        jqueryForm.removeData("validator");
        jqueryForm.removeData("unobtrusiveValidation");
        window.jQuery.validator.unobtrusive.parse(jqueryForm);
    }

    addItemButton.addEventListener("click", function () {
        const firstRow = itemsContainer.querySelector("[data-item-row]");
        if (!firstRow) return;

        const newItemRow = firstRow.cloneNode(true);
        newItemRow.querySelectorAll("input, textarea, select").forEach(function (field) {
            field.value = "";
            field.classList.remove("input-validation-error");
        });
        itemsContainer.appendChild(newItemRow);
        refreshItemRows();
        refreshFormValidation();

        const firstInput = newItemRow.querySelector("input");
        if (firstInput) firstInput.focus();
    });

    itemsContainer.addEventListener("click", function (event) {
        const removeButton = event.target.closest("[data-remove-item]");
        if (!removeButton || itemsContainer.querySelectorAll("[data-item-row]").length === 1) return;

        removeButton.closest("[data-item-row]").remove();
        refreshItemRows();
        refreshFormValidation();
        addItemButton.focus();
    });

    refreshItemRows();
});
