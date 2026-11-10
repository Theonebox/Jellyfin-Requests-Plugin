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
        // While Requests is open, hide everything that normally sits behind/around the page:
        // Jellyfin's current view, item backdrops, and the Media Bar plugin's slideshow
        // (#slides-container, which it mounts directly under <body> with its own Play buttons).
        // The view itself is painted with the theme's page background (copied from <html> when
        // opened); the :where() keeps that rule zero-specificity so custom CSS can override it.
        style.textContent =
            `html.${OPEN_CLASS} .mainAnimatedPages, html.${OPEN_CLASS} .mainAnimated,`
            + ` html.${OPEN_CLASS} #reactRoot > .MuiBox-root > main { visibility: hidden !important; }`
            + `html.${OPEN_CLASS} #slides-container, html.${OPEN_CLASS} .backdropContainer { display: none !important; }`
            + `#${VIEW_ID}[hidden] { display: none !important; }`
            + `#${VIEW_ID} { position: fixed; left: 0; right: 0; bottom: 0; overflow-y: auto;`
            + ` overscroll-behavior: contain; z-index: 998; -webkit-overflow-scrolling: touch; }`
            + `:where(#${VIEW_ID}) { background-color: var(--requests-page-background-color, #101010);`
            + ` background-image: var(--requests-page-background-image, none); background-size: cover; background-attachment: fixed; }`
            + `.requestsHeaderButton-standalone { display: inline-flex; align-items: center; justify-content: center;`
            + ` padding: 12px; margin: 0; border: 0; border-radius: 50%; background: transparent; color: inherit; cursor: pointer; }`
            + `.requestsHeaderButton-standalone:hover { background-color: rgba(128, 128, 128, .15); }`
            + `#${BUTTON_ID}:is(.MuiIconButton-root, .requestsHeaderButton-standalone).requestsHeaderButton-active { background-color: rgba(128, 128, 128, .2); }`;
        document.head.append(style);
    }

    // ---------- header button ----------

    // Material "how_to_vote" icon, used in the React header which draws its icons as inline SVG.
    const HOW_TO_VOTE_PATH = "M18 13h-.68l-2 2h1.91L19 17H5l1.78-2h2.05l-2-2H6l-3 3v4c0 1.1.89 2 1.99 2H19c1.1 0 2-.89 2-2v-4l-3-3zm-1-5.05l-4.95 4.95-3.54-3.54 4.95-4.95L17 7.95zm-4.24-5.66L6.39 8.66c-.39.39-.39 1.02 0 1.41l4.95 4.95c.39.39 1.02.39 1.41 0l6.36-6.36c.39-.39.39-1.02 0-1.41L14.16 2.3c-.38-.4-1.01-.4-1.4-.01z";

    // Dashboard and other administrator-only routes, e.g. #/dashboard/plugins, #/configurationpage?name=…
    const DASHBOARD_ROUTE = /^#?\/?(dashboard|configurationpage|metadata|edititemmetadata|librarysetup|networking|devices|serveractivity|userprofiles|useredit|scheduledtasks|plugins)(\/|\?|\.html|$)/i;

    function isDashboardRoute() {
        const route = (window.location.hash || "").replace(/^#!?/, "") || window.location.pathname.replace(/^.*\/web\/?/, "/");
        return DASHBOARD_ROUTE.test(route);
    }

    function isVisible(element) {
        return Boolean(element && element.getClientRects().length);
    }

    // Jellyfin has two header implementations and both can exist in the DOM at once (the unused
    // one is hidden): the classic .skinHeader and the React/MUI .MuiAppBar-root (the
    // "experimental" layout, also used by Jellyfin Enhanced). Only the visible one counts.
    // Returns { kind, parent, before, template } describing where and how the button goes.
    function findInsertionPoint() {
        const appBar = document.querySelector(".MuiAppBar-root");
        if (isVisible(appBar)) {
            // Copy only the SyncPlay button's look: other toolbar buttons (from plugins) can have
            // unrelated styling. Until SyncPlay renders, the button styles itself and is rebuilt
            // once SyncPlay appears.
            const sync = appBar.querySelector('button[aria-label="SyncPlay"], button[title="SyncPlay"]');
            if (sync && sync.parentElement) {
                return { kind: "mui-sync", parent: sync.parentElement, before: sync, template: sync };
            }
            const firstIcon = Array.from(appBar.querySelectorAll(".MuiIconButton-root"))
                .find((button) => button.id !== BUTTON_ID && button.getAttribute("aria-controls") !== "app-user-menu" && isVisible(button));
            if (firstIcon) {
                return { kind: "mui", parent: firstIcon.parentElement, before: firstIcon, template: null };
            }
            const toolbar = appBar.querySelector(".MuiToolbar-root");
            return toolbar ? { kind: "mui", parent: toolbar, before: null, template: null } : null;
        }

        const headerRight = document.querySelector(".skinHeader .headerRight");
        if (isVisible(headerRight)) {
            // SyncPlay may be hidden (class "hide") when the user lacks SyncPlay access;
            // the button still goes in that slot.
            const before = headerRight.querySelector(".headerSyncButton")
                || headerRight.querySelector(".headerCastButton")
                || headerRight.firstChild;
            return { kind: "classic", parent: headerRight, before: before, template: null };
        }

        return null;
    }

    function createButton(point) {
        const button = document.createElement("button");
        button.id = BUTTON_ID;
        button.type = "button";
        button.title = "Requests";
        button.dataset.kind = point.kind;
        button.setAttribute("aria-label", "Requests");

        if (point.kind === "classic") {
            button.setAttribute("is", "paper-icon-button-light");
            button.className = "headerButton headerButtonRight paper-icon-button-light requestsHeaderButton";
            // Same markup Jellyfin uses for its own classic header icons.
            button.innerHTML = '<span class="material-icons how_to_vote" aria-hidden="true"></span>';
        } else {
            const svgTemplate = point.template && point.template.querySelector("svg");
            button.className = point.template
                ? point.template.className.replace(/\bMui-focusVisible\b/g, "") + " requestsHeaderButton"
                : "requestsHeaderButton requestsHeaderButton-standalone";
            // The icon always gets an explicit size so it can't collapse to zero width when the
            // copied classes don't size it.
            button.innerHTML = `<svg class="${svgTemplate ? svgTemplate.getAttribute("class") : ""}" focusable="false" aria-hidden="true" viewBox="0 0 24 24"`
                + ' style="width:1em;height:1em;font-size:1.5rem;fill:currentColor;flex-shrink:0">'
                + `<path d="${HOW_TO_VOTE_PATH}"></path></svg>`;
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

        // No button when signed out, or on the administrator dashboard (its own layout with a
        // side drawer; requests are managed from the dashboard's Requests entry there).
        if (!isAuthenticated() || isDashboardRoute()) {
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

        if (button && button.dataset.kind !== point.kind) {
            button.remove(); // Header changed (layout switch, SyncPlay appeared); rebuild.
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
            // rAF for a prompt update; the timeout covers background tabs where rAF is paused.
            // ensureButton clears addScheduled, so whichever fires second is a cheap no-op.
            const run = function () {
                if (addScheduled) {
                    ensureButton();
                }
            };
            window.requestAnimationFrame(run);
            window.setTimeout(run, 250);
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

    // Copy the theme's page background (set on <html>/<body> by the theme or custom CSS) so the
    // Requests view is opaque but still looks like a native page.
    function applyThemeBackground(view) {
        for (const element of [document.documentElement, document.body]) {
            const style = window.getComputedStyle(element);
            const color = style.backgroundColor;
            if (color && color !== "transparent" && !/rgba\(.*,\s*0\)$/.test(color)) {
                view.style.setProperty("--requests-page-background-color", color);
                view.style.setProperty("--requests-page-background-image", style.backgroundImage || "none");
                return;
            }
        }
    }

    // Media Bar plays trailers in its slideshow; pause them while Requests covers the page and
    // resume only the ones we paused.
    const pausedMedia = new Set();

    function pauseBackgroundMedia() {
        const container = document.getElementById("slides-container");
        if (!container) {
            return;
        }
        container.querySelectorAll("video").forEach(function (video) {
            if (!video.paused) {
                video.pause();
                pausedMedia.add(video);
            }
        });
        container.querySelectorAll('iframe[src*="youtube"]').forEach(function (frame) {
            try {
                frame.contentWindow.postMessage(JSON.stringify({ event: "command", func: "pauseVideo", args: [] }), "*");
                pausedMedia.add(frame);
            } catch (_) {
                // Cross-origin player without the JS API; it's hidden anyway.
            }
        });
    }

    function resumeBackgroundMedia() {
        pausedMedia.forEach(function (media) {
            if (!media.isConnected) {
                return;
            }
            if (media.tagName === "VIDEO") {
                media.play().catch(function () { /* autoplay may be blocked; ignore */ });
            } else {
                try {
                    media.contentWindow.postMessage(JSON.stringify({ event: "command", func: "playVideo", args: [] }), "*");
                } catch (_) {
                    // Ignore.
                }
            }
        });
        pausedMedia.clear();
    }

    async function open() {
        const view = await getView();
        applyThemeBackground(view);
        view.style.top = headerBottom() + "px";
        view.hidden = false;
        view.scrollTop = 0;
        pauseBackgroundMedia();
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
            resumeBackgroundMedia();
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
