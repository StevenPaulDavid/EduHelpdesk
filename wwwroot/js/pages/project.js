// The project page (Pages/Project.cshtml). Panels, tabs and autosubmitting selects are handled by actions.js; the
// address's #fragment (#notes, #item-…, #quote-…) opens the right tab there too.
(function () {
    // After a change made in a panel - a price added, a file uploaded, a sub-item renamed - the page comes back with
    // that panel marked data-reopen, and it opens again through its own button, so closing it later returns focus there.
    // Waits for DOMContentLoaded so actions.js (deferred) is listening before the click.
    document.addEventListener("DOMContentLoaded", () => {
        // Razor always writes data- attributes (empty when null), so look for the value, not just the attribute.
        const panel = document.querySelector('.blade-backdrop[data-reopen="true"]');
        if (!panel) return;
        const opener = document.querySelector(`[data-open-blade="${CSS.escape(panel.id)}"]`);
        if (opener) opener.click();
    });
})();
