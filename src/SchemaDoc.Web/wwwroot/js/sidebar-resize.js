// Resizable sidebar with localStorage persistence.
// Wires a drag handle so the user can change sidebar width by dragging.
window.sidebarResize = (function () {
    const STORAGE_KEY = 'schemadoc.sidebarWidth';
    const MIN_WIDTH = 180;
    const MAX_WIDTH = 600;

    function getSavedWidth() {
        const v = parseInt(localStorage.getItem(STORAGE_KEY), 10);
        if (isNaN(v)) return null;
        return Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, v));
    }

    function applyWidth(px) {
        const aside = document.querySelector('aside[data-sidebar]');
        if (aside) aside.style.width = px + 'px';
    }

    function init() {
        const saved = getSavedWidth();
        if (saved !== null) applyWidth(saved);

        const handle = document.querySelector('[data-sidebar-handle]');
        if (!handle || handle.dataset.bound === '1') return;
        handle.dataset.bound = '1';

        let dragging = false;

        const onMove = (e) => {
            if (!dragging) return;
            const x = e.clientX;
            const w = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, x));
            applyWidth(w);
            // Avoid text selection while dragging
            e.preventDefault();
        };

        const onUp = () => {
            if (!dragging) return;
            dragging = false;
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            const aside = document.querySelector('aside[data-sidebar]');
            if (aside) localStorage.setItem(STORAGE_KEY, parseInt(aside.style.width, 10));
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
        };

        handle.addEventListener('mousedown', (e) => {
            dragging = true;
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            window.addEventListener('mousemove', onMove);
            window.addEventListener('mouseup', onUp);
            e.preventDefault();
        });

        // Double-click resets to default
        handle.addEventListener('dblclick', () => {
            applyWidth(256);
            localStorage.setItem(STORAGE_KEY, 256);
        });
    }

    return { init };
})();
