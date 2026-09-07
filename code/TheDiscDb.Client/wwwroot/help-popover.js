const registrations = new Map();

export function register(root, dotNetReference) {
    unregister(root);

    const handlePointerDown = event => {
        if (!root.contains(event.target)) {
            dotNetReference.invokeMethodAsync("DismissAsync");
        }
    };

    const handleKeyDown = event => {
        if (event.key === "Escape") {
            event.preventDefault();
            dotNetReference.invokeMethodAsync("DismissAsync");
        }
    };

    document.addEventListener("pointerdown", handlePointerDown, true);
    document.addEventListener("keydown", handleKeyDown, true);
    registrations.set(root, { handlePointerDown, handleKeyDown });
}

export function unregister(root) {
    const registration = registrations.get(root);
    if (!registration) {
        return;
    }

    document.removeEventListener("pointerdown", registration.handlePointerDown, true);
    document.removeEventListener("keydown", registration.handleKeyDown, true);
    registrations.delete(root);
}
