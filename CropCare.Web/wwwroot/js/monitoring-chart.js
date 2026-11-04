// CropCare Batch 6: dependency-free SVG chart for a single API history page.
// This script does not send measurements or modify the database.
(() => {
    'use strict';
    const host = document.getElementById('soilChart');
    if (!host) return;
    const dateInput = document.getElementById('historyDate');
    const clearButton = document.getElementById('clearHistoryDate');
    const status = document.getElementById('chartStatus');
    const count = document.getElementById('historyCount');
    const table = document.getElementById('monitoringHistoryTable');
    const ns = 'http://www.w3.org/2000/svg';
    let records = [];
    try { records = JSON.parse(host.dataset.points || '[]'); }
    catch { records = []; }

    const dateInPhilippines = ms => {
        if (!Number.isFinite(ms) || ms <= 0) return '';
        const parts = new Intl.DateTimeFormat('en-US', {
            timeZone: 'Asia/Manila', year: 'numeric',
            month: '2-digit', day: '2-digit'
        }).formatToParts(new Date(ms));
        const value = name => parts.find(p => p.type === name)?.value || '';
        return `${value('year')}-${value('month')}-${value('day')}`;
    };
    const make = (tag, attrs = {}, content) => {
        const node = document.createElementNS(ns, tag);
        for (const [key, val] of Object.entries(attrs))
            node.setAttribute(key, String(val));
        if (content !== undefined) node.textContent = String(content);
        return node;
    };
    const prettyTime = time => new Intl.DateTimeFormat('en-PH', {
        timeZone: 'Asia/Manila', month: 'short', day: 'numeric',
        hour: 'numeric', minute: '2-digit'
    }).format(new Date(time));

    function draw() {
        const day = dateInput?.value || '';
        const visible = records.filter(point =>
            !day || dateInPhilippines(Number(point.time)) === day);
        const points = visible.filter(point =>
            Number.isFinite(Number(point.time)) && Number(point.time) > 0 &&
            point.value !== null && Number.isFinite(Number(point.value)))
            .map(point => ({ time: Number(point.time), value: Number(point.value) }))
            .sort((a, b) => a.time - b.time);
        host.replaceChildren();
        const shownRows = Array.from(document.querySelectorAll('[data-history-card]'));
        let shown = 0;
        for (const tr of shownRows) {
            const match = !day || tr.dataset.readingDate === day;
            tr.hidden = !match;
            if (match) shown++;
        }
        if (count) count.textContent = `${shown} readings visible on this page`;
        if (points.length === 0) {
            status.textContent = 'No timestamped measurements for this metric and date on this page.';
            host.textContent = 'No graph data is available for the current selection.';
            return;
        }
        const width = 800, height = 310;
        const left = 65, right = 25, top = 25, bottom = 60;
        const innerW = width - left - right, innerH = height - top - bottom;
        const loX = points[0].time, hiX = points[points.length - 1].time;
        let loY = Math.min(...points.map(p => p.value));
        let hiY = Math.max(...points.map(p => p.value));
        const pad = Math.max((hiY - loY) * 0.15, Math.abs(hiY) * 0.02, 0.5);
        loY -= pad;
        hiY += pad;
        const X = ms => left + (hiX === loX ? innerW / 2 : (ms - loX) * innerW / (hiX - loX));
        const Y = val => top + innerH - (val - loY) * innerH / (hiY - loY);
        const svg = make('svg', {
            viewBox: `0 0 ${width} ${height}`, width: '100%',
            role: 'img', 'aria-label': `Graph of ${host.dataset.label || 'soil value'}`,
            style: 'max-height:360px; display:block;'
        });
        for (let i = 0; i <= 4; i++) {
            const val = loY + (hiY - loY) * i / 4;
            const y = Y(val);
            svg.appendChild(make('line', {
                x1: left, y1: y, x2: width - right, y2: y,
                stroke: '#e3e8e4', 'stroke-width': 1
            }));
            svg.appendChild(make('text', {
                x: left - 8, y: y + 4, 'text-anchor': 'end',
                'font-size': 13, fill: '#555'
            }, val.toFixed(1)));
        }
        svg.appendChild(make('line', {
            x1: left, y1: top, x2: left,
            y2: top + innerH, stroke: '#67766b'
        }));
        svg.appendChild(make('line', {
            x1: left, y1: top + innerH,
            x2: width - right, y2: top + innerH, stroke: '#67766b'
        }));
        // Null measurements break a line instead of implying an observed value between samples.
        let segment = [];
        const flush = () => {
            if (segment.length > 1) svg.appendChild(make('polyline', {
                points: segment.join(' '), fill: 'none', stroke: '#198754',
                'stroke-width': 3, 'stroke-linejoin': 'round', 'stroke-linecap': 'round'
            }));
            segment = [];
        };
        for (const row of visible.slice().sort((a,b) => Number(a.time) - Number(b.time))) {
            if (row.value === null || !Number.isFinite(Number(row.value)) || !Number.isFinite(Number(row.time)) || Number(row.time) <= 0) flush();
            else segment.push(`${X(Number(row.time))},${Y(Number(row.value))}`);
        }
        flush();
        for (const point of points) {
            const circle = make('circle', {
                cx: X(point.time), cy: Y(point.value), r: 4,
                fill: '#198754'
            });
            circle.appendChild(make('title', {},
                `${prettyTime(point.time)}: ${point.value} ${host.dataset.unit || ''}`));
            svg.appendChild(circle);
        }
        const label = (x, text, anchor) => svg.appendChild(make('text', {
            x, y: height - 18, 'text-anchor': anchor,
            'font-size': 13, fill: '#555'
        }, text));
        label(left, prettyTime(loX), 'start');
        label(width - right, prettyTime(hiX), 'end');
        host.appendChild(svg);
        status.textContent = `${points.length} plotted measurements from this page. Hover on points to see exact values.`;
    }
    dateInput?.addEventListener('change', draw);
    clearButton?.addEventListener('click', () => {
        if (dateInput) dateInput.value = '';
        draw();
    });
    draw();
})();
