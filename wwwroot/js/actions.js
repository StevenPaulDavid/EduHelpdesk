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
//                               Escape closes it again
//   data-show="id …" / data-hide="id …"   shows and hides elements, for the edit-in-place forms
//   data-describe="id"          on a select: puts the chosen option's data-description into that element
//   data-expand="selector"      opens every <details> the selector matches; data-collapse closes them
//   role="tab" + aria-controls  inside a [role=tablist]: shows that tab's panel and hides its siblings'
(function () {
    const ids = value => (value || "").split(/\s+/).filter(Boolean).map(id => document.getElementById(id)).filter(Boolean);

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
            if (el.dataset.openBlade) document.getElementById(el.dataset.openBlade)?.classList.add("open");
            if (el.hasAttribute("data-close-blade")) el.closest(".blade-backdrop")?.classList.remove("open");
            if (el.dataset.show || el.dataset.hide) {
                ids(el.dataset.show).forEach(x => x.hidden = false);
                ids(el.dataset.hide).forEach(x => x.hidden = true);
            }
            if (el.dataset.expand) document.querySelectorAll(el.dataset.expand).forEach(d => d.open = true);
            if (el.dataset.collapse) document.querySelectorAll(el.dataset.collapse).forEach(d => d.open = false);
            if (el.getAttribute("role") === "tab") {
                el.closest("[role=tablist]")?.querySelectorAll("[role=tab]").forEach(tab => {
                    const on = tab === el;
                    tab.classList.toggle("active", on);
                    tab.setAttribute("aria-selected", String(on));
                    const panel = document.getElementById(tab.getAttribute("aria-controls"));
                    if (panel) { panel.hidden = !on; panel.classList.toggle("active", on); }
                });
            }
            return;
        }
        // The backdrop itself, not anything inside the panel.
        if (e.target.classList?.contains("blade-backdrop")) { e.target.classList.remove("open"); return; }
        const row = e.target.closest("tr[data-href]");
        if (row && !e.target.closest("a, button, input, select, textarea, label")) window.location.href = row.dataset.href;
    });

    document.addEventListener("keydown", e => {
        if (e.key === "Escape") document.querySelectorAll(".blade-backdrop.open").forEach(b => b.classList.remove("open"));
    });

    if (document.body?.hasAttribute("data-print-on-load")) window.addEventListener("load", () => window.print());
})();
