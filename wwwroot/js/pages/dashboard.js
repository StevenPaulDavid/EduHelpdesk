// The dashboard (Pages/Reports/Dashboard.cshtml) redraws itself every few minutes while it is on screen: it fetches
// the same address and swaps in the new #dashboard, so the page doesn't jump or lose its place. A tab in the
// background waits, and catches up as soon as it is looked at again. A failed fetch leaves the last figures showing.
// The header marks the request as a redraw, which doesn't keep the sign-in alive (Program.cs): a dashboard left open
// overnight still signs out after the usual idle hours.
(() => {
    const board = document.getElementById("dashboard");
    if (!board) return;
    const every = (Number(board.getAttribute("data-refresh-seconds")) || 300) * 1000;
    let last = Date.now();

    const refresh = async () => {
        last = Date.now();
        try {
            const response = await fetch(location.href, { headers: { "X-Requested-With": "dashboard-refresh" }, credentials: "same-origin" });
            // Signed out, or the page has gone: leave things as they are rather than show a sign-in form in its place.
            if (!response.ok || response.redirected) return;
            const fresh = new DOMParser().parseFromString(await response.text(), "text/html").getElementById("dashboard");
            const current = document.getElementById("dashboard");
            if (!fresh || !current) return;
            // Tables someone has opened stay open.
            const open = [...current.querySelectorAll("details[open]")].map(x => [...current.querySelectorAll("details")].indexOf(x));
            current.replaceChildren(...fresh.childNodes);
            const details = [...current.querySelectorAll("details")];
            open.forEach(index => { if (details[index]) details[index].open = true; });
        } catch {
            // Offline for a moment; the next turn tries again.
        }
    };

    setInterval(() => { if (!document.hidden && Date.now() - last >= every) refresh(); }, 15000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden && Date.now() - last >= every) refresh(); });
})();
