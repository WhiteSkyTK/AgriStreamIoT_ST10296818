// Field dashboard: live telemetry (GetLatestTelemetry function), moisture chart, outlook,
// and the irrigation pipeline tracker (TriggerIrrigation -> queue -> ProcessIrrigationCommand -> Azure SQL).
(function () {
    'use strict';

    const config = JSON.parse(document.getElementById('field-config').textContent);
    let snapshot = JSON.parse(document.getElementById('field-snapshot').textContent);

    // Categorical slots in fixed order (validated for colour-vision deficiency); colour follows the probe.
    const SERIES = ['#2a78d6', '#eb6834', '#1baf7a', '#6250d6'];
    const INK = '#22302a';
    const GRID = '#e3e8e0';
    const WATER = '#2b76b9';
    const TZ = 'Africa/Johannesburg';
    const colourFor = id => SERIES[Math.max(0, config.sensors.indexOf(id)) % SERIES.length];
    const threshold = Number(config.threshold);

    const fmtTime = ms => new Date(ms).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', timeZone: TZ });
    const fmtTimeSec = ms => new Date(ms).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: TZ });
    const fixed = (v, d) => (v === null || v === undefined || Number.isNaN(Number(v))) ? '–' : Number(v).toFixed(d);
    const sleep = ms => new Promise(r => setTimeout(r, ms));

    // ── Field switcher ──────────────────────────────────────────────
    const switcher = document.getElementById('field-switch');
    if (switcher) switcher.addEventListener('change', () => { window.location.href = switcher.value; });

    // ── Chart plugins: crosshair + direct labels at line ends ───────
    const crosshair = {
        id: 'crosshair',
        afterDatasetsDraw(chart) {
            const active = chart.tooltip && chart.tooltip.getActiveElements();
            if (!active || !active.length) return;
            const x = active[0].element.x;
            const { top, bottom } = chart.chartArea;
            const ctx = chart.ctx;
            ctx.save();
            ctx.strokeStyle = 'rgba(34,48,42,.35)';
            ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(x, top); ctx.lineTo(x, bottom); ctx.stroke();
            ctx.restore();
        }
    };
    const endLabels = {
        id: 'endLabels',
        afterDatasetsDraw(chart) {
            const ctx = chart.ctx;
            ctx.save();
            ctx.font = '600 12px Archivo, system-ui, sans-serif';
            ctx.textBaseline = 'middle';
            chart.data.datasets.forEach((ds, i) => {
                const meta = chart.getDatasetMeta(i);
                if (meta.hidden || !meta.data.length) return;
                const last = meta.data[meta.data.length - 1];
                ctx.fillStyle = INK;
                ctx.fillText(ds.shortLabel || ds.label, last.x + 6, last.y);
            });
            ctx.restore();
        }
    };

    // ── Moisture chart ──────────────────────────────────────────────
    function seriesDatasets(snap) {
        const now = Date.now();
        const min = now - config.hours * 3600 * 1000;
        const byDevice = {};
        (snap.series || []).forEach(p => {
            (byDevice[p.deviceId] = byDevice[p.deviceId] || []).push({ x: Date.parse(p.timeUtc), y: p.soilMoisturePct });
        });
        const datasets = config.sensors.filter(id => byDevice[id]).map(id => ({
            label: id,
            shortLabel: id.split('-').pop(),
            data: byDevice[id].sort((a, b) => a.x - b.x),
            borderColor: colourFor(id),
            backgroundColor: colourFor(id),
            borderWidth: 2,
            pointRadius: 0,
            pointHoverRadius: 4,
            pointHoverBorderColor: '#fff',
            pointHoverBorderWidth: 2,
            tension: 0.25
        }));
        datasets.push({
            label: 'Irrigation threshold',
            shortLabel: 'threshold',
            data: [{ x: min, y: threshold }, { x: now, y: threshold }],
            borderColor: INK,
            borderDash: [6, 4],
            borderWidth: 1.5,
            pointRadius: 0,
            pointHoverRadius: 0,
            isThreshold: true
        });
        return { datasets, min, max: now };
    }

    function renderLegend(datasets) {
        const legend = document.getElementById('moisture-legend');
        legend.innerHTML = '';
        datasets.forEach(ds => {
            const item = document.createElement('span');
            if (ds.isThreshold) item.className = 'is-threshold';
            item.innerHTML = '<i style="background:' + (ds.isThreshold ? 'transparent' : ds.borderColor) + '"></i>';
            item.appendChild(document.createTextNode(ds.isThreshold ? 'Irrigation threshold (' + threshold + '%)' : ds.label));
            legend.appendChild(item);
        });
    }

    let moistureChart = null;
    function drawMoisture(snap) {
        const canvas = document.getElementById('moisture-chart');
        if (!canvas || typeof Chart === 'undefined') return;
        const { datasets, min, max } = seriesDatasets(snap);
        renderLegend(datasets);
        if (moistureChart) {
            moistureChart.data.datasets = datasets;
            moistureChart.options.scales.x.min = min;
            moistureChart.options.scales.x.max = max;
            moistureChart.update('none');
            return;
        }
        moistureChart = new Chart(canvas, {
            type: 'line',
            data: { datasets },
            options: {
                maintainAspectRatio: false,
                animation: false,
                parsing: false,
                layout: { padding: { right: 64 } },
                interaction: { mode: 'x', intersect: false },
                scales: {
                    x: {
                        type: 'linear', min, max,
                        grid: { color: GRID },
                        border: { color: '#c9d1c5' },
                        ticks: { color: '#5e6b63', maxTicksLimit: 8, callback: v => fmtTime(v) }
                    },
                    y: {
                        suggestedMin: Math.max(0, threshold - 10),
                        suggestedMax: threshold + 14,
                        grid: { color: GRID },
                        border: { display: false },
                        ticks: { color: '#5e6b63', callback: v => v + '%' },
                        title: { display: true, text: 'Soil moisture (%)', color: '#5e6b63' }
                    }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        filter: item => !item.dataset.isThreshold,
                        backgroundColor: '#ffffff',
                        titleColor: INK, bodyColor: INK,
                        borderColor: '#c9d1c5', borderWidth: 1,
                        padding: 10, boxPadding: 4,
                        callbacks: {
                            title: items => items.length ? fmtTime(items[0].parsed.x) + ' SAST' : '',
                            label: item => ' ' + item.dataset.label + ': ' + item.parsed.y.toFixed(1) + '%'
                        }
                    }
                }
            },
            plugins: [crosshair, endLabels]
        });
    }

    // ── 48-hour outlook (Monte Carlo on the web app) ────────────────
    let outlookChart = null;
    async function loadOutlook() {
        const text = document.getElementById('outlook-text');
        try {
            const res = await fetch(config.urls.outlook, { headers: { Accept: 'application/json' } });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const o = await res.json();
            const p10 = o.bands.map(b => ({ x: b.hour, y: b.p10 }));
            const p50 = o.bands.map(b => ({ x: b.hour, y: b.p50 }));
            const p90 = o.bands.map(b => ({ x: b.hour, y: b.p90 }));
            const datasets = [
                { label: 'Likely high (P90)', data: p90, borderWidth: 0, pointRadius: 0, fill: '+1', backgroundColor: 'rgba(43,118,185,.16)' },
                { label: 'Likely low (P10)', data: p10, borderWidth: 0, pointRadius: 0, fill: false },
                { label: 'Most likely', data: p50, borderColor: WATER, borderWidth: 2, pointRadius: 0, tension: 0.2 },
                { label: 'Threshold', data: [{ x: 1, y: o.thresholdPct }, { x: o.hoursAhead, y: o.thresholdPct }], borderColor: INK, borderDash: [6, 4], borderWidth: 1.5, pointRadius: 0 }
            ];
            const canvas = document.getElementById('outlook-chart');
            if (canvas && typeof Chart !== 'undefined') {
                if (outlookChart) outlookChart.destroy();
                outlookChart = new Chart(canvas, {
                    type: 'line',
                    data: { datasets },
                    options: {
                        maintainAspectRatio: false, animation: false, parsing: false,
                        interaction: { mode: 'x', intersect: false },
                        scales: {
                            x: { type: 'linear', min: 1, max: o.hoursAhead, grid: { color: GRID }, ticks: { color: '#5e6b63', stepSize: 12, callback: v => '+' + v + ' h' } },
                            y: { grid: { color: GRID }, border: { display: false }, ticks: { color: '#5e6b63', callback: v => v + '%' } }
                        },
                        plugins: {
                            legend: { display: false },
                            tooltip: {
                                filter: item => item.datasetIndex !== 3,
                                backgroundColor: '#ffffff', titleColor: INK, bodyColor: INK, borderColor: '#c9d1c5', borderWidth: 1,
                                callbacks: {
                                    title: items => items.length ? 'In ' + items[0].parsed.x + ' h' : '',
                                    label: item => ' ' + item.dataset.label + ': ' + item.parsed.y.toFixed(1) + '%'
                                }
                            }
                        }
                    },
                    plugins: [crosshair]
                });
            }
            const pct = v => Math.round(v * 100) + '%';
            const when = o.medianHoursToThreshold ? 'Half the scenarios need irrigation within ' + o.medianHoursToThreshold + ' h.' : 'Most scenarios stay above the threshold for 48 h.';
            text.textContent = 'Chance of falling below ' + o.thresholdPct + '%: ' + pct(o.probabilityBelowThreshold24h) + ' within 24 h, ' +
                pct(o.probabilityBelowThreshold48h) + ' within 48 h. ' + when + ' Band shows the P10–P90 range of ' +
                o.iterations.toLocaleString('en-GB') + ' simulated weather paths (' + Math.round(o.computeMs) + ' ms).';
        } catch (err) {
            text.textContent = 'Outlook unavailable: ' + err.message;
        }
    }

    // ── Live refresh (calls GetLatestTelemetry through the web app) ──
    function moistureState(v) {
        if (v === null || v === undefined) return ['is-unknown', 'No data'];
        if (v < threshold) return ['is-dry', 'Below threshold'];
        if (v < threshold + 3) return ['is-watch', 'Near threshold'];
        return ['is-good', 'Healthy'];
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el && el.textContent !== value) {
            el.textContent = value;
            el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
        }
    }

    function updateSummary(snap) {
        const s = (snap.fields || [])[0] || {};
        const avg = s.avgSoilMoisturePct;
        setText('avg-moisture', fixed(avg, 1));
        const [css, label] = moistureState(avg);
        const state = document.getElementById('moisture-state');
        state.className = 'moisture-state ' + css;
        state.textContent = label;
        const gauge = document.getElementById('hero-gauge');
        if (gauge && avg !== null && avg !== undefined) {
            gauge.style.setProperty('--level', Math.min(100, Math.max(0, avg / 50 * 100)).toFixed(1) + '%');
            gauge.classList.toggle('is-dry', css === 'is-dry');
        }
        setText('stat-min', fixed(s.minSoilMoisturePct, 1) + '%');
        setText('stat-temp', fixed(s.avgSoilTemperatureC, 1) + ' °C');
        setText('stat-ph', fixed(s.avgSoilPh, 2));
        setText('stat-ec', fixed(s.avgSoilEcDsM, 2) + ' dS/m');
        setText('stat-sensors', (s.sensorsReporting || 0) + ' of ' + config.sensors.length);
        if (s.latestReadingUtc) setText('stat-last', fmtTimeSec(Date.parse(s.latestReadingUtc)));
    }

    function ago(iso) {
        const secs = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
        if (secs < 45) return 'just now';
        if (secs < 3600) return Math.max(1, Math.floor(secs / 60)) + ' min ago';
        if (secs < 86400) return Math.floor(secs / 3600) + ' h ago';
        return Math.floor(secs / 86400) + ' days ago';
    }

    function updateSensors(snap) {
        const latest = {};
        (snap.latestReadings || []).forEach(r => { latest[r.deviceId] = r; });
        document.querySelectorAll('#sensor-grid [data-sensor]').forEach(tile => {
            const r = latest[tile.dataset.sensor];
            if (!r) return;
            tile.querySelectorAll('dd[data-k]').forEach(dd => {
                const key = dd.dataset.k;
                const digits = key === 'soilPh' || key === 'soilEcDsM' ? 2 : (key === 'batteryPct' || key === 'signalDbm' || key === 'nitrogenPpm') ? 0 : 1;
                const value = fixed(r[key], digits) + dd.dataset.u;
                if (dd.textContent !== value) { dd.textContent = value; dd.classList.remove('flash'); void dd.offsetWidth; dd.classList.add('flash'); }
            });
            const age = tile.querySelector('[data-k="age"]');
            if (age) age.textContent = 'Reported ' + ago(r.readingUtc);
        });
    }

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function updateEvents(snap) {
        const list = document.getElementById('event-list');
        const events = snap.recentIrrigation || [];
        if (!events.length) return;
        list.innerHTML = events.map(e =>
            '<li><time>' + fmtTime(Date.parse(e.processedAtUtc)) + '</time>' +
            '<div><div class="what">' + escapeHtml(e.action) + ' ' + escapeHtml(e.deviceId) + '</div>' +
            '<div class="why">' + escapeHtml(e.outcomeDetail || e.reason) + '</div>' +
            '<div class="why">' + escapeHtml(e.trigger) + ' by ' + escapeHtml(e.requestedBy) + '</div></div>' +
            '<span class="pill is-' + escapeHtml(String(e.outcome).toLowerCase()) + '">' + escapeHtml(e.outcome) + '</span></li>').join('');
    }

    function updateWeather(snap) {
        const w = snap.environment;
        if (!w) return;
        document.querySelectorAll('#weather-grid [data-w]').forEach(el => {
            const key = el.dataset.w;
            const digits = key === 'et0Mm' ? 2 : (key === 'humidityPct' || key === 'windSpeedKmh') ? 0 : 1;
            el.textContent = fixed(w[key], digits) + el.dataset.u;
        });
        const age = document.getElementById('weather-age');
        if (age) age.textContent = ago(w.readingUtc);
        const alert = document.getElementById('weather-alert');
        if (alert) {
            alert.hidden = !w.alertLevel || w.alertLevel === 'None';
            alert.textContent = w.notes || '';
            alert.classList.toggle('is-warning', w.alertLevel === 'Warning');
        }
    }

    function updateController(c) {
        if (!c) return;
        const pill = document.getElementById('controller-status');
        if (pill) { pill.className = 'pill ' + c.statusClass; pill.textContent = c.status; }
        const detail = document.getElementById('controller-detail');
        if (detail) {
            detail.textContent = c.status === 'Irrigating' && c.irrigationEnds
                ? 'Irrigating until ' + c.irrigationEnds + '. Last command ' + c.lastCommandId + '.'
                : c.lastCommandId ? 'Last command ' + c.lastCommandAction + ' at ' + c.lastCommandAt + ' (' + c.statusUpdatedBy + ').' : 'No commands yet.';
        }
    }

    function updateSource(payload) {
        const line = document.getElementById('source-line');
        line.classList.toggle('is-fallback', !payload.viaFunction);
        line.classList.remove('is-refreshing'); void line.offsetWidth; line.classList.add('is-refreshing');
        document.getElementById('source-label').textContent = payload.source;
        document.getElementById('source-ms').textContent = payload.elapsedMs + ' ms';
        const snap = payload.snapshot;
        document.getElementById('source-cache').textContent = snap.cached
            ? 'cached ' + Math.round(snap.cacheAgeSeconds) + ' s'
            : 'query ' + Math.round(snap.queryMs) + ' ms, ' + snap.rowsRead + ' rows';
        document.getElementById('source-served').textContent = payload.functionServedBy ? 'served by ' + payload.functionServedBy : '';
    }

    let refreshing = false;
    async function refreshTelemetry() {
        if (refreshing || document.hidden) return;
        refreshing = true;
        try {
            const res = await fetch(config.urls.telemetry + '?hours=' + config.hours, { headers: { Accept: 'application/json' } });
            if (!res.ok) return;
            const payload = await res.json();
            snapshot = payload.snapshot;
            updateSummary(snapshot);
            updateSensors(snapshot);
            updateEvents(snapshot);
            updateWeather(snapshot);
            updateController(payload.controller);
            updateSource(payload);
            drawMoisture(snapshot);
        } catch (e) {
            // Network blip: the next refresh tries again.
        } finally {
            refreshing = false;
        }
    }

    // ── Irrigation tracker ──────────────────────────────────────────
    const form = document.getElementById('irrigate-form');
    const tracker = document.getElementById('tracker');

    function addStep(text, state) {
        const li = document.createElement('li');
        li.className = 'is-' + state;
        li.innerHTML = '<i class="bi" aria-hidden="true"></i><div><span class="t"></span><small></small></div>';
        li.querySelector('.t').textContent = text;
        setStep(li, state);
        tracker.appendChild(li);
        return li;
    }
    function setStep(li, state, note) {
        li.className = 'is-' + state;
        const icon = li.querySelector('.bi');
        icon.className = 'bi ' + (state === 'done' ? 'bi-check-circle-fill' : state === 'error' ? 'bi-x-circle-fill' : 'bi-arrow-repeat');
        if (note !== undefined) li.querySelector('small').textContent = note;
    }

    async function runIrrigation(action) {
        const buttons = form.querySelectorAll('button');
        buttons.forEach(b => { b.disabled = true; });
        tracker.hidden = false;
        tracker.innerHTML = '';
        const started = performance.now();
        const elapsed = () => ((performance.now() - started) / 1000).toFixed(1) + ' s';

        const step1 = addStep('1. Calling the TriggerIrrigation function (HTTP POST)', 'active');
        const body = new FormData(form);
        body.set('Action', action);
        let data;
        try {
            const res = await fetch(config.urls.trigger, { method: 'POST', body, headers: { Accept: 'application/json' } });
            data = await res.json().catch(() => ({}));
            if (!res.ok || !data.ok) throw new Error(data.error || ('HTTP ' + res.status));
        } catch (err) {
            setStep(step1, 'error', err.message);
            buttons.forEach(b => { b.disabled = false; });
            return;
        }
        setStep(step1, 'done', 'Accepted (202) in ' + data.elapsedMs + ' ms' + (data.servedBy ? ' by ' + data.servedBy : ''));
        addStep('2. ' + data.commandId + ' placed on the ' + data.queue + ' queue', 'done');
        const step3 = addStep('3. Waiting for ProcessIrrigationCommand (queue trigger)', 'active');

        const query = '?fieldId=' + encodeURIComponent(config.fieldId) + '&commandId=' + encodeURIComponent(data.commandId) +
            '&deviceId=' + encodeURIComponent(config.controllerId);
        const deadline = Date.now() + 90000;
        while (Date.now() < deadline) {
            await sleep(1500);
            try {
                const st = await (await fetch(config.urls.commandStatus + query, { headers: { Accept: 'application/json' } })).json();
                if (!st.processed) { setStep(step3, 'active', 'Waiting… ' + elapsed()); continue; }
                if (st.outcome === 'Applied') {
                    setStep(step3, 'done', 'Processed after ' + elapsed() + ' (' + st.processedBy + ')');
                    const d = st.device;
                    addStep('4. Device register (Azure SQL): ' + config.controllerId + ' is ' + d.status +
                        (d.status === 'Irrigating' && d.irrigationEnds ? ' until ' + d.irrigationEnds : ''), 'done');
                } else {
                    setStep(step3, 'error', st.outcome + ': ' + st.detail);
                }
                updateController(st.device);
                refreshTelemetry();
                buttons.forEach(b => { b.disabled = false; });
                return;
            } catch (e) {
                setStep(step3, 'active', 'Checking again… ' + elapsed());
            }
        }
        setStep(step3, 'error', 'Not processed within 90 s. Is the Function App running? The command waits safely on the queue.');
        buttons.forEach(b => { b.disabled = false; });
    }

    if (form && config.canIrrigate) {
        form.addEventListener('submit', ev => {
            ev.preventDefault();
            const action = (ev.submitter && ev.submitter.dataset.action) || 'START';
            runIrrigation(action);
        });
    }

    // ── Start ───────────────────────────────────────────────────────
    drawMoisture(snapshot);
    loadOutlook();
    setInterval(refreshTelemetry, 15000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshTelemetry(); });
})();
