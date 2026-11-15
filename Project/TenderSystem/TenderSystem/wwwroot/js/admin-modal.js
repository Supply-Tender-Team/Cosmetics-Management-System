// Admin modals (Abdallah): confirmations, Add / Edit Employee and Supplier Review.
//
// How it works (no business logic here, the controllers do all checks):
//  - A link with data-admin-modal is loaded with fetch + "X-Requested-With: XMLHttpRequest".
//    The controller then returns only the form / review (a partial view) and we show it in the modal.
//  - A form with data-modal-form is posted with fetch (anti-forgery token included, it is in the form).
//      * JSON { saved: true }  -> success: reload the list behind the modal, then close it.
//      * HTML                  -> show it in the modal (validation errors, or the updated supplier review).
//  - [data-modal-refresh] is the part of the page that is reloaded (same URL, so search and filters stay).
//  - Without JavaScript (or if anything fails) the links simply open the normal pages.
(function () {
    'use strict';
    if (!window.bootstrap) return;

    // The same confirmation dialog handles normal POST forms and forms inside the AJAX modal.
    const confirmElement = document.createElement('div');
    confirmElement.className = 'modal fade admin-modal admin-confirm';
    confirmElement.tabIndex = -1;
    confirmElement.setAttribute('aria-labelledby', 'adminConfirmTitle');
    confirmElement.setAttribute('aria-describedby', 'adminConfirmMessage');
    confirmElement.innerHTML =
        '<div class="modal-dialog modal-dialog-centered"><div class="modal-content">' +
        '<div class="modal-header"><h2 class="modal-title" id="adminConfirmTitle"></h2>' +
        '<button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div>' +
        '<div class="modal-body admin-confirm-body"><span class="admin-confirm-icon" aria-hidden="true"><i class="bi"></i></span>' +
        '<p id="adminConfirmMessage" class="mb-0"></p></div>' +
        '<div class="modal-footer"><button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>' +
        '<button type="button" class="btn btn-brand" data-confirm-submit>Confirm</button></div></div></div>';
    document.body.appendChild(confirmElement);
    const confirmation = new bootstrap.Modal(confirmElement);
    const approvedForms = new WeakSet();
    let pendingForm = null;
    let pendingTrigger = null;
    let submitting = false;
    let inlinePrompt = null;

    function submitConfirmedForm() {
        if (!pendingForm || submitting) return;
        submitting = true;
        const form = pendingForm;
        const trigger = pendingTrigger;
        approvedForms.add(form);
        if (inlinePrompt) inlinePrompt.restore();
        else {
            const button = confirmElement.querySelector('[data-confirm-submit]');
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            confirmation.hide();
        }
        form.requestSubmit(trigger);
        setTimeout(() => approvedForms.delete(form), 0);
        if (form.closest('#adminModal')) {
            submitting = false;
            pendingForm = null;
            pendingTrigger = null;
        }
    }

    document.addEventListener('submit', event => {
        const form = event.target;
        if (!form.matches('form[data-admin-confirm]')) return;
        if (approvedForms.has(form)) { approvedForms.delete(form); return; }
        event.preventDefault();
        event.stopPropagation();
        if (pendingForm || submitting) return;
        pendingForm = form;
        pendingTrigger = event.submitter || form.querySelector('button[type="submit"]');
        const title = form.dataset.confirmTitle || 'Confirm action?';
        const message = form.dataset.confirmMessage || 'Continue with this action?';
        const iconName = form.dataset.confirmIcon || 'bi-question-circle';
        const buttonText = pendingTrigger?.textContent.trim() || 'Confirm';
        const parentModal = form.closest('#adminModal');
        if (parentModal) {
            const body = parentModal.querySelector('.modal-body');
            const heading = parentModal.querySelector('.modal-title');
            const savedTitle = heading.textContent;
            const panel = document.createElement('div');
            panel.className = 'admin-inline-confirm';
            panel.innerHTML = '<span class="admin-confirm-icon" aria-hidden="true"><i class="bi"></i></span>' +
                '<p class="mb-3"></p><div class="d-flex flex-wrap gap-2">' +
                '<button type="button" class="btn btn-outline-secondary" data-confirm-cancel>Cancel</button>' +
                '<button type="button" class="btn btn-brand" data-confirm-submit>Confirm</button></div>';
            panel.querySelector('i').className = 'bi ' + iconName;
            panel.querySelector('p').textContent = message;
            panel.querySelector('[data-confirm-submit]').textContent = buttonText;
            body.appendChild(panel);
            [...body.children].forEach(child => { if (child !== panel) child.hidden = true; });
            heading.textContent = title;
            inlinePrompt = { restore() {
                [...body.children].forEach(child => { child.hidden = false; });
                panel.remove();
                heading.textContent = savedTitle;
                inlinePrompt = null;
            } };
            panel.querySelector('[data-confirm-cancel]').addEventListener('click', () => {
                inlinePrompt.restore();
                pendingForm = null;
                pendingTrigger?.focus();
                pendingTrigger = null;
            });
            panel.querySelector('[data-confirm-submit]').addEventListener('click', submitConfirmedForm);
            panel.querySelector('[data-confirm-cancel]').focus();
            return;
        }
        confirmElement.querySelector('#adminConfirmTitle').textContent = title;
        confirmElement.querySelector('#adminConfirmMessage').textContent = message;
        const icon = confirmElement.querySelector('.admin-confirm-icon i');
        icon.className = 'bi ' + iconName;
        confirmElement.classList.toggle('admin-confirm-warning', form.dataset.confirmTone === 'warning');
        confirmElement.querySelector('[data-confirm-submit]').textContent = buttonText;
        confirmation.show();
    }, true);

    confirmElement.querySelector('[data-confirm-submit]').addEventListener('click', submitConfirmedForm);

    confirmElement.addEventListener('hidden.bs.modal', () => {
        if (!submitting && pendingTrigger?.isConnected) pendingTrigger.focus();
        pendingForm = null;
        pendingTrigger = null;
        if (!submitting) {
            const button = confirmElement.querySelector('[data-confirm-submit]');
            button.disabled = false;
            button.removeAttribute('aria-busy');
        }
    });

    if (!window.fetch || !window.DOMParser) return;

    const ajaxHeaders = { 'X-Requested-With': 'XMLHttpRequest' };

    // ---- the modal shell (one per page) ----
    const modalElement = document.createElement('div');
    modalElement.className = 'modal fade admin-modal';
    modalElement.id = 'adminModal';
    modalElement.tabIndex = -1;
    modalElement.setAttribute('aria-labelledby', 'adminModalTitle');
    modalElement.setAttribute('aria-hidden', 'true');
    modalElement.innerHTML =
        '<div class="modal-dialog modal-dialog-centered modal-dialog-scrollable modal-lg modal-fullscreen-sm-down">' +
        '  <div class="modal-content">' +
        '    <div class="modal-header">' +
        '      <h2 class="modal-title" id="adminModalTitle"></h2>' +
        '      <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>' +
        '    </div>' +
        '    <div class="modal-body"></div>' +
        '  </div>' +
        '</div>';
    document.body.appendChild(modalElement);

    const modal = new bootstrap.Modal(modalElement);
    const titleElement = modalElement.querySelector('.modal-title');
    const bodyElement = modalElement.querySelector('.modal-body');

    let triggerHref = null;   // which link opened the modal (to give focus back on close)
    let isDirty = false;      // the user typed something that is not saved yet
    let isBusy = false;       // a request is running (blocks double submits and closing)
    let isOpening = false;

    // A redirect to the login page means the session expired: open that page normally.
    function isLoginPage(response) {
        return response.redirected && /\/Account\/Login/i.test(response.url);
    }

    function showContent(html) {
        bodyElement.innerHTML = html;
        const root = bodyElement.querySelector('[data-modal-title]');
        titleElement.textContent = root ? root.getAttribute('data-modal-title') : 'Details';
        bodyElement.scrollTop = 0;
    }

    function showError(text) {
        const alert = document.createElement('div');
        alert.className = 'alert alert-danger admin-modal-error';
        alert.setAttribute('role', 'alert');
        alert.textContent = text;
        bodyElement.querySelector('.admin-modal-error')?.remove();
        bodyElement.prepend(alert);
        bodyElement.scrollTop = 0;
    }

    function focusFirstField() {
        const message = bodyElement.querySelector('.alert[tabindex="-1"], .validation-summary-errors');
        const invalidField = bodyElement.querySelector('.input-validation-error');
        // Only forms marked data-modal-autofocus (Add / Edit Employee) start in their first field.
        // A review starts at the top (focus on the close button), not inside a decision field.
        const field = bodyElement.querySelector('[data-modal-autofocus] :is(input:not([type="hidden"]):not([readonly]), select, textarea)');
        const target = invalidField || message || field || modalElement.querySelector('.btn-close');
        if (target) {
            if (!target.hasAttribute('tabindex') && target.tagName === 'DIV') target.setAttribute('tabindex', '-1');
            target.focus();
        }
    }

    // Reload the list part of the page from the same URL (keeps search, filters and the TempData message).
    async function refreshList() {
        const region = document.querySelector('[data-modal-refresh]');
        if (!region) return;
        const response = await fetch(window.location.href, { credentials: 'same-origin' });
        if (!response.ok || isLoginPage(response)) return;
        const page = new DOMParser().parseFromString(await response.text(), 'text/html');
        const fresh = page.querySelector('[data-modal-refresh]');
        if (fresh) region.innerHTML = fresh.innerHTML;
    }

    // ---- open: click on a [data-admin-modal] link ----
    document.addEventListener('click', async event => {
        const link = event.target.closest('a[data-admin-modal]');
        if (!link || event.defaultPrevented) return;
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return; // new tab etc.
        event.preventDefault();
        if (isOpening) return;

        isOpening = true;
        link.classList.add('is-loading');
        link.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(link.href, { headers: ajaxHeaders, credentials: 'same-origin' });
            if (!response.ok || isLoginPage(response)) throw new Error('Could not load');
            showContent(await response.text());
            isDirty = false;
            triggerHref = link.getAttribute('href');
            modal.show();
        } catch {
            window.location.href = link.href; // safe fallback: the normal page
        } finally {
            isOpening = false;
            link.classList.remove('is-loading');
            link.removeAttribute('aria-busy');
        }
    });

    // ---- inside the modal ----
    bodyElement.addEventListener('click', event => {
        if (event.target.closest('[data-modal-cancel]')) {
            event.preventDefault();
            modal.hide();
        }
    });

    bodyElement.addEventListener('input', event => {
        if (event.target.closest('form[data-modal-form]')) isDirty = true;
    });

    bodyElement.addEventListener('submit', async event => {
        const form = event.target.closest('form[data-modal-form]');
        // Confirmation is handled in the capture phase before this AJAX submission.
        if (!form || event.defaultPrevented) return;
        event.preventDefault();
        if (isBusy) return;

        isBusy = true;
        const buttons = form.querySelectorAll('button[type="submit"]');
        buttons.forEach(button => { button.disabled = true; button.setAttribute('aria-busy', 'true'); });
        bodyElement.setAttribute('aria-busy', 'true');

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: ajaxHeaders,
                credentials: 'same-origin'
            });
            if (isLoginPage(response)) { window.location.href = response.url; return; }
            if (!response.ok) throw new Error('Request failed');

            const contentType = response.headers.get('content-type') || '';
            if (contentType.includes('application/json')) {
                // Saved: refresh the list (shows the success message) and close.
                isDirty = false;
                await refreshList();
                isBusy = false;
                modal.hide();
                return;
            }

            // HTML: validation errors (form shown again, typed values kept) or the updated supplier review.
            const keepsForm = !form.hasAttribute('data-modal-refresh-list');
            showContent(await response.text());
            if (!keepsForm) {
                isDirty = false;
                await refreshList();
            }
            focusFirstField();
        } catch {
            showError('Something went wrong. Please check the list behind this window, then try again.');
            buttons.forEach(button => { button.disabled = false; button.removeAttribute('aria-busy'); });
        } finally {
            isBusy = false;
            bodyElement.removeAttribute('aria-busy');
        }
    });

    // ---- closing: Escape, backdrop, close button, Cancel link ----
    let discardPrompt = null;
    let discardAllowed = false;
    modalElement.addEventListener('hide.bs.modal', event => {
        if (isBusy) { event.preventDefault(); return; }
        if (!isDirty || discardAllowed) return;
        event.preventDefault();
        if (discardPrompt) return;
        const savedTitle = titleElement.textContent;
        discardPrompt = document.createElement('div');
        discardPrompt.className = 'admin-discard-prompt';
        discardPrompt.innerHTML =
            '<span class="admin-confirm-icon" aria-hidden="true"><i class="bi bi-exclamation-circle"></i></span>' +
            '<p class="mb-3">You have unsaved changes. Close this window and discard them?</p>' +
            '<div class="d-flex flex-wrap gap-2"><button type="button" class="btn btn-outline-secondary" data-keep-editing>Keep editing</button>' +
            '<button type="button" class="btn btn-brand" data-discard>Discard changes</button></div>';
        bodyElement.appendChild(discardPrompt);
        [...bodyElement.children].forEach(child => { if (child !== discardPrompt) child.hidden = true; });
        titleElement.textContent = 'Discard changes?';
        discardPrompt.querySelector('[data-keep-editing]').addEventListener('click', restore);
        discardPrompt.querySelector('[data-discard]').addEventListener('click', () => {
            discardAllowed = true;
            restore();
            modal.hide();
        });
        function restore() {
            [...bodyElement.children].forEach(child => { child.hidden = false; });
            discardPrompt.remove();
            discardPrompt = null;
            titleElement.textContent = savedTitle;
            if (!discardAllowed) focusFirstField();
        }
        discardPrompt.querySelector('[data-keep-editing]').focus();
    });

    modalElement.addEventListener('shown.bs.modal', focusFirstField);

    modalElement.addEventListener('hidden.bs.modal', () => {
        bodyElement.innerHTML = '';
        isDirty = false;
        discardAllowed = false;
        // Give focus back to the button that opened the modal (it may have been re-rendered by refreshList).
        const trigger = triggerHref && [...document.querySelectorAll('a[data-admin-modal]')]
            .find(link => link.getAttribute('href') === triggerHref);
        const fallback = document.querySelector('[data-modal-refresh] [role="status"], [data-modal-refresh] h1');
        if (trigger) trigger.focus();
        else if (fallback) { fallback.setAttribute('tabindex', '-1'); fallback.focus(); }
        triggerHref = null;
    });
})();
