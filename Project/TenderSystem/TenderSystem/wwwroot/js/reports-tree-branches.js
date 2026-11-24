// Places actual filter controls on new twigs without covering the existing tree nodes.
window.ReportTreeBranches = function (garden, root, reducedMotion) {
    'use strict';
    var ns = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(ns, 'svg'); svg.classList.add('rt-shoots'); svg.setAttribute('aria-hidden', 'true'); garden.append(svg);
    var current = null, previousNodes = [], previousSize = '', pending = false;
    function rect(element, bounds) {
        var r = element.getBoundingClientRect();
        return { x: r.left - bounds.left, y: r.top - bounds.top, w: r.width, h: r.height };
    }
    function overlaps(a, b, gap) { return a.x < b.x+b.w+gap && a.x+a.w+gap > b.x && a.y < b.y+b.h+gap && a.y+a.h+gap > b.y; }
    function pager(group, total) {
        var item = group.section.querySelector('.rt-subtree-pager');
        if (!item) {
            item = document.createElement('div'); item.className = 'rt-subtree-pager';
            var before = document.createElement('button'), text = document.createElement('span'), after = document.createElement('button');
            before.type = after.type = 'button'; before.textContent = '←'; after.textContent = '→';
            before.setAttribute('aria-label', 'Previous leaves'); after.setAttribute('aria-label', 'More leaves');
            item.append(before, text, after); group.section.append(item);
            before.addEventListener('click', function () { group.treePage--; pending = true; arrange(group, group.allGroups); });
            after.addEventListener('click', function () { group.treePage++; pending = true; arrange(group, group.allGroups); });
        }
        var pages = Math.ceil(total / group.pageSize);
        item.hidden = pages <= 1;
        item.children[0].disabled = group.treePage === 0;
        item.children[2].disabled = group.treePage >= pages - 1;
        item.children[1].textContent = (group.treePage + 1) + ' / ' + pages;
        return item;
    }
    function arrange(group, groups) {
        if (!group || group.section.hidden) { svg.replaceChildren(); current = null; return; }
        var wide = window.matchMedia('(min-width: 768px)').matches && garden.clientWidth >= 740 && !group.forceCompact;
        garden.classList.toggle('rt-compact-tree', !wide);
        group.section.classList.toggle('rt-subtree', wide);
        var destination = wide ? garden : group.unit;
        if (group.section.parentElement !== destination) destination.append(group.section);
        var choices = Array.from(group.section.querySelectorAll('.rt-option'));
        group.allGroups = groups;
        group.pageSize = choices.some(function (choice) { return choice.textContent.length > 130; }) ? 1 : (group.key === 'tender' ? 2 : 3);
        group.treePage = Math.min(group.treePage || 0, Math.max(0, Math.ceil(choices.length / group.pageSize) - 1));
        choices.forEach(function (choice, index) { choice.hidden = wide && Math.floor(index / group.pageSize) !== group.treePage; });
        var pageControl = choices.length ? pager(group, choices.length) : group.section.querySelector('.rt-subtree-pager');
        if (pageControl && !choices.length) pageControl.hidden = true;
        if (pageControl && !wide) pageControl.hidden = true;
        group.section.querySelectorAll('.rt-field').forEach(function (field) { field.classList.toggle('rt-native-leaf', !field.querySelector('.rt-options')); });
        if (!wide) {
            svg.replaceChildren(); previousSize = '';
            group.section.querySelectorAll('.rt-node').forEach(function (node) { node.classList.remove('rt-node'); });
            return;
        }
        var nodes = choices.filter(function (choice) { return !choice.hidden; });
        nodes = nodes.concat(Array.from(group.section.querySelectorAll('.rt-native-leaf')));
        nodes = nodes.concat(Array.from(group.section.querySelectorAll('.rt-no-options')));
        if (pageControl && !pageControl.hidden) nodes.push(pageControl);
        nodes.push(group.section.querySelector('.rt-section-actions'));
        var bounds = garden.getBoundingClientRect(), size = bounds.width + '/' + bounds.height + '/' + group.key + '/' + group.treePage;
        if (!pending && current === group && size === previousSize && nodes.length === previousNodes.length && nodes.every(function (node, i) { return node === previousNodes[i]; })) return;
        var animate = pending || current !== group; pending = false; current = group; previousNodes = nodes; previousSize = size;
        svg.setAttribute('viewBox', '0 0 ' + bounds.width + ' ' + bounds.height); svg.replaceChildren();
        var parent = rect(group.leaf, bounds);
        var obstacles = groups.map(function (item) { return rect(item.leaf, bounds); }); obstacles.push(rect(root, bounds));
        var failed = false;
        nodes.forEach(function (node, index) {
            if (failed) return;
            node.classList.add('rt-node');
            var w = node.offsetWidth, h = node.offsetHeight, candidates = [];
            for (var y = 12 + h/2; y < bounds.height-h/2-12; y += 22) {
                for (var x = 12 + w/2; x < bounds.width-w/2-12; x += 22) {
                    var box = { x: x-w/2, y: y-h/2, w: w, h: h };
                    if (obstacles.some(function (other) { return overlaps(box, other, 11); })) continue;
                    var dx = x-(parent.x+parent.w/2), dy = y-(parent.y+parent.h);
                    candidates.push({ box: box, score: Math.hypot(dx,dy) + (dy < 12 ? 170 : 0) + Math.max(0,dy-160)*1.4 });
                }
            }
            candidates.sort(function (a,b) { return a.score-b.score; });
            if (!candidates.length) {
                // A narrow desktop can use the same readable inline branch as mobile.
                failed = true; group.forceCompact = true; previousSize = ''; pending = true;
                arrange(group, groups);
                return;
            }
            var placed = candidates[0].box; obstacles.push(placed);
            var cx = placed.x+w/2, cy = placed.y+h/2;
            node.style.setProperty('--child-x', cx+'px'); node.style.setProperty('--child-y', cy+'px');
            var px = parent.x+parent.w/2, py = cy > parent.y+parent.h/2 ? parent.y+parent.h-2 : parent.y+2;
            var tipY = cy > py ? placed.y+3 : placed.y+h-3, dy = tipY-py;
            var d = 'M '+px+' '+py+' C '+px+' '+(py+dy*.5)+', '+cx+' '+(tipY-dy*.35)+', '+cx+' '+tipY;
            var twig = document.createElementNS(ns, 'path'); twig.setAttribute('d',d); twig.setAttribute('class','rt-new-twig'); svg.append(twig);
            twig.style.setProperty('--twig-length',twig.getTotalLength());
            if (animate && !reducedMotion.matches) {
                twig.classList.add('rt-twig-grow');
                node.animate([{ opacity:0, transform:'translate(calc(-50% + '+(px-cx)+'px), calc(-50% + '+(py-cy)+'px)) scale(.55)' },
                    { opacity:1, transform:'translate(-50%,-50%) scale(1)' }], {duration:420,delay:index*45,easing:'cubic-bezier(.22,.61,.36,1)',fill:'backwards'});
            }
        });
    }
    return {
        show: function (group, groups) { group.section.hidden=false; pending=true; group.allGroups=groups; requestAnimationFrame(function () { if (!group.section.inert) arrange(group, groups); }); },
        arrange: arrange,
        clear: function () { svg.replaceChildren(); current=null; previousNodes=[]; garden.classList.toggle('rt-compact-tree', window.innerWidth < 768 || garden.clientWidth < 740); }
    };
};
