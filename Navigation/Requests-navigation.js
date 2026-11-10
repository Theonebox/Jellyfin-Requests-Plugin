(function () {
    "use strict";

    // Injected into Jellyfin's index.html by NavigationStartupFilter.
    // Adds a "Requests" button to the header. Clicking it opens the Requests page
    // as an overlay that sits on top of Jellyfin's own view, so it never touches
    // Jellyfin's router (the built-in plugin page route is administrator-only).

    if (window.__requestsNavigationLoaded) {
        return;
    }
    window.__requestsNavigationLoaded = true;

    const BUTTON_ID = "requests-header-button";
    const VIEW_ID = "requests-public-view";
    const ASSET_VERSION = "1.0.0.67";

    let addScheduled = false;
    let loadingView = null;

    function client() {
        return window.ApiClient;
    }

    function isAuthenticated() {
        const api = client();
        return Boolean(api && typeof api.getCurrentUserId === "function" && api.getCurrentUserId());
    }

    function assetUrl(file) {
        const api = client();
        const path = `Requests/Web/${file}?v=${ASSET_VERSION}`;
        return api && typeof api.getUrl === "function" ? api.getUrl(path) : "/" + path;
    }

    function getHeaderContainer() {
        const legacy = document.querySelector(".skinHeader .headerRight");
        if (legacy) {
            return legacy;
        }

        const userMenu = document.querySelector('[aria-controls="app-user-menu"]');
        const toolbar = (userMenu && userMenu.closest(".MuiToolbar-root"))
            || document.querySelector(".MuiAppBar-root .MuiToolbar-root");
        if (!toolbar) {
            return null;
        }

        let container = userMenu;
        while (container && container.parentElement !== toolbar) {
            container = container.parentElement;
        }
        return container || toolbar;
    }

    function addButton() {
        addScheduled = false;
        const existing = document.getElementById(BUTTON_ID);

        if (!isAuthenticated()) {
            if (existing) {
                existing.remove();
            }
            closeRequestsPage();
            return;
        }

        if (existing && existing.isConnected) {
            return;
        }

        const header = getHeaderContainer();
        if (!header) {
            return;
        }

        const button = document.createElement("button");
        button.id = BUTTON_ID;
        button.type = "button";
        button.className = "headerButton headerButtonRight paper-icon-button-light";
        button.title = "Requests";
        button.setAttribute("aria-label", "Requests");
        button.innerHTML = '<span class="material-icons" aria-hidden="true">how_to_vote</span>';
        button.addEventListener("click", function (event) {
            event.preventDefault();
            event.stopPropagation();
            toggleRequestsPage();
        });

        header.prepend(button);
    }

    function scheduleAdd() {
        if (!addScheduled) {
            addScheduled = true;
            window.requestAnimationFrame(addButton);
        }
    }

    function headerBottom() {
        const header = document.querySelector(".skinHeader") || document.querySelector(".MuiAppBar-root");
        if (!header) {
            return 0;
        }
        const rect = header.getBoundingClientRect();
        return Math.max(0, Math.round(rect.bottom));
    }

    function positionView(view) {
        view.style.top = headerBottom() + "px";
    }

    function ensureStylesheet() {
        if (!document.getElementById("requests-public-stylesheet")) {
            const link = document.createElement("link");
            link.id = "requests-public-stylesheet";
            link.rel = "stylesheet";
            link.href = assetUrl("requests.css");
            document.head.append(link);
        }
    }

    async function createView() {
        ensureStylesheet();

        const response = await fetch(assetUrl("requests.html"), { credentials: "same-origin" });
        if (!response.ok) {
            throw new Error(`Unable to load the Requests page (${response.status}).`);
        }

        const view = document.createElement("div");
        view.id = VIEW_ID;
        view.className = "requests-public-view";
        view.hidden = true;
        view.style.cssText = "position:fixed;left:0;right:0;bottom:0;z-index:1001;overflow-y:auto;"
            + "background:var(--requests-page-background, #101010);";
        view.innerHTML = await response.text();

        const close = document.createElement("button");
        close.type = "button";
        close.className = "paper-icon-button-light requests-public-close";
        close.title = "Close Requests";
        close.setAttribute("aria-label", "Close Requests");
        close.style.cssText = "position:absolute;top:12px;right:12px;z-index:2;";
        close.innerHTML = '<span class="material-icons" aria-hidden="true">close</span>';
        close.addEventListener("click", closeRequestsPage);
        view.prepend(close);

        document.body.append(view);

        // requests.js initialises itself against #requestsPage once it is in the DOM.
        await new Promise(function (resolve, reject) {
            const script = document.createElement("script");
            script.src = assetUrl("requests.js");
            script.onload = resolve;
            script.onerror = function () {
                reject(new Error("Unable to load the Requests page script."));
            };
            document.body.append(script);
        });

        return view;
    }

    async function openRequestsPage() {
        let view = document.getElementById(VIEW_ID);
        if (!view) {
            loadingView = loadingView || createView().finally(function () { loadingView = null; });
            view = await loadingView;
        }

        // Match Jellyfin's theme background so the overlay blends in.
        const background = window.getComputedStyle(document.body).backgroundColor;
        if (background && background !== "rgba(0, 0, 0, 0)" && background !== "transparent") {
            view.style.setProperty("--requests-page-background", background);
        }

        positionView(view);
        view.hidden = false;
        document.documentElement.classList.add("requests-view-open");
        document.getElementById(BUTTON_ID)?.classList.add("requests-header-button-active");

        const page = view.querySelector("#requestsPage");
        if (page) {
            page.dispatchEvent(new CustomEvent("requestsshow"));
        }
    }

    function closeRequestsPage() {
        const view = document.getElementById(VIEW_ID);
        if (view && !view.hidden) {
            view.hidden = true;
        }
        document.documentElement.classList.remove("requests-view-open");
        document.getElementById(BUTTON_ID)?.classList.remove("requests-header-button-active");
    }

    function isOpen() {
        const view = document.getElementById(VIEW_ID);
        return Boolean(view && !view.hidden);
    }

    function toggleRequestsPage() {
        if (isOpen()) {
            closeRequestsPage();
            return;
        }
        openRequestsPage().catch(function (error) {
            console.error("[Requests]", error);
            if (window.Dashboard && typeof window.Dashboard.alert === "function") {
                window.Dashboard.alert(error.message);
            }
        });
    }

    function initialize() {
        addButton();

        // Jellyfin re-renders its header on navigation and login/logout; re-add the
        // button when that happens. The callback is coalesced into one frame and
        // only does work when the button is missing, so it is cheap.
        new MutationObserver(scheduleAdd).observe(document.body, { childList: true, subtree: true });

        // Any Jellyfin navigation closes the overlay.
        window.addEventListener("hashchange", closeRequestsPage);
        window.addEventListener("popstate", closeRequestsPage);

        // Clicking another header control (home, back, search, user menu...) closes it too.
        document.addEventListener("click", function (event) {
            if (!isOpen() || !(event.target instanceof Element)) {
                return;
            }
            if (event.target.closest("#" + BUTTON_ID)) {
                return;
            }
            if (event.target.closest(".skinHeader, .MuiAppBar-root, .mainDrawer, .MuiDrawer-root")) {
                closeRequestsPage();
            }
        }, true);

        document.addEventListener("keydown", function (event) {
            if (event.key !== "Escape" || !isOpen()) {
                return;
            }
            const openModal = document.querySelector("#" + VIEW_ID + " .requests-modal:not([hidden])");
            if (!openModal) {
                closeRequestsPage();
            }
        });

        window.addEventListener("resize", function () {
            const view = document.getElementById(VIEW_ID);
            if (view && !view.hidden) {
                positionView(view);
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
