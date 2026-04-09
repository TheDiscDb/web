export function initialize(lastIndicator, componentInstance) {
    const options = {
        root: findClosestScrollContainer(lastIndicator),
        rootMargin: '0px',
        threshold: 0,
    };

    const observer = new IntersectionObserver(async (entries) => {
        // When the lastIndicator element is visible => invoke the C# method `LoadMoreItems`
        for (const entry of entries) {
            if (entry.isIntersecting) {
                observer.unobserve(lastIndicator);
                await componentInstance.invokeMethodAsync("LoadMoreItems");
            }
        }
    }, options);

    observer.observe(lastIndicator);

    // Allow to cleanup resources when the Razor component is removed from the page
    return {
        dispose: () => dispose(observer),
        onNewItems: () => {
            observer.unobserve(lastIndicator);
            observer.observe(lastIndicator);
        },
    };
}

// Cleanup resources
function dispose(observer) {
    observer.disconnect();
}

// Find the parent element with a vertical scrollbar
// This container should be used as the root for the IntersectionObserver.
// Only returns an element that actually constrains content (scrollHeight > clientHeight),
// not one that merely has overflow-y set but grows to fit all content.
function findClosestScrollContainer(element) {
    while (element) {
        const style = getComputedStyle(element);
        if (style.overflowY !== 'visible' && element.scrollHeight > element.clientHeight) {
            return element;
        }
        element = element.parentElement;
    }
    return null;
}