// The role editor (Pages/People/Role.cshtml). The header tick box sets or clears its whole column; it also reflects the
// column's state, so ticking every row by hand leaves the header ticked rather than out of step.
(function () {
    const columnBoxes = column => [...document.querySelectorAll(`input[name="grant"][data-column="${column}"]`)];
    function syncColumnHeader(column) {
        const header = document.querySelector(`.permission-all[data-column="${column}"]`);
        if (!header) return;
        const boxes = columnBoxes(column);
        const ticked = boxes.filter(b => b.checked).length;
        header.checked = boxes.length > 0 && ticked === boxes.length;
        header.indeterminate = ticked > 0 && ticked < boxes.length;
    }
    for (const header of document.querySelectorAll(".permission-all")) {
        header.addEventListener("change", () => {
            columnBoxes(header.dataset.column).forEach(b => b.checked = header.checked);
            header.indeterminate = false;
        });
    }
    for (const box of document.querySelectorAll('input[name="grant"]')) {
        box.addEventListener("change", () => syncColumnHeader(box.dataset.column));
    }
    document.querySelectorAll(".permission-all").forEach(h => syncColumnHeader(h.dataset.column));
})();
