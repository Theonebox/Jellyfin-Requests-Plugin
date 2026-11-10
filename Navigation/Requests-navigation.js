(function () {
    "use strict";

    const buttonId = "requests-header-button";
    const viewId = "requests-public-view";

    function isAuthenticated() {
        return typeof ApiClient !== "undefined"
            && typeof ApiClient.getCurrentUserId === "function"
            && Boolean(ApiClient.getCurrentUserId());
    }

    function getHeaderRightContainer() {
        const legacy = document.querySelector(".headerRight:not([data-requests-fallback])");
        if (legacy && legacy.offsetParent !== null) {
            return legacy;
        }

        const userMenu = document.querySelector('[aria-controls="app-user-menu"]');
        const toolbar = userMenu?.closest(".MuiToolbar-root")
            || document.querySelector(".MuiAppBar-root .MuiToolbar-root");
        if (!toolbar) {
            return null;
        }

        let container = userMenu;
        while (container && container.parentElement !== toolbar) {
            container = container.parentElement;
        }

        if (!container) {
            container = document.createElement("div");
            container.className = "headerRight";
            container.dataset.requestsFallback = "true";
            toolbar.appendChild(container);
        }

        return container;
    }

    function addButton() {
        if (!isAuthenticated()) {
            document.getElementById(buttonId)?.remove();
            return;
        }

        if (document.getElementById(buttonId)) {
            return;
        }

        const dice = document.getElementById("randomItemButton");
        const header = dice?.parentElement || getHeaderRightContainer();
        if (!header) {
            return;
        }

        const button = document.createElement("button");
        button.id = buttonId;
        button.type = "button";
        button.setAttribute("is", "paper-icon-button-light");
        button.className = "headerButton headerButtonRight paper-icon-button-light";
        button.title = "Requests";
        button.setAttribute("aria-label", "Requests");
        button.innerHTML = '<span class="material-icons" aria-hidden="true">how_to_vote</span>';
        button.addEventListener("click", function (event) {
            event.preventDefault();
            event.stopPropagation();
            showRequestsPage().catch(function (error) {
                console.error("Requests page could not be loaded.", error);
            });
        });

        if (dice?.parentElement === header) {
            dice.parentElement.insertBefore(button, dice.nextSibling);
        } else {
            header.prepend(button);
        }

    }

    async function showRequestsPage() {
        if (!isAuthenticated()) {
            return;
        }

        let view = document.getElementById(viewId);
        if (!view) {
            const response = await fetch("/Requests/Web/requests.html", { credentials: "same-origin" });
            if (!response.ok) {
                throw new Error("Unable to load Requests.");
            }

            const documentFragment = document.createRange().createContextualFragment(await response.text());
            view = document.createElement("div");
            view.id = viewId;
            view.className = "page type-interior requests-public-view";
            view.append(documentFragment);

            const script = document.createElement("script");
            script.src = "/Requests/Web/requests.js?v=1.0.0.63";
            view.append(script);
            (document.querySelector(".mainAnimated") || document.body).append(view);
        }

        document.querySelectorAll(".mainAnimated > .page").forEach(function (page) {
            page.hidden = page !== view;
        });
        view.hidden = false;
    }

    function hideRequestsPage() {
        const view = document.getElementById(viewId);
        if (!view) {
            return;
        }

        view.hidden = true;
        document.querySelectorAll(".mainAnimated > .page").forEach(function (page) {
            if (page !== view) {
                page.hidden = false;
            }
        });
    }

    function syncRequestsPage() {
        hideRequestsPage();
    }

    function scheduleAdd() {
        window.requestAnimationFrame(addButton);
    }

    function initialize() {
        addButton();
        new MutationObserver(scheduleAdd).observe(document.body, { childList: true, subtree: true });
        window.addEventListener("hashchange", function () {
            scheduleAdd();
            syncRequestsPage();
        });
        window.addEventListener("pageshow", scheduleAdd);
        syncRequestsPage();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
