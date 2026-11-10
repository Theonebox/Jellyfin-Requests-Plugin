(function () {
    "use strict";

    // Loaded on every web client page (see WebClientInjector). Adds a "Requests" button to the
    // header next to SyncPlay and shows the Requests page in a layer above Jellyfin's view.
    //
    // Jellyfin's own plugin-page route (#/configurationpage) is administrator-only, so the public
    // page can't use it. Instead the current Jellyfin view is hidden while Requests is open and the
    // theme's background shows through, so themes and custom CSS apply exactly as on native pages.

    if (window.__requestsNavigationLoaded) {
        return;
    }
    window.__requestsNavigationLoaded = true;

    const BUTTON_ID = "requests-header-button";
    const VIEW_ID = "requests-public-view";
    const OPEN_CLASS = "requests-view-open";
    const scriptVersion = (function () {
        const current = document.currentScript && document.currentScript.src;
        const match = current && /[?&]v=([^&]+)/.exec(current);
        return match ? match[1] : String(Date.now());
    }());

    let addScheduled = false;
    let viewPromise = null;

    // ---------- helpers ----------

    function apiClient() {
        return window.ApiClient;
    }

    function isAuthenticated() {
        const api = apiClient();
        try {
            return Boolean(api && typeof api.getCurrentUserId === "function" && api.getCurrentUserId());
        } catch (_) {
            return false;
        }
    }

    function assetUrl(file) {
        const api = apiClient();
        const path = `Requests/Web/${file}?v=${encodeURIComponent(scriptVersion)}`;
        return api && typeof api.getUrl === "function" ? api.getUrl(path) : "../" + path;
    }

    function injectBaseStyles() {
        if (document.getElementById("requests-navigation-style")) {
            return;
        }
        const style = document.createElement("style");
        style.id = "requests-navigation-style";
        // Hide Jellyfin's current view while Requests is open so the theme background shows through.
        style.textContent =
            `html.${OPEN_CLASS} .mainAnimatedPages, html.${OPEN_CLASS} .mainAnimated,`
            + ` html.${OPEN_CLASS} #reactRoot > .MuiBox-root > main { visibility: hidden !important; }`
            + `#${VIEW_ID}[hidden] { display: none !important; }`
            + `#${VIEW_ID} { position: fixed; left: 0; right: 0; bottom: 0; overflow-y: auto;`
            + ` overscroll-behavior: contain; z-index: 2; -webkit-overflow-scrolling: touch; }`
            + `.MuiToolbar-root #${BUTTON_ID} { background: transparent; border: 0; color: inherit;`
            + ` cursor: pointer; padding: 8px; border-radius: 50%; display: inline-flex; }`;
        document.head.append(style);
    }

    // ---------- header button ----------

    // Returns { parent, before } describing where the button goes.
    function findInsertionPoint() {
        // Classic layout: <div class="headerRight"> ... <button class="headerSyncButton"> ...
        const sync = document.querySelector(".skinHeader .headerSyncButton");
        if (sync && sync.parentElement) {
            return { parent: sync.parentElement, before: sync };
        }

        const headerRight = document.querySelector(".skinHeader .headerRight");
        if (headerRight) {
            const cast = headerRight.querySelector(".headerCastButton");
            return { parent: headerRight, before: cast || headerRight.firstChild };
        }

        // Experimental (React/MUI) layout.
        const muiSync = document.querySelector('.MuiAppBar-root [title="SyncPlay"], .MuiAppBar-root [aria-label="SyncPlay"]');
        if (muiSync) {
            const target = muiSync.closest("button") || muiSync;
            return { parent: target.parentElement, before: target };
        }

        const userMenu = document.querySelector('.MuiAppBar-root [aria-controls="app-user-menu"]');
        if (userMenu) {
            const target = userMenu.closest("button") || userMenu;
            return { parent: target.parentElement, before: target };
        }

        return null;
    }

    function createButton() {
        const button = document.createElement("button");
        button.id = BUTTON_ID;
        button.type = "button";
        button.setAttribute("is", "paper-icon-button-light");
        button.className = "headerButton headerButtonRight paper-icon-button-light requestsHeaderButton";
        button.title = "Requests";
        button.setAttribute("aria-label", "Requests");
        // Same markup Jellyfin uses for its own header icons (class-based Material icon).
        button.innerHTML = '<span class="material-icons how_to_vote" aria-hidden="true"></span>';
        button.addEventListener("click", function (event) {
            event.preventDefault();
            event.stopPropagation();
            toggle();
        });
        return button;
    }

    function ensureButton() {
        addScheduled = false;
        let button = document.getElementById(BUTTON_ID);

        if (!isAuthenticated()) {
            if (button) {
                button.remove();
            }
            close();
            return;
        }

        const point = findInsertionPoint();
        if (!point || !point.parent) {
            return;
        }

        if (!button) {
            button = createButton();
        } else if (button.parentElement === point.parent && button.nextSibling === point.before) {
            return; // Already in place.
        }

        point.parent.insertBefore(button, point.before && point.before.parentElement === point.parent ? point.before : null);
    }

    function scheduleEnsureButton() {
        if (!addScheduled) {
            addScheduled = true;
            window.requestAnimationFrame(ensureButton);
        }
    }

    // ---------- Requests view ----------

    function headerBottom() {
        const header = document.querySelector(".skinHeader") || document.querySelector(".MuiAppBar-root");
        return header ? Math.max(0, Math.round(header.getBoundingClientRect().bottom)) : 0;
    }

    function ensureStylesheet() {
        if (document.getElementById("requests-public-stylesheet")) {
            return Promise.resolve();
        }
        return new Promise(function (resolve) {
            const link = document.createElement("link");
            link.id = "requests-public-stylesheet";
            link.rel = "stylesheet";
            link.href = assetUrl("requests.css");
            link.onload = resolve;
            link.onerror = resolve; // The page still works unstyled.
            // Insert before the first existing <style>/<link> that comes from the user's custom CSS
            // is not possible to detect reliably; our rules are low-specificity so custom CSS wins anyway.
            document.head.append(link);
        });
    }

    function loadScript() {
        return new Promise(function (resolve, reject) {
            const script = document.createElement("script");
            script.src = assetUrl("requests.js");
            script.onload = resolve;
            script.onerror = function () {
                reject(new Error("Unable to load the Requests page script."));
            };
            document.body.append(script);
        });
    }

    async function createView() {
        const [, response] = await Promise.all([
            ensureStylesheet(),
            fetch(assetUrl("requests.html"), { credentials: "same-origin" })
        ]);
        if (!response.ok) {
            throw new Error(`Unable to load the Requests page (${response.status}).`);
        }

        const view = document.createElement("div");
        view.id = VIEW_ID;
        view.className = "requestsOverlay";
        view.hidden = true;
        view.setAttribute("role", "region");
        view.setAttribute("aria-label", "Requests");
        view.innerHTML = await response.text();
        document.body.append(view);

        // requests.js wires itself to #requestsPage and exposes window.RequestsPage.
        await loadScript();
        return view;
    }

    function getView() {
        const existing = document.getElementById(VIEW_ID);
        if (existing) {
            return Promise.resolve(existing);
        }
        if (!viewPromise) {
            viewPromise = createView().catch(function (error) {
                viewPromise = null;
                document.getElementById(VIEW_ID)?.remove();
                throw error;
            });
        }
        return viewPromise;
    }

    function isOpen() {
        const view = document.getElementById(VIEW_ID);
        return Boolean(view && !view.hidden);
    }

    async function open() {
        const view = await getView();
        view.style.top = headerBottom() + "px";
        view.hidden = false;
        view.scrollTop = 0;
        document.documentElement.classList.add(OPEN_CLASS);
        document.getElementById(BUTTON_ID)?.classList.add("requestsHeaderButton-active");
        document.getElementById(BUTTON_ID)?.setAttribute("aria-pressed", "true");
        if (window.RequestsPage && typeof window.RequestsPage.show === "function") {
            window.RequestsPage.show();
        }
    }

    function close() {
        const view = document.getElementById(VIEW_ID);
        if (view && !view.hidden) {
            view.hidden = true;
            if (window.RequestsPage && typeof window.RequestsPage.hide === "function") {
                window.RequestsPage.hide();
            }
        }
        document.documentElement.classList.remove(OPEN_CLASS);
        const button = document.getElementById(BUTTON_ID);
        if (button) {
            button.classList.remove("requestsHeaderButton-active");
            button.setAttribute("aria-pressed", "false");
        }
    }

    function toggle() {
        if (isOpen()) {
            close();
            return;
        }
        open().catch(function (error) {
            console.error("[Requests]", error);
            if (window.Dashboard && typeof window.Dashboard.alert === "function") {
                window.Dashboard.alert(error.message);
            }
        });
    }

    // ---------- startup ----------

    function initialize() {
        injectBaseStyles();
        ensureButton();

        // The header is (re)built after login and when switching layouts/users. The observer callback
        // is coalesced to one check per frame and does nothing when the button is already in place.
        new MutationObserver(scheduleEnsureButton).observe(document.body, { childList: true, subtree: true });

        // Jellyfin navigation of any kind closes the Requests view.
        window.addEventListener("hashchange", close);
        window.addEventListener("popstate", close);
        document.addEventListener("viewshow", close);

        // Clicking another header/drawer control (home, back, search, tabs, user menu) closes it too.
        document.addEventListener("click", function (event) {
            if (!isOpen() || !(event.target instanceof Element) || event.target.closest("#" + BUTTON_ID)) {
                return;
            }
            if (event.target.closest(".skinHeader, .MuiAppBar-root, .mainDrawer, .MuiDrawer-root")) {
                close();
            }
        }, true);

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && isOpen()
                && !(window.RequestsPage && window.RequestsPage.hasOpenDialog && window.RequestsPage.hasOpenDialog())) {
                close();
            }
        });

        window.addEventListener("resize", function () {
            const view = document.getElementById(VIEW_ID);
            if (view && !view.hidden) {
                view.style.top = headerBottom() + "px";
            }
        });

        window.RequestsNavigation = { open: open, close: close, toggle: toggle };
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
