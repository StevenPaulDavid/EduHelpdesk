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
        remove.textContent = "♜";
        row.lastElementChild.replaceChildren(remove);
        rows.insertBefore(row, spare);
        row.querySelector("input").focus();
    });
})();
