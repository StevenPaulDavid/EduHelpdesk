// Limits a model dropdown to the models that belong to the selected make.
// Model options carry data-make (empty means the model can be used with any make).
// With no make selected every model is offered. keepInitialSelection keeps the model that is
// already selected when the page loads, even if it does not match the make (used when editing).
// A page opts in with data-model-filter="<model select id>" on the make select, plus data-keep-initial when editing.
function initModelFilter(makeSelect, modelSelect, options) {
    const keepInitialSelection = !!(options && options.keepInitialSelection);
    const emptyText = (options && options.emptyText) || "No models for this make";
    const placeholder = Array.from(modelSelect.options).find(o => o.value === "");
    const allModels = Array.from(modelSelect.options)
        .filter(o => o.value !== "")
        .map(o => ({ value: o.value, text: o.text, make: (o.dataset.make || "").toLowerCase() }));
    let firstRun = true;

    function apply() {
        const make = makeSelect.value.toLowerCase();
        const selected = modelSelect.value;
        const keep = firstRun && keepInitialSelection ? selected : null;
        modelSelect.length = 0;
        if (placeholder) modelSelect.add(new Option(placeholder.text, ""));
        const matches = allModels.filter(m => make === "" || m.make === "" || m.make === make || m.value === keep);
        matches.forEach(m => modelSelect.add(new Option(m.text, m.value, false, m.value === selected)));
        if (!placeholder && matches.length === 0) modelSelect.add(new Option(emptyText, ""));
        firstRun = false;
    }

    makeSelect.addEventListener("change", apply);
    apply();
}

document.querySelectorAll("select[data-model-filter]").forEach(make => {
    const model = document.getElementById(make.dataset.modelFilter);
    if (model) initModelFilter(make, model, { keepInitialSelection: make.hasAttribute("data-keep-initial") });
});
