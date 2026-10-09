// School day and periods (Pages/Settings/SchoolDay.cshtml).
(function () {
    const rows = document.getElementById("period-rows");
    if (!rows) return;
    // Removing a row just takes it out of the form; nothing changes until Save periods.
    rows.addEventListener("click", e => {
        const button = e.target.closest(".period-remove");
        if (button) button.closest("tr").remove();
    });
    document.getElementById("period-add").addEventListener("click", () => {
        const spare = rows.querySelector(".period-spare");
        const row = spare.cloneNode(true);
        row.classList.remove("period-spare");
        row.querySelectorAll("input").forEach(input => input.value = "");
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "icon-button delete-icon period-remove";
        remove.title = "Remove period";
        remove.setAttribute("aria-label", "Remove period");
        // The same bin as Icons.Bin (Pages/Shared/Icons.cs), which the server-drawn rows use.
        remove.innerHTML = '<svg class="ico" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4 7h16M10 11v6M14 11v6M9 7V4h6v3M6 7l1 13h10l1-13"/></svg>';
        row.lastElementChild.replaceChildren(remove);
        rows.insertBefore(row, spare);
        row.querySelector("input").focus();
    });
})();
