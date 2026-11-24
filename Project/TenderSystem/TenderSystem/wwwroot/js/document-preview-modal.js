(() => {
    const triggers = document.querySelectorAll('a[data-document-preview]');
    if (!triggers.length || !window.bootstrap?.Modal) return;

    const scriptUrl = document.currentScript?.dataset.qrLibraryUrl || 'https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.min.js';
    let qrLibraryPromise;
    const loadQrLibrary = () => {
        if (typeof window.jsQR === 'function') return Promise.resolve();
        if (!qrLibraryPromise) {
            qrLibraryPromise = new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = scriptUrl;
                script.onload = resolve;
                script.onerror = () => reject(new Error('Could not load the QR reader from the CDN.'));
                document.head.appendChild(script);
            });
        }
        return qrLibraryPromise;
    };

    const modalRoot = document.createElement('div');
    modalRoot.className = 'modal fade';
    modalRoot.tabIndex = -1;
    modalRoot.setAttribute('aria-hidden', 'true');
    modalRoot.innerHTML = `
        <div class="modal-dialog modal-dialog-centered modal-xl modal-fullscreen-md-down">
            <div class="modal-content" style="background:#faf5f0;border:1px solid #d8c1ae;border-radius:1rem;overflow:hidden">
                <div class="modal-header" style="border-color:#d8c1ae">
                    <h2 class="modal-title fs-5" style="color:#4a302c"></h2>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body" style="min-height:35vh;display:flex;align-items:center;justify-content:center;background:#efe7e1;padding:1rem"></div>
                <div class="modal-footer" style="border-color:#d8c1ae">
                    <span class="small me-auto" data-qr-scan-status role="status" aria-live="polite"></span>
                    <button type="button" class="btn btn-outline-brand document-preview-scan" hidden><i class="bi bi-qr-code-scan me-2" aria-hidden="true"></i>Scan QR</button>
                    <a class="btn btn-brand document-preview-download"><i class="bi bi-download me-2" aria-hidden="true"></i>Download</a>
                    <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
                </div>
            </div>
        </div>`;
    document.body.appendChild(modalRoot);

    const modal = new bootstrap.Modal(modalRoot);
    const title = modalRoot.querySelector('.modal-title');
    const body = modalRoot.querySelector('.modal-body');
    const download = modalRoot.querySelector('.document-preview-download');
    const scanButton = modalRoot.querySelector('.document-preview-scan');
    const scanStatus = modalRoot.querySelector('[data-qr-scan-status]');
    let currentPortalUrl = null;

    modalRoot.addEventListener('hidden.bs.modal', () => {
        body.replaceChildren();
        scanStatus.textContent = '';
        currentPortalUrl = null;
    });

    scanButton.addEventListener('click', async () => {
        const image = body.querySelector('img');
        if (!image || !currentPortalUrl) return;

        const portal = window.open('about:blank', '_blank');
        if (!portal) {
            scanStatus.textContent = 'Allow pop-ups for this local site, then try scanning again.';
            return;
        }

        scanButton.disabled = true;
        scanStatus.textContent = 'Scanning the guarantee QR…';
        try {
            await loadQrLibrary();
            await image.decode();
            const bitmap = await createImageBitmap(image);
            let result;
            try {
                const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height));
                const width = Math.max(1, Math.round(bitmap.width * scale));
                const height = Math.max(1, Math.round(bitmap.height * scale));
                const canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;
                const context = canvas.getContext('2d', { willReadFrequently: true });
                context.drawImage(bitmap, 0, 0, width, height);
                const pixels = context.getImageData(0, 0, width, height);
                result = window.jsQR(pixels.data, width, height, { inversionAttempts: 'attemptBoth' });
            } finally {
                bitmap.close?.();
            }

            if (!result?.data) {
                portal.close();
                scanStatus.textContent = 'No readable QR code was found in this guarantee image.';
                return;
            }

            const target = new URL(currentPortalUrl, window.location.origin);
            target.searchParams.set('payload', result.data);
            portal.location.replace(target.href);
            scanStatus.textContent = 'QR scanned. The demo bank portal opened in a new tab.';
        } catch {
            portal.close();
            scanStatus.textContent = 'The QR could not be scanned. Try a sharper image with the whole code visible.';
        } finally {
            scanButton.disabled = false;
        }
    });

    document.addEventListener('click', (event) => {
        const link = event.target.closest('a[data-document-preview]');
        if (!link) return;
        event.preventDefault();

        const name = link.dataset.documentName || link.textContent.trim() || 'Document preview';
        const url = link.dataset.previewUrl;
        const extension = name.split('.').pop()?.toLowerCase();
        title.textContent = name;
        download.href = link.dataset.downloadUrl || link.href;
        body.replaceChildren();
        scanStatus.textContent = '';
        currentPortalUrl = link.dataset.qrPortalUrl || null;
        scanButton.hidden = !(String(link.dataset.qrScan).toLowerCase() === 'true' && ['png', 'jpg', 'jpeg'].includes(extension));

        if (['png', 'jpg', 'jpeg'].includes(extension)) {
            const image = document.createElement('img');
            image.src = url;
            image.alt = `Preview of ${name}`;
            image.style.cssText = 'display:block;max-width:100%;max-height:75vh;object-fit:contain;border-radius:.5rem';
            body.appendChild(image);
        } else if (extension === 'pdf') {
            const frame = document.createElement('iframe');
            frame.src = url;
            frame.title = `Preview of ${name}`;
            frame.style.cssText = 'width:100%;height:min(75vh,900px);border:0;border-radius:.5rem;background:white';
            body.appendChild(frame);
        } else {
            const notice = document.createElement('div');
            notice.className = 'alert alert-light border text-center mb-0';
            notice.textContent = 'This file type cannot be previewed here. Use Download to open it.';
            body.appendChild(notice);
        }
        modal.show();
    });
})();
