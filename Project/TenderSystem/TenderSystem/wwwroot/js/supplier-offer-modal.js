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

    function readableMessage(text) {
        try {
            var payload = JSON.parse(text);
            if (typeof payload === 'string') return payload;
            if (payload.detail) return payload.detail;
            if (payload.title) return payload.title;
        } catch (error) { }
        return text.replace(/^"|"$/g, '').trim();
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
                var message = readableMessage(await response.text());
                throw new Error(message || 'The offer form is no longer available.');
            }
            content.innerHTML = await response.text();
            loaded = true;
        } catch (error) {
            showMessage(error.message || 'Could not load the offer form. Please close this window and try again.');
        } finally {
            loading = false;
        }
    });

    modal.addEventListener('submit', async function (event) {
        var form = event.target.closest('form.supplier-offer-form');
        if (!form || !modal.contains(form)) return;
        event.preventDefault();

        var submitter = event.submitter;
        if (submitter) {
            submitter.disabled = true;
            submitter.dataset.originalText = submitter.textContent;
            submitter.textContent = 'Saving…';
        }

        try {
            var formData = submitter ? new FormData(form, submitter) : new FormData(form);
            var response = await fetch(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                body: formData
            });

            if (response.redirected) {
                window.location.assign(response.url);
                return;
            }

            var responseHtml = await response.text();
            var parsed = new DOMParser().parseFromString(responseHtml, 'text/html');
            var refreshedForm = parsed.querySelector('form.supplier-offer-form');
            if (refreshedForm) {
                content.replaceChildren(refreshedForm);
                loaded = true;
                content.scrollTop = 0;
            } else {
                showMessage('The offer could not be saved. Please close this window and try again.');
            }
        } catch (error) {
            showMessage('Could not reach the server. Check your connection and try again.');
        } finally {
            if (submitter && submitter.isConnected) {
                submitter.disabled = false;
                submitter.textContent = submitter.dataset.originalText || 'Save';
            }
        }
    });
})();
