// /Jobs?show=board: moving a card to another column, by dragging it or with its Move menu. Either way the move is posted
// to the page's Move handler, which applies the same rules as the ticket page and says why when it can't be done. The
// page is drawn again after a move, so the board always shows what was saved. Without this script the Move menu still
// works as an ordinary form.
(function () {
    const board = document.getElementById("ticket-board");
    if (!board || board.dataset.canEdit !== "true") return;
    const notice = document.getElementById("board-notice");
    const dialog = document.getElementById("board-close-dialog");
    const token = () => board.querySelector("input[name=__RequestVerificationToken]")?.value || "";

    function say(text) {
        if (!notice) return;
        notice.textContent = text;
        notice.hidden = !text;
        if (text) notice.scrollIntoView({ block: "nearest" });
    }

    // Asks for the closing message; resolves to it, or to null when the box is cancelled.
    function askForMessage(reference) {
        return new Promise(resolve => {
            if (!dialog || typeof dialog.showModal !== "function") { resolve(null); return; }
            const form = document.getElementById("board-close-form");
            const box = document.getElementById("board-close-message");
            document.getElementById("board-close-ref").textContent = reference;
            box.value = "";
            let answer = null;
            const submitted = () => { answer = box.value.trim() || null; };
            const cancel = () => dialog.close();
            const closed = () => {
                form.removeEventListener("submit", submitted);
                document.getElementById("board-close-cancel").removeEventListener("click", cancel);
                resolve(answer);
            };
            form.addEventListener("submit", submitted);
            document.getElementById("board-close-cancel").addEventListener("click", cancel);
            dialog.addEventListener("close", closed, { once: true });
            dialog.showModal();
            box.focus();
        });
    }

    async function move(card, target, closingMessage) {
        const body = new URLSearchParams({ number: card.dataset.number, target, __RequestVerificationToken: token() });
        if (closingMessage) body.set("closingMessage", closingMessage);
        board.classList.add("board-busy");
        let result;
        try {
            const response = await fetch(board.dataset.moveUrl, { method: "POST", body, headers: { "X-Requested-With": "fetch" }, credentials: "same-origin" });
            if (!response.ok) throw new Error(String(response.status));
            result = await response.json();
        } catch {
            board.classList.remove("board-busy");
            say("That didn't save. Check you're still signed in, then try again.");
            return;
        }
        if (result.ok) {
            // Kept for the redrawn page to show, the way a form's message would be.
            try { sessionStorage.setItem("board-message", result.message || ""); } catch { /* private browsing */ }
            location.reload();
            return;
        }
        board.classList.remove("board-busy");
        if (result.needsMessage && !closingMessage) {
            const message = await askForMessage(card.dataset.ref || "ticket");
            if (message) await move(card, target, message);
            return;
        }
        say(result.message || "That ticket couldn't be moved.");
    }

    try {
        const kept = sessionStorage.getItem("board-message");
        if (kept) { sessionStorage.removeItem("board-message"); say(kept); }
    } catch { /* private browsing */ }

    // The Move menu.
    board.addEventListener("submit", event => {
        const form = event.target.closest(".board-move-form");
        if (!form) return;
        event.preventDefault();
        const card = form.closest(".board-card");
        const target = form.elements.target.value;
        if (target === card.closest("[data-board-column]").dataset.boardColumn) { form.closest("details").open = false; return; }
        move(card, target);
    });

    // Dragging.
    let dragged = null;
    const columnOf = element => element?.closest?.("[data-board-column]");
    const clearDrop = () => board.querySelectorAll(".board-drop").forEach(x => x.classList.remove("board-drop"));

    board.addEventListener("dragstart", event => {
        const card = event.target.closest?.(".board-card");
        if (!card) return;
        dragged = card;
        card.classList.add("board-dragging");
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", card.dataset.ref || card.dataset.number);
    });
    board.addEventListener("dragend", () => {
        dragged?.classList.remove("board-dragging");
        dragged = null;
        clearDrop();
    });
    board.addEventListener("dragover", event => {
        const column = columnOf(event.target);
        if (!dragged || !column) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        if (!column.classList.contains("board-drop")) { clearDrop(); column.classList.add("board-drop"); }
    });
    board.addEventListener("drop", event => {
        const column = columnOf(event.target);
        if (!dragged || !column) return;
        event.preventDefault();
        const card = dragged;
        clearDrop();
        if (column === columnOf(card)) return;
        move(card, column.dataset.boardColumn);
    });
})();
