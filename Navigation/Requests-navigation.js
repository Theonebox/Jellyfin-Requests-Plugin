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
            + `#${BUTTON_ID}.requestsHeaderButton-active { opacity: 1; }`
            + `#${BUTTON_ID}.MuiIconButton-root.requestsHeaderButton-active { background-color: rgba(128, 128, 128, .2); }`;
        document.head.append(style);
    }

    // ---------- header button ----------

    // Material "how_to_vote" icon, used in the React header which draws its icons as inline SVG.
    const HOW_TO_VOTE_PATH = "M18 13h-.68l-2 2h1.91L19 17H5l1.78-2h2.05l-2-2H6l-3 3v4c0 1.1.89 2 1.99 2H19c1.1 0 2-.89 2-2v-4l-3-3zm-1-5.05l-4.95 4.95-3.54-3.54 4.95-4.95L17 7.95zm-4.24-5.66L6.39 8.66c-.39.39-.39 1.02 0 1.41l4.95 4.95c.39.39 1.02.39 1.41 0l6.36-6.36c.39-.39.39-1.02 0-1.41L14.16 2.3c-.38-.4-1.01-.4-1.4-.01z";

    function isVisible(element) {
        return Boolean(element && element.getClientRects().length);
    }

    // Jellyfin has two header implementations and both can exist in the DOM at once (the unused
    // one is hidden): the classic .skinHeader and the React/MUI .MuiAppBar-root (the
    // "experimental" layout, also used by Jellyfin Enhanced). Only the visible one counts.
    // Returns { layout, parent, before, template } describing where the button goes.
    function findInsertionPoint() {
        const appBar = document.querySelector(".MuiAppBar-root");
        if (isVisible(appBar)) {
            const sync = appBar.querySelector('button[aria-label="SyncPlay"], button[title="SyncPlay"]');
            if (sync && sync.parentElement) {
                return { layout: "mui", parent: sync.parentElement, before: sync, template: sync };
            }
            const iconButtons = Array.from(appBar.querySelectorAll(".MuiIconButton-root"))
                .filter((button) => button.id !== BUTTON_ID && button.getAttribute("aria-controls") !== "app-user-menu" && isVisible(button));
            if (iconButtons.length) {
                return { layout: "mui", parent: iconButtons[0].parentElement, before: iconButtons[0], template: iconButtons[0] };
            }
            const toolbar = appBar.querySelector(".MuiToolbar-root");
            return toolbar ? { layout: "mui", parent: toolbar, before: null, template: null } : null;
        }

        const skinHeader = document.querySelector(".skinHeader");
        if (isVisible(skinHeader)) {
            const headerRight = skinHeader.querySelector(".headerRight");
            if (!headerRight) {
                return null;
            }
            // SyncPlay may be hidden (class "hide") when the user lacks SyncPlay access;
            // the button still goes in that slot.
            const before = headerRight.querySelector(".headerSyncButton")
                || headerRight.querySelector(".headerCastButton")
                || headerRight.firstChild;
            return { layout: "classic", parent: headerRight, before: before, template: null };
        }

        return null;
    }

    function createButton(point) {
        const button = document.createElement("button");
        button.id = BUTTON_ID;
        button.type = "button";
        button.title = "Requests";
        button.dataset.layout = point.layout;
        button.setAttribute("aria-label", "Requests");

        if (point.layout === "mui") {
            // Copy the neighbouring React icon button's classes so it looks identical (size,
            // hover ripple area, colour) under any theme.
            const svgClass = point.template && point.template.querySelector("svg")
                ? point.template.querySelector("svg").getAttribute("class")
                : "MuiSvgIcon-root MuiSvgIcon-fontSizeMedium";
            button.className = (point.template ? point.template.className.replace(/\bMui-focusVisible\b/g, "") : "MuiButtonBase-root MuiIconButton-root MuiIconButton-colorInherit MuiIconButton-sizeLarge")
                + " requestsHeaderButton";
            button.innerHTML = `<svg class="${svgClass}" focusable="false" aria-hidden="true" viewBox="0 0 24 24"><path d="${HOW_TO_VOTE_PATH}"></path></svg>`;
        } else {
            button.setAttribute("is", "paper-icon-button-light");
            button.className = "headerButton headerButtonRight paper-icon-button-light requestsHeaderButton";
            // Same markup Jellyfin uses for its own classic header icons.
            button.innerHTML = '<span class="material-icons how_to_vote" aria-hidden="true"></span>';
        }

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

        if (button && button.dataset.layout !== point.layout) {
            button.remove(); // Layout switched; rebuild with the right look.
            button = null;
        }

        if (!button) {
            button = createButton(point);
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
        const header = [document.querySelector(".MuiAppBar-root"), document.querySelector(".skinHeader")].find(isVisible);
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
