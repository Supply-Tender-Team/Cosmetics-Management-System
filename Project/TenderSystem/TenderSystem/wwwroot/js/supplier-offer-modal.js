(function () {
    var modal = document.getElementById('prepare-offer-modal');
    if (!modal) return;

    var content = modal.querySelector('[data-prepare-offer-content]');
    var loaded = false;
    var loading = false;

    function showMessage(message) {
        var box = document.createElement('div');
        box.className = 'supplier-prepare-modal-error';
        box.setAttribute('role', 'alert');
        box.textContent = message;
        content.replaceChildren(box);
    }

    modal.addEventListener('show.bs.modal', async function () {
        if (loaded || loading) return;
        loading = true;
        content.innerHTML = '<div class="supplier-prepare-modal-loading"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Loading offer form…</div>';
        try {
            var response = await fetch(modal.dataset.formUrl, {
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) {
                if (response.status === 409) {
                    showMessage('An offer has already been submitted for this tender. You can view it from My offers.');
                    return;
                }
                throw new Error('The offer form is no longer available.');
            }
            if (response.redirected) {
                window.location.assign(response.url);
                return;
            }
            var responseHtml = await response.text();
            var parsed = new DOMParser().parseFromString(responseHtml, 'text/html');
            var form = parsed.querySelector('form.supplier-offer-form');
            if (!form) throw new Error('Could not load the offer form. Please close this window and try again.');
            content.replaceChildren(form);
            loaded = true;
        } catch (error) {
            showMessage(error.message || 'Could not load the offer form. Please close this window and try again.');
        } finally {
            loading = false;
        }
    });
})();
