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

    function decorate(link) {
        link.id = linkId;
        link.href = route;
        link.removeAttribute("aria-current");
        link.querySelectorAll("svg, .MuiButton-startIcon, .MuiListItemIcon-root").forEach(function (icon) {
            icon.remove();
        });

        const text = link.querySelector(".MuiListItemText-primary, .MuiTypography-root");
        if (text) {
            text.textContent = "Requests";
        } else {
            const textNode = Array.from(link.childNodes).reverse()
                .find(function (node) { return node.nodeType === Node.TEXT_NODE && node.textContent.trim(); });
            if (textNode) {
                textNode.textContent = "Requests";
            } else {
                link.appendChild(document.createTextNode("Requests"));
            }
        }
    }

    function addModernNavigation() {
        const bar = document.querySelector("header.MuiAppBar-root .MuiToolbar-root .MuiStack-root");
        if (bar && !bar.querySelector("#" + linkId)) {
            const template = Array.from(bar.children).find(function (element) {
                return element.tagName === "A" && (element.getAttribute("href") || "").indexOf("#/home") === 0;
            });
            if (template) {
                const link = template.cloneNode(true);
                decorate(link);
                bar.appendChild(link);
            }
        }

        const drawer = document.querySelector(".MuiDrawer-paper ul.MuiList-root");
        if (drawer && !drawer.querySelector("#" + drawerLinkId)) {
            const template = Array.from(drawer.children).find(function (element) {
                const link = element.querySelector("a");
                return link && (link.getAttribute("href") || "").indexOf("#/home") === 0;
            });
            if (template) {
                const item = template.cloneNode(true);
                const link = item.querySelector("a");
                if (link) {
                    link.id = drawerLinkId;
                    decorate(link);
                    drawer.appendChild(item);
                }
            }
        }
    }

    function addLegacyNavigation() {
        const container = document.querySelector(".customMenuOptions");
        if (!container || container.querySelector("#" + linkId)) {
            return;
        }

        const link = document.createElement("a");
        link.id = linkId;
        link.className = "navMenuOption lnkMediaFolder";
        link.href = route;
        link.innerHTML = '<span class="material-icons navMenuOptionIcon how_to_vote" aria-hidden="true"></span>'
            + '<span class="navMenuOptionText">Requests</span>';
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
