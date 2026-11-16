document.querySelectorAll('[data-voice-search-button]').forEach((button, index) => {
    const scope = button.closest('[data-voice-search-scope]');
    const isLiveFilter = scope?.dataset.voiceSearchScope === 'filter';
    const form = button?.closest('form');
    const search = (scope || form)?.querySelector('[data-voice-search-input]');
    const status = (scope || form)?.querySelector('[data-voice-search-status]') ||
        (form?.nextElementSibling?.matches('[data-voice-search-status]') ? form.nextElementSibling : null);
    const token = document.querySelector('#voiceSearchToken input[name="__RequestVerificationToken"]');
    if (!status || !search || (!isLiveFilter && !form)) return;
    if (scope && !isLiveFilter && (form.method !== 'get' || search.form !== form)) return;
    const hasWhisper = Boolean(button.dataset.url && token);

    const sampleRate = 16000;
    const maxSeconds = 30;
    let recorder = null;
    let microphone = null;
    let timer = null;
    let busy = false;
    let submitted = false;
    let recognition = null;
    let recognitionReady = false;
    let useWhisperNext = false;
    let originalSearch = '';
    let finalSpeech = '';
    let speechError = '';

    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

    if (index === 0) {
        const privacyStyle = document.createElement('style');
        privacyStyle.textContent = `
        .voice-privacy { margin-top: .65rem; padding: 1rem; border: 1px solid var(--warm-beige); border-radius: var(--radius); background: var(--cream-white); color: var(--deep-brown); box-shadow: 0 8px 24px color-mix(in srgb, var(--deep-brown) 12%, transparent); }
        .voice-privacy[hidden] { display: none; }
        .voice-privacy-title { display: flex; align-items: center; gap: .5rem; font-weight: var(--weight-bold); }
        .voice-privacy-title i { color: var(--burgundy); }
        .voice-privacy p { margin: .4rem 0 .85rem; font-size: var(--text-control); }
        .voice-privacy-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
        .voice-privacy :focus-visible { outline: 2px solid var(--burgundy); outline-offset: 2px; }
        @media (prefers-reduced-motion: no-preference) { .voice-privacy:not([hidden]) { animation: voice-privacy-enter .2s ease-out both; } @keyframes voice-privacy-enter { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: translateY(0); } } }
        `;
        document.head.appendChild(privacyStyle);
    }

    const privacyTitleId = 'voicePrivacyTitle-' + index;
    const privacyMessageId = 'voicePrivacyMessage-' + index;
    const privacy = document.createElement('div');
    privacy.className = 'voice-privacy';
    privacy.hidden = true;
    privacy.setAttribute('role', 'group');
    privacy.setAttribute('aria-labelledby', privacyTitleId);
    privacy.setAttribute('aria-describedby', privacyMessageId);
    privacy.innerHTML = '<div class="voice-privacy-title" id="' + privacyTitleId + '"><i class="bi bi-shield-lock" aria-hidden="true"></i>Browser speech recognition</div>' +
        '<p id="' + privacyMessageId + '">Your browser may send microphone audio to an external provider for speech recognition. Choose Accept to use browser recognition.</p>' +
        '<div class="voice-privacy-actions"><button type="button" class="btn btn-brand" data-voice-accept>Accept</button>' +
        '<button type="button" class="btn btn-outline-secondary" data-voice-decline>Decline</button></div>';
    status.insertAdjacentElement('afterend', privacy);
    if (scope) {
        privacy.querySelector('.voice-privacy-actions').insertAdjacentHTML('beforebegin',
            '<label class="form-label">Recognition language' +
            '<select class="form-select form-select-sm mb-2" data-voice-search-language>' +
            '<option value="en-US">English</option><option value="ar-JO">العربية</option>' +
            '</select></label>');
    }
    const language = privacy.querySelector('[data-voice-search-language]');

    function closePrivacy() {
        privacy.hidden = true;
        button.setAttribute('aria-expanded', 'false');
        button.focus();
    }
    privacy.querySelector('[data-voice-accept]').addEventListener('click', () => {
        closePrivacy();
        recognitionReady = true;
        startWebSpeech();
    });
    privacy.querySelector('[data-voice-decline]').addEventListener('click', () => {
        closePrivacy();
        show('Browser recognition was declined. No recording started.');
    });
    privacy.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); closePrivacy(); }
    });

    function show(message) { status.textContent = message; }
    function cleanSpeech(words) {
        return words.replace(/[!?،؛؟…“”"()]+/gu, ' ')
            .replace(/[.,;:](?=\s|$)/gu, ' ')
            .replace(/\s+/g, ' ').trim();
    }
    function submitSearch(words) {
        const query = cleanSpeech(words);
        if (submitted || !query) return;
        submitted = true;
        search.value = search.maxLength > 0 ? query.slice(0, search.maxLength) : query;
        if (isLiveFilter) {
            search.dispatchEvent(new Event('input', { bubbles: true }));
            show('Search updated.');
            return;
        }
        show('Speech recognized. Searching…');
        form.requestSubmit();
    }
    function idleButton() {
        busy = false;
        button.disabled = false;
        button.setAttribute('aria-pressed', 'false');
        button.setAttribute('aria-label', useWhisperNext && hasWhisper ? 'Start local Whisper recording' : 'Start voice search');
    }
    function reset() {
        if (timer) clearTimeout(timer);
        timer = null;
        microphone?.getTracks().forEach(track => track.stop());
        microphone = null;
        recorder = null;
        idleButton();
    }

    function writeWav(samples) {
        const bytes = new ArrayBuffer(44 + samples.length * 2);
        const view = new DataView(bytes);
        const label = (offset, value) => {
            for (let i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i));
        };
        label(0, 'RIFF');
        view.setUint32(4, bytes.byteLength - 8, true);
        label(8, 'WAVE');
        label(12, 'fmt ');
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        label(36, 'data');
        view.setUint32(40, samples.length * 2, true);
        for (let i = 0; i < samples.length; i++) {
            const value = Math.max(-1, Math.min(1, samples[i]));
            view.setInt16(44 + i * 2, value < 0 ? value * 32768 : value * 32767, true);
        }
        return new Blob([bytes], { type: 'audio/wav' });
    }

    async function convertToWav(recording) {
        const context = new AudioContext();
        try {
            const decoded = await context.decodeAudioData(await recording.arrayBuffer());
            if (decoded.duration <= 0 || decoded.duration > maxSeconds)
                throw new Error('Record up to 30 seconds of speech and try again.');
            const length = Math.ceil(decoded.duration * sampleRate);
            const offline = new OfflineAudioContext(1, length, sampleRate);
            const source = offline.createBufferSource();
            source.buffer = decoded;
            source.connect(offline.destination);
            source.start();
            const rendered = await offline.startRendering();
            return writeWav(rendered.getChannelData(0));
        } finally {
            await context.close();
        }
    }

    async function transcribe(recording) {
        if (!hasWhisper) return;
        show('Processing speech…');
        const wav = await convertToWav(recording);
        const data = new FormData();
        data.append('audio', wav, 'voice-search.wav');
        const response = await fetch(button.dataset.url, {
            method: 'POST',
            headers: { RequestVerificationToken: token.value },
            body: data,
            credentials: 'same-origin'
        });
        const result = await response.json();
        if (!response.ok) throw new Error(result.error || 'Voice search could not process the recording.');
        const words = typeof result.text === 'string' ? result.text.trim() : '';
        if (!words) throw new Error('No speech was recognized. Please try again or type your search.');
        submitSearch(words);
    }

    function stopRecording() {
        if (!recorder || recorder.state !== 'recording') return;
        button.disabled = true;
        show('Preparing recording…');
        recorder.stop();
    }

    function startWebSpeech() {
        originalSearch = search.value;
        finalSpeech = '';
        speechError = '';
        recognition = new SpeechRecognition();
        recognition.interimResults = true;
        recognition.continuous = false;
        if (language) recognition.lang = language.value;
        recognition.onstart = () => show('Listening… Click the microphone again to stop.');
        recognition.onresult = event => {
            if (submitted) return;
            const finalParts = [];
            const interimParts = [];
            for (let i = 0; i < event.results.length; i++) {
                const result = event.results[i];
                if (result.isFinal) finalParts.push(result[0].transcript.trim());
                else interimParts.push(result[0].transcript.trim());
            }
            finalSpeech = finalParts.filter(Boolean).join(' ');
            const liveWords = [...finalParts, ...interimParts].filter(Boolean).join(' ');
            search.value = search.maxLength > 0 ? liveWords.slice(0, search.maxLength) : liveWords;
            if (finalSpeech) {
                button.disabled = true;
                try { recognition.stop(); } catch { /* The browser may have ended this short session. */ }
                submitSearch(finalSpeech);
                return;
            }
            show(interimParts.length ? 'Listening… Live words are shown in Search.' : 'Listening… Click the microphone again to stop.');
        };
        recognition.onerror = event => {
            if (submitted) return;
            speechError = event.error || 'unknown error';
            useWhisperNext = true;
            search.value = originalSearch;
            show(hasWhisper ? `Browser recognition failed (${speechError}). Click the microphone to make a new recording with Whisper.` : `Browser recognition failed (${speechError}). Please try again or type your search.`);
        };
        recognition.onend = () => {
            recognition = null;
            if (submitted) {
                if (isLiveFilter) {
                    submitted = false;
                    idleButton();
                }
                return;
            }
            idleButton();
            if (speechError) return;
            search.value = originalSearch;
            useWhisperNext = true;
            show(hasWhisper ? 'No final speech was recognized. Click the microphone to make a new recording with Whisper.' : 'No final speech was recognized. Please try again or type your search.');
        };
        busy = true;
        button.setAttribute('aria-pressed', 'true');
        button.setAttribute('aria-label', 'Stop voice recognition');
        show('Starting browser recognition…');
        try { recognition.start(); }
        catch (error) {
            recognition = null;
            useWhisperNext = true;
            idleButton();
            show(hasWhisper ? 'Browser recognition could not start. Click the microphone to make a new recording with Whisper.' : 'Browser recognition could not start. Please try again or type your search.');
        }
    }

    async function startWhisper() {
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.MediaRecorder ||
            !window.AudioContext || !window.OfflineAudioContext) {
            show('Voice search requires a supported browser on HTTPS or localhost.');
            return;
        }

        busy = true;
        button.disabled = true;
        show('Requesting microphone access…');
        try {
            microphone = await navigator.mediaDevices.getUserMedia({ audio: true });
            const chunks = [];
            recorder = new MediaRecorder(microphone);
            recorder.addEventListener('dataavailable', event => {
                if (event.data.size) chunks.push(event.data);
            });
            recorder.addEventListener('error', () => {
                show('Recording failed. Please try again.');
                reset();
            });
            recorder.addEventListener('stop', async () => {
                if (timer) clearTimeout(timer);
                microphone?.getTracks().forEach(track => track.stop());
                try {
                    await transcribe(new Blob(chunks, { type: recorder.mimeType }));
                } catch (error) {
                    show(error.message || 'Voice search failed. Please try again.');
                } finally {
                    reset();
                }
            }, { once: true });
            recorder.start();
            button.disabled = false;
            button.setAttribute('aria-pressed', 'true');
            button.setAttribute('aria-label', 'Stop voice recording');
            show('Recording… Click the microphone again to stop.');
            timer = setTimeout(stopRecording, 29000);
        } catch (error) {
            show(error.name === 'NotAllowedError' ? 'Microphone permission was denied.' :
                'Microphone recording is unavailable. Please try again or type your search.');
            reset();
        }
    }

    button.addEventListener('click', () => {
        if (recognition) {
            if (busy) {
                button.disabled = true;
                show('Finishing recognition…');
                recognition.stop();
            }
            return;
        }
        if (recorder?.state === 'recording') {
            stopRecording();
            return;
        }
        if (busy || submitted) return;
        if (scope && !window.isSecureContext) {
            show('Voice search requires HTTPS or localhost.');
            return;
        }
        if (!privacy.hidden) return;
        if (SpeechRecognition && (!useWhisperNext || !hasWhisper)) {
            if (!recognitionReady) {
                privacy.hidden = false;
                button.setAttribute('aria-expanded', 'true');
                privacy.querySelector('[data-voice-accept]').focus();
                return;
            }
            startWebSpeech();
        } else if (hasWhisper) {
            startWhisper();
        } else {
            show('Browser speech recognition is unavailable. Please type your search.');
        }
    });
});
