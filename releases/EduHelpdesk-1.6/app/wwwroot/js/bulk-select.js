// The tick boxes and "With selected" bar on the ticket, asset and part lists - one copy instead of the three that each
// page used to carry. It works from the markup:
//   form#bulk-form     data-match-count (every match, not just this page), data-noun and data-nouns ("ticket", "tickets")
//   input[name=ids]    the row tick boxes; #select-page ticks the whole page
//   #bulk-operation    the action; each option's fields live in #bulk-<value>-field, and are disabled while another
//                      action is chosen so they aren't posted. An option may carry data-hint (shown under the bar),
//                      data-button (the button's wording) and data-confirm (asked before posting; {count} is the number
//                      selected, and any other {name} is the value of that form field)
//   data-required-message   on a control inside an action's fields: the action can't be applied while it is blank
(function () {
    const form = document.getElementById("bulk-form");
    if (!form) return;
    const noun = form.dataset.noun || "item";
    const nouns = form.dataset.nouns || noun + "s";
    const matchCount = parseInt(form.dataset.matchCount || "0", 10);
    const selectAll = form.querySelector("[name=selectAll]");
    const banner = document.getElementById("select-banner");
    const pageBox = document.getElementById("select-page");
    const operation = document.getElementById("bulk-operation");
    const counter = document.getElementById("selected-count");
    const apply = document.getElementById("bulk-apply");
    const hint = document.getElementById("bulk-hint");
    const rowBoxes = () => [...form.querySelectorAll("input[name=ids]")];
    const allMatching = () => selectAll.value === "true";
    const selectedTotal = () => allMatching() ? matchCount : rowBoxes().filter(b => b.checked).length;
    const chosen = () => operation?.selectedOptions[0];

    function bannerWith(text, buttonText, onClick) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "link-button";
        button.textContent = buttonText;
        button.addEventListener("click", onClick);
        banner.replaceChildren(text + " ", button);
        banner.hidden = false;
    }

    function selectionChanged() {
        const boxes = rowBoxes();
        const checked = boxes.filter(b => b.checked).length;
        if (pageBox) pageBox.checked = boxes.length > 0 && checked === boxes.length;
        const total = selectedTotal();
        if (counter) counter.textContent = total === 0 ? "None selected" : total + " selected";
        // Once the whole page is ticked and there are more matches than fit on it, offer to take in every match.
        if (!banner) return;
        if (allMatching()) {
            bannerWith(`All ${matchCount} matching ${nouns} are selected.`, "Clear selection", () => {
                boxes.forEach(b => b.checked = false);
                selectAll.value = "false";
                selectionChanged();
            });
        } else if (boxes.length > 0 && checked === boxes.length && matchCount > boxes.length) {
            bannerWith(`All ${boxes.length} ${nouns} on this page are selected.`, `Select all ${matchCount} matching ${nouns}`, () => {
                selectAll.value = "true";
                selectionChanged();
            });
        } else {
            banner.hidden = true;
        }
    }

    // A role that may only export gets that one option, so any of the fields may legitimately be missing.
    function operationChanged() {
        if (!operation) return;
        for (const option of operation.options) {
            const field = document.getElementById(`bulk-${option.value}-field`);
            if (!field) continue;
            const on = option.value === operation.value;
            field.hidden = !on;
            field.querySelectorAll("select, textarea, input").forEach(control => control.disabled = !on);
        }
        if (apply) apply.textContent = chosen()?.dataset.button || "Apply to selected";
        if (hint) {
            hint.textContent = chosen()?.dataset.hint || "";
            hint.hidden = !hint.textContent;
        }
    }

    form.addEventListener("change", e => {
        if (e.target === pageBox) {
            rowBoxes().forEach(b => b.checked = pageBox.checked);
            selectAll.value = "false";
            selectionChanged();
        } else if (e.target.name === "ids") {
            selectAll.value = "false";
            selectionChanged();
        } else if (e.target === operation) {
            operationChanged();
        }
    });

    form.addEventListener("submit", e => {
        const total = selectedTotal();
        if (total === 0) { e.preventDefault(); alert(`Select at least one ${noun} first.`); return; }
        const option = chosen();
        const field = option && document.getElementById(`bulk-${option.value}-field`);
        const blank = field && [...field.querySelectorAll("[data-required-message]")].find(control => !control.value.trim());
        if (blank) { e.preventDefault(); alert(blank.dataset.requiredMessage); blank.focus(); return; }
        const value = option ? option.value : "export";
        const question = option?.dataset.confirm
            ? option.dataset.confirm.replace(/\{(\w+)\}/g, (_, key) => key === "count" ? String(total) : (form.elements[key]?.value ?? ""))
            : value !== "export" && total > 20 ? `Change ${total} ${nouns}?` : null;
        if (question && !confirm(question)) e.preventDefault();
    });

    operationChanged();
    selectionChanged();
})();
