// Pings and pop-ups for new activity, on every page of the helpdesk and the staff portal once someone is signed in
// (the layout writes the #notify-root element this reads; without it the script does nothing). Switched on per
// person and per browser from the Notifications page, and exposed to that page as window.EduNotify.
//
// How it works: while a page is open and notifications are on, it asks /notifications/poll for anything newer than the
// last one this browser handled. The server holds each request open until something arrives (up to 25 seconds), so a
// ping follows the event within a second and an idle page sends two small requests a minute. The "last handled" mark
// is kept in localStorage, so a second tab doesn't announce the same thing again, and a page opened later hears about
// what came in while it was closed.
//
// What it shows depends on the address the page was loaded from. A browser only allows desktop notifications on https
// or localhost, so anywhere else - the usual plain http://servername - it shows a banner in the page instead. The
// ping sound is made here with the Web Audio API rather than loaded from a file; a browser may refuse to play it until
// the page has been clicked, in which case the desktop notification's own sound is left on.
(function () {
    const root = document.getElementById("notify-root");
    if (!root) return;
    const { audience, account, poll: pollUrl } = root.dataset;
    const prefsKey = `edu.notify.prefs.${audience}.${account}`;
    const cursorKey = `edu.notify.cursor.${audience}.${account}`;
    // More than this at once (a browser that was closed over a weekend) is announced as a single count.
    const MAX_SEPARATE = 3;
    const MAX_BANNERS = 4;

    const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
    const write = (key, value) => { try { value === null ? localStorage.removeItem(key) : localStorage.setItem(key, value); } catch { /* private mode */ } };

    const loadPrefs = () => {
        try { return { on: false, sound: true, ...JSON.parse(read(prefsKey) || "{}") }; } catch { return { on: false, sound: true }; }
    };
    let prefs = loadPrefs();

    // ---- What this browser can do ----

    const capability = () => {
        const supported = "Notification" in window;
        const permission = supported ? Notification.permission : "unsupported";
        return { supported, secure: window.isSecureContext === true, permission, canToast: supported && window.isSecureContext === true && permission === "granted" };
    };

    // ---- The ping ----

    let audioContext = null;
    let soundBlocked = false;
    const audio = () => {
        if (audioContext) return audioContext;
        const Context = window.AudioContext || window.webkitAudioContext;
        if (!Context) return null;
        try { audioContext = new Context(); } catch { return null; }
        return audioContext;
    };
    // A click or key press lets the browser start sound, so the first one on a page sets the context going.
    ["pointerdown", "keydown", "touchstart"].forEach(type => document.addEventListener(type, () => {
        const context = audio();
        if (context && context.state === "suspended") context.resume().then(() => { soundBlocked = false; announce(); }).catch(() => { });
    }, { passive: true }));

    // Two soft notes, a fifth apart. Returns whether it could be played.
    const ping = () => {
        const context = audio();
        if (!context) { soundBlocked = true; return false; }
        if (context.state !== "running") { context.resume().catch(() => { }); soundBlocked = true; announce(); return false; }
        const start = context.currentTime;
        [[880, 0], [1318.5, 0.14]].forEach(([frequency, delay]) => {
            const oscillator = context.createOscillator();
            const gain = context.createGain();
            oscillator.type = "sine";
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0.0001, start + delay);
            gain.gain.exponentialRampToValueAtTime(0.2, start + delay + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, start + delay + 0.6);
            oscillator.connect(gain).connect(context.destination);
            oscillator.start(start + delay);
            oscillator.stop(start + delay + 0.65);
        });
        return true;
    };

    // ---- What is shown ----

    let stack = null;
    const banner = item => {
        if (!stack) {
            stack = document.createElement("div");
            stack.className = "notify-stack";
            stack.setAttribute("role", "status");
            stack.setAttribute("aria-live", "polite");
            document.body.append(stack);
        }
        const box = document.createElement("div");
        box.className = "notify-banner";
        const link = document.createElement(item.url ? "a" : "div");
        link.className = "notify-banner-text";
        if (item.url) link.href = item.url;
        const title = document.createElement("strong");
        title.textContent = item.title;
        link.append(title);
        if (item.body) { const body = document.createElement("span"); body.textContent = item.body; link.append(body); }
        const close = document.createElement("button");
        close.type = "button";
        close.className = "notify-banner-close";
        close.setAttribute("aria-label", "Dismiss");
        close.textContent = "×";
        const remove = () => box.remove();
        close.addEventListener("click", remove);
        box.append(link, close);
        stack.prepend(box);
        while (stack.children.length > MAX_BANNERS) stack.lastElementChild.remove();
        let timer = setTimeout(remove, 15000);
        box.addEventListener("mouseenter", () => clearTimeout(timer));
        box.addEventListener("mouseleave", () => { timer = setTimeout(remove, 6000); });
    };

    // silent: our own ping has played (or sound is off), so the system must not add its sound on top.
    const toast = (item, silent) => {
        try {
            const popup = new Notification(item.title, { body: item.body || "", tag: "edu-" + item.id, icon: root.dataset.icon || undefined, silent });
            popup.onclick = () => {
                window.focus();
                if (item.url) window.open(item.url, "_blank");
                popup.close();
            };
        } catch {
            // Some browsers (a phone's) only allow them through a service worker.
            banner(item);
        }
    };

    const present = items => {
        if (!items.length) return;
        const newest = items[items.length - 1];
        const shown = items.length > MAX_SEPARATE ? [{ id: newest.id, title: `${items.length} new notifications`, body: "", url: newest.url }] : items;
        const played = prefs.sound ? ping() : false;
        const canToast = capability().canToast;
        shown.forEach(item => canToast ? toast(item, played || !prefs.sound) : banner(item));
    };

    // ---- Asking the server ----

    let status = "off";
    let controller = null;
    // Increases each time the loop is (re)started, so an old loop that wakes up after being stopped knows to quit.
    let generation = 0;

    const announce = () => document.dispatchEvent(new CustomEvent("edunotify", { detail: state() }));
    const setStatus = value => { if (status !== value) { status = value; announce(); } };
    const state = () => ({ ...capability(), prefs: { ...prefs }, status, soundBlocked });

    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

    const run = async () => {
        const mine = ++generation;
        controller?.abort();
        if (!prefs.on) { setStatus("off"); return; }
        setStatus("connecting");
        let delay = 0;
        while (mine === generation) {
            controller = new AbortController();
            const cursor = read(cursorKey);
            try {
                const url = `${pollUrl}?audience=${encodeURIComponent(audience)}&after=${cursor === null ? -1 : cursor}&wait=25`;
                const response = await fetch(url, { credentials: "same-origin", cache: "no-store", signal: controller.signal });
                if (mine !== generation) return;
                if (response.status === 401) { setStatus("signed-out"); return; }
                if (!response.ok) throw new Error("HTTP " + response.status);
                const data = await response.json();
                if (mine !== generation) return;
                delay = 0;
                setStatus("connected");
                // Another tab may have handled these already: only what is newer than the shared mark is news.
                const handled = read(cursorKey);
                const floor = handled === null ? -1 : Number(handled);
                const fresh = cursor === null ? [] : data.items.filter(item => item.id > floor);
                write(cursorKey, String(Math.max(floor, data.cursor)));
                present(fresh);
            } catch (error) {
                if (mine !== generation || error.name === "AbortError") return;
                setStatus("retrying");
                delay = Math.min(60000, delay ? delay * 2 : 5000);
                await sleep(delay);
            }
        }
    };

    // Switching on starts from now rather than replaying whatever is still on file for this account.
    const setPrefs = changes => {
        const wasOn = prefs.on;
        prefs = { ...prefs, ...changes };
        write(prefsKey, JSON.stringify(prefs));
        if (prefs.on && !wasOn) write(cursorKey, null);
        if (prefs.on !== wasOn) run(); else announce();
    };

    const requestPermission = async () => {
        if (!capability().supported || !window.isSecureContext) return capability().permission;
        try { await Notification.requestPermission(); } catch { /* an older browser's callback form isn't worth supporting */ }
        announce();
        return capability().permission;
    };

    // Another tab switching it on or off, or changing a setting.
    window.addEventListener("storage", event => {
        if (event.key !== prefsKey) return;
        const wasOn = prefs.on;
        prefs = loadPrefs();
        if (prefs.on !== wasOn) run(); else announce();
    });
    // The page is going away: let the held request go rather than leave the server waiting for it.
    window.addEventListener("pagehide", () => { generation++; controller?.abort(); });
    // ...and a page brought back from the browser's back/forward cache starts asking again.
    window.addEventListener("pageshow", event => { if (event.persisted) run(); });

    window.EduNotify = { state, setPrefs, requestPermission, ping, banner, audience };
    run();
})();
