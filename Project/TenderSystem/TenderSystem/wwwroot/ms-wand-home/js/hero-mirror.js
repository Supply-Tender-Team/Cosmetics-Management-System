(() => {
    const faceStage = document.getElementById('heroFaceVideo')?.closest('.face-stage');
    if (faceStage) {
        const stage = faceStage.closest('.stage');
        const observer = new MutationObserver(() => {
            stage.classList.toggle('is-camera-on', !faceStage.hidden);
        });
        observer.observe(faceStage, { attributeFilter: ['hidden'] });
        return;
    }

    const toggle = document.getElementById('heroMirrorToggle');
    const video = document.getElementById('heroMirrorVideo');
    const status = document.getElementById('heroMirrorStatus');
    const stage = video.closest('.stage');
    let stream;
    let request = 0;

    function stop() {
        request += 1;
        stream?.getTracks().forEach(track => track.stop());
        stream = undefined;
        video.srcObject = null;
        video.hidden = true;
        stage.classList.remove('is-camera-on');
        toggle.disabled = false;
        toggle.textContent = 'See your beauty';
        toggle.setAttribute('aria-pressed', 'false');
        status.textContent = '';
    }

    toggle.hidden = false;
    toggle.addEventListener('click', async () => {
        if (stream) {
            stop();
            return;
        }
        if (!navigator.mediaDevices?.getUserMedia) {
            status.textContent = 'The mirror needs a camera and a secure connection.';
            return;
        }

        const attempt = ++request;
        toggle.disabled = true;
        status.textContent = 'Waiting for camera permission…';
        try {
            const camera = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false });
            if (attempt !== request || document.hidden) {
                camera.getTracks().forEach(track => track.stop());
                return;
            }

            stream = camera;
            camera.getVideoTracks().forEach(track => track.addEventListener('ended', stop, { once: true }));
            video.srcObject = camera;
            await video.play();
            if (attempt !== request) return;
            video.hidden = false;
            stage.classList.add('is-camera-on');
            toggle.disabled = false;
            toggle.textContent = 'Turn off camera';
            toggle.setAttribute('aria-pressed', 'true');
            status.textContent = 'Live preview only';
        } catch {
            if (attempt !== request) return;
            stop();
            status.textContent = 'Camera unavailable. Check your browser permissions and try again.';
        }
    });

    window.addEventListener('pagehide', stop);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) stop();
    });
})();
