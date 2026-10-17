// The tooltip for the charts (Pages/Shared/Charts.cs): any mark with data-tip shows it beside the pointer, or beside
// the mark when it has the keyboard focus. "|" starts a new line; the first line is bold. Listening on the document
// means charts redrawn in place (the dashboard's refresh) need nothing doing again.
(() => {
    let tip = null;
    const show = (mark, x, y) => {
        const text = mark.getAttribute("data-tip");
        if (!text) return;
        if (!tip) {
            tip = document.createElement("div");
            tip.className = "chart-tip";
            tip.setAttribute("role", "tooltip");
            document.body.appendChild(tip);
        }
        const [first, ...rest] = text.split("|");
        tip.replaceChildren();
        const head = document.createElement("strong");
        head.textContent = first;
        tip.appendChild(head);
        rest.forEach(line => tip.appendChild(Object.assign(document.createElement("div"), { textContent: line })));
        tip.hidden = false;
        // Above and to the right of the point, kept inside the window.
        const box = tip.getBoundingClientRect();
        const left = Math.min(x + 14, window.innerWidth - box.width - 8);
        const top = y - box.height - 12 < 8 ? y + 18 : y - box.height - 12;
        tip.style.left = Math.max(8, left) + "px";
        tip.style.top = top + "px";
    };
    const hide = () => { if (tip) tip.hidden = true; };

    document.addEventListener("pointermove", e => {
        const mark = e.target instanceof Element ? e.target.closest("[data-tip]") : null;
        if (mark) show(mark, e.clientX, e.clientY); else hide();
    }, { passive: true });
    document.addEventListener("focusin", e => {
        const mark = e.target instanceof Element ? e.target.closest("[data-tip]") : null;
        if (!mark) { hide(); return; }
        const box = mark.getBoundingClientRect();
        show(mark, box.left + box.width / 2, box.top);
    });
    document.addEventListener("focusout", hide);
    window.addEventListener("scroll", hide, { passive: true });
})();
