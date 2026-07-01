/**
 * Shared touch/pen drag-to-reorder for tables across the site.
 *
 * The native HTML5 drag-and-drop API (draggable / ondragstart) does not fire on
 * touch devices, so dragging a handle does nothing on mobile. This module adds a
 * Pointer Events path that works for touch and pen, calling back into the Blazor
 * component to perform the actual reorder. Mouse is intentionally left to the
 * native HTML5 path so desktop behaviour is unchanged.
 *
 * It is page-agnostic: any table can opt in by giving its <tbody> (or other
 * stable container) to `init`, marking each row with `data-row-index`, the drag
 * affordance with `data-drag-handle`, and optionally non-reorderable rows with
 * `data-deleted="true"`. The Blazor component must expose JSInvokable
 * `StartDrag(int)`, `MoveRow(int)` and `EndDrag()` methods.
 *
 * Pointer capture is taken on the stable container element (the <tbody>) rather
 * than the drag handle. Each reorder re-renders and moves the handle's row in the
 * DOM, which would release a capture held on the handle and leave the drag stuck
 * after a single move. The container is never reordered, so capturing there keeps
 * the gesture alive for the whole drag.
 */
(function () {
    function init(container, dotNetRef) {
        if (!container || !container.dataset || container.dataset.touchSortableInit) {
            return;
        }
        container.dataset.touchSortableInit = 'true';

        let pointerId = null;
        let lastIndex = -1;

        function rowOf(el) {
            return el && el.closest ? el.closest('[data-row-index]') : null;
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
            lastIndex = parseInt(row.dataset.rowIndex, 10);

            // Capture on the stable container, not the handle: reordering moves
            // the handle's row in the DOM and would otherwise drop the capture.
            try { container.setPointerCapture(pointerId); } catch (_) { /* ignore */ }
            container.addEventListener('pointermove', onPointerMove);
            container.addEventListener('pointerup', onPointerUp);
            container.addEventListener('pointercancel', onPointerUp);

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
            try { container.releasePointerCapture(pointerId); } catch (_) { /* ignore */ }
            container.removeEventListener('pointermove', onPointerMove);
            container.removeEventListener('pointerup', onPointerUp);
            container.removeEventListener('pointercancel', onPointerUp);
            pointerId = null;
            lastIndex = -1;
        }

        container.addEventListener('pointerdown', onPointerDown);

        container._touchSortableDispose = function () {
            cleanup();
            container.removeEventListener('pointerdown', onPointerDown);
            delete container.dataset.touchSortableInit;
        };
    }

    function dispose(container) {
        if (container && container._touchSortableDispose) {
            container._touchSortableDispose();
            container._touchSortableDispose = null;
        }
    }

    window.touchSortable = { init: init, dispose: dispose };
})();
