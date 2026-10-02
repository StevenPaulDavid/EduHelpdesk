// The Notifications page (Pages/Notifications, Pages/Portal/Notifications, Pages/Shared/_NotificationSettings). What the
// choices are saved in, and the asking and showing, is wwwroot/js/notifications.js; this only drives the controls.
(function () {
    const api = window.EduNotify;
    const el = id => document.getElementById(id);
    const on = el("notify-on");
    if (!api || !on) return;
    const sound = el("notify-sound");
    const allow = el("notify-allow");
    const mode = el("notify-mode");
    const soundNote = el("notify-sound-note");
    const connection = el("notify-connection");
    const help = el("notify-help");
    const permissionText = el("notify-permission");
    const testForm = el("notify-test-form");
    const testHint = el("notify-test-hint");
    const testNote = el("notify-test-note");

    const connectionText = {
        off: "Off",
        disabled: "Switched off by your administrator",
        connecting: "Connecting…",
        connected: "On - listening while a helpdesk page is open",
        retrying: "Can't reach the helpdesk just now - trying again",
        "signed-out": "You've been signed out - sign in again to carry on"
    };

    const render = () => {
        const s = api.state();
        on.checked = s.prefs.on;
        sound.checked = s.prefs.sound;
        // Switched off for this side in Settings → Notifications: nothing here can work, so nothing here is offered.
        on.disabled = !s.enabled;
        sound.disabled = !s.enabled || !s.prefs.on;
        testForm.querySelector("button").disabled = !s.enabled || !s.prefs.on;
        testHint.hidden = s.prefs.on || !s.enabled;
        connection.textContent = connectionText[s.status] || s.status;
        soundNote.hidden = !(s.prefs.on && s.prefs.sound && s.soundBlocked);

        let text, tone = "notice";
        if (!s.enabled) {
            tone = "notice notice-warning";
            text = "Notifications have been switched off by your school's administrator, so there's nothing to switch on here at the moment.";
        } else if (!s.supported || !s.secure) {
            tone = "notice notice-warning";
            text = !s.secure
                ? "This page was opened on an address your browser doesn't treat as secure (http:// rather than https://), so it won't allow pop-up desktop notifications. You'll get a ping and a banner at the top of the page instead."
                : "This browser can't show desktop notifications, so you'll get a ping and a banner at the top of the page instead.";
        } else if (s.permission === "granted") {
            text = "Desktop notifications are allowed in this browser, so you'll get a pop-up in the corner of the screen.";
        } else if (s.permission === "denied") {
            tone = "notice notice-warning";
            text = "This browser is blocking notifications from this site. Allow them from the site settings next to the address bar (the padlock) and reload; until then you'll get a ping and a banner at the top of the page.";
        } else if (s.asked) {
            tone = "notice notice-warning";
            text = "The browser didn't show its question, or it was dismissed, so pop-ups are still not allowed. Follow the steps below; until then you'll get a ping and a banner at the top of the page.";
        } else {
            text = "Your browser hasn't been asked yet. Press the button below to allow desktop pop-ups; until you do, you'll get a ping and a banner at the top of the page.";
        }
        mode.className = tone;
        mode.textContent = text;
        allow.hidden = !(s.enabled && s.supported && s.secure && s.permission === "default" && !s.asked);
        // The browser's own setting for this site, and - once it has declined to ask or been refused - how to change it.
        permissionText.textContent = !s.supported ? "not available in this browser" : !s.secure ? "not available on this address"
            : s.permission === "granted" ? "Allowed" : s.permission === "denied" ? "Blocked" : s.asked ? "Not allowed (the browser did not ask, or it was dismissed)" : "Not asked yet";
        help.hidden = !(s.enabled && s.supported && s.secure && s.permission !== "granted" && (s.permission === "denied" || s.asked));
    };

    on.addEventListener("change", () => {
        api.setPrefs({ on: on.checked });
        // Asked now, from the click, because a browser only shows its permission prompt in answer to one.
        if (on.checked && api.state().permission === "default") api.requestPermission();
    });
    sound.addEventListener("change", () => {
        api.setPrefs({ sound: sound.checked });
        if (sound.checked) api.ping();
    });
    allow.addEventListener("click", () => api.requestPermission());

    // Sent in the background, so the page stays put and the result arrives the way a real one would: through the same
    // route as everything else. That is what makes it a test of the whole thing rather than of this button.
    testForm.addEventListener("submit", async event => {
        event.preventDefault();
        testNote.textContent = "Sending…";
        try {
            const response = await fetch(testForm.action, { method: "POST", body: new FormData(testForm), headers: { "X-Requested-With": "fetch" }, credentials: "same-origin" });
            testNote.textContent = response.ok ? "Sent - it should appear in a moment." : "That didn't send. Reload the page and try again.";
        } catch {
            testNote.textContent = "That didn't send. Check you're still connected and try again.";
        }
    });

    document.addEventListener("edunotify", render);
    render();
})();
