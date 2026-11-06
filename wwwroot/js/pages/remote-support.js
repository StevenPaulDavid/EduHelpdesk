// Remote support on the ticket page (Pages/Job, HelpdeskStore.RemoteSupport): while a Quick Assist request is under way,
// ask every few seconds where it has got to and show it, so the technician knows when to expect the other screen. Once
// it has finished, the page reloads once to show the new lines in the history.
(function () {
    const block = document.getElementById("remote-support");
    if (!block || block.dataset.active !== "true") return;
    const url = block.dataset.statusUrl;
    const text = block.querySelector("[data-remote-text]");
    const badge = block.querySelector("[data-remote-badge]");
    let tries = 0;

    async function check() {
        tries++;
        try {
            const response = await fetch(url, { headers: { "Accept": "application/json" }, cache: "no-store" });
            if (!response.ok) return schedule();
            const status = await response.json();
            if (text && status.text) text.textContent = status.text;
            if (!status.active) {
                if (badge) { badge.textContent = "Finished"; badge.classList.add("badge-muted"); }
                window.location.reload();
                return;
            }
        } catch (e) { /* the network blipped: try again shortly */ }
        schedule();
    }

    // Every 3 seconds for the first ten minutes (the code's life), then every 30 while Quick Assist stays open.
    function schedule() {
        setTimeout(check, tries < 200 ? 3000 : 30000);
    }

    schedule();
})();
