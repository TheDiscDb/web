/**
 * Search autocomplete - works immediately without waiting for Blazor interactive runtime.
 * Attaches to #site-search and renders suggestions into .autocomplete .options.
 */
(function () {
    const DEBOUNCE_MS = 300;
    const MIN_QUERY = 3;
    const LIMIT = 5;

    let timer = null;
    let controller = null;
    let selectedIndex = -1;
    let suggestions = [];

    function init() {
        const input = document.getElementById('site-search');
        if (!input) return;

        const autocomplete = input.closest('.autocomplete');
        if (!autocomplete) return;

        // Avoid double-init
        if (autocomplete.dataset.acInit) return;
        autocomplete.dataset.acInit = 'true';

        input.addEventListener('input', () => onInput(input, autocomplete));
        input.addEventListener('search', () => onInput(input, autocomplete));
        input.addEventListener('keydown', (e) => onKeyDown(e, input, autocomplete));
        input.addEventListener('focus', () => onFocus(input, autocomplete));
        document.addEventListener('click', (e) => {
            if (!autocomplete.contains(e.target)) hideDropdown(autocomplete);
        });
    }

    function onInput(input, autocomplete) {
        const query = input.value?.trim();
        selectedIndex = -1;

        if (!query || query.length < MIN_QUERY) {
            hideDropdown(autocomplete);
            return;
        }

        clearTimeout(timer);
        timer = setTimeout(() => fetchSuggestions(query, autocomplete, input), DEBOUNCE_MS);
    }

    async function fetchSuggestions(query, autocomplete, input) {
        controller?.abort();
        controller = new AbortController();

        try {
            const res = await fetch(
                `/api/search?q=${encodeURIComponent(query)}&limit=${LIMIT}`,
                { signal: controller.signal }
            );
            if (!res.ok) return;

            suggestions = await res.json();
            if (suggestions.length > 0 && document.activeElement === input) {
                renderDropdown(autocomplete, suggestions, input.value);
            } else {
                hideDropdown(autocomplete);
            }
        } catch (e) {
            if (e.name !== 'AbortError') hideDropdown(autocomplete);
        }
    }

    function renderDropdown(autocomplete, items, query) {
        let existing = autocomplete.querySelector('.options');
        if (!existing) {
            existing = document.createElement('div');
            existing.className = 'options';
            autocomplete.appendChild(existing);
        }

        let html = '';
        items.forEach((item, i) => {
            const imgHtml = item.imageUrl
                ? `<img src="/images/${item.imageUrl}?width=40&height=60" width="40" height="60" alt="" class="option-thumb" />`
                : '';
            const typeHtml = item.type
                ? `<span class="option-type">${escapeHtml(item.type)}</span>`
                : '';
            const selectedClass = i === selectedIndex ? ' selected' : '';
            const url = item.relativeUrl ? item.relativeUrl.toLowerCase() : '#';
            html += `<a href="${escapeHtml(url)}" class="option${selectedClass}" data-index="${i}">
                ${imgHtml}
                <span class="option-text">
                    <span class="option-title">${escapeHtml(item.title || '')}</span>
                    ${typeHtml}
                </span>
            </a>`;
        });

        html += `<a href="/search?q=${encodeURIComponent(query)}" class="option see-all">
            See all results for "${escapeHtml(query)}"
        </a>`;

        existing.innerHTML = html;
        existing.style.display = '';

        existing.querySelectorAll('.option').forEach(el => {
            el.addEventListener('click', () => {
                hideDropdown(autocomplete);
                // On mobile, dismiss the offcanvas nav menu
                const offcanvasEl = document.getElementById('navbarOffcanvas');
                if (offcanvasEl) {
                    const offcanvas = bootstrap.Offcanvas.getInstance(offcanvasEl);
                    offcanvas?.hide();
                }
            });
        });
    }

    function hideDropdown(autocomplete) {
        const options = autocomplete.querySelector('.options');
        if (options) options.style.display = 'none';
        selectedIndex = -1;
        suggestions = [];
    }

    function onKeyDown(e, input, autocomplete) {
        const options = autocomplete.querySelector('.options');
        if (!options || options.style.display === 'none' || suggestions.length === 0) {
            return;
        }

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                selectedIndex = Math.min(selectedIndex + 1, suggestions.length - 1);
                updateSelection(options);
                break;
            case 'ArrowUp':
                e.preventDefault();
                selectedIndex = Math.max(selectedIndex - 1, -1);
                updateSelection(options);
                break;
            case 'Enter':
                if (selectedIndex >= 0 && selectedIndex < suggestions.length) {
                    e.preventDefault();
                    hideDropdown(autocomplete);
                    // On mobile, dismiss the offcanvas nav menu
                    const offcanvasEl = document.getElementById('navbarOffcanvas');
                    if (offcanvasEl) {
                        const offcanvas = bootstrap.Offcanvas.getInstance(offcanvasEl);
                        offcanvas?.hide();
                    }
                    const url = suggestions[selectedIndex].relativeUrl;
                    if (url) window.location.href = url.toLowerCase();
                }
                break;
            case 'Escape':
                hideDropdown(autocomplete);
                break;
        }
    }

    function updateSelection(options) {
        options.querySelectorAll('.option:not(.see-all)').forEach((el, i) => {
            el.classList.toggle('selected', i === selectedIndex);
        });
    }

    function onFocus(input, autocomplete) {
        if (suggestions.length > 0 && input.value?.trim().length >= MIN_QUERY) {
            const options = autocomplete.querySelector('.options');
            if (options) options.style.display = '';
        }
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    // Dismiss offcanvas when nav links inside it are clicked (mobile)
    function initNavDismiss() {
        const offcanvasEl = document.getElementById('navbarOffcanvas');
        if (!offcanvasEl || offcanvasEl.dataset.navDismissInit) return;
        offcanvasEl.dataset.navDismissInit = 'true';

        offcanvasEl.addEventListener('click', (e) => {
            const link = e.target.closest('a.nav-link');
            if (!link) return;
            const instance = bootstrap.Offcanvas.getInstance(offcanvasEl);
            instance?.hide();
        });
    }

    // Initialize on DOM ready and after Blazor enhanced navigation
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => { init(); initNavDismiss(); });
    } else {
        init();
        initNavDismiss();
    }

    // Re-initialize after Blazor enhanced page updates
    const observer = new MutationObserver(() => {
        if (document.getElementById('site-search') && !document.querySelector('[data-ac-init]')) {
            init();
        }
        initNavDismiss();
    });
    observer.observe(document.body, { childList: true, subtree: true });
})();
