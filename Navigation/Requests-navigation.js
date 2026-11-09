(function () {
    "use strict";

    const buttonId = "requests-header-button";
    const route = "#/configurationpage?name=RequestsUser";

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
        button.addEventListener("click", function () {
            window.location.hash = route;
        });

        if (dice?.parentElement === header) {
            dice.parentElement.insertBefore(button, dice.nextSibling);
        } else {
            header.prepend(button);
        }
    }

    function scheduleAdd() {
        window.requestAnimationFrame(addButton);
    }

    function initialize() {
        addButton();
        new MutationObserver(scheduleAdd).observe(document.body, { childList: true, subtree: true });
        window.addEventListener("hashchange", scheduleAdd);
        window.addEventListener("pageshow", scheduleAdd);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
