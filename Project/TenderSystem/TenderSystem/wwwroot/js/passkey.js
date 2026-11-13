// Fingerprint / device-unlock sign-in with WebAuthn passkeys.
// Markup: Views/Shared/_PasskeyRegister.cshtml (data-passkey-register)  and the button on the login page (data-passkey-login).
//
// The browser asks the DEVICE to do the secret part: the device unlocks its private key with your fingerprint (or Face ID or
// the Windows Hello PIN) and signs a one-time challenge. The server only ever sees the signed answer and the public key.
// Passkeys work on https:// pages (or localhost) only, and belong to the address you registered them on.
//
// Events:  "passkey:registered" (on the register widget)   "passkey:loggedin" (on the login button, before the redirect)
(function () {
  function dec(s) {                                   // base64url text -> ArrayBuffer
    s = s.replace(/-/g, '+').replace(/_/g, '/');
    while (s.length % 4) s += '=';
    var bin = atob(s), bytes = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes.buffer;
  }
  function enc(buffer) {                              // ArrayBuffer -> base64url text
    var bytes = new Uint8Array(buffer), bin = '';
    for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  async function postJson(url, body, token) {
    var res = await fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
      body: JSON.stringify(body || {})
    });
    if (!res.ok) throw new Error('Server answered ' + res.status);
    return res.json();
  }

  function antiforgery(root) {
    var input = (root && root.querySelector('input[name="__RequestVerificationToken"]')) || document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function supported() {
    return !!(window.PublicKeyCredential && navigator.credentials && window.isSecureContext);
  }

  function explain(err) {
    var n = err && err.name;
    if (n === 'NotAllowedError') return 'It was cancelled or timed out, or this device has no passkey for this site. Try again.';
    if (n === 'InvalidStateError') return 'This device already has a passkey for your account.';
    if (n === 'SecurityError') return 'The browser refused: open the site with the same https:// address you registered with.';
    if (n === 'NotSupportedError') return 'This device or browser cannot create this kind of passkey.';
    return 'Something went wrong: ' + (err && err.message ? err.message : err);
  }

  // ───────────────────────── register ─────────────────────────
  function initRegister(root) {
    var status = root.querySelector('.passkey-status');
    var nameInput = root.querySelector('.passkey-name');
    var buttons = root.querySelectorAll('button[data-attachment]');
    function say(text, kind) { status.textContent = text || ''; status.className = 'face-status passkey-status' + (kind ? ' ' + kind : ''); }

    if (!supported()) say('Passkeys need an https:// page (or localhost) and a modern browser.', 'error');

    async function run(attachment) {
      if (!supported()) { say('Passkeys need an https:// page (or localhost) and a modern browser.', 'error'); return; }
      buttons.forEach(function (b) { b.disabled = true; });
      say('Follow the prompt on your device...');
      try {
        var token = antiforgery(root);
        var begin = await postJson(root.dataset.optionsUrl, { origin: location.origin, attachment: attachment || null }, token);

        var pk = begin.options;
        pk.challenge = dec(pk.challenge);
        pk.user.id = dec(pk.user.id);
        (pk.excludeCredentials || []).forEach(function (c) { c.id = dec(c.id); });

        var cred = await navigator.credentials.create({ publicKey: pk });
        var result = await postJson(root.dataset.registerUrl, {
          optionsId: begin.optionsId,
          deviceName: nameInput ? nameInput.value : '',
          credential: {
            id: cred.id,
            rawId: enc(cred.rawId),
            type: cred.type,
            clientExtensionResults: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {},
            response: {
              attestationObject: enc(cred.response.attestationObject),
              clientDataJSON: enc(cred.response.clientDataJSON),
              transports: cred.response.getTransports ? cred.response.getTransports() : []
            }
          }
        }, token);

        if (result.ok) {
          say(result.message, 'ok');
          root.dispatchEvent(new CustomEvent('passkey:registered', { bubbles: true, detail: result }));
          if (root.dataset.reload === 'true') setTimeout(function () { location.reload(); }, 900);
        } else {
          say(result.message || 'It did not work. Please try again.', 'error');
        }
      } catch (err) {
        say(explain(err), 'error');
      } finally {
        buttons.forEach(function (b) { b.disabled = false; });
      }
    }

    buttons.forEach(function (b) { b.addEventListener('click', function () { run(b.dataset.attachment); }); });
  }

  // ───────────────────────── remove a device ─────────────────────────
  function initRemove(button) {
    button.addEventListener('click', async function () {
      if (!confirm('Remove this device? You will not be able to sign in with it any more.')) return;
      try {
        var res = await postJson(button.dataset.url, { id: parseInt(button.dataset.passkeyRemove, 10) }, antiforgery(null));
        if (res.ok) location.reload();
      } catch (e) { alert('Could not remove it: ' + e.message); }
    });
  }

  // ───────────────────────── sign in (no email needed) ─────────────────────────
  function initLogin(button) {
    var status = document.querySelector(button.dataset.status);
    function say(text, kind) { if (status) { status.textContent = text || ''; status.className = 'face-status' + (kind ? ' ' + kind : ''); } }

    button.addEventListener('click', async function () {
      if (!supported()) { say('Fingerprint sign-in needs an https:// page (or localhost) and a modern browser.', 'error'); return; }
      button.disabled = true;
      say('Follow the prompt on your device...');
      try {
        var token = antiforgery(null);
        var begin = await postJson(button.dataset.optionsUrl, { origin: location.origin }, token);

        var pk = begin.options;
        pk.challenge = dec(pk.challenge);
        (pk.allowCredentials || []).forEach(function (c) { c.id = dec(c.id); });

        var cred = await navigator.credentials.get({ publicKey: pk });
        var result = await postJson(button.dataset.loginUrl, {
          optionsId: begin.optionsId,
          credential: {
            id: cred.id,
            rawId: enc(cred.rawId),
            type: cred.type,
            clientExtensionResults: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {},
            response: {
              authenticatorData: enc(cred.response.authenticatorData),
              clientDataJSON: enc(cred.response.clientDataJSON),
              signature: enc(cred.response.signature),
              userHandle: cred.response.userHandle ? enc(cred.response.userHandle) : null
            }
          }
        }, token);

        if (result.ok) {
          say('Welcome back!', 'ok');
          button.dispatchEvent(new CustomEvent('passkey:loggedin', { bubbles: true }));
          window.location.href = result.redirectUrl || '/';
        } else {
          say(result.message || 'It did not work. Please try again.', 'error');
        }
      } catch (err) {
        say(explain(err), 'error');
      } finally {
        button.disabled = false;
      }
    });
  }

  document.querySelectorAll('[data-passkey-register]').forEach(initRegister);
  document.querySelectorAll('[data-passkey-remove]').forEach(initRemove);
  document.querySelectorAll('[data-passkey-login]').forEach(initLogin);
})();
