(() => {
    const pickers = document.querySelectorAll('[data-tender-picker]');

    for (const picker of pickers) {
        const form = picker.closest('form');
        const trigger = picker.querySelector('[data-tender-trigger]');
        const current = picker.querySelector('[data-tender-current]');
        const value = picker.querySelector('[data-tender-value]');
        const menu = picker.querySelector('[data-tender-menu]');
        const search = picker.querySelector('[data-tender-search]');
        const list = picker.querySelector('[data-tender-options]');
        const empty = picker.querySelector('[data-tender-empty]');
        const options = Array.from(picker.querySelectorAll('[data-tender-option]'));

        if (!form || !trigger || !current || !value || !menu || !search || !list) continue;

        function getVisibleOptions() {
            return options.filter(option => !option.hidden);
        }

        function updateOptions(query = '') {
            const term = query.trim().toLocaleLowerCase();
            for (const option of options) {
                option.hidden = !option.dataset.search.toLocaleLowerCase().includes(term);
            }
            if (empty) empty.hidden = getVisibleOptions().length > 0;
        }

        function positionMenu() {
            const rect = trigger.getBoundingClientRect();
            const edgeGap = 12;
            const menuGap = 8;
            const below = Math.max(0, window.innerHeight - rect.bottom - edgeGap);
            const above = Math.max(0, rect.top - edgeGap);
            const opensUp = below < 220 && above > below;
            const available = opensUp ? above : below;
            menu.classList.toggle('is-open-up', opensUp);
            // Cap the popup to the exact space on its chosen side so it never escapes the viewport.
            menu.style.maxHeight = `${Math.max(0, Math.min(420, available - menuGap))}px`;
        }

        function openMenu() {
            menu.hidden = false;
            picker.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            search.value = '';
            updateOptions();
            positionMenu();
            search.focus();
        }

        function closeMenu(returnFocus = false) {
            menu.hidden = true;
            menu.classList.remove('is-open-up');
            picker.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
            menu.style.maxHeight = '';
            if (returnFocus) trigger.focus();
        }

        function selectOption(option) {
            value.value = option.dataset.value;
            for (const item of options) {
                const isSelected = item === option;
                item.classList.toggle('is-selected', isSelected);
                item.setAttribute('aria-selected', String(isSelected));
            }

            const title = document.createElement('span');
            title.className = 'compare-tender-current-title';
            title.textContent = option.querySelector('.compare-tender-option-main > span')?.textContent ?? '';
            const meta = document.createElement('small');
            meta.textContent = [...option.querySelectorAll('.compare-tender-option-meta small')]
                .map(item => item.textContent.trim()).join(' · ');
            current.replaceChildren(title, meta);
            closeMenu(true);
        }

        trigger.addEventListener('click', () => menu.hidden ? openMenu() : closeMenu());
        trigger.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                if (menu.hidden) openMenu();
            }
        });

        search.addEventListener('input', () => updateOptions(search.value));
        search.addEventListener('keydown', event => {
            const shown = getVisibleOptions();
            if (event.key === 'ArrowDown' && shown.length) {
                event.preventDefault();
                shown[0].focus();
            } else if (event.key === 'ArrowUp' && shown.length) {
                event.preventDefault();
                shown[shown.length - 1].focus();
            } else if (event.key === 'Escape') {
                event.preventDefault();
                closeMenu(true);
            }
        });

        for (const option of options) {
            option.addEventListener('click', () => selectOption(option));
        }

        list.addEventListener('keydown', event => {
            const shown = getVisibleOptions();
            const index = shown.indexOf(document.activeElement);
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                const step = event.key === 'ArrowDown' ? 1 : -1;
                shown[(index + step + shown.length) % shown.length]?.focus();
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
            search.setCustomValidity('Choose a tender before loading the comparison.');
            search.reportValidity();
            search.addEventListener('input', () => search.setCustomValidity(''), { once: true });
        });
    }

    const supplierPicker = document.querySelector('[data-sourcing-supplier-picker]');
    if (!supplierPicker) return;

    const supplierTrigger = supplierPicker.querySelector('[data-sourcing-supplier-trigger]');
    const supplierCurrent = supplierPicker.querySelector('[data-sourcing-supplier-current]');
    const supplierValue = supplierPicker.querySelector('[data-sourcing-supplier-value]');
    const supplierMenu = supplierPicker.querySelector('[data-sourcing-supplier-menu]');
    const supplierSearch = supplierPicker.querySelector('[data-sourcing-supplier-search]');
    const supplierList = supplierPicker.querySelector('[data-sourcing-supplier-options]');
    const supplierEmpty = supplierPicker.querySelector('[data-sourcing-supplier-empty]');
    const supplierOptions = Array.from(supplierPicker.querySelectorAll('[data-sourcing-supplier-option]'));

    if (!supplierTrigger || !supplierCurrent || !supplierValue || !supplierMenu || !supplierSearch || !supplierList) return;

    function getVisibleSupplierOptions() {
        return supplierOptions.filter(option => !option.hidden);
    }

    function filterSuppliers(query = '') {
        const term = query.trim().toLocaleLowerCase();
        for (const option of supplierOptions) {
            option.hidden = !option.dataset.search.toLocaleLowerCase().includes(term);
        }
        if (supplierEmpty) supplierEmpty.hidden = getVisibleSupplierOptions().length > 0;
    }

    function positionSupplierMenu() {
        const rect = supplierTrigger.getBoundingClientRect();
        const below = Math.max(0, window.innerHeight - rect.bottom - 12);
        const above = Math.max(0, rect.top - 12);
        const opensUp = below < Math.min(supplierMenu.scrollHeight, 220) && above > below;
        const available = opensUp ? above : below;
        supplierMenu.classList.toggle('is-open-up', opensUp);
        supplierMenu.style.maxHeight = `${Math.max(0, Math.min(420, available - 8))}px`;
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
        positionSupplierMenu();
        supplierSearch.focus();
    }

    function selectSupplier(option) {
        supplierValue.value = option.dataset.value;
        supplierCurrent.textContent = option.dataset.label;
        for (const item of supplierOptions) {
            const selected = item === option;
            item.classList.toggle('is-selected', selected);
            item.setAttribute('aria-selected', String(selected));
        }
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
        const shown = getVisibleSupplierOptions();
        if (event.key === 'ArrowDown' && shown.length) {
            event.preventDefault();
            shown[0].focus();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            closeSupplierMenu(true);
        }
    });
    for (const option of supplierOptions) option.addEventListener('click', () => selectSupplier(option));
    supplierList.addEventListener('keydown', event => {
        const shown = getVisibleSupplierOptions();
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
        if (!supplierMenu.hidden) positionSupplierMenu();
    });
    window.addEventListener('scroll', () => {
        if (!supplierMenu.hidden) positionSupplierMenu();
    }, true);
})();
