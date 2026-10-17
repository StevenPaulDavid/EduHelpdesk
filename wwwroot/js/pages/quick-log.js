// Log a job already done (Pages/QuickLog.cshtml): "Who dealt with it" lists only the people on the chosen category's
// desk, keeping the person picked if they are still in it. Without script every person is listed and the server checks.
(function () {
    const category = document.getElementById("quicklog-category");
    const person = document.getElementById("quicklog-person");
    if (!category || !person) return;
    const everyone = Array.from(person.options).map(o => ({ value: o.value, text: o.text, desks: (o.dataset.desks || "").split(" ") }));
    const filter = () => {
        const desk = category.selectedOptions[0]?.dataset.desk || "";
        const selected = person.value;
        person.length = 0;
        everyone.filter(p => desk === "" || p.desks.includes(desk)).forEach(p => {
            const option = new Option(p.text, p.value, false, p.value === selected);
            option.dataset.desks = p.desks.join(" ");
            person.add(option);
        });
    };
    category.addEventListener("change", filter);
    filter();
})();
