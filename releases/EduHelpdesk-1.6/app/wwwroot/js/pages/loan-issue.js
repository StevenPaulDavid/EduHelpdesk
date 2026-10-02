// Issue a loan (Pages/Loans/Issue.cshtml).
(function () {
    // Picking a person clears the typed name, and vice versa, so only one borrower can be sent.
    const userSelect = document.getElementById("borrower-user");
    const nameInput = document.getElementById("borrower-name");
    userSelect.addEventListener("change", () => { if (userSelect.value) nameInput.value = ""; });
    nameInput.addEventListener("input", () => { if (nameInput.value.trim()) { userSelect.value = ""; userSelect.pickerRefresh?.(); } });

    // Show only the fields that apply. A single device is directory-only and has no notes field of its own, so both are
    // hidden for it - the server enforces the same rules either way.
    const what = document.getElementById("loan-what");
    const kitBlock = document.getElementById("kit-block");
    const assetBlock = document.getElementById("asset-block");
    const nameBlock = document.getElementById("borrower-name-block");
    const notesBlock = document.getElementById("notes-block");
    const sync = () => {
        const asset = what.value === "asset";
        kitBlock.hidden = asset;
        assetBlock.hidden = !asset;
        nameBlock.hidden = asset;
        notesBlock.hidden = asset;
        if (asset) nameInput.value = "";
    };
    what.addEventListener("change", sync);
    sync();
})();
