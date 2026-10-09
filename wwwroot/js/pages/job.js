// The ticket page (Pages/Job.cshtml). Dialogs, tabs, print buttons and confirms are handled by actions.js.
(function () {
    // The pencil beside a field swaps the value for that field's own small form (.inline-control in site.css).
    document.querySelectorAll("[data-edit-field]").forEach(button => button.addEventListener("click", () => {
        const name = button.dataset.editField;
        document.getElementById(name + "-value").style.display = "none";
        button.style.display = "none";
        document.getElementById(name + "-control").classList.add("visible");
    }));

    // Edit details swaps the title and description for their form, and Cancel swaps them back.
    const display = document.getElementById("ticket-content-display");
    const form = document.getElementById("ticket-content-form");
    const edit = document.getElementById("ticket-content-edit");
    const editing = on => {
        display.style.display = on ? "none" : "";
        edit.style.display = on ? "none" : "";
        form.classList.toggle("visible", on);
    };
    edit?.addEventListener("click", () => editing(true));
    document.querySelector("[data-cancel-content]")?.addEventListener("click", () => editing(false));

    // The ticket's bar sticks to the top of the screen; once it has, it gets a shadow so it reads as above the page.
    const bar = document.getElementById("ticket-bar");
    if (bar && "IntersectionObserver" in window) {
        const marker = document.createElement("div");
        marker.setAttribute("aria-hidden", "true");
        bar.before(marker);
        new IntersectionObserver(([entry]) => bar.classList.toggle("is-stuck", !entry.isIntersecting)).observe(marker);
    }

    // Dropping files anywhere on the page attaches them to the ticket, through the Attach files form. They aren't
    // shared with the requester - that is a choice made in the panel, or with Share afterwards.
    const uploadForm = document.getElementById("attachment-form");
    const overlay = document.getElementById("drop-overlay");
    if (uploadForm && overlay) {
        const input = uploadForm.querySelector("input[type=file]");
        const hasFiles = e => [...(e.dataTransfer?.types ?? [])].includes("Files");
        // Only page-wide drags show the overlay: the panel's own file box handles drops made onto it.
        const inPanel = e => e.target instanceof Element && e.target.closest(".blade-backdrop.open");
        let depth = 0;
        document.addEventListener("dragenter", e => { if (!hasFiles(e) || inPanel(e)) return; depth++; overlay.hidden = false; });
        document.addEventListener("dragleave", e => { if (!hasFiles(e) || inPanel(e)) return; depth = Math.max(0, depth - 1); if (depth === 0) overlay.hidden = true; });
        document.addEventListener("dragover", e => { if (hasFiles(e) && !inPanel(e)) e.preventDefault(); });
        document.addEventListener("drop", e => {
            if (!hasFiles(e) || inPanel(e)) return;
            e.preventDefault();
            depth = 0;
            overlay.hidden = true;
            if (!e.dataTransfer.files.length) return;
            input.files = e.dataTransfer.files;
            uploadForm.requestSubmit();
        });
    }

    // The reply box: Reply or Internal note, and the rest opens out once someone starts to write. The button says what
    // it will do - a reply or a note, and any status change on the way.
    const composer = document.getElementById("composer");
    const status = document.getElementById("comment-status");
    const submit = document.getElementById("comment-submit");
    const text = document.getElementById("ticket-comment");
    if (!composer || !status || !submit || !text) return;
    const isNote = () => composer.querySelector("[name=internalNote]:checked")?.value === "true";
    const replyPlaceholder = text.placeholder;
    if (!text.value) composer.classList.add("is-collapsed");
    const open = () => composer.classList.remove("is-collapsed");
    text.addEventListener("focus", open);

    const update = () => {
        const changed = status.value !== "" && status.value.toLowerCase() !== status.dataset.current.toLowerCase();
        const noun = isNote() ? "internal note" : "reply";
        submit.textContent = !changed ? "Add " + noun
            : status.value === "Closed" ? "Add " + noun + " & close ticket"
            : "Add " + noun + " & set status to " + status.value;
    };
    // An internal note is never shown to the requester, so there is nobody to notify: the tick goes and stays off.
    const notify = composer.querySelector("[name=notifyRequester]");
    const notifyLabel = document.getElementById("notify-requester-label");
    const syncKind = () => {
        const note = isNote();
        composer.classList.toggle("is-note", note);
        text.placeholder = note ? text.dataset.notePlaceholder : replyPlaceholder;
        if (notify && notifyLabel) {
            if (note) notify.checked = false;
            notifyLabel.hidden = note;
        }
        update();
    };
    status.addEventListener("change", update);
    composer.querySelectorAll("[name=internalNote]").forEach(radio => radio.addEventListener("change", () => { syncKind(); open(); text.focus(); }));
    syncKind();
})();
