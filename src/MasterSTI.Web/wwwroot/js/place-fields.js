// PDF.js-based field placement viewer. Renders each PDF page as a canvas
// inside the host element so signature markers can be absolute-positioned
// against the page wrapper — they scroll with the page content instead of
// floating against the outer container the way an `<iframe>`+overlay does.
//
// Public API (window.verasignPlaceFields):
//   mount(host, pdfUrl, dotnetRef, initialFields) -> { pageCount }
//   setPlacing(host, on)
//   renderFields(host, fields)
//   dispose(host)
//   getCanvasRect()  // legacy shim for the mock-only fallback path
(function () {
    const PDFJS_VERSION = '4.10.38';
    const PDFJS_URL = `https://cdn.jsdelivr.net/npm/pdfjs-dist@${PDFJS_VERSION}/build/pdf.mjs`;
    const WORKER_URL = `https://cdn.jsdelivr.net/npm/pdfjs-dist@${PDFJS_VERSION}/build/pdf.worker.mjs`;

    let pdfjsPromise = null;
    function loadPdfJs() {
        if (!pdfjsPromise) {
            pdfjsPromise = import(/* @vite-ignore */ PDFJS_URL).then(mod => {
                mod.GlobalWorkerOptions.workerSrc = WORKER_URL;
                return mod;
            });
        }
        return pdfjsPromise;
    }

    const FIELD_COLORS = {
        signature: '#0F3E93',
        initial: '#0A6B39',
        date: '#D97706',
    };
    function colorFor(kind) { return FIELD_COLORS[kind] || '#606878'; }

    function buildMarker(field) {
        const c = colorFor(field.kind);
        const el = document.createElement('div');
        el.className = 'vs-pf__field';
        el.style.left = field.x + '%';
        el.style.top = field.y + '%';
        el.style.width = field.w + '%';
        el.style.height = field.h + '%';
        el.style.background = c + '18';
        el.style.borderColor = c;
        el.style.color = c;
        const who = (field.who || '').split(' ')[0];
        if (field.order != null) {
            const badge = document.createElement('span');
            badge.className = 'vs-pf__field-badge';
            badge.style.background = c;
            badge.textContent = '(' + field.order + ')';
            el.appendChild(badge);
            el.appendChild(document.createTextNode(' ' + (field.kind || '').toUpperCase() + ' · ' + who));
        } else {
            el.textContent = (field.kind || '').toUpperCase() + ' · ' + who;
        }
        return el;
    }

    const stateByHost = new WeakMap();

    async function mount(host, pdfUrl, dotnetRef, initialFields) {
        if (!host) return { pageCount: 0 };
        if (stateByHost.has(host)) await dispose(host);

        const pdfjs = await loadPdfJs();
        const cssWidth = Math.max(320, host.clientWidth || 800);

        host.innerHTML = '';
        host.classList.add('vs-pf__pdf-host');

        const doc = await pdfjs.getDocument({ url: pdfUrl }).promise;
        const pages = [];

        for (let i = 1; i <= doc.numPages; i++) {
            const page = await doc.getPage(i);
            const unscaled = page.getViewport({ scale: 1 });
            const scale = cssWidth / unscaled.width;
            const viewport = page.getViewport({ scale });

            const wrapper = document.createElement('div');
            wrapper.className = 'vs-pf__page';
            wrapper.dataset.page = String(i);

            const canvas = document.createElement('canvas');
            const dpr = Math.min(window.devicePixelRatio || 1, 2);
            canvas.width = Math.floor(viewport.width * dpr);
            canvas.height = Math.floor(viewport.height * dpr);
            canvas.style.width = viewport.width + 'px';
            canvas.style.height = viewport.height + 'px';
            wrapper.appendChild(canvas);

            const overlay = document.createElement('div');
            overlay.className = 'vs-pf__page__overlay';
            wrapper.appendChild(overlay);

            const markers = document.createElement('div');
            markers.className = 'vs-pf__page__markers';
            wrapper.appendChild(markers);

            host.appendChild(wrapper);

            const ctx = canvas.getContext('2d');
            ctx.scale(dpr, dpr);
            await page.render({ canvasContext: ctx, viewport }).promise;

            const pageIndex = i;
            overlay.addEventListener('click', (ev) => {
                const st = stateByHost.get(host);
                if (!st || !st.placing) return;
                const rect = wrapper.getBoundingClientRect();
                const x = ((ev.clientX - rect.left) / rect.width) * 100;
                const y = ((ev.clientY - rect.top) / rect.height) * 100;
                ev.stopPropagation();
                try { dotnetRef.invokeMethodAsync('OnFieldPlaced', pageIndex, x, y); }
                catch (e) { console.warn('OnFieldPlaced invoke failed', e); }
            });

            pages.push({ index: i, wrapper, canvas, overlay, markers });
        }

        const st = { host, doc, pages, placing: false, dotnetRef };
        stateByHost.set(host, st);

        if (Array.isArray(initialFields) && initialFields.length > 0) {
            renderFields(host, initialFields);
        }
        return { pageCount: doc.numPages };
    }

    function setPlacing(host, on) {
        const st = stateByHost.get(host);
        if (!st) return;
        st.placing = !!on;
        for (const p of st.pages) {
            p.overlay.classList.toggle('vs-pf__page__overlay--active', st.placing);
        }
    }

    function renderFields(host, fields) {
        const st = stateByHost.get(host);
        if (!st) return;
        for (const p of st.pages) p.markers.replaceChildren();
        if (!Array.isArray(fields)) return;
        for (const f of fields) {
            const pageIdx = ((f.page || 1) - 1);
            const target = st.pages[pageIdx] || st.pages[0];
            if (!target) continue;
            target.markers.appendChild(buildMarker(f));
        }
    }

    async function dispose(host) {
        const st = stateByHost.get(host);
        if (!st) return;
        try { await st.doc.cleanup(); } catch (_) { }
        try { st.doc.destroy(); } catch (_) { }
        host.innerHTML = '';
        stateByHost.delete(host);
    }

    // Legacy shim — kept so the mock-fallback branch (no DocumentId) still
    // builds a sane bounding rect for its own click handler. Real-doc flow
    // uses the per-page overlay above and ignores this.
    function getCanvasRect() {
        const el = document.getElementById('vs-pf-canvas');
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return { left: r.left, top: r.top, width: r.width, height: r.height };
    }

    window.verasignPlaceFields = { mount, setPlacing, renderFields, dispose, getCanvasRect };
})();
