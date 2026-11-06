// Multi-file upload with progress (XMLHttpRequest gives upload progress events).
(function () {
    'use strict';
    const form = document.getElementById('upload-form');
    if (!form) return;
    const input = document.getElementById('files');
    const zone = document.getElementById('dropzone');
    const list = document.getElementById('file-list');
    const progress = document.getElementById('progress');
    const bar = progress.querySelector('div');
    const result = document.getElementById('upload-result');
    const button = document.getElementById('upload-button');

    const size = b => b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB';

    function showFiles() {
        const files = [...input.files];
        list.hidden = files.length === 0;
        list.innerHTML = '';
        files.forEach(f => {
            const li = document.createElement('li');
            const name = document.createElement('span');
            name.textContent = f.name;
            const s = document.createElement('span');
            s.className = 'muted';
            s.textContent = size(f.size);
            li.append(name, s);
            list.appendChild(li);
        });
        if (files.length) {
            const total = files.reduce((t, f) => t + f.size, 0);
            const summary = document.createElement('li');
            summary.innerHTML = '<strong></strong><strong></strong>';
            summary.children[0].textContent = files.length + ' file(s)';
            summary.children[1].textContent = size(total);
            list.appendChild(summary);
        }
    }

    input.addEventListener('change', showFiles);
    ['dragenter', 'dragover'].forEach(e => zone.addEventListener(e, ev => { ev.preventDefault(); zone.classList.add('is-over'); }));
    ['dragleave', 'drop'].forEach(e => zone.addEventListener(e, ev => { ev.preventDefault(); zone.classList.remove('is-over'); }));
    zone.addEventListener('drop', ev => { input.files = ev.dataTransfer.files; showFiles(); });

    function notice(kind, text) {
        result.innerHTML = '';
        const div = document.createElement('div');
        div.className = 'notice is-' + kind;
        div.innerHTML = '<i class="bi ' + (kind === 'success' ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill') + '" aria-hidden="true"></i><div></div>';
        div.lastChild.textContent = text;
        result.appendChild(div);
        return div;
    }

    form.addEventListener('submit', ev => {
        ev.preventDefault();
        if (!form.fieldId.value) { notice('error', 'Choose the field the files belong to.'); return; }
        if (!input.files.length) { notice('error', 'Choose at least one file.'); return; }

        const xhr = new XMLHttpRequest();
        xhr.open('POST', form.action);
        xhr.setRequestHeader('Accept', 'application/json');
        progress.hidden = false;
        bar.style.width = '0%';
        button.disabled = true;
        const started = performance.now();

        xhr.upload.addEventListener('progress', e => {
            if (e.lengthComputable) bar.style.width = (e.loaded / e.total * 100).toFixed(1) + '%';
        });
        xhr.addEventListener('load', () => {
            button.disabled = false;
            let data = {};
            try { data = JSON.parse(xhr.responseText); } catch (e) { /* not JSON */ }
            if (xhr.status >= 200 && xhr.status < 300 && data.ok) {
                const seconds = ((performance.now() - started) / 1000).toFixed(1);
                const box = notice('success', data.message + ' (' + seconds + ' s)');
                const link = document.createElement('a');
                link.href = '/Media?category=' + (form.category.value === 'DroneImagery' ? 'drone' : 'media');
                link.textContent = ' Open the gallery to watch the audit status.';
                box.lastChild.appendChild(link);
                input.value = '';
                showFiles();
            } else {
                notice('error', data.error || data.message || ('Upload failed (HTTP ' + xhr.status + '). Files over 200 MB are rejected.'));
            }
        });
        xhr.addEventListener('error', () => {
            button.disabled = false;
            notice('error', 'The upload was interrupted. Check your connection and try again.');
        });
        xhr.send(new FormData(form));
    });
})();
