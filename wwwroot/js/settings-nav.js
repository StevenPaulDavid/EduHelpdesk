// The settings menu down the left of every settings page (Pages/Shared/_SettingsLayout.cshtml).
(function () {
    const menu = document.getElementById("settings-nav");
    if (!menu) return;

    // On a phone or a narrow window the menu folds to one line naming the page, and opens when tapped. It is drawn open,
    // so without script it is simply there.
    const narrow = window.matchMedia("(max-width: 900px)");
    const fit = () => { menu.open = !narrow.matches; };
    fit();
    narrow.addEventListener("change", fit);
    // On a wide screen it is always open: its summary is hidden there, so it can't be folded by accident.
    menu.addEventListener("toggle", () => { if (!narrow.matches && !menu.open) menu.open = true; });

    // A long menu scrolls inside itself; start it with the current page in view rather than at the top.
    const here = menu.querySelector("a[aria-current=page]");
    if (here && menu.open && menu.scrollHeight > menu.clientHeight) {
        menu.scrollTop = Math.max(0, here.offsetTop - menu.clientHeight / 3);
    }

    // Find a setting: hides the entries (and whole groups) that don't match, by title or keyword.
    const find = document.getElementById("settings-nav-find");
    const none = menu.querySelector(".settings-nav-none");
    find?.addEventListener("input", () => {
        const words = find.value.toLowerCase().split(/\s+/).filter(Boolean);
        let shown = 0;
        menu.querySelectorAll(".settings-nav-group").forEach(group => {
            let any = false;
            group.querySelectorAll("li").forEach(li => {
                const text = (li.querySelector("a")?.dataset.keywords || "").toLowerCase();
                const hit = words.every(w => text.includes(w));
                li.hidden = !hit;
                any ||= hit;
                if (hit) shown++;
            });
            group.hidden = !any;
        });
        if (none) none.hidden = shown > 0;
    });
    // Enter opens the first match.
    find?.addEventListener("keydown", e => {
        if (e.key !== "Enter") return;
        const first = [...menu.querySelectorAll("li:not([hidden]) a")][0];
        if (first) { e.preventDefault(); first.click(); }
    });
})();
