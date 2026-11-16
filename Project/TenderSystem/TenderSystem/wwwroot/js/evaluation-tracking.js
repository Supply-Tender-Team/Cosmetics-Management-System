(() => {
    function positionPickerMenu(trigger, menu) {
        const rect = trigger.getBoundingClientRect();
        const below = Math.max(0, window.innerHeight - rect.bottom - 12);
        const above = Math.max(0, rect.top - 12);
        const opensUp = below < Math.min(menu.scrollHeight, 220) && above > below;
        const available = opensUp ? above : below;
        menu.classList.toggle('is-open-up', opensUp);
        menu.style.maxHeight = `${Math.max(120, Math.min(420, available - 8))}px`;
    }

    const supplierPicker = document.querySelector('[data-tracking-supplier-picker]');
    if (supplierPicker) {
        const supplierTrigger = supplierPicker.querySelector('[data-tracking-supplier-trigger]');
        const supplierCurrent = supplierPicker.querySelector('[data-tracking-supplier-current]');
        const supplierValue = supplierPicker.querySelector('[data-tracking-supplier-value]');
        const supplierMenu = supplierPicker.querySelector('[data-tracking-supplier-menu]');
        const supplierSearch = supplierPicker.querySelector('[data-tracking-supplier-search]');
        const supplierList = supplierPicker.querySelector('[data-tracking-supplier-options]');
        const supplierEmpty = supplierPicker.querySelector('[data-tracking-supplier-empty]');
        const supplierOptions = [...supplierPicker.querySelectorAll('[data-tracking-supplier-option]')];

        if (supplierTrigger && supplierCurrent && supplierValue && supplierMenu && supplierSearch && supplierList) {
            const visibleSupplierOptions = () => supplierOptions.filter(option => !option.hidden);

            function filterSuppliers(query = '') {
                const term = query.trim().toLocaleLowerCase();
                for (const option of supplierOptions) {
                    option.hidden = !option.dataset.search.toLocaleLowerCase().includes(term);
                }
                if (supplierEmpty) supplierEmpty.hidden = visibleSupplierOptions().length > 0;
            }

            function closeSupplierMenu(returnFocus = false) {
                supplierMenu.hidden = true;
                supplierMenu.classList.remove('is-open-up');
                supplierPicker.classList.remove('is-open');
                supplierTrigger.setAttribute('aria-expanded', 'false');
                supplierMenu.style.maxHeight = '';
                if (returnFocus) supplierTrigger.focus();
            }

            function openSupplierMenu() {
                supplierMenu.hidden = false;
                supplierPicker.classList.add('is-open');
                supplierTrigger.setAttribute('aria-expanded', 'true');
                supplierSearch.value = '';
                filterSuppliers();
                positionPickerMenu(supplierTrigger, supplierMenu);
                supplierSearch.focus();
            }

            function selectSupplier(option) {
                supplierValue.value = option.dataset.value;
                for (const item of supplierOptions) {
                    const selected = item === option;
                    item.classList.toggle('is-selected', selected);
                    item.setAttribute('aria-selected', String(selected));
                }

                const title = document.createElement('strong');
                title.textContent = option.dataset.label;
                const subtitle = document.createElement('small');
                subtitle.textContent = option.dataset.value ? 'Selected supplier' : 'Select or search a supplier name';
                supplierCurrent.replaceChildren(title, subtitle);
                closeSupplierMenu(true);
            }

            supplierTrigger.addEventListener('click', () => supplierMenu.hidden ? openSupplierMenu() : closeSupplierMenu());
            supplierTrigger.addEventListener('keydown', event => {
                if (['ArrowDown', 'Enter', ' '].includes(event.key)) {
                    event.preventDefault();
                    if (supplierMenu.hidden) openSupplierMenu();
                }
            });
            supplierSearch.addEventListener('input', () => filterSuppliers(supplierSearch.value));
            supplierSearch.addEventListener('keydown', event => {
                const shown = visibleSupplierOptions();
                if (event.key === 'ArrowDown' && shown.length) {
                    event.preventDefault();
                    shown[0].focus();
                } else if (event.key === 'Enter' && shown.length) {
                    event.preventDefault();
                    selectSupplier(shown[0]);
                } else if (event.key === 'Escape') {
                    event.preventDefault();
                    closeSupplierMenu(true);
                }
            });
            for (const option of supplierOptions) option.addEventListener('click', () => selectSupplier(option));
            supplierList.addEventListener('keydown', event => {
                const shown = visibleSupplierOptions();
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
                    closeSupplierMenu(true);
                }
            });
            document.addEventListener('pointerdown', event => {
                if (!supplierPicker.contains(event.target)) closeSupplierMenu();
            });
            window.addEventListener('resize', () => {
                if (!supplierMenu.hidden) positionPickerMenu(supplierTrigger, supplierMenu);
            });
            window.addEventListener('scroll', () => {
                if (!supplierMenu.hidden) positionPickerMenu(supplierTrigger, supplierMenu);
            }, true);
        }
    }

    const picker = document.querySelector('[data-tracking-tender-picker]');
    if (!picker) return;

    const form = picker.closest('form');
    const trigger = picker.querySelector('[data-tracking-tender-trigger]');
    const current = picker.querySelector('[data-tracking-tender-current]');
    const value = picker.querySelector('[data-tracking-tender-value]');
    const menu = picker.querySelector('[data-tracking-tender-menu]');
    const search = picker.querySelector('[data-tracking-tender-search]');
    const list = picker.querySelector('[data-tracking-tender-options]');
    const empty = picker.querySelector('[data-tracking-tender-empty]');
    const options = [...picker.querySelectorAll('[data-tracking-tender-option]')];
    if (!form || !trigger || !current || !value || !menu || !search || !list) return;

    const visibleOptions = () => options.filter(option => !option.hidden);

    function filterOptions(query = '') {
        const term = query.trim().toLocaleLowerCase();
        for (const option of options) option.hidden = !option.dataset.search.toLocaleLowerCase().includes(term);
        if (empty) empty.hidden = visibleOptions().length > 0;
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
        positionPickerMenu(trigger, menu);
        search.focus();
    }

    function selectOption(option) {
        value.value = option.dataset.value;
        for (const item of options) {
            const selected = item === option;
            item.classList.toggle('is-selected', selected);
            item.setAttribute('aria-selected', String(selected));
        }

        const title = document.createElement('strong');
        title.textContent = option.querySelector('strong')?.textContent ?? '';
        const meta = document.createElement('small');
        meta.textContent = option.querySelector('small')?.textContent ?? '';
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
        if (!menu.hidden) positionPickerMenu(trigger, menu);
    });
    window.addEventListener('scroll', () => {
        if (!menu.hidden) positionPickerMenu(trigger, menu);
    }, true);
    form.addEventListener('submit', event => {
        if (value.value) return;
        event.preventDefault();
        openMenu();
        search.setCustomValidity('Choose a tender before viewing progress.');
        search.reportValidity();
        search.addEventListener('input', () => search.setCustomValidity(''), { once: true });
    });
})();
