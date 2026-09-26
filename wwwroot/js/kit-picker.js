// The kit editor's asset picker. Only the kit's own contents are on the page; everything else is found by searching,
// because a register of a thousand-plus assets cannot be scrolled as a checklist. Each chosen asset is a row carrying a
// hidden assetIds input, so the form posts exactly what the list shows. The server re-checks every addition.
function initKitPicker(root) {
    const search = root.querySelector("#kit-search");
    const contents = root.querySelector("#kit-contents");
    const results = root.querySelector("#kit-results");
    const hint = root.querySelector("#kit-search-hint");
    const empty = root.querySelector("#kit-empty");
    const count = root.querySelector("#kit-count");
    if (!search) return; // read-only while the kit is out on loan
    const defaultHint = hint.textContent;
    const url = new URL(root.dataset.searchUrl, window.location.href);
    let matches = [];
    let active = -1;
    let timer = null;
    let latest = 0;

    const chosen = () => new Set(Array.from(contents.querySelectorAll("input[name=assetIds]"), x => x.value));

    function refreshCount() {
        const n = contents.children.length;
        count.textContent = n;
        empty.hidden = n > 0;
    }

    function close() {
        results.hidden = true;
        results.replaceChildren();
        search.setAttribute("aria-expanded", "false");
        search.removeAttribute("aria-activedescendant");
        matches = [];
        active = -1;
    }

    function add(asset) {
        const row = document.createElement("li");
        const input = Object.assign(document.createElement("input"), { type: "hidden", name: "assetIds", value: asset.id });
        const label = document.createElement("span");
        const link = Object.assign(document.createElement("a"), { href: asset.url });
        link.appendChild(Object.assign(document.createElement("strong"), { textContent: asset.tag }));
        label.append(link, " ", Object.assign(document.createElement("span"), { className: "muted", textContent: asset.detail }));
        const remove = Object.assign(document.createElement("button"), { type: "button", className: "button button-secondary kit-remove", textContent: "Remove" });
        remove.setAttribute("aria-label", "Remove " + asset.tag);
        row.append(input, label, remove);
        contents.appendChild(row);
        refreshCount();
        search.value = "";
        hint.textContent = asset.tag + " added. Save the kit to keep the change.";
        close();
        search.focus();
    }

    function highlight(index) {
        active = index;
        Array.from(results.children).forEach((el, i) => el.classList.toggle("is-active", i === index));
        if (index >= 0) {
            search.setAttribute("aria-activedescendant", results.children[index].id);
            results.children[index].scrollIntoView({ block: "nearest" });
        }
    }

    function render(found, limit) {
        const already = chosen();
        matches = found.filter(x => !already.has(String(x.id))).slice(0, limit);
        results.replaceChildren(...matches.map((asset, i) => {
            const item = document.createElement("li");
            item.id = "kit-result-" + i;
            item.setAttribute("role", "option");
            item.append(Object.assign(document.createElement("strong"), { textContent: asset.tag }), " ",
                Object.assign(document.createElement("span"), { className: "muted", textContent: asset.detail }));
            // mousedown rather than click, so the search box does not lose focus and close the list first.
            item.addEventListener("mousedown", e => { e.preventDefault(); add(asset); });
            return item;
        }));
        results.hidden = matches.length === 0;
        search.setAttribute("aria-expanded", String(matches.length > 0));
        highlight(matches.length > 0 ? 0 : -1);
        hint.textContent = matches.length === 0 ? "No assets match that can go in a kit."
            : found.length > limit ? `Showing the first ${limit}. Keep typing to narrow it down.`
            : defaultHint;
    }

    async function lookup() {
        const q = search.value.trim();
        if (!q) { close(); hint.textContent = defaultHint; return; }
        const ticket = ++latest;
        url.searchParams.set("q", q);
        try {
            const response = await fetch(url, { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(response.status);
            const found = await response.json();
            if (ticket === latest) render(found, 20); // ignore answers that arrive after a newer search
        } catch {
            if (ticket === latest) { close(); hint.textContent = "The search didn't work. Reload the page and try again."; }
        }
    }

    search.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(lookup, 200); });
    search.addEventListener("keydown", e => {
        if (e.key === "ArrowDown" && matches.length) { e.preventDefault(); highlight((active + 1) % matches.length); }
        else if (e.key === "ArrowUp" && matches.length) { e.preventDefault(); highlight((active - 1 + matches.length) % matches.length); }
        // Enter picks the highlighted asset. It must never submit the form from here - a half-typed search is not a save.
        else if (e.key === "Enter") { e.preventDefault(); if (active >= 0) add(matches[active]); }
        else if (e.key === "Escape") { close(); }
    });
    search.addEventListener("blur", () => setTimeout(close, 150));
    search.addEventListener("focus", () => { if (search.value.trim()) lookup(); });

    contents.addEventListener("click", e => {
        const button = e.target.closest(".kit-remove");
        if (!button) return;
        const row = button.closest("li");
        const tag = row.querySelector("strong")?.textContent ?? "Asset";
        row.remove();
        refreshCount();
        hint.textContent = tag + " removed. Save the kit to keep the change.";
    });
}

const kitPicker = document.getElementById("kit-picker");
if (kitPicker) initKitPicker(kitPicker);
