// Help on every page (Pages/Shared/HelpTipTagHelper.cs, _Layout.cshtml):
// - The ? beside a field opens its small explanation; Esc, a click elsewhere or opening another one closes it.
//   The marks sit inside <label>s, so their clicks are kept from reaching the label (which would focus the input or
//   tick a checkbox).
// - The page's Help link moves from the top of the page into its heading, as a small ? after the title.
(() => {
    let open = null;

    function close(restoreFocus) {
        if (!open) return;
        const mark = open;
        open = null;
        mark.setAttribute('aria-expanded', 'false');
        const pop = document.getElementById(mark.getAttribute('aria-controls'));
        if (pop) { pop.hidden = true; pop.classList.remove('help-tip-left'); }
        if (restoreFocus) mark.focus();
    }

    function show(mark) {
        if (open === mark) { close(false); return; }
        close(false);
        const pop = document.getElementById(mark.getAttribute('aria-controls'));
        if (!pop) return;
        pop.hidden = false;
        mark.setAttribute('aria-expanded', 'true');
        open = mark;
        // Keep it on screen: open to the left when it would run off the right-hand edge.
        if (pop.getBoundingClientRect().right > document.documentElement.clientWidth - 8) pop.classList.add('help-tip-left');
    }

    document.addEventListener('click', event => {
        const mark = event.target.closest('.help-tip-mark[aria-controls]');
        if (mark) { event.preventDefault(); event.stopPropagation(); show(mark); return; }
        const pop = event.target.closest('.help-tip-pop');
        if (pop) { if (!event.target.closest('a')) event.preventDefault(); return; }
        close(false);
    }, true);

    document.addEventListener('keydown', event => {
        const mark = event.target.closest && event.target.closest('.help-tip-mark[aria-controls]');
        if (mark && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); show(mark); return; }
        if (event.key === 'Escape' && open) { event.preventDefault(); close(true); }
    });

    document.addEventListener('DOMContentLoaded', () => {
        const link = document.getElementById('page-help-link');
        const heading = document.querySelector('main .page-heading h1, main .hero h1, main h1');
        if (link && heading) {
            heading.append(link);
            link.classList.add('page-help-link-placed');
        }
    });
})();
