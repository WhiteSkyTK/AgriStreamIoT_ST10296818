// Site-wide behaviour: confirmation dialog for destructive forms, auto-submitting filters.
(function () {
    'use strict';

    // <form data-confirm="Delete this device?"> shows the shared modal before submitting.
    const modalEl = document.getElementById('confirmModal');
    let pendingForm = null;
    if (modalEl && window.bootstrap) {
        const modal = new bootstrap.Modal(modalEl);
        document.addEventListener('submit', function (ev) {
            const form = ev.target;
            if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed === 'yes') return;
            ev.preventDefault();
            pendingForm = form;
            document.getElementById('confirmMessage').textContent = form.dataset.confirm;
            document.getElementById('confirmButton').textContent = form.dataset.confirmLabel || 'Confirm';
            modal.show();
        }, true);
        document.getElementById('confirmButton').addEventListener('click', function () {
            if (!pendingForm) return;
            pendingForm.dataset.confirmed = 'yes';
            modal.hide();
            pendingForm.requestSubmit ? pendingForm.requestSubmit() : pendingForm.submit();
        });
    }

    // <select data-autosubmit> submits its form when changed (filters).
    document.querySelectorAll('select[data-autosubmit]').forEach(function (select) {
        select.addEventListener('change', function () { select.form && select.form.submit(); });
    });

    // Buttons that start long server work show progress text and cannot be double-clicked.
    document.querySelectorAll('form[data-busy]').forEach(function (form) {
        form.addEventListener('submit', function () {
            const button = form.querySelector('button[type="submit"]');
            if (button && !form.dataset.confirm) {
                button.disabled = true;
                button.textContent = form.dataset.busy;
            }
        });
    });
})();
