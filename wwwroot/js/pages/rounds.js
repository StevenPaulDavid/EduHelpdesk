// /Rounds: "Tick all done" ticks every room's Done box on its round, for the usual day when the whole sheet comes back done.
document.addEventListener('click', event => {
    const button = event.target.closest('[data-all-done]');
    if (!button) return;
    button.closest('form').querySelectorAll('input[type=checkbox][name$=".Done"]').forEach(box => { box.checked = true; });
});