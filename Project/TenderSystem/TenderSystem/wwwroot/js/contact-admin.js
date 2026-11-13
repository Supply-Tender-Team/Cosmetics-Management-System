// Admin Contact Messages (Dev 3): small loading states only. Everything works without JavaScript.
(function () {
    const panel = document.querySelector('.inbox-panel');

    function showLoading(button) {
        if (panel) {
            panel.classList.add('is-loading');
            panel.setAttribute('aria-busy', 'true');
        }
        if (button) button.classList.add('is-loading');
    }

    // Search form: dim the list and show a spinner while the new results load.
    const searchForm = document.querySelector('[data-inbox-search]');
    if (searchForm) {
        searchForm.addEventListener('submit', () => showLoading(searchForm.querySelector('button[type="submit"]')));
    }

    // Tabs and "clear" links: same loading feedback (normal clicks only, not Ctrl/Cmd+click).
    document.querySelectorAll('[data-inbox-nav]').forEach(link => {
        link.addEventListener('click', event => {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
            showLoading(null);
        });
    });

    // Press "/" to jump to the search box (ignored while typing in a field).
    const searchInput = document.getElementById('inboxSearch');
    document.addEventListener('keydown', event => {
        if (!searchInput || event.key !== '/') return;
        const tag = document.activeElement ? document.activeElement.tagName : '';
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
        event.preventDefault();
        searchInput.focus();
    });

    // Mark as Read: disable the button after the first click to avoid double posts.
    const markForm = document.querySelector('[data-mark-read]');
    if (markForm) {
        markForm.addEventListener('submit', () => {
            const button = markForm.querySelector('button[type="submit"]');
            button.disabled = true;
            button.classList.add('is-loading');
            button.querySelector('.letter-mark-text').textContent = 'Marking…';
        });
    }

    // Browser "back" can restore a page from cache with the loading state still on.
    window.addEventListener('pageshow', () => {
        if (panel) {
            panel.classList.remove('is-loading');
            panel.removeAttribute('aria-busy');
        }
        document.querySelectorAll('.is-loading').forEach(element => {
            element.classList.remove('is-loading');
            if (element.tagName === 'BUTTON') element.disabled = false;
        });
    });
})();
