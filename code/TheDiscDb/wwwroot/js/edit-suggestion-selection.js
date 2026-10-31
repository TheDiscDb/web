export function initialize(container, receiver) {
    function getSuggestionId(event) {
        if (container.disabled || !(event.target instanceof Element) ||
            event.target.closest("a, button, input, select, textarea, label, [role='button'], [contenteditable]")) {
            return null;
        }

        const row = event.target.closest("tbody tr");
        const checkbox = row?.querySelector("input[data-suggestion-id]");
        return row && container.contains(row) && checkbox && !checkbox.disabled
            ? Number(checkbox.dataset.suggestionId)
            : null;
    }

    async function onClick(event) {
        const id = getSuggestionId(event);
        if (id !== null) {
            await receiver.invokeMethodAsync(
                "SelectSuggestionRow", id, event.ctrlKey || event.metaKey, event.shiftKey);
        }
    }

    function onMouseDown(event) {
        if (event.shiftKey && getSuggestionId(event) !== null) {
            event.preventDefault();
        }
    }

    container.addEventListener("click", onClick);
    container.addEventListener("mousedown", onMouseDown);

    return {
        dispose() {
            container.removeEventListener("click", onClick);
            container.removeEventListener("mousedown", onMouseDown);
        }
    };
}
