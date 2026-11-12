// /Jobs?show=planner: putting a ticket on a day by dragging its card there, or with its Plan menu. A week's cells also
// say whose row they are, so a drop there gives the ticket to that person; the "Not planned" list takes a ticket off the
// plan. Either way the change is posted to the page's Plan handler, which applies the ticket page's rules and says why
// when it can't be done. The page is drawn again afterwards, so the planner always shows what was saved. Without this
// script the Plan menu still works as an ordinary form.
(function () {
    const planner = document.getElementById("ticket-planner");
    if (!planner || planner.dataset.canEdit !== "true") return;
    const notice = document.getElementById("planner-notice");
    const token = () => planner.querySelector("input[name=__RequestVerificationToken]")?.value || "";

    function say(text) {
        if (!notice) return;
        notice.textContent = text;
        notice.hidden = !text;
        if (text) notice.scrollIntoView({ block: "nearest" });
    }

    // fields: planFor and tech, or clear.
    async function plan(card, fields) {
        const body = new URLSearchParams({ number: card.dataset.number, __RequestVerificationToken: token(), ...fields });
        planner.classList.add("board-busy");
        let result;
        try {
            const response = await fetch(planner.dataset.planUrl, { method: "POST", body, headers: { "X-Requested-With": "fetch" }, credentials: "same-origin" });
            if (!response.ok) throw new Error(String(response.status));
            result = await response.json();
        } catch {
            planner.classList.remove("board-busy");
            say("That didn't save. Check you're still signed in, then try again.");
            return;
        }
        if (result.ok) {
            // Kept for the redrawn page to show, the way a form's message would be, along with where it was scrolled to.
            try {
                sessionStorage.setItem("planner-message", result.message || "");
                sessionStorage.setItem("planner-scroll", String(window.scrollY));
            } catch { /* private browsing */ }
            location.reload();
            return;
        }
        planner.classList.remove("board-busy");
        say(result.message || "That ticket couldn't be planned.");
    }

    try {
        const kept = sessionStorage.getItem("planner-message");
        const scroll = sessionStorage.getItem("planner-scroll");
        sessionStorage.removeItem("planner-message");
        sessionStorage.removeItem("planner-scroll");
        if (scroll) window.scrollTo(0, parseInt(scroll, 10) || 0);
        if (kept) { notice.textContent = kept; notice.hidden = false; }
    } catch { /* private browsing */ }

    // The Plan menu. Which button was pressed says whether it's a day or "Take off the plan".
    planner.addEventListener("submit", event => {
        const form = event.target.closest(".plan-form");
        if (!form) return;
        event.preventDefault();
        const card = form.closest(".plan-card");
        if (event.submitter?.name === "clear") { plan(card, { clear: "true" }); return; }
        if (!form.elements.planFor.value) { say("Choose a day to plan it for."); return; }
        plan(card, { planFor: form.elements.planFor.value, tech: form.elements.tech.value });
    });

    // Dragging.
    let dragged = null;
    const zoneOf = element => element?.closest?.(".plan-drop");
    const clearDrop = () => planner.querySelectorAll(".board-drop").forEach(x => x.classList.remove("board-drop"));

    planner.addEventListener("dragstart", event => {
        const card = event.target.closest?.(".plan-card");
        if (!card || card.getAttribute("draggable") !== "true") return;
        dragged = card;
        card.classList.add("board-dragging");
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", card.dataset.ref || card.dataset.number);
    });
    planner.addEventListener("dragend", () => {
        dragged?.classList.remove("board-dragging");
        dragged = null;
        clearDrop();
    });
    planner.addEventListener("dragover", event => {
        const zone = zoneOf(event.target);
        if (!dragged || !zone) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        if (!zone.classList.contains("board-drop")) { clearDrop(); zone.classList.add("board-drop"); }
    });
    planner.addEventListener("drop", event => {
        const zone = zoneOf(event.target);
        if (!dragged || !zone) return;
        event.preventDefault();
        const card = dragged;
        clearDrop();
        if (zone === zoneOf(card)) return;
        if (zone.dataset.planClear) { plan(card, { clear: "true" }); return; }
        const fields = { planFor: zone.dataset.planDay };
        if (zone.dataset.planTech) fields.tech = zone.dataset.planTech;
        plan(card, fields);
    });
})();
