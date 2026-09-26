// Log a ticket (Pages/NewTicket.cshtml).
(function () {
    // Choosing a team narrows the technician list to that team, keeping the technician if they are in it.
    const technicianSelect = document.getElementById("ticket-technician-select");
    const teamSelect = document.getElementById("ticket-team-select");
    if (technicianSelect && teamSelect) {
        const allTechnicians = Array.from(technicianSelect.options).filter(o => o.value !== "")
            .map(o => ({ value: o.value, text: o.text, team: (o.dataset.team || "").toLowerCase() }));
        const filterTechnicians = () => {
            const team = teamSelect.value.toLowerCase();
            const selected = technicianSelect.value;
            technicianSelect.length = 1;
            allTechnicians
                .filter(t => team === "" || t.team === team)
                .forEach(t => technicianSelect.add(new Option(t.text, t.value, false, t.value === selected)));
        };
        teamSelect.addEventListener("change", filterTechnicians);
        filterTechnicians();
    }

    // Custom attributes that belong to particular categories only show while one of those categories is chosen.
    const categorySelect = document.getElementById("ticket-category-select");
    categorySelect?.addEventListener("change", () => {
        const category = categorySelect.value.toLowerCase();
        document.querySelectorAll(".ticket-attribute-field").forEach(field => {
            const categories = JSON.parse(field.getAttribute("data-categories") || "[]");
            field.hidden = categories.length > 0 && !categories.some(c => c.toLowerCase() === category);
        });
    });
})();
