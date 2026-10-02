// A part's page (Pages/Parts/Edit.cshtml): shows the total a delivery will make before it is saved, so a typo like 30
// for 3 is caught at the desk.
(function () {
    const input = document.getElementById("restock-delivered");
    const preview = document.getElementById("restock-preview");
    if (!input || !preview) return;
    const current = Number(preview.dataset.current);
    const initial = preview.textContent;
    input.addEventListener("input", () => {
        const delivered = Number(input.value);
        preview.textContent = Number.isInteger(delivered) && delivered > 0
            ? `${current} in stock + ${delivered} delivered = ${current + delivered} after saving.`
            : initial;
    });
})();
