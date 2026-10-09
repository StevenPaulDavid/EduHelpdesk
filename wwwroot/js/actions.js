// The pages' small behaviours, declared with data- attributes rather than onclick/onchange handlers written into the
// HTML. The content security policy refuses inline script (Services/SecurityHeaders.cs), so an injected <script> or
// onerror= would not run - and nor would a handler of our own left in a page. Loaded by every layout, including the
// print pages.
//
//   data-confirm="…"            on a form, asked before it submits; on a button, asked before its click goes through
//   data-autosubmit             on a select or input: submits its form as soon as it changes
//   data-action="print|close"   prints the page, or closes the pop-up window
//   data-open-window="url"      opens the address in a new window (the print views)
//   data-print-on-load          on <body>: opens the print dialog once the page is up
//   data-href="url"             on a table row: a click anywhere on it opens the address, apart from its own links and controls
//   data-open-blade="id"        opens a slide-in panel (.blade-backdrop); data-close-blade, a click on the backdrop or
//                               Escape closes it again. Markup: .blade[role=dialog] > .blade-head (h2 + .blade-close),
//                               then the content, with any .blade-actions last so they stay in view as it scrolls
//   data-show="id …" / data-hide="id …"   shows and hides elements, for the edit-in-place forms
//   data-describe="id"          on a select: puts the chosen option's data-description into that element
//   data-expand="selector"      opens every <details> the selector matches; data-collapse closes them
//   role="tab" + aria-controls  inside a [role=tablist]: shows that tab's panel and hides its siblings'. An address
//                               #fragment naming a panel, or anything inside one, opens that tab when the page loads
(function () {
    const ids = value => (value || "").split(/\s+/).filter(Boolean).map(id => document.getElementById(id)).filter(Boolean);

    // Slide-in panels. Opening one moves the keyboard into it (its first field, or the panel itself) and stops the page
    // behind from scrolling; Tab stays inside it while it is open; closing it puts focus back on what opened it - or,
    // when that was an item in a menu that has since folded away, on the menu's own button.
    const focusable = "a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";
    let bladeOpener = null;
    const visible = el => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length);
    const openBlade = (backdrop, opener) => {
        if (!backdrop) return;
        bladeOpener = opener;
        backdrop.classList.add("open");
        document.documentElement.classList.add("blade-open");
        const panel = backdrop.querySelector(".blade");
        const first = [...(panel?.querySelectorAll(focusable) ?? [])].find(el => visible(el) && !el.matches(".blade-close"));
        if (first) first.focus();
        else if (panel) { panel.tabIndex = -1; panel.focus(); }
    };
    const closeBlade = backdrop => {
        backdrop.classList.remove("open");
        if (document.querySelector(".blade-backdrop.open")) return;
        document.documentElement.classList.remove("blade-open");
        // An item in a folded menu still has a size, so rather than guess, try it and fall back if focus didn't move.
        const opener = bladeOpener;
        bladeOpener = null;
        opener?.focus();
        if (opener && document.activeElement !== opener) opener.closest("details")?.querySelector("summary")?.focus();
    };

    const selectTab = el => el.closest("[role=tablist]")?.querySelectorAll("[role=tab]").forEach(tab => {
        const on = tab === el;
        tab.classList.toggle("active", on);
        tab.setAttribute("aria-selected", String(on));
        const panel = document.getElementById(tab.getAttribute("aria-controls"));
        if (panel) { panel.hidden = !on; panel.classList.toggle("active", on); }
    });

    // After a form posts back to "#task-…" or "#stage-…", the thing it points at may sit in a tab that isn't the one
    // open by default: open that tab, then scroll to it, which the browser couldn't do while it was hidden.
    const target = location.hash.length > 1 ? document.getElementById(decodeURIComponent(location.hash.slice(1))) : null;
    const hiddenPanel = target?.closest("[role=tabpanel][hidden]");
    const tab = hiddenPanel && document.querySelector(`[role=tab][aria-controls="${CSS.escape(hiddenPanel.id)}"]`);
    if (tab) { selectTab(tab); target.scrollIntoView({ block: "center" }); }

    document.addEventListener("submit", e => {
        const form = e.target;
        if (form.dataset.confirm && !confirm(form.dataset.confirm)) e.preventDefault();
    });

    document.addEventListener("change", e => {
        const control = e.target;
        if (control.hasAttribute?.("data-autosubmit") && control.form) control.form.submit();
        if (control.dataset?.describe) {
            const target = document.getElementById(control.dataset.describe);
            if (target) target.textContent = control.selectedOptions[0]?.dataset.description || "";
        }
    });

    document.addEventListener("click", e => {
        // A form's own data-confirm is asked on submit, so only a button or link carrying one is looked for here.
        const el = e.target.closest(":not(form)[data-confirm], [data-action], [data-open-window], [data-open-blade], [data-close-blade], [data-show], [data-hide], [data-expand], [data-collapse], [role=tab]");
        if (el) {
            if (el.dataset.confirm && !confirm(el.dataset.confirm)) { e.preventDefault(); return; }
            if (el.dataset.action === "print") window.print();
            if (el.dataset.action === "close") window.close();
            if (el.dataset.openWindow) window.open(el.dataset.openWindow, "_blank");
            if (el.dataset.openBlade) openBlade(document.getElementById(el.dataset.openBlade), el);
            if (el.hasAttribute("data-close-blade")) { const backdrop = el.closest(".blade-backdrop"); if (backdrop) closeBlade(backdrop); }
            if (el.dataset.show || el.dataset.hide) {
                ids(el.dataset.show).forEach(x => x.hidden = false);
                ids(el.dataset.hide).forEach(x => x.hidden = true);
            }
            if (el.dataset.expand) document.querySelectorAll(el.dataset.expand).forEach(d => d.open = true);
            if (el.dataset.collapse) document.querySelectorAll(el.dataset.collapse).forEach(d => d.open = false);
            if (el.getAttribute("role") === "tab") selectTab(el);
            return;
        }
        // The backdrop itself, not anything inside the panel.
        if (e.target.classList?.contains("blade-backdrop")) { closeBlade(e.target); return; }
        const row = e.target.closest("tr[data-href]");
        if (row && !e.target.closest("a, button, input, select, textarea, label")) window.location.href = row.dataset.href;
    });

    document.addEventListener("keydown", e => {
        const open = document.querySelector(".blade-backdrop.open");
        if (!open) return;
        if (e.key === "Escape") { closeBlade(open); return; }
        if (e.key === "Tab") {
            const items = [...open.querySelectorAll(focusable)].filter(visible);
            if (items.length === 0) { e.preventDefault(); return; }
            const first = items[0], last = items[items.length - 1];
            if (e.shiftKey && (document.activeElement === first || !open.contains(document.activeElement))) { e.preventDefault(); last.focus(); }
            else if (!e.shiftKey && (document.activeElement === last || !open.contains(document.activeElement))) { e.preventDefault(); first.focus(); }
        }
    });

    if (document.body?.hasAttribute("data-print-on-load")) window.addEventListener("load", () => window.print());
})();
