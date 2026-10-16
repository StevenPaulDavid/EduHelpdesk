// Recurring tickets (Pages/Settings/RecurringTickets.cshtml): each form shows only the "when" fields its Repeats choice
// uses. Without JavaScript they all show, and the server ignores the ones that don't apply.
(function () {
    const update = box => {
        const frequency = box.querySelector("[data-frequency]").value;
        const byWeekday = box.querySelector("[data-week-of-month]").value !== "0";
        box.querySelectorAll("[data-for]").forEach(el => {
            let shown = el.dataset.for.split(" ").includes(frequency);
            if (shown && frequency === "monthly" && el.hasAttribute("data-week-day")) shown = byWeekday;
            if (shown && frequency === "monthly" && el.hasAttribute("data-month-day")) shown = !byWeekday;
            el.hidden = !shown;
        });
    };
    document.querySelectorAll("[data-recurrence]").forEach(box => {
        update(box);
        box.addEventListener("change", e => {
            if (e.target.matches("[data-frequency], [data-week-of-month]")) update(box);
        });
    });
})();
