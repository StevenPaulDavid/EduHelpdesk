// Behaviour shared by every page that uses the main layout. Loaded with defer, so the page has been parsed by the
// time this runs.
(function () {
    // Only one menu open at a time - header menus and page action menus alike - and clicking away, choosing an
    // item, or pressing Escape closes them.
    const menus = [...document.querySelectorAll("details.nav-menu")];
    menus.forEach(m => m.addEventListener("toggle", () => {
        if (m.open) menus.filter(o => o !== m).forEach(o => o.open = false);
    }));
    document.addEventListener("click", e => {
        menus.forEach(m => { if (!m.contains(e.target) || e.target.closest(".nav-panel button, .nav-panel a")) m.open = false; });
    });
    document.addEventListener("keydown", e => {
        if (e.key === "Escape") menus.forEach(m => m.open = false);
    });

    // Lists that turn into cards on a phone (table.stack-table, site.css). Each cell is labelled with its column's
    // heading so the card can say what each value is; a heading marked data-stack="head" becomes the card's title and
    // "check" its tick box. Only a labelled table is restacked, so without this the table simply scrolls sideways.
    document.querySelectorAll("table.stack-table").forEach(table => {
        const heads = [...table.querySelectorAll("thead th")].map(th => ({
            label: th.textContent.replace(/[▲▼]/g, "").replace(/\s+/g, " ").trim(),
            role: th.dataset.stack
        }));
        table.querySelectorAll("tbody tr").forEach(row => {
            let column = 0;
            [...row.cells].forEach(cell => {
                const head = heads[column];
                column += cell.colSpan;
                if (!head || cell.colSpan > 1) return;
                if (head.role) cell.classList.add("stack-" + head.role);
                else if (head.label && !cell.hasAttribute("data-label")) cell.setAttribute("data-label", head.label);
            });
        });
        table.classList.add("is-labelled");
    });

    // The phone-width Menu button, which shows and hides the nav and account menu under the header bar. It only
    // appears below tablet width (site.css); on a wider screen they are always in the bar.
    const topbar = document.querySelector(".topbar");
    const toggle = document.querySelector(".menu-toggle");
    if (!topbar || !toggle) return;
    const setOpen = open => {
        topbar.classList.toggle("menu-open", open);
        toggle.setAttribute("aria-expanded", String(open));
    };
    toggle.addEventListener("click", () => setOpen(!topbar.classList.contains("menu-open")));
    document.addEventListener("click", e => { if (!topbar.contains(e.target)) setOpen(false); });
    document.addEventListener("keydown", e => {
        if (e.key === "Escape" && topbar.classList.contains("menu-open")) { setOpen(false); toggle.focus(); }
    });
})();
