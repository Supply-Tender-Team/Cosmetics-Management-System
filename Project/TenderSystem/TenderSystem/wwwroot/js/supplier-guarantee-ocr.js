document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-ocr-default]").forEach((field) => {
        field.addEventListener("input", () => { field.dataset.ocrDefault = "false"; });
    });

    const fieldsFor = (index) => ({
        referenceNumber: `Guarantees[${index}].ReferenceNumber`,
        issuerName: `Guarantees[${index}].IssuerName`,
        beneficiaryName: `Guarantees[${index}].BeneficiaryName`,
        amount: `Guarantees[${index}].Amount`,
        currency: `Guarantees[${index}].Currency`,
        issuedAt: `Guarantees[${index}].IssuedAt`,
        expiresAt: `Guarantees[${index}].ExpiresAt`,
        guarantorName: `Guarantees[${index}].GuarantorName`,
        guarantorNationalNumber: `Guarantees[${index}].GuarantorNationalNumber`,
        guarantorAddress: `Guarantees[${index}].GuarantorAddress`,
        guarantorPhone: `Guarantees[${index}].GuarantorPhone`
    });

    document.querySelectorAll("[data-guarantee-ocr]").forEach((input) => {
        input.addEventListener("change", async () => {
            const index = input.dataset.guaranteeOcr;
            const status = document.querySelector(`[data-ocr-status="${index}"]`);
            const file = input.files?.[0];
            if (!file || !status) return;

            status.hidden = false;
            status.classList.remove("is-success", "is-error");
            status.classList.add("is-loading");
            status.textContent = "Reading the document on this server…";

            const body = new FormData();
            body.append("file", file);
            body.append("guaranteeType", input.dataset.guaranteeType || "");
            const token = input.closest("form")?.querySelector('input[name="__RequestVerificationToken"]')?.value;

            try {
                const response = await fetch(input.dataset.ocrUrl, {
                    method: "POST",
                    body,
                    headers: token ? { RequestVerificationToken: token } : {},
                    credentials: "same-origin"
                });
                const result = await response.json();
                if (!response.ok) throw new Error(result.message || "This file could not be read.");

                let filled = 0;
                let alreadyEntered = 0;
                for (const [key, name] of Object.entries(fieldsFor(index))) {
                    const value = result.fields?.[key];
                    const field = input.closest("form")?.querySelector(`[name="${name}"]`);
                    if (value === null || value === undefined || value === "" || !field) continue;
                    if (field.value.trim() && field.dataset.ocrDefault !== "true") {
                        alreadyEntered++;
                        continue;
                    }
                    field.value = key.endsWith("At") ? String(value).slice(0, 10) : value;
                    field.dispatchEvent(new Event("input", { bubbles: true }));
                    field.dispatchEvent(new Event("change", { bubbles: true }));
                    filled++;
                }

                status.classList.remove("is-loading");
                status.classList.add("is-success");
                status.textContent = filled
                    ? `Suggested ${filled} field${filled === 1 ? "" : "s"}. Review them before submitting.${alreadyEntered ? ` ${alreadyEntered} field${alreadyEntered === 1 ? " was" : "s were"} already filled and left unchanged.` : ""}`
                    : "The file was read, but no reliable fields were detected. Please enter the details manually.";
            } catch (error) {
                status.classList.remove("is-loading");
                status.classList.add("is-error");
                status.textContent = error instanceof Error ? error.message : "We could not read this file. Enter the details manually.";
            }
        });
    });
});
