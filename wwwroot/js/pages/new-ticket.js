// Log a ticket (Pages/NewTicket.cshtml).
(function () {
    const deskSelect = document.getElementById("ticket-desk-select");
    const categorySelect = document.getElementById("ticket-category-select");
    const technicianSelect = document.getElementById("ticket-technician-select");
    const teamSelect = document.getElementById("ticket-team-select");

    // Each list is kept whole here and rebuilt from it, since hidden options can still be picked in some browsers.
    const snapshot = select => select ? Array.from(select.options).map(o => ({ value: o.value, text: o.text, data: { ...o.dataset } })) : [];
    const allCategories = snapshot(categorySelect);
    const allTeams = snapshot(teamSelect);
    const allTechnicians = snapshot(technicianSelect);
    const desk = () => deskSelect ? deskSelect.value : "";

    const rebuild = (select, options, keep) => {
        const selected = select.value;
        select.length = 0;
        options.filter(keep).forEach(o => {
            const option = new Option(o.text, o.value, false, o.value === selected);
            Object.assign(option.dataset, o.data);
            select.add(option);
        });
    };

    // With several desks the desk box narrows the categories and teams to that desk's (the category decides the desk).
    const filterByDesk = () => {
        if (!deskSelect) return;
        if (categorySelect) {
            rebuild(categorySelect, allCategories, o => o.data.desk === desk());
            categorySelect.dispatchEvent(new Event("change"));
        }
        if (teamSelect) rebuild(teamSelect, allTeams, o => o.value === "" || o.data.desk === desk());
    };

    // Choosing a team narrows the technician list to that team (and, with several desks, to people on the desk),
    // keeping the technician if they are still in it.
    const filterTechnicians = () => {
        if (!technicianSelect) return;
        const team = teamSelect ? teamSelect.value.toLowerCase() : "";
        rebuild(technicianSelect, allTechnicians, o => o.value === ""
            || ((team === "" || (o.data.team || "").toLowerCase() === team)
                && (!deskSelect || (o.data.desks || "").split(" ").includes(desk()))));
    };

    deskSelect?.addEventListener("change", () => { filterByDesk(); filterTechnicians(); });
    teamSelect?.addEventListener("change", filterTechnicians);
    filterByDesk();
    filterTechnicians();

    // Custom attributes that belong to particular categories only show while one of those categories is chosen, and
    // their section heading only while at least one of them shows.
    const attributes = document.getElementById("ticket-attributes");
    const showAttributes = () => {
        const category = (categorySelect?.value || "").toLowerCase();
        let shown = 0;
        document.querySelectorAll(".ticket-attribute-field").forEach(field => {
            const categories = JSON.parse(field.getAttribute("data-categories") || "[]");
            field.hidden = categories.length > 0 && !categories.some(c => c.toLowerCase() === category);
            if (!field.hidden) shown++;
        });
        if (attributes) attributes.hidden = shown === 0;
    };
    categorySelect?.addEventListener("change", showAttributes);
    deskSelect?.addEventListener("change", showAttributes);
    showAttributes();
})();
