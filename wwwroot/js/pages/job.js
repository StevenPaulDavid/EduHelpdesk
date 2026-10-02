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

    // The comment button says what else it will do: a note rather than a comment, and any status change on the way.
    const status = document.getElementById("comment-status");
    const submit = document.getElementById("comment-submit");
    const internal = document.querySelector("[name=internalNote]");
    if (!status || !submit) return;
    const update = () => {
        const changed = status.value !== "" && status.value.toLowerCase() !== status.dataset.current.toLowerCase();
        const noun = internal?.checked ? "internal note" : "comment";
        submit.textContent = !changed ? "Add " + noun
            : status.value === "Closed" ? "Add " + noun + " & close ticket"
            : "Add " + noun + " & set status to " + status.value;
    };
    status.addEventListener("change", update);
    internal?.addEventListener("change", update);

    // An internal note is never shown to the requester, so there is nobody to notify: the tick goes and stays off.
    const notify = document.querySelector("[name=notifyRequester]");
    const notifyLabel = document.getElementById("notify-requester-label");
    const syncNotify = () => {
        if (!notify || !notifyLabel) return;
        if (internal?.checked) notify.checked = false;
        notifyLabel.hidden = Boolean(internal?.checked);
    };
    internal?.addEventListener("change", syncNotify);
    syncNotify();
})();
