// Presentation only: keep the original authorized GET fields and report/export queries.
(function () {
    'use strict';
    var form = document.querySelector('[data-report-tree="true"]');
    if (!form) return;
    var original = form.querySelector('.row');
    if (!original) return;
    var reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    function el(tag, className, text) {
        var item = document.createElement(tag);
        if (className) item.className = className;
        if (text) item.textContent = text;
        return item;
    }
    var groups = [
        { key: 'department', title: 'Department', icon: 'diagram-3', names: ['departmentIds'], hint: 'Choose one or more departments available in this report.' },
        { key: 'supplier', title: 'Supplier', icon: 'shop', names: ['supplierIds'], hint: 'Choose one or more suppliers available to your account.' },
        { key: 'tender', title: 'Tenders', icon: 'file-earmark-text', names: ['tenderNumbers'], hint: 'Choose one or more tenders available to your account.' },
        { key: 'status', title: 'Status', icon: 'check2-circle', names: ['statuses'], hint: 'Choose one or more recorded statuses, or keep all statuses.' },
        { key: 'dates', title: 'Date range', icon: 'calendar3', names: ['from', 'to'], hint: 'Dates use UTC. Choose the same date twice to see one day.' },
        { key: 'search', title: 'Order', icon: 'search', names: ['sort'], hint: 'Choose how to arrange the matching records.' }
    ].map(function (group) {
        group.fields = group.names.map(function (name) { return form.elements.namedItem(name); }).filter(Boolean);
        return group;
    }).filter(function (group) { return group.fields.length; });
    var shell = el('section', 'rt-garden'); shell.setAttribute('aria-label', 'Report filter tree');
    var heading = el('div', 'rt-heading');
    var headingText = el('div');
    headingText.append(el('span', 'rt-eyebrow', 'Follow a branch. Find your focus.'), el('h3', '', 'Let your report take shape'));
    heading.append(headingText, el('p', '', 'Every branch is optional. Your results change only when you choose Show results.'));
    var workspace = el('div', 'rt-workspace');
    var garden = el('div', 'rt-canopy');
    var art = el('div', 'rt-art'); art.setAttribute('aria-hidden', 'true');
    var treeImage = el('img'); treeImage.alt = ''; treeImage.src = form.dataset.treeArt;
    treeImage.decoding = 'async'; treeImage.draggable = false;
    treeImage.addEventListener('error', function () { art.hidden = true; });
    art.append(treeImage);
    var glow = el('canvas', 'rt-wood-light'); glow.width = 960; glow.height = 640;
    art.append(glow);
    var woodLight = window.ReportTreeLight ? window.ReportTreeLight(treeImage, glow, reduced) : null;
    var leaves = el('div', 'rt-leaves');
    var choiceBed = el('div', 'rt-choice-bed');
    var root = el('button', 'rt-root'); root.type = 'button';
    root.append(el('i', 'bi bi-flower2'), el('small', '', 'Your report'), el('strong', '', form.dataset.treeTitle));
    var trail = el('div', 'rt-trail'); trail.setAttribute('aria-label', 'Filter selection summary');
    var live = el('p', 'rt-live'); live.setAttribute('role', 'status');
    var branchLayout = window.ReportTreeBranches ? window.ReportTreeBranches(garden, root, reduced) : null;
    var active = null, hovered = null, dirty = false, paintFrame;
    var standardMode = false;
    function isSet(field) { return field.name === 'sort' ? field.value !== 'newest' : !!field.value; }
    function textFor(field) { return field.tagName === 'SELECT' ? Array.from(field.selectedOptions).map(function (option) { return option.textContent; }).join(' or ') : field.value; }
    function refresh() {
        trail.replaceChildren(el('span', 'rt-trail-label', dirty ? 'Your next report' : 'Current report'));
        var count = 0;
        groups.forEach(function (group) {
            var selected = group.fields.filter(isSet);
            group.leaf.classList.toggle('rt-selected', selected.length > 0);
            group.leaf.classList.toggle('rt-focused', active === group);
            group.summary.textContent = selected.length ? selected.map(textFor).join(' → ') : 'All records';
            group.summary.title = group.summary.textContent;
            group.toggle.checked = selected.length > 0 || active === group || !!group.confirmedAll;
            if (selected.length) {
                count++;
                var chip = el('button', 'rt-chip', group.title + ': ' + selected.map(textFor).join(' → '));
                chip.type = 'button'; chip.addEventListener('click', function () { open(group, true); }); trail.append(chip);
            }
        });
        if (!count) trail.append(el('span', 'rt-all', 'All available records'));
        live.textContent = dirty ? 'Selections changed. Show results to apply them; the table below has not changed.' : 'Open any branch, or show all results.';
        shell.classList.toggle('rt-dirty', dirty);
        cancelAnimationFrame(paintFrame); paintFrame = requestAnimationFrame(draw);
    }
    function markDirty(event) {
        dirty = true;
        document.querySelectorAll('[data-report-exports] a').forEach(function (link) { link.setAttribute('aria-disabled', 'true'); link.classList.add('disabled'); });
        var pending = form.querySelector('[data-report-pending]'); if (pending) pending.hidden = false;
        refresh();
        if (event && event.target && event.target.name !== 'sort') refreshRelatedChoices(event.target);
    }
    function clear(group) {
        group.confirmedAll = false;
        group.fields.forEach(function (field) { field.value = field.name === 'sort' ? 'newest' : ''; field.dispatchEvent(new Event('change', { bubbles: true })); });
        group.section.querySelectorAll('[data-rt-choice]').forEach(function (input) { input.checked = input.value === ''; });
        if (active === group) open(null, false);
        markDirty();
    }
    function open(group, focus) {
        active = group;
        garden.classList.toggle('rt-has-active', !!group);
        root.querySelector('small').textContent = 'Your report';
        root.setAttribute('aria-label', 'Report scope: ' + form.dataset.treeTitle);
        groups.forEach(function (item) {
            item.edit.setAttribute('aria-expanded', String(item === group));
            item.unit.classList.toggle('rt-unfolded', item === group);
            unfold(item, item === group);
        });
        refresh();
        if (group && focus) group.section.querySelector('h4').focus({ preventScroll: true });
        requestAnimationFrame(reserveSpace);
    }
    function unfold(group, show) {
        var section = group.section;
        section.inert = !show; section.setAttribute('aria-hidden', String(!show));
        if (group.animation) { group.animation.cancel(); group.animation = null; }
        if (show) {
            section.hidden = false;
            if (branchLayout) branchLayout.show(group, groups);
        } else if (!section.hidden) {
            if (reduced.matches) section.hidden = true;
            else {
                group.animation = section.animate([{ opacity:1 }, { opacity:0 }], {duration:160,easing:'ease-out'});
                group.animation.onfinish = function () { if (active !== group) section.hidden = true; group.animation = null; };
            }
        }
    }
    function reserveSpace() { garden.style.marginBottom = ''; }
    function checkboxChoices(field, container) {
        var oldLabel = container.querySelector('label'); if (oldLabel) oldLabel.hidden = true;
        var list = el('div', 'rt-options'); list.setAttribute('role', 'group');
        list.setAttribute('aria-label', oldLabel ? oldLabel.textContent : field.name);
        field.hidden = true;
        field.renderTreeChoices = function () {
            list.replaceChildren();
            var selected = Array.from(field.selectedOptions).map(function (option) { return option.value; });
            var options = Array.from(field.options);
            // An empty selection means all matching records on the server, including
            // related choices refreshed later. Do not enumerate or widen that scope.
            if (field.multiple && !options.some(function (option) { return !option.value; })) {
                var allLabel = el('label', 'rt-option');
                var allCheck = el('input'); allCheck.type = 'checkbox'; allCheck.value = '';
                allCheck.dataset.rtChoice = 'true'; allCheck.checked = selected.length === 0;
                allCheck.addEventListener('change', function () {
                    Array.from(field.options).forEach(function (option) { option.selected = false; });
                    field.renderTreeChoices(); field.dispatchEvent(new Event('change', { bubbles: true }));
                });
                allLabel.append(allCheck, el('span', '', 'All matching ' + (field.name === 'tenderNumbers' ? 'tenders' : field.name === 'departmentIds' ? 'departments' : field.name === 'supplierIds' ? 'suppliers' : 'statuses')));
                allLabel.title = 'Includes every record allowed by your other filters and access.';
                list.append(allLabel);
            }
            options.forEach(function (option, index) {
                var label = el('label', 'rt-option');
                label.title = option.textContent;
                label.style.setProperty('--breath-delay', (index % 5) * -.65 + 's');
                var check = el('input'); check.type = 'checkbox'; check.value = option.value;
                check.checked = option.value ? selected.includes(option.value) : selected.length === 0;
                check.dataset.rtChoice = 'true';
                check.addEventListener('change', function () {
                    if (!option.value) Array.from(field.options).forEach(function (item) { item.selected = false; });
                    else option.selected = check.checked;
                    field.renderTreeChoices(); field.dispatchEvent(new Event('change', { bubbles: true }));
                    var replacement = Array.from(list.querySelectorAll('input')).find(function (item) { return item.value === check.value; });
                    if (replacement) replacement.focus({ preventScroll: true });
                });
                label.append(check, el('span', '', option.textContent)); list.append(label);
            });
            if (!field.options.length) list.append(el('p', 'rt-no-options', 'No options match the current selections. Clear another branch or change the dates.'));
        };
        container.append(list); field.renderTreeChoices();
    }
    var optionsRequest, optionsTimer, optionsVersion = 0;
    function refreshRelatedChoices(changed) {
        var downstream = { departmentIds: ['supplierIds','tenderNumbers','statuses'], supplierIds: ['tenderNumbers','statuses'], tenderNumbers: ['statuses'] };
        (downstream[changed.name] || []).forEach(function (name) {
            var field = form.elements.namedItem(name);
            if (field) { Array.from(field.options).forEach(function (option) { option.selected = false; }); if (field.renderTreeChoices) field.renderTreeChoices(); }
        });
        refresh();
        clearTimeout(optionsTimer); if (optionsRequest) optionsRequest.abort();
        var version = ++optionsVersion;
        optionsTimer = setTimeout(async function () {
            optionsRequest = new AbortController();
            try {
                var url = new URL(form.dataset.treeOptions, location.href);
                new URLSearchParams(new FormData(form)).forEach(function (value, key) { url.searchParams.append(key, value); });
                var response = await fetch(url, { signal: optionsRequest.signal, headers: { 'Accept': 'application/json' } });
                if (!response.ok) throw new Error('Choices unavailable');
                var data = await response.json(); if (version !== optionsVersion) return;
                var focused = document.activeElement;
                var focusField = focused.closest('.rt-field') ? focused.closest('.rt-field').querySelector('select') : null;
                var focusValue = focused.value;
                var choices = { departmentIds: data.departments, supplierIds: data.suppliers, tenderNumbers: data.tenders, statuses: data.statuses.map(function (value) { return { text: value, value: value }; }) };
                var pruned = false;
                Object.keys(choices).forEach(function (name) {
                    var field = form.elements.namedItem(name); if (!field) return;
                    var selected = Array.from(field.selectedOptions).map(function (option) { return option.value; });
                    if (selected.some(function (value) { return !choices[name].some(function (item) { return item.value === value; }); })) pruned = true;
                    field.replaceChildren();
                    choices[name].forEach(function (item) { field.add(new Option(item.text, item.value, false, selected.includes(item.value))); });
                    if (field.renderTreeChoices) field.renderTreeChoices();
                });
                if (focusField && focusField.parentElement) {
                    var replacement = Array.from(focusField.parentElement.querySelectorAll('[data-rt-choice]')).find(function (input) { return input.value === focusValue; });
                    if (replacement) replacement.focus({ preventScroll: true });
                }
                refresh();
                if (pruned) live.textContent = 'Selections that no longer match were cleared. Show results to apply your current choices.';
            } catch (error) {
                if (error.name !== 'AbortError' && version === optionsVersion) live.textContent = 'Options could not refresh. Show results will reload them from the system.';
            }
        }, 160);
    }
    groups.forEach(function (group, index) {
        var unit = el('div', 'rt-branch-unit' + (index % 2 ? ' rt-right' : '')); group.unit = unit;
        var leaf = el('div', 'rt-leaf'); group.leaf = leaf;
        var checkboxLabel = el('label', 'rt-toggle');
        var toggle = el('input'); toggle.type = 'checkbox'; toggle.setAttribute('aria-label', 'Use ' + group.title.toLowerCase() + ' filter');
        var edit = el('button', 'rt-leaf-button'); edit.type = 'button'; edit.setAttribute('aria-expanded', 'false');
        var icon = el('i', 'bi bi-' + group.icon); icon.setAttribute('aria-hidden', 'true');
        var summary = el('small', '', 'All records'); group.summary = summary; group.toggle = toggle; group.edit = edit;
        var label = el('span'); label.append(el('strong', '', group.title), summary); edit.append(icon, label, el('span', 'rt-arrow', '↗'));
        checkboxLabel.append(toggle); leaf.append(checkboxLabel, edit); unit.append(leaf); leaves.append(unit);
        var section = el('section', 'rt-section'); section.id = 'rt-section-' + group.key; section.hidden = true; group.section = section;
        edit.setAttribute('aria-controls', section.id); toggle.setAttribute('aria-controls', section.id);
        var h = el('h4', '', group.title); h.tabIndex = -1;
        section.append(el('span', 'rt-eyebrow', 'Refine your report'), h, el('p', 'rt-hint', group.hint));
        group.fields.forEach(function (field) {
            var container = field.closest('.row > div'); container.className = 'rt-field'; section.append(container);
            if (field.tagName === 'SELECT' && field.name !== 'sort') checkboxChoices(field, container);
            field.addEventListener('input', markDirty); field.addEventListener('change', markDirty);
        });
        var actions = el('div', 'rt-section-actions');
        var done = el('button', 'btn btn-brand rt-done', 'Done ✓'); done.type = 'button'; done.addEventListener('click', function () {
            group.confirmedAll = !group.fields.some(isSet);
            open(null, false);
            if (group.confirmedAll) live.textContent = 'All matching ' + group.title.toLowerCase() + ' included within your other filters and access. Show results to view them.';
            edit.focus({ preventScroll: true });
        });
        done.title = 'Close this branch and keep your selections. Show results applies the filters.';
        actions.append(el('small', '', 'Uncheck the leaf to clear this filter.'), done); section.append(actions); choiceBed.append(section);
        edit.addEventListener('click', function () { open(active === group ? null : group, false); });
        toggle.addEventListener('change', function () { if (toggle.checked) open(group, false); else clear(group); });
        function highlight(value) { hovered = value; cancelAnimationFrame(paintFrame); paintFrame = requestAnimationFrame(draw); }
        leaf.addEventListener('pointerenter', function () { highlight(group); });
        leaf.addEventListener('pointerleave', function () { highlight(null); });
        leaf.addEventListener('focusin', function () { highlight(group); });
        leaf.addEventListener('focusout', function (event) { if (!leaf.contains(event.relatedTarget)) highlight(null); });
        leaf.style.setProperty('--leaf-order', index);
        unit.style.setProperty('--branch-order', index);
        var position = { department: [22, 17], supplier: [78, 17], tender: [15, 42], status: [85, 42], dates: [24, 64], search: [76, 64] }[group.key];
        unit.style.setProperty('--node-x', position[0] + '%'); unit.style.setProperty('--node-y', position[1] + '%');
    });
    var leftLimb = el('div', 'rt-limb'), rightLimb = el('div', 'rt-limb');
    groups.forEach(function (group, index) { (index % 2 ? rightLimb : leftLimb).append(group.unit); });
    leaves.append(leftLimb, rightLimb);
    var viewBar = el('div', 'rt-view-bar'), modeLabel = el('span', 'rt-view-label', 'Tree view');
    var switchButton = el('button', 'btn btn-outline-secondary rt-switch', 'Switch'); switchButton.type = 'button';
    var switchIcon = el('i', 'bi bi-arrow-left-right'); switchIcon.setAttribute('aria-hidden', 'true'); switchButton.prepend(switchIcon);
    switchButton.title = 'Switch to standard filters';
    switchButton.setAttribute('aria-label', 'Switch to standard filters'); switchButton.setAttribute('aria-pressed', 'false');
    var standard = el('div', 'rt-standard-grid'); standard.hidden = true;
    switchButton.addEventListener('click', function () {
        standardMode = !standardMode; open(null, false);
        workspace.hidden = standardMode; standard.hidden = !standardMode;
        groups.forEach(function (group) {
            group.fields.forEach(function (field) {
                var container = field.closest('.rt-field');
                container.classList.remove('rt-node', 'rt-native-leaf');
                (standardMode ? standard : group.section).append(container);
                var label = container.querySelector('label');
                if (label && label.htmlFor === field.id) label.hidden = !standardMode && field.multiple;
                container.querySelectorAll('.rt-option').forEach(function (option) { option.hidden = false; });
            });
        });
        modeLabel.textContent = standardMode ? 'Standard filters' : 'Tree view';
        switchButton.setAttribute('aria-label', standardMode ? 'Switch to tree filters' : 'Switch to standard filters');
        switchButton.title = standardMode ? 'Switch to tree filters' : 'Switch to standard filters';
        switchButton.setAttribute('aria-pressed', String(standardMode));
        live.textContent = 'View switched. Your selections are unchanged.';
        refresh(); switchButton.focus({ preventScroll: true });
    });
    viewBar.append(modeLabel, switchButton);
    root.addEventListener('click', function () { open(null, false); });
    form.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && active) { var button = active.edit; open(null, false); button.focus({ preventScroll: true }); }
    });
    document.addEventListener('pointerdown', function (event) {
        if (active && !active.unit.contains(event.target) && !active.section.contains(event.target) && !event.target.closest('.rt-chip')) open(null, false);
    });
    garden.append(art, root, leaves); workspace.append(garden, choiceBed); shell.append(heading, viewBar, workspace, standard, trail, live);
    original.querySelectorAll('input[type="hidden"]').forEach(function (input) { form.append(input); });
    original.replaceWith(shell);
    var description = form.querySelector('.reports-filter-description'); if (description) description.hidden = true;
    var submit = form.querySelector('button[type="submit"]'); if (submit) { submit.textContent = 'Show results'; submit.classList.add('rt-submit'); }
    var disclosure = form.closest('[data-report-filter-disclosure]');
    if (disclosure) { disclosure.open = true; disclosure.addEventListener('toggle', function () { requestAnimationFrame(draw); }); }
    var label = document.getElementById('filters-heading'); if (label) label.replaceChildren(document.createTextNode('Explore your report '), el('small', '', '(optional)'));
    function draw() {
        if (standardMode) { if (woodLight) woodLight.set(null); if (branchLayout) branchLayout.clear(); return; }
        var selected = active || hovered || groups.find(function (group) { return group.fields.some(isSet); });
        if (woodLight) woodLight.set(selected ? selected.key : null);
        if (branchLayout) { if (active) branchLayout.arrange(active, groups); else branchLayout.clear(); }
    }
    var frame;
    if (window.ResizeObserver) new ResizeObserver(function () { cancelAnimationFrame(frame); frame = requestAnimationFrame(draw); }).observe(garden);
    else window.addEventListener('resize', draw);
    form.addEventListener('invalid', function (event) { var group = groups.find(function (item) { return item.fields.includes(event.target); }); if (group) open(group, false); }, true);
    refresh();
})();
