// Camera widget for face sign-in and face enrollment.
// Markup: Views/Shared/_FaceCapture.cshtml  (an element with data-face-capture).
//
// What it does, in plain steps:
//   1. opens the camera (needs HTTPS or localhost),
//   2. asks the server for a one-time challenge (the moves to do),
//        sign-in:  ONE step, "look straight" (a small anti-photo model on the server checks the face is live);
//                  if the server is not sure, it automatically asks once more, this time with a head turn to one random side,
//        setup and approvals: look straight, then turn to both sides (careful, done once or protecting an action),
//   3. shows each instruction and captures a few small JPEG frames after the person had time to move,
//   4. sends the frames to the server, which does ALL the checking (liveness + recognition). The browser decides nothing.
//
// Events on the widget element:  "face:enrolled" (enrollment ok)   "face:loggedin" (login ok, before the redirect)
(function () {
  var PROMPTS = {
    center: 'Look straight at the camera',
    left: 'Now slowly turn your head to the LEFT',
    right: 'Now slowly turn your head to the RIGHT'
  };
  // Waits and frames per kind of challenge. Frames are 4 or 5 per step so the server always has enough straight and turned ones.
  var TIMING = {
    careful: { centerReadyMs: 1400, turnReadyMs: 1400, frames: 4, gapMs: 350, width: 480 },   // setup and approvals
    quick:   { centerReadyMs: 500,  turnReadyMs: 1100, frames: 4, gapMs: 280, width: 480 },   // sign-in with a head turn (the fallback)
    passive: { centerReadyMs: 400,  turnReadyMs: 0,    frames: 6, gapMs: 200, width: 640 }    // sign-in, one step (about 2 seconds): bigger frames keep the fine detail the anti-photo model needs
  };

  function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

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

  function init(root) {
    var stage = root.querySelector('.face-stage');
    var video = root.querySelector('video');
    var prompt = root.querySelector('.face-prompt');
    var status = root.querySelector('.face-status');
    var button = root.querySelector('.face-start');
    var mode = root.dataset.mode;
    var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');
    var canvas = document.createElement('canvas');
    var stream = null;

    function say(text, kind) { status.textContent = text || ''; status.className = 'face-status' + (kind ? ' ' + kind : ''); }

    function stopCamera() {
      if (stream) { stream.getTracks().forEach(function (t) { t.stop(); }); stream = null; }
      video.srcObject = null;
      stage.hidden = true;
      stage.classList.remove('is-capturing');
    }

    function grabFrame(width) {
      var w = width, h = Math.round(width * video.videoHeight / video.videoWidth) || Math.round(width * 0.75);
      canvas.width = w; canvas.height = h;
      canvas.getContext('2d').drawImage(video, 0, 0, w, h);
      return canvas.toDataURL('image/jpeg', 0.88).split(',')[1];   // plain base64, no "data:" prefix
    }

    // One round: ask for a challenge, show the moves, capture the frames, send them. Returns the server's answer.
    async function attempt(email, token, strict) {
      // approve mode (assistant command center): the challenge is tied to ONE action instead of an email
      var who = mode === 'approve' ? { actionId: root.dataset.actionId } : { email: email, strict: !!strict };
      var challenge = await postJson(root.dataset.challengeUrl, who, token);
      if (challenge.ok === false) return { ok: false, message: challenge.message || 'This action is no longer waiting.' };

      var passive = mode === 'login' && challenge.steps.length === 1;
      var timing = passive ? TIMING.passive : (mode === 'login' ? TIMING.quick : TIMING.careful);

      stage.classList.add('is-capturing');
      var frames = [];
      for (var i = 0; i < challenge.steps.length; i++) {
        var step = challenge.steps[i];
        prompt.textContent = PROMPTS[step] || step;
        say(challenge.steps.length > 1 ? 'Step ' + (i + 1) + ' of ' + challenge.steps.length : 'Hold still for a moment');
        var ready = step === 'center' ? timing.centerReadyMs : timing.turnReadyMs;   // time to follow the instruction before the first frame
        if (ready) await sleep(ready);
        for (var k = 0; k < timing.frames; k++) {
          frames.push({ step: step, image: grabFrame(timing.width) });
          await sleep(timing.gapMs);
        }
      }

      prompt.textContent = 'Checking...';
      say('Checking, one moment...');
      var submit = { challengeId: challenge.challengeId, frames: frames };
      if (mode === 'approve') submit.actionId = root.dataset.actionId; else submit.email = email;
      return postJson(root.dataset.submitUrl, submit, token);
    }

    async function run() {
      var email = '';
      if (mode === 'login') {
        var input = document.querySelector(root.dataset.emailInput);
        email = input ? input.value.trim() : '';   // empty = "smart" sign-in: the server recognises the face among everybody
      }
      if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        say('The camera needs a secure page. Open the site with https:// (or on localhost).', 'error'); return;
      }

      button.disabled = true;
      say('Opening the camera...');
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
        video.srcObject = stream;
        stage.hidden = false;
        await video.play();
        await sleep(600);   // let the camera adjust its exposure

        var token = tokenInput ? tokenInput.value : '';
        var result = await attempt(email, token, false);

        // sign-in only: the one-step check was not sure it is a live face, so ask once more WITH a head turn (the camera stays on)
        if (mode === 'login' && result && result.needTurn) {
          say('One more move, please...');
          await sleep(500);
          result = await attempt(email, token, true);
        }

        if (mode === 'approve') {
          // the chat reads the server's answer (done, refused, or cancelled after too many tries)
          say(result.message || (result.ok ? 'Done.' : 'It did not work.'), result.ok ? 'ok' : 'error');
          stopCamera();
          root.dispatchEvent(new CustomEvent('face:approved', { bubbles: true, detail: result }));
        } else if (result.ok) {
          say(result.message || 'Done.', 'ok');
          stopCamera();
          if (mode === 'login') {
            root.dispatchEvent(new CustomEvent('face:loggedin', { bubbles: true }));
            window.location.href = result.redirectUrl || '/';
          } else {
            root.dispatchEvent(new CustomEvent('face:enrolled', { bubbles: true, detail: result }));
            button.textContent = 'Add another face (another device or lighting)';
          }
        } else {
          say(result.message || 'It did not work. Please try again.', 'error');
        }
      } catch (err) {
        var denied = err && (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError');
        var none = err && err.name === 'NotFoundError';
        say(denied ? 'Camera access was blocked. Allow the camera for this site and try again.'
          : none ? 'No camera was found on this device.'
          : 'Something went wrong: ' + (err && err.message ? err.message : err), 'error');
      } finally {
        stopCamera();
        button.disabled = false;
      }
    }

    button.addEventListener('click', run);
  }

  window.MsWandFace = { init: init };      // for widgets created after the page loaded (the assistant's action card)
  document.querySelectorAll('[data-face-capture]').forEach(init);
})();
