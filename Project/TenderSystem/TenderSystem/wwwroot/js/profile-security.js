(function () {
    var dialog = document.getElementById('remove-device-dialog');
    var name = document.getElementById('remove-device-name');
    var error = document.getElementById('remove-device-error');
    var cancel = document.getElementById('cancel-remove-device');
    var confirm = document.getElementById('confirm-remove-device');
    if (!dialog || !name || !error || !cancel || !confirm) return;

    var selectedButton = null;

    function showError(message) {
        error.textContent = message;
        error.hidden = false;
    }

    document.querySelectorAll('[data-profile-passkey-remove]').forEach(function (button) {
        button.addEventListener('click', function () {
            selectedButton = button;
            name.textContent = button.dataset.deviceName || 'this device';
            error.textContent = '';
            error.hidden = true;
            dialog.showModal();
        });
    });

    cancel.addEventListener('click', function () {
        dialog.close();
        selectedButton = null;
    });

    confirm.addEventListener('click', async function () {
        if (!selectedButton) return;

        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (!tokenInput) {
            showError('Security token missing. Reload the page and try again.');
            return;
        }

        confirm.disabled = true;
        confirm.textContent = 'Removing…';
        try {
            var response = await fetch(selectedButton.dataset.url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': tokenInput.value
                },
                body: JSON.stringify({ id: Number(selectedButton.dataset.profilePasskeyRemove) })
            });
            var result = await response.json();
            if (response.ok && result.ok) {
                window.location.reload();
                return;
            }
            showError(result.message || 'This device could not be removed.');
        } catch (requestError) {
            showError('Could not reach the server. Check your connection and try again.');
        } finally {
            confirm.disabled = false;
            confirm.textContent = 'Remove device';
        }
    });
})();
