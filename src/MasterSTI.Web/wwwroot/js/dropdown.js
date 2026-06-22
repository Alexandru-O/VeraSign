/* MasterSTI / VeraSign — Dropdown helpers.
   Minimal click-outside detector for Components/UI/Dropdown.razor. */
(function () {
    var registry = new WeakMap();

    function attach(element, dotnetRef) {
        if (!element) return;
        // Avoid double-attach.
        var existing = registry.get(element);
        if (existing) return;
        var handler = function (e) {
            if (!element.contains(e.target)) {
                try { dotnetRef.invokeMethodAsync('CloseFromJs'); } catch (_) { /* circuit gone */ }
            }
        };
        document.addEventListener('mousedown', handler, true);
        registry.set(element, handler);
    }

    function detach(element) {
        if (!element) return;
        var handler = registry.get(element);
        if (handler) {
            document.removeEventListener('mousedown', handler, true);
            registry.delete(element);
        }
    }

    window.verasignDropdown = { attach: attach, detach: detach };
})();
