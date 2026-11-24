// Relight the existing artwork through a soft mask, keeping the bark's own detail.
window.ReportTreeLight = function (image, canvas, reducedMotion) {
    'use strict';
    var context = canvas.getContext('2d');
    var wood = document.createElement('canvas'), mask = document.createElement('canvas');
    wood.width = mask.width = canvas.width; wood.height = mask.height = canvas.height;
    var woodContext = wood.getContext('2d'), maskContext = mask.getContext('2d');
    var frame, ready = false, selected = null, samples = [], start = 0, lastPaint = 0;
    // Coordinates follow the artwork's six boughs, not business data or report IDs.
    var stem = [[.50,.94],[.53,.87],[.50,.80],[.455,.73],[.47,.66]];
    var upper = [[.50,.59],[.47,.53]];
    var routes = {
        department: upper.concat([[.43,.47],[.42,.39],[.395,.32],[.33,.28],[.25,.23],[.20,.19]]),
        supplier: upper.concat([[.54,.46],[.58,.40],[.61,.31],[.68,.26],[.76,.20],[.80,.18]]),
        tender: upper.concat([[.42,.47],[.36,.45],[.30,.48],[.23,.47],[.15,.46]]),
        status: upper.concat([[.53,.54],[.60,.57],[.69,.54],[.77,.49],[.85,.46]]),
        dates: [[.44,.64],[.37,.66],[.31,.69],[.24,.69]],
        search: [[.50,.61],[.58,.61],[.65,.64],[.72,.68],[.79,.70]]
    };
    function prepare() {
        if (!image.naturalWidth || !context) return;
        woodContext.clearRect(0,0,wood.width,wood.height);
        woodContext.drawImage(image,0,0,wood.width,wood.height);
        woodContext.globalCompositeOperation='source-atop';
        woodContext.globalAlpha=.66;
        woodContext.fillStyle=getComputedStyle(canvas).color;
        woodContext.fillRect(0,0,wood.width,wood.height);
        woodContext.globalAlpha=1;
        woodContext.globalCompositeOperation='source-over';
        ready = true;
        if (selected) begin();
    }
    function sample(points) {
        var result = [];
        for (var i = 0; i < points.length - 1; i++) {
            var a = points[Math.max(0, i - 1)], b = points[i], c = points[i + 1], d = points[Math.min(points.length - 1, i + 2)];
            for (var step = 0; step < 12; step++) {
                var t = step / 12, t2 = t * t, t3 = t2 * t;
                function coordinate(axis) { return .5 * ((2 * b[axis]) + (-a[axis] + c[axis]) * t + (2*a[axis]-5*b[axis]+4*c[axis]-d[axis])*t2 + (-a[axis]+3*b[axis]-3*c[axis]+d[axis])*t3); }
                result.push([coordinate(0) * canvas.width, coordinate(1) * canvas.height]);
            }
        }
        return result;
    }
    function paint(progress) {
        maskContext.clearRect(0,0,mask.width,mask.height);
        maskContext.lineCap='round';maskContext.lineJoin='round';
        var head=progress*(samples.length+20);
        for(var i=1;i<samples.length && i<head+1;i++) {
            var fraction=i/samples.length;
            var reveal=Math.min(1,Math.max(0,(head-i+1)/8));
            var wave=Math.max(0,1-Math.abs(head-i-4)/15);
            maskContext.strokeStyle='rgba(255,255,255,'+reveal*(.36+.64*wave)+')';
            maskContext.lineWidth=53*(1-fraction)+11;
            maskContext.beginPath();maskContext.moveTo(samples[i-1][0],samples[i-1][1]);
            maskContext.lineTo(samples[i][0],samples[i][1]);maskContext.stroke();
        }
        context.clearRect(0,0,canvas.width,canvas.height);
        context.globalCompositeOperation='source-over';context.drawImage(wood,0,0);
        context.globalCompositeOperation='destination-in';context.filter='blur(4px)';
        context.drawImage(mask,0,0);context.filter='none';
        context.globalCompositeOperation='source-over';
    }
    function tick(time) {
        if (!start) start = time;
        var progress = Math.min(1, (time - start) / 2100);
        if (time - lastPaint >= 30 || progress === 1) {
            // Smooth leading edge; the revealed bark stays illuminated behind it.
            paint(progress * progress * (3 - 2 * progress)); lastPaint = time;
        }
        if (progress < 1) frame = requestAnimationFrame(tick);
    }
    function begin() {
        cancelAnimationFrame(frame);
        samples = sample(stem.concat(routes[selected])); start = 0; lastPaint = 0;
        canvas.classList.add('rt-light-on');
        if (reducedMotion.matches) paint(1);
        else { paint(0); frame = requestAnimationFrame(tick); }
    }
    image.addEventListener('load', prepare);
    if (image.complete && image.naturalWidth) requestAnimationFrame(prepare);
    reducedMotion.addEventListener('change', function () { if (selected && ready) begin(); });
    return {
        set: function (key) {
            if (selected === key) return;
            selected = routes[key] ? key : null;
            cancelAnimationFrame(frame);
            if (!selected) { canvas.classList.remove('rt-light-on'); return; }
            if (ready) begin();
        }
    };
};
