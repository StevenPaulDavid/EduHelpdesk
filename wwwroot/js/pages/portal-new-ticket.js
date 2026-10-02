// Report a problem in the staff portal (Pages/Portal/NewTicket.cshtml).
// Without this script the page still works: every catalogue item is listed and the title box is always shown.
(function () {
    const category = document.getElementById("portal-category");
    const sub = document.getElementById("portal-subcategory");
    const titleField = document.getElementById("portal-title-field");
    const priority = document.getElementById("portal-priority");
    if (!category || !sub || !titleField) return;

    const title = titleField.querySelector("input");
    // The list is: a "Choose what's wrong" placeholder, the catalogue items, and "Something else" (blank value) last.
    const placeholder = sub.options[0];
    const other = sub.options[sub.options.length - 1];
    const items = Array.from(sub.options).slice(1, -1).map(o => ({ value: o.value, text: o.text, category: (o.dataset.category || "").toLowerCase(), priority: o.dataset.priority || "" }));
    let priorityTouched = false;
    priority?.addEventListener("change", () => { priorityTouched = true; });

    // Only the chosen category's items. A category with none has nothing to choose from, so it goes straight to
    // "Something else" and the placeholder is dropped.
    const showItems = categoryChanged => {
        const wanted = category.value.toLowerCase();
        // On load the server's choice stands; once the category changes, the old pick belongs to the old category.
        const selected = categoryChanged ? "?" : sub.value;
        const matching = items.filter(i => i.category === wanted);
        sub.length = 0;
        if (matching.length > 0) sub.add(placeholder);
        matching.forEach(i => {
            const option = new Option(i.text, i.value, false, i.value === selected);
            option.dataset.priority = i.priority;
            sub.add(option);
        });
        sub.add(other);
        other.text = matching.length > 0 ? "Something else - I'll describe it" : "Describe the problem yourself";
        // Keep what was chosen if it is still there, otherwise start again from the top of the list.
        const keep = Array.from(sub.options).findIndex(o => o.value === selected);
        sub.selectedIndex = keep >= 0 ? keep : 0;
        update(false);
    };

    // The title box is only for "Something else". An item's starting priority is a suggestion, kept until the
    // person has chosen a priority themselves.
    const update = fromItemChange => {
        const isOther = sub.value === "";
        titleField.hidden = !isOther;
        if (title) title.required = isOther;
        if (fromItemChange && priority && !priorityTouched) {
            const suggested = sub.selectedOptions[0]?.dataset.priority;
            if (suggested) priority.value = suggested;
        }
    };

    category.addEventListener("change", () => showItems(true));
    sub.addEventListener("change", () => update(true));
    // Not update(true) on load: the server has already chosen the priority, whether that's the default, a repeated
    // ticket's, or what was posted before a validation error.
    showItems(false);
})();
