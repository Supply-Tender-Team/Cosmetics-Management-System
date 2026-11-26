document.addEventListener("DOMContentLoaded", () => {
    const modalRoot = document.getElementById("supplierOfferDetailsModal");
    const modalContent = modalRoot?.querySelector("[data-offer-details-content]");
    if (!modalRoot || !modalContent || !window.bootstrap?.Modal) return;

    let pendingRequest;

    modalRoot.addEventListener("show.bs.modal", async (event) => {
        const trigger = event.relatedTarget;
        const url = trigger?.dataset.offerDetailsUrl;
        if (!url) return;

        pendingRequest?.abort();
        pendingRequest = new AbortController();
        modalContent.innerHTML = '<div class="modal-body supplier-offer-modal-loading" role="status"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Loading offer details…</div>';

        try {
            const response = await fetch(url, {
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "text/html" },
                signal: pendingRequest.signal
            });
            if (!response.ok) throw new Error("We could not load this offer. Please try again.");

            const page = new DOMParser().parseFromString(await response.text(), "text/html");
            const fragment = page.querySelector(".supplier-offer-modal-fragment");
            if (!fragment) throw new Error("Offer details were unavailable. Please refresh and try again.");
            modalContent.innerHTML = fragment.innerHTML;
        } catch (error) {
            if (error instanceof DOMException && error.name === "AbortError") return;
            const message = error instanceof Error ? error.message : "We could not load this offer. Please try again.";
            modalContent.innerHTML = `<div class="modal-body supplier-offer-modal-error" role="alert"><i class="bi bi-exclamation-circle" aria-hidden="true"></i><p>${message}</p></div>`;
        }
    });

    modalRoot.addEventListener("hidden.bs.modal", () => {
        pendingRequest?.abort();
        pendingRequest = undefined;
        modalContent.innerHTML = '<div class="modal-body supplier-offer-modal-loading" role="status">Choose an offer to see its details.</div>';
    });
});
