/**
 * Touch/pen drag-to-reorder for the chapter edit table.
 *
 * The native HTML5 drag-and-drop API (draggable / ondragstart) does not fire on
 * touch devices, so dragging the handle does nothing on mobile. This module adds
 * a Pointer Events path that works for touch and pen, calling back into the
 * Blazor component to perform the actual reorder. Mouse is intentionally left to
 * the native HTML5 path so desktop behaviour is unchanged.
 */
(function () {
    function init(container, dotNetRef) {
        if (!container || container.dataset.chapterSortableInit) {
            return;
        }
        container.dataset.chapterSortableInit = 'true';

        let pointerId = null;
        let handleEl = null;
        let lastIndex = -1;

        function rowOf(el) {
            return el && el.closest ? el.closest('tr[data-row-index]') : null;
        }

        function onPointerDown(e) {
            // Let the native HTML5 drag-and-drop handle mouse on desktop.
            if (e.pointerType === 'mouse') {
                return;
            }
            if (pointerId !== null) {
                return;
            }

            const handle = e.target.closest ? e.target.closest('[data-drag-handle]') : null;
            if (!handle || !container.contains(handle)) {
                return;
            }

            const row = rowOf(handle);
            if (!row || row.dataset.deleted === 'true') {
                return;
            }

            e.preventDefault();
            pointerId = e.pointerId;
            handleEl = handle;
            lastIndex = parseInt(row.dataset.rowIndex, 10);

            try { handle.setPointerCapture(pointerId); } catch (_) { /* ignore */ }
            handle.addEventListener('pointermove', onPointerMove);
            handle.addEventListener('pointerup', onPointerUp);
            handle.addEventListener('pointercancel', onPointerUp);

            dotNetRef.invokeMethodAsync('StartDrag', lastIndex);
        }

        function onPointerMove(e) {
            if (e.pointerId !== pointerId) {
                return;
            }
            e.preventDefault();

            const target = rowOf(document.elementFromPoint(e.clientX, e.clientY));
            if (!target || !container.contains(target) || target.dataset.deleted === 'true') {
                return;
            }

            const toIndex = parseInt(target.dataset.rowIndex, 10);
            if (Number.isNaN(toIndex) || toIndex === lastIndex) {
                return;
            }

            lastIndex = toIndex;
            dotNetRef.invokeMethodAsync('MoveRow', toIndex);
        }

        function onPointerUp(e) {
            if (e.pointerId !== pointerId) {
                return;
            }
            cleanup();
            dotNetRef.invokeMethodAsync('EndDrag');
        }

        function cleanup() {
            if (handleEl) {
                try { handleEl.releasePointerCapture(pointerId); } catch (_) { /* ignore */ }
                handleEl.removeEventListener('pointermove', onPointerMove);
                handleEl.removeEventListener('pointerup', onPointerUp);
                handleEl.removeEventListener('pointercancel', onPointerUp);
            }
            pointerId = null;
            handleEl = null;
            lastIndex = -1;
        }

        container.addEventListener('pointerdown', onPointerDown);

        container._chapterSortableDispose = function () {
            cleanup();
            container.removeEventListener('pointerdown', onPointerDown);
            delete container.dataset.chapterSortableInit;
        };
    }

    function dispose(container) {
        if (container && container._chapterSortableDispose) {
            container._chapterSortableDispose();
            container._chapterSortableDispose = null;
        }
    }

    window.chapterSortable = { init: init, dispose: dispose };
})();
