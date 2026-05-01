// Quebec's Cave — theme persistence.
//
// On page load (inline in App.razor), apply the stored theme before paint
// to avoid a flash of wrong-mode content. The toggle button calls cycle()
// which flips Cave Dark <-> Forest Light and updates localStorage.
//
// Phase 4 will sync the choice to the API for logged-in users.

(function () {
    const KEY = "quebecs-cave:theme";
    const THEMES = ["dark", "light"];

    function get() {
        try { return localStorage.getItem(KEY); } catch { return null; }
    }

    function set(theme) {
        if (!THEMES.includes(theme)) return;
        try { localStorage.setItem(KEY, theme); } catch { /* private mode etc. */ }
        document.documentElement.setAttribute("data-theme", theme);
        updateToggleIcon(theme);
    }

    function applyInitial() {
        const stored = get();
        const theme = THEMES.includes(stored) ? stored : "dark";
        document.documentElement.setAttribute("data-theme", theme);
    }

    function cycle() {
        const current = document.documentElement.getAttribute("data-theme") || "dark";
        const next = current === "dark" ? "light" : "dark";
        set(next);
    }

    function updateToggleIcon(theme) {
        const el = document.getElementById("theme-toggle-icon");
        if (!el) return;
        el.textContent = theme === "dark" ? "☀" : "☾";
        const btn = document.getElementById("theme-toggle");
        if (btn) {
            btn.setAttribute("aria-label",
                theme === "dark" ? "Switch to light mode" : "Switch to dark mode");
            btn.title = btn.getAttribute("aria-label");
        }
    }

    // Make sure the icon syncs once the toggle component renders.
    document.addEventListener("DOMContentLoaded", () => {
        const theme = document.documentElement.getAttribute("data-theme") || "dark";
        updateToggleIcon(theme);
    });

    window.quebTheme = { get, set, cycle, applyInitial };

    applyInitial();
})();
