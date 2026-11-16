(() => {
    const picker = document.querySelector('[data-compliance-tender-picker]');
    if (!picker) return;

    const form = picker.closest('form');
    const trigger = picker.querySelector('[data-compliance-tender-trigger]');
    const current = picker.querySelector('[data-compliance-tender-current]');
    const value = picker.querySelector('[data-compliance-tender-value]');
    const menu = picker.querySelector('[data-compliance-tender-menu]');
    const search = picker.querySelector('[data-compliance-tender-search]');
    const list = picker.querySelector('[data-compliance-tender-options]');
    const empty = picker.querySelector('[data-compliance-tender-empty]');
    const options = [...picker.querySelectorAll('[data-compliance-tender-option]')];
    if (!form || !trigger || !current || !value || !menu || !search || !list) return;

    const visibleOptions = () => options.filter(option => !option.hidden);

    function filterOptions(query = '') {
        const term = query.trim().toLocaleLowerCase();
        for (const option of options) option.hidden = !option.dataset.search.toLocaleLowerCase().includes(term);
        if (empty) empty.hidden = visibleOptions().length > 0;
    }

    function positionMenu() {
        const rect = trigger.getBoundingClientRect();
        const below = Math.max(0, window.innerHeight - rect.bottom - 12);
        const above = Math.max(0, rect.top - 12);
        const opensUp = below < Math.min(menu.scrollHeight, 220) && above > below;
        const available = opensUp ? above : below;
        menu.classList.toggle('is-open-up', opensUp);
        menu.style.maxHeight = `${Math.max(120, Math.min(420, available - 8))}px`;
    }

    function closeMenu(returnFocus = false) {
        menu.hidden = true;
        menu.classList.remove('is-open-up');
        picker.classList.remove('is-open');
        trigger.setAttribute('aria-expanded', 'false');
        menu.style.maxHeight = '';
        if (returnFocus) trigger.focus();
    }

    function openMenu() {
        menu.hidden = false;
        picker.classList.add('is-open');
        trigger.setAttribute('aria-expanded', 'true');
        search.value = '';
        filterOptions();
        positionMenu();
        search.focus();
    }

    function selectOption(option) {
        value.value = option.dataset.value;
        for (const item of options) {
            const selected = item === option;
            item.classList.toggle('is-selected', selected);
            item.setAttribute('aria-selected', String(selected));
        }
        const title = document.createElement('span');
        title.className = 'compliance-tender-current-title';
        title.textContent = option.querySelector('.compliance-tender-option-title')?.textContent ?? '';
        const meta = document.createElement('small');
        meta.textContent = [...option.querySelectorAll('.compliance-tender-option-meta small')]
            .map(item => item.textContent.trim()).join(' · ');
        current.replaceChildren(title, meta);
        closeMenu(true);
    }

    trigger.addEventListener('click', () => menu.hidden ? openMenu() : closeMenu());
    trigger.addEventListener('keydown', event => {
        if (['ArrowDown', 'Enter', ' '].includes(event.key)) {
            event.preventDefault();
            if (menu.hidden) openMenu();
        }
    });
    search.addEventListener('input', () => filterOptions(search.value));
    search.addEventListener('keydown', event => {
        const shown = visibleOptions();
        if (event.key === 'ArrowDown' && shown.length) {
            event.preventDefault();
            shown[0].focus();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            closeMenu(true);
        }
    });
    for (const option of options) option.addEventListener('click', () => selectOption(option));
    list.addEventListener('keydown', event => {
        const shown = visibleOptions();
        const index = shown.indexOf(document.activeElement);
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && shown.length) {
            event.preventDefault();
            const step = event.key === 'ArrowDown' ? 1 : -1;
            shown[(index + step + shown.length) % shown.length].focus();
        } else if (event.key === 'Home' && shown.length) {
            event.preventDefault();
            shown[0].focus();
        } else if (event.key === 'End' && shown.length) {
            event.preventDefault();
            shown[shown.length - 1].focus();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            closeMenu(true);
        }
    });

    document.addEventListener('pointerdown', event => {
        if (!picker.contains(event.target)) closeMenu();
    });
    window.addEventListener('resize', () => {
        if (!menu.hidden) positionMenu();
    });
    window.addEventListener('scroll', () => {
        if (!menu.hidden) positionMenu();
    }, true);
    form.addEventListener('submit', event => {
        if (value.value) return;
        event.preventDefault();
        openMenu();
        search.setCustomValidity('Choose a tender before loading the offers.');
        search.reportValidity();
        search.addEventListener('input', () => search.setCustomValidity(''), { once: true });
    });
})();
