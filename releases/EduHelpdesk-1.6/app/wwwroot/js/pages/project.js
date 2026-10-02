// The project page (Pages/Project.cshtml). Blades, tabs, edit-in-place and expand/collapse are handled by actions.js.
(function () {
    // A link or redirect to one item (or one supplier's quote) opens the collapsed card it points into.
    const target = location.hash && document.getElementById(location.hash.slice(1));
    const card = target && target.closest("details.project-item");
    if (card && !card.open) {
        card.open = true;
        target.scrollIntoView();
    }
})();
