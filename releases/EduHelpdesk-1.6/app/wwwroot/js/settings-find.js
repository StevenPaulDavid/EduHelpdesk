// Settings index: typing in "Find a setting" narrows the cards to those whose title, description or keywords contain
// every word typed, and hides a group left with none. Without JavaScript, every card simply stays on show.
(function () {
    const box = document.getElementById("settings-find");
    if (!box) return;
    const none = document.getElementById("settings-find-none");
    const cards = [...document.querySelectorAll(".settings-card")];
    const text = new Map(cards.map(card => [card, (card.textContent + " " + (card.dataset.keywords || "")).toLowerCase()]));

    function filter() {
        const words = box.value.toLowerCase().split(/\s+/).filter(Boolean);
        let shown = 0;
        for (const card of cards) {
            const match = words.every(w => text.get(card).includes(w));
            card.hidden = !match;
            if (match) shown++;
        }
        document.querySelectorAll(".settings-group").forEach(group => {
            group.hidden = !group.querySelector(".settings-card:not([hidden])");
        });
        none.hidden = shown > 0;
    }

    box.addEventListener("input", filter);
    // Enter opens the only match, so "logo" then Enter goes straight to Branding & logo.
    box.addEventListener("keydown", e => {
        if (e.key !== "Enter") return;
        e.preventDefault();
        const visible = cards.filter(card => !card.hidden && card.tagName === "A");
        if (visible.length === 1) window.location.href = visible[0].href;
    });
    box.addEventListener("keydown", e => { if (e.key === "Escape") { box.value = ""; filter(); } });
})();
