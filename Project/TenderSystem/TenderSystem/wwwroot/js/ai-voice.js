/* Voice typing for the assistant chat (AI-12). Used by ai-chat.js: window.MsWandVoice.attach({...}).
   Two ways to turn speech into text:
   - "browser": the browser's own speech recognition (Web Speech API). The browser may send the audio to its vendor's speech service
     (for example Google in Chrome), so the user confirms a short privacy notice once per session.
   - "whisper": the audio is recorded here, converted to 16 kHz mono WAV and sent to /AiChat/Transcribe, where the local Whisper model
     (VOICE_MODEL_PATH) turns it into text. Nothing leaves the company.
   The recognised text goes INTO THE INPUT BOX for the user to read and send. It is never sent by itself and never stored. */
(function () {
  'use strict';
  if (window.MsWandVoice) return;

  var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  var MAX_SECONDS = 30, SAMPLE_RATE = 16000;

  function store(key, value) {
    try { if (value === undefined) return sessionStorage.getItem(key); sessionStorage.setItem(key, value); } catch (e) { /* ignore */ }
    return null;
  }

  // mono 16-bit PCM WAV (the format VoiceTranscriptionService accepts)
  function writeWav(samples) {
    var buffer = new ArrayBuffer(44 + samples.length * 2);
    var view = new DataView(buffer);
    function text(offset, s) { for (var i = 0; i < s.length; i++) view.setUint8(offset + i, s.charCodeAt(i)); }
    text(0, 'RIFF'); view.setUint32(4, 36 + samples.length * 2, true); text(8, 'WAVE'); text(12, 'fmt ');
    view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
    view.setUint32(24, SAMPLE_RATE, true); view.setUint32(28, SAMPLE_RATE * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
    text(36, 'data'); view.setUint32(40, samples.length * 2, true);
    for (var i = 0; i < samples.length; i++) {
      var s = Math.max(-1, Math.min(1, samples[i]));
      view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
    }
    return new Blob([buffer], { type: 'audio/wav' });
  }

  // whatever the microphone recorded (webm / mp4) -> 16 kHz mono samples
  async function toSamples(blob) {
    var Ctx = window.AudioContext || window.webkitAudioContext;
    var ctx = new Ctx();
    try {
      var decoded = await ctx.decodeAudioData(await blob.arrayBuffer());
      var length = Math.min(Math.ceil(decoded.duration * SAMPLE_RATE), MAX_SECONDS * SAMPLE_RATE);
      var offline = new OfflineAudioContext(1, Math.max(length, 1), SAMPLE_RATE);
      var source = offline.createBufferSource();
      source.buffer = decoded; source.connect(offline.destination); source.start();
      return (await offline.startRendering()).getChannelData(0);
    } finally { if (ctx.close) ctx.close(); }
  }

  /**
   * opts: { button, langButton, input, mode: 'whisper' | 'browser', url, token(), autosize(), notify(text) }
   * Returns false (and hides the buttons) when this browser cannot do voice typing in that mode.
   */
  function attach(opts) {
    var button = opts.button, input = opts.input;
    if (!button || !input) return false;
    var canWhisper = opts.mode === 'whisper' && !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder && opts.url);
    var canBrowser = opts.mode !== 'whisper' && !!Recognition;
    if (!canWhisper && !canBrowser) { button.hidden = true; if (opts.langButton) opts.langButton.hidden = true; return false; }

    button.hidden = false;
    var lang = store('aiVoiceLang') || 'en';
    var lang_btn = opts.langButton;
    if (lang_btn) {
      lang_btn.hidden = canWhisper;                                  // the local model finds the language by itself
      lang_btn.textContent = lang.toUpperCase();
      lang_btn.addEventListener('click', function () {
        lang = lang === 'en' ? 'ar' : 'en';
        store('aiVoiceLang', lang);
        lang_btn.textContent = lang.toUpperCase();
        lang_btn.title = lang === 'ar' ? 'Speaking Arabic' : 'Speaking English';
      });
    }

    var live = false, recognition = null, recorder = null, stream = null, stopTimer = null, startedWith = '';

    function setLive(on) {
      live = on;
      button.classList.toggle('is-live', on);
      button.setAttribute('aria-pressed', on ? 'true' : 'false');
      button.title = on ? 'Stop' : 'Speak your question';
      button.querySelector('i').className = on ? 'bi bi-stop-fill' : 'bi bi-mic-fill';
    }
    function busy(on) { button.disabled = on; button.classList.toggle('is-busy', on); }
    function put(text) {
      input.value = (startedWith + text).trimStart();
      if (opts.autosize) opts.autosize();
      input.focus();
    }

    // ---------- browser speech recognition
    function startBrowser() {
      if (store('aiVoiceOk') !== '1') {
        var ok = window.confirm('Voice typing uses the speech service of your browser. Depending on the browser (for example Chrome), what you say may be sent to its vendor to be turned into text. Nothing is sent to this system until you press Send. Continue?');
        if (!ok) return;
        store('aiVoiceOk', '1');
      }
      recognition = new Recognition();
      recognition.lang = lang === 'ar' ? 'ar-JO' : 'en-US';
      recognition.interimResults = true;
      recognition.continuous = false;
      startedWith = input.value.trim() ? input.value.trim() + ' ' : '';
      recognition.onresult = function (event) {
        var said = '';
        for (var i = 0; i < event.results.length; i++) said += event.results[i][0].transcript;
        put(said);
      };
      recognition.onerror = function (event) {
        var reason = event.error === 'not-allowed' || event.error === 'service-not-allowed' ? 'The microphone is blocked. Allow it for this site and try again.'
          : event.error === 'no-speech' ? 'I did not hear anything. Please try again.'
          : event.error === 'audio-capture' ? 'No microphone was found.'
          : 'Voice typing did not work here. Please type your question.';
        if (opts.notify) opts.notify(reason);
        setLive(false);
      };
      recognition.onend = function () { setLive(false); };
      try { recognition.start(); setLive(true); } catch (e) { setLive(false); }
    }

    // ---------- local Whisper through the server
    async function startWhisper() {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      } catch (e) {
        if (opts.notify) opts.notify(e && e.name === 'NotFoundError' ? 'No microphone was found.' : 'The microphone is blocked. Allow it for this site and try again.');
        return;
      }
      var chunks = [];
      recorder = new MediaRecorder(stream);
      recorder.ondataavailable = function (e) { if (e.data && e.data.size) chunks.push(e.data); };
      recorder.onstop = async function () {
        if (stopTimer) { clearTimeout(stopTimer); stopTimer = null; }
        stream.getTracks().forEach(function (t) { t.stop(); }); stream = null;
        setLive(false); busy(true);
        try {
          var wav = writeWav(await toSamples(new Blob(chunks, { type: recorder.mimeType || 'audio/webm' })));
          var form = new FormData();
          form.append('audio', wav, 'voice.wav');
          var res = await fetch(opts.url, { method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': opts.token() }, body: form });
          var data = null; try { data = await res.json(); } catch (e) { /* not JSON */ }
          if (res.ok && data && data.text) { startedWith = input.value.trim() ? input.value.trim() + ' ' : ''; put(data.text); }
          else if (opts.notify) opts.notify((data && (data.error || data.message)) || 'Voice typing did not work. Please type your question.');
        } catch (e) {
          if (opts.notify) opts.notify('Voice typing did not work. Please type your question.');
        }
        busy(false);
      };
      recorder.start();
      setLive(true);
      stopTimer = setTimeout(function () { if (recorder && recorder.state === 'recording') recorder.stop(); }, MAX_SECONDS * 1000);
    }

    button.addEventListener('click', function () {
      if (live) {                                                   // second click = stop
        if (recognition) { try { recognition.stop(); } catch (e) { /* ignore */ } }
        if (recorder && recorder.state === 'recording') recorder.stop();
        return;
      }
      if (canWhisper) startWhisper(); else startBrowser();
    });
    return true;
  }

  window.MsWandVoice = { attach: attach };
})();
