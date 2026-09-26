// Type-to-search for long dropdowns. Any <select data-picker> - a requester out of hundreds, an asset out of a
// thousand - becomes a text box that lists the options matching what is typed (every word, in any order, anywhere
// in the option's text or its data-search). A multiple select keeps what is chosen as chips above the box.
//
// The real <select> stays in the form, hidden, and is what gets posted: choosing an option only sets it and fires its
// change event, so the page's own validation and scripts carry on as before. Without JavaScript the plain dropdown
// is still there. Loaded on every page by the layout.
(function () {
    const LIMIT = 50;
    let count = 0;

    function textOf(option) { return option.textContent.trim(); }
    function searchOf(option) { return (textOf(option) + " " + (option.dataset.search || "")).toLowerCase(); }

    function enhance(select) {
        if (select.dataset.pickerReady) return;
        select.dataset.pickerReady = "true";
        const id = "picker-" + (++count);
        const multiple = select.multiple;
        const options = () => [...select.options];
        // The "nothing chosen" option (Select a user…, Unassigned, Anyone) supplies the placeholder, and still
        // appears in the list so it can be chosen back.
        const blank = options().find(o => o.value === "");

        const wrap = document.createElement("div");
        wrap.className = "picker" + (multiple ? " picker-multiple" : "");
        select.parentNode.insertBefore(wrap, select);

        const chips = multiple ? Object.assign(document.createElement("ul"), { className: "picker-chips" }) : null;
        const input = Object.assign(document.createElement("input"), { type: "text", className: "picker-input", autocomplete: "off", id: id + "-input" });
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-expanded", "false");
        input.setAttribute("aria-controls", id + "-list");
        input.placeholder = select.dataset.pickerPlaceholder || (multiple ? "Type to search and add…" : blank ? textOf(blank) : "Type to search…");
        const list = Object.assign(document.createElement("ul"), { className: "picker-results", id: id + "-list", hidden: true });
        list.setAttribute("role", "listbox");
        if (multiple) list.setAttribute("aria-multiselectable", "true");

        if (chips) wrap.appendChild(chips);
        wrap.append(input, list);
        wrap.appendChild(select);
        // A <label for> that named the select now names the box, so clicking the label still lands somewhere useful.
        if (select.id) document.querySelectorAll(`label[for="${CSS.escape(select.id)}"]`).forEach(label => label.htmlFor = input.id);
        select.classList.add("picker-native");
        select.tabIndex = -1;
        select.setAttribute("aria-hidden", "true");
        // A hidden control can't show the browser's "please fill this in" message, so the box takes over required.
        const needed = select.required;
        select.required = false;
        input.disabled = select.disabled;
        new MutationObserver(() => { input.disabled = select.disabled; if (select.disabled) close(); })
            .observe(select, { attributes: true, attributeFilter: ["disabled"] });

        let matches = [];
        let active = -1;

        function current() { return options().find(o => o.selected && o.value !== ""); }
        function showSelection() {
            if (multiple) {
                chips.replaceChildren(...options().filter(o => o.selected).map(o => {
                    const chip = document.createElement("li");
                    const remove = Object.assign(document.createElement("button"), { type: "button", className: "picker-remove", textContent: "×" });
                    remove.setAttribute("aria-label", "Remove " + textOf(o));
                    remove.addEventListener("click", () => { o.selected = false; changed(); input.focus(); });
                    chip.append(Object.assign(document.createElement("span"), { textContent: textOf(o) }), remove);
                    return chip;
                }));
                input.value = "";
            } else {
                input.value = current() ? textOf(current()) : "";
            }
            // Required means "something chosen": for a multiple select, only while nothing is.
            input.required = needed && (!multiple || options().every(o => !o.selected));
        }
        function changed() {
            showSelection();
            select.dispatchEvent(new Event("change", { bubbles: true }));
        }

        function close() {
            list.hidden = true;
            list.replaceChildren();
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
            matches = [];
            active = -1;
        }
        function highlight(index) {
            active = index;
            [...list.querySelectorAll("[role=option]")].forEach((el, i) => el.classList.toggle("is-active", i === index));
            const el = list.querySelector("#" + id + "-o" + index);
            if (el) { input.setAttribute("aria-activedescendant", el.id); el.scrollIntoView({ block: "nearest" }); }
            else input.removeAttribute("aria-activedescendant");
        }
        function choose(option) {
            if (multiple) option.selected = true;
            else select.value = option.value;
            changed();
            close();
            if (multiple) input.focus();
        }
        function open(query) {
            const words = query.toLowerCase().split(/\s+/).filter(Boolean);
            const found = options().filter(o => !o.disabled && !(multiple && o.selected) && !(multiple && o.value === "")
                && words.every(w => searchOf(o).includes(w)));
            matches = found.slice(0, LIMIT);
            list.replaceChildren(...matches.map((option, i) => {
                const item = Object.assign(document.createElement("li"), { id: id + "-o" + i, textContent: textOf(option) });
                item.setAttribute("role", "option");
                if (option.selected && option.value !== "") item.setAttribute("aria-selected", "true");
                // mousedown rather than click, so the box keeps focus and the list doesn't close first.
                item.addEventListener("mousedown", e => { e.preventDefault(); choose(option); });
                return item;
            }));
            if (found.length === 0) list.appendChild(Object.assign(document.createElement("li"), { className: "picker-note", textContent: "No matches." }));
            else if (found.length > LIMIT) list.appendChild(Object.assign(document.createElement("li"), { className: "picker-note", textContent: `Showing ${LIMIT} of ${found.length}. Keep typing to narrow it down.` }));
            list.hidden = false;
            input.setAttribute("aria-expanded", "true");
            const selectedIndex = matches.findIndex(o => o.selected && o.value !== "");
            highlight(matches.length === 0 ? -1 : Math.max(0, selectedIndex));
        }

        input.addEventListener("focus", () => { input.select(); open(""); });
        input.addEventListener("input", () => open(input.value));
        input.addEventListener("keydown", e => {
            if (e.key === "ArrowDown") { e.preventDefault(); if (list.hidden) open(input.value); else if (matches.length) highlight((active + 1) % matches.length); }
            else if (e.key === "ArrowUp") { e.preventDefault(); if (matches.length) highlight((active - 1 + matches.length) % matches.length); }
            // Enter picks the highlighted option. With the list shut it is left alone, so it still submits a form.
            else if (e.key === "Enter" && !list.hidden) { e.preventDefault(); if (active >= 0) choose(matches[active]); }
            else if (e.key === "Escape" && !list.hidden) { e.preventDefault(); close(); showSelection(); }
            else if (e.key === "Backspace" && multiple && input.value === "") {
                const last = options().filter(o => o.selected).pop();
                if (last) { last.selected = false; changed(); }
            }
        });
        // Leaving the box never leaves half-typed text looking like a choice: it goes back to what is chosen. Clearing
        // it completely on a single select chooses the blank option, where there is one.
        input.addEventListener("blur", () => setTimeout(() => {
            if (document.activeElement === input) return;
            close();
            if (!multiple && input.value.trim() === "" && blank && select.value !== "") { select.value = ""; changed(); }
            else showSelection();
        }, 120));
        // For a page script that sets the select itself: select.pickerRefresh() shows the new value in the box.
        select.pickerRefresh = showSelection;
        select.form?.addEventListener("reset", () => setTimeout(showSelection));
        showSelection();
    }

    document.querySelectorAll("select[data-picker]").forEach(enhance);
    window.enhancePickers = root => (root || document).querySelectorAll("select[data-picker]").forEach(enhance);
})();
