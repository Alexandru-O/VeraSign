// Lightweight PDF viewer helper. The Blazor circuit fetches the PDF bytes
// through the authenticated HttpClient (so JWT + ownership checks run) and
// hands the base64 payload here; we turn it into a blob URL the <iframe>
// can render. The browser stays out of the API auth path entirely.
(function () {
    function base64ToBytes(b64) {
        const bin = atob(b64);
        const arr = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        return arr;
    }

    window.verasignPdfViewer = {
        show(iframe, b64) {
            if (!iframe) return null;
            const blob = new Blob([base64ToBytes(b64)], { type: 'application/pdf' });
            const url = URL.createObjectURL(blob);
            iframe.src = url;
            return url;
        },
        revoke(url) {
            if (!url) return;
            try { URL.revokeObjectURL(url); } catch (_) { /* swallow */ }
        },
        triggerDownload(b64, filename, mimeType) {
            const blob = new Blob([base64ToBytes(b64)], { type: mimeType || 'application/pdf' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename || 'document.pdf';
            document.body.appendChild(a);
            a.click();
            a.remove();
            // Revoke after the browser has handed the blob to the download
            // manager — a sync revoke can cancel the save in some browsers.
            setTimeout(() => { try { URL.revokeObjectURL(url); } catch (_) {} }, 4000);
        }
    };
})();
