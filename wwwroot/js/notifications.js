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
    // Settings → Notifications can switch pings off for everyone or for one side, and can stop the reminder below.
    const enabled = root.dataset.enabled !== "false";
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

    const announce = () => { document.dispatchEvent(new CustomEvent("edunotify", { detail: state() })); syncNudge(); };
    const setStatus = value => { if (status !== value) { status = value; announce(); } };
    const state = () => ({ ...capability(), enabled, prefs: { ...prefs }, status, soundBlocked, asked: askedResult });

    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

    const run = async () => {
        const mine = ++generation;
        controller?.abort();
        if (!enabled) { setStatus("disabled"); return; }
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
                // Switched off while this page was open. It stays off until the page is next loaded.
                if (data.disabled) { setStatus("disabled"); return; }
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

    // What the browser answered the last time it was asked on this page. A browser that decides not to show its question
    // (Edge's quiet prompt, a page with a certificate warning, one dismissed too often) answers "default" or "denied" at
    // once, and without this the page would go on saying it hasn't been asked.
    // Kept for the browser session, so the page the reminder links to knows too.
    const askedKey = `edu.notify.asked.${audience}.${account}`;
    const readAsked = () => { try { return sessionStorage.getItem(askedKey); } catch { return null; } };
    const writeAsked = value => { try { value === "granted" ? sessionStorage.removeItem(askedKey) : sessionStorage.setItem(askedKey, value); } catch { /* private mode */ } };
    let askedResult = readAsked();
    const requestPermission = async () => {
        if (!capability().supported || !window.isSecureContext) return capability().permission;
        try { askedResult = await Notification.requestPermission(); } catch { askedResult = "error"; }
        writeAsked(askedResult);
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

    // ---- The reminder ----

    // A slim bar at the top of the page content for someone who hasn't switched notifications on in this browser - or has, but the
    // browser hasn't been allowed to show pop-ups. "Not now" hides it for a week. Not shown where it would be redundant
    // (the Notifications page itself), when this side is switched off, or when Settings has turned the reminder off.
    const nudgeKey = `edu.notify.nudge.${audience}.${account}`;
    const WEEK = 7 * 24 * 60 * 60 * 1000;
    let nudgeBar = null;
    const nudgeWanted = () => {
        if (!enabled || root.dataset.prompt !== "true" || document.getElementById("notify-on")) return null;
        const s = capability();
        if (!prefs.on) return "on";
        // Asked and still not granted means the browser won't show its question, so another press of the button is no use.
        if (s.supported && s.secure && s.permission !== "granted") return s.permission === "denied" || (askedResult && askedResult !== "granted") ? "blocked" : "allow";
        return null;
    };
    function syncNudge() {
        const wanted = nudgeWanted();
        const dismissed = Number(read(nudgeKey)) > Date.now();
        if (!wanted || dismissed) { nudgeBar?.remove(); nudgeBar = null; return; }
        if (nudgeBar?.dataset.kind === wanted) return;
        nudgeBar?.remove();
        const bar = document.createElement("div");
        bar.className = "notify-nudge";
        bar.dataset.kind = wanted;
        bar.setAttribute("role", "region");
        bar.setAttribute("aria-label", "Notifications");
        const text = document.createElement("span");
        const who = audience === "portal" ? "when the IT team updates one of your tickets" : "when a ticket arrives or someone replies";
        text.textContent = wanted === "on" ? `Notifications are off. Switch them on to get a ping and a pop-up on this computer ${who}.`
            : wanted === "allow" ? "Notifications are on, but this browser hasn't been allowed to show pop-ups yet."
            : "Notifications are on, but this browser is blocking pop-ups from this site.";
        const buttons = document.createElement("span");
        buttons.className = "notify-nudge-actions";
        const act = wanted === "blocked" ? document.createElement("a") : document.createElement("button");
        act.className = "button button-primary";
        act.textContent = wanted === "on" ? "Turn on" : wanted === "allow" ? "Allow pop-ups" : "How to allow them";
        if (wanted === "blocked") act.href = root.dataset.settings;
        else {
            act.type = "button";
            act.addEventListener("click", () => {
                if (wanted === "on") setPrefs({ on: true });
                if (capability().permission === "default") requestPermission().then(announce);
                announce();
            });
        }
        const later = document.createElement("button");
        later.type = "button";
        later.className = "button button-secondary";
        later.textContent = "Not now";
        later.addEventListener("click", () => { write(nudgeKey, String(Date.now() + WEEK)); syncNudge(); });
        buttons.append(act, later);
        bar.append(text, buttons);
        // Inside the portal's narrow column when the page has one, so the bar lines up with what is under it.
        (document.querySelector("main .portal-shell") || document.querySelector("main"))?.prepend(bar);
        nudgeBar = bar;
    }

    window.EduNotify = { state, setPrefs, requestPermission, ping, banner, audience };
    run();
    syncNudge();
})();
