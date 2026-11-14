document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("supplier-profile-form");
    if (!form) return;

    const editableFields = form.querySelectorAll("[data-profile-editable]");
    const editButton = document.getElementById("supplier-profile-edit");
    const cancelButton = document.getElementById("supplier-profile-cancel");
    const saveButton = document.getElementById("supplier-profile-save");

    const setEditing = (editing) => {
        form.classList.toggle("is-editing", editing);
        editableFields.forEach((field) => {
            if (field.tagName === "SELECT") field.disabled = !editing;
            else field.readOnly = !editing;
        });
        editButton.hidden = editing;
        cancelButton.hidden = !editing;
        saveButton.hidden = !editing;
    };

    editButton.addEventListener("click", () => setEditing(true));
    cancelButton.addEventListener("click", () => window.location.reload());
    setEditing(form.dataset.editing === "true");
});
