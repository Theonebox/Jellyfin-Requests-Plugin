// Adds one authenticated navigation link while reusing Jellyfin's native link styling.
(function () {
    "use strict";

    const linkId = "requests-navigation-link";
    const drawerLinkId = "requests-navigation-drawer-link";
    const route = "#/configurationpage?name=RequestsUser";

    function isAuthenticated() {
        return typeof ApiClient !== "undefined"
            && ((typeof ApiClient.getCurrentUserId === "function" && ApiClient.getCurrentUserId())
                || (typeof ApiClient.accessToken === "function" && ApiClient.accessToken()));
    }

    function decorate(link, id) {
        link.id = id;
        link.href = route;
        link.title = "Requests";
        link.setAttribute("aria-label", "Requests");
        link.removeAttribute("aria-current");
        link.querySelectorAll("svg, .MuiButton-startIcon, .MuiListItemIcon-root").forEach(function (icon) {
            icon.remove();
        });
        link.querySelectorAll(".MuiListItemText-primary, .MuiTypography-root, .navMenuOptionText").forEach(function (text) {
            text.textContent = "Requests";
        });

        if (!link.querySelector(".requests-navigation-icon")) {
            const icon = document.createElement("span");
            icon.className = "material-icons requests-navigation-icon";
            icon.setAttribute("aria-hidden", "true");
            icon.textContent = "move_to_inbox";
            link.insertBefore(icon, link.firstChild);
        }

    }

    function addModernNavigation() {
        const toolbar = document.querySelector("header.MuiAppBar-root .MuiToolbar-root");
        const template = toolbar && toolbar.querySelector('a[href^="#/home"]');
        const bar = template && template.parentElement;
        if (bar && !bar.querySelector("#" + linkId)) {
            const link = template.cloneNode(true);
            decorate(link, linkId);
            bar.appendChild(link);
        }

        const drawer = document.querySelector(".MuiDrawer-paper ul.MuiList-root");
        if (drawer && !drawer.querySelector("#" + drawerLinkId)) {
            const template = drawer.querySelector('a[href^="#/home"]');
            if (template) {
                const item = template.closest("li") || template.parentElement;
                const clonedItem = item.cloneNode(true);
                const link = clonedItem.querySelector("a");
                decorate(link, drawerLinkId);
                drawer.appendChild(clonedItem);
            }
        }
    }

    function addLegacyNavigation() {
        const container = document.querySelector(".customMenuOptions");
        if (!container || container.querySelector("#" + linkId)) {
            return;
        }

        const link = document.createElement("a");
        link.className = "navMenuOption lnkMediaFolder";
        decorate(link, linkId);
        link.querySelector(".requests-navigation-icon").classList.add("navMenuOptionIcon");
        const text = document.createElement("span");
        text.className = "navMenuOptionText";
        text.textContent = "Requests";
        link.appendChild(text);
        container.appendChild(link);
    }

    function sync() {
        if (!isAuthenticated()) {
            document.querySelectorAll("#" + linkId + ", #" + drawerLinkId).forEach(function (link) {
                link.remove();
            });
            return;
        }

        addModernNavigation();
        addLegacyNavigation();
    }

    function scheduleSync() {
        window.requestAnimationFrame(sync);
    }

    function initialize() {
        sync();
        new MutationObserver(scheduleSync).observe(document.body, { childList: true, subtree: true });
        window.addEventListener("hashchange", scheduleSync);
        window.addEventListener("pageshow", scheduleSync);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
