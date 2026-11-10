(function () {
    "use strict";

    // Public Requests page. Loaded once by Requests-navigation.js after requests.html is in the DOM.
    // Exposes window.RequestsPage = { show, hide, hasOpenDialog } for the navigation script.

    if (window.RequestsPage) {
        return;
    }

    const page = document.getElementById("requestsPage");
    if (!page) {
        return;
    }

    const $ = (selector, root) => (root || page).querySelector(selector);

    // The dialog lives under <body> so it layers above Jellyfin's header.
    const dialog = $("#requestsDialog");
    document.body.append(dialog);
    const d = (selector) => dialog.querySelector(selector);

    const el = {
        form: $("#requestsSearchForm"),
        input: $("#requestsSearchInput"),
        type: $("#requestsMediaType"),
        typeContainer: $(".requestsSearch-type"),
        searchButton: $("#requestsSearchButton"),
        status: $("#requestsStatus"),
        results: $("#requestsResults"),
        myStatus: $("#requestsMyStatus"),
        myList: $("#requestsMyList"),
        dialogTitle: d("#requestsDialogTitle"),
        dialogPoster: d("#requestsDialogPoster"),
        dialogMeta: d("#requestsDialogMeta"),
        dialogOverview: d("#requestsDialogOverview"),
        tvScope: d("#requestsTvScope"),
        seasonList: d("#requestsSeasonList"),
        episodeList: d("#requestsEpisodeList"),
        episodeIds: d("#requestsEpisodeIds"),
        quality: d("#requestsQuality"),
        note: d("#requestsNote"),
        dialogMessage: d("#requestsDialogMessage"),
        submit: d("#requestsDialogSubmit")
    };

    const DEFAULT_QUALITY = ["720p", "1080p", "4K"];
    const MAX_SEASONS = 30;
    const EPISODE_PATTERN = /^S\d{1,3}E\d{1,4}$/i;

    const state = {
        results: [],
        myRequests: [],
        requested: new Map(), // mediaKey -> status
        quality: DEFAULT_QUALITY,
        media: { movies: true, tvShows: true },
        // Configured statuses (id -> { name, color }); the first one is the "not processed yet"
        // status in which users may still cancel.
        statuses: new Map(),
        initialStatus: "Pending",
        dialogItem: null,
        lastFocus: null,
        searchController: null,
        submitting: false
    };

    // ---------- API ----------

    function apiUrl(path) {
        const client = window.ApiClient;
        const relative = String(path).replace(/^\/+/, "");
        return client && typeof client.getUrl === "function" ? client.getUrl(relative) : "../" + relative;
    }

    function authHeaders() {
        const client = window.ApiClient;
        const token = client && typeof client.accessToken === "function" ? client.accessToken() : "";
        return token ? { Authorization: `MediaBrowser Token="${token}"` } : {};
    }

    async function api(path, options) {
        options = options || {};
        const headers = { Accept: "application/json", ...authHeaders() };
        if (options.body !== undefined) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(apiUrl(path), {
            method: options.method || "GET",
            headers: headers,
            body: options.body === undefined ? undefined : JSON.stringify(options.body),
            signal: options.signal
        });

        // 204 No Content (e.g. DELETE) has no body, so never assume JSON.
        const text = await response.text();
        let body = null;
        if (text) {
            try {
                body = JSON.parse(text);
            } catch (_) {
                body = text;
            }
        }

        if (!response.ok) {
            const message = typeof body === "string" && body.length < 300
                ? body
                : body && (body.error || body.Error || body.message || body.title);
            const error = new Error(message || `Request failed (${response.status}).`);
            error.status = response.status;
            throw error;
        }

        return body;
    }

    // Jellyfin returns PascalCase by default; plugin DTOs may be camelCase. Read either.
    function field(item, name) {
        if (!item) {
            return undefined;
        }
        const value = item[name];
        return value !== undefined && value !== null ? value : item[name.charAt(0).toUpperCase() + name.slice(1)];
    }

    // ---------- helpers ----------

    function isTv(item) {
        const type = String(field(item, "mediaType") || "").toLowerCase();
        const category = String(field(item, "mediaCategory") || "").toLowerCase();
        return type.startsWith("tv") || category === "tvshows";
    }

    function mediaKey(item) {
        const id = field(item, "providerId") || field(item, "mediaId") || "";
        return id ? `${isTv(item) ? "tv" : "movie"}:${String(id).toLowerCase()}` : "";
    }

    // Class hook for custom CSS, e.g. .requestsStatus-in-progress.
    function statusClass(status) {
        return "requestsStatus-" + String(status || "").replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase().replace(/[^a-z0-9-]/g, "");
    }

    function statusLabel(status) {
        const option = state.statuses.get(status);
        return option ? option.name : String(status || "").replace(/([a-z])([A-Z])/g, "$1 $2");
    }

    function statusColor(status) {
        const option = state.statuses.get(status);
        return option ? option.color : "";
    }

    function scopeLabel(request) {
        switch (field(request, "requestScope")) {
            case "SeasonSelection":
                return "Season " + (field(request, "requestedSeasonNumbers") || []).join(", ");
            case "EpisodeSelection":
                return (field(request, "requestedEpisodeNames") || field(request, "requestedEpisodeIds") || []).join(", ");
            case "EntireSeries":
                return "Entire series";
            default:
                return "";
        }
    }

    function formatDate(value) {
        if (!value) {
            return "";
        }
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? "" : date.toLocaleDateString();
    }

    function cssUrl(url) {
        return `url("${String(url).replace(/["\\\n\r]/g, encodeURIComponent)}")`;
    }

    function icon(name, className) {
        const span = document.createElement("span");
        span.className = `material-icons ${name}${className ? " " + className : ""}`;
        span.setAttribute("aria-hidden", "true");
        return span;
    }

    function setMessage(element, text, isError) {
        element.textContent = text || "";
        element.classList.toggle("requestsMessage-error", Boolean(isError));
    }

    function setButtonText(button, text) {
        const label = button.querySelector("span") || button;
        label.textContent = text;
    }

    // ---------- settings ----------

    function fillSelect(select, values, labels) {
        const previous = select.value;
        select.replaceChildren(...values.map(function (value, index) {
            return new Option(labels ? labels[index] : value, value);
        }));
        if (values.includes(previous)) {
            select.value = previous;
        }
    }

    async function loadSettings() {
        const [quality, media, statuses] = await Promise.allSettled([
            api("Requests/Configuration/QualityOptions"),
            api("Requests/Configuration/MediaOptions"),
            api("Requests/Configuration/StatusOptions")
        ]);

        if (statuses.status === "fulfilled") {
            const options = field(statuses.value, "options") || [];
            state.statuses = new Map(options.map((option) => [field(option, "id"), { name: field(option, "name"), color: field(option, "color") }]));
            state.initialStatus = options.length ? field(options[0], "id") : "Pending";
            // Status names/colours may have changed: redraw what's on screen.
            renderMyRequests();
            refreshCardStates();
        }

        if (quality.status === "fulfilled") {
            const options = field(quality.value, "options");
            state.quality = Array.isArray(options) && options.length ? options : DEFAULT_QUALITY;
        }
        fillSelect(el.quality, state.quality);

        if (media.status === "fulfilled") {
            state.media = {
                movies: Boolean(field(media.value, "movies")),
                tvShows: Boolean(field(media.value, "tvShows"))
            };
        }

        const values = [];
        const labels = [];
        if (state.media.movies && state.media.tvShows) {
            values.push("All");
            labels.push("All");
        }
        if (state.media.movies) {
            values.push("Movies");
            labels.push("Movies");
        }
        if (state.media.tvShows) {
            values.push("TV Shows");
            labels.push("TV Shows");
        }
        fillSelect(el.type, values, labels);
        // Only one type enabled: nothing to choose.
        el.typeContainer.hidden = values.length <= 1;

        const label = state.media.movies && !state.media.tvShows ? "Search movies"
            : !state.media.movies && state.media.tvShows ? "Search TV shows"
                : "Search movies and TV shows";
        $("label[for=requestsSearchInput]").textContent = label;
    }

    // ---------- my requests ----------

    function renderMyRequests() {
        el.myList.replaceChildren();
        if (state.myRequests.length === 0) {
            setMessage(el.myStatus, "You haven't requested anything yet.");
            return;
        }
        setMessage(el.myStatus, "");

        const fragment = document.createDocumentFragment();
        state.myRequests.forEach(function (request) {
            const status = field(request, "status") || "Pending";
            const title = field(request, "title") || "Untitled";
            const year = field(request, "productionYear");

            // visualCardBox gives the theme's card background, like Jellyfin's own cards.
            const item = document.createElement("div");
            item.className = "visualCardBox requestsMine-item";

            const poster = document.createElement("div");
            poster.className = "requestsMine-poster defaultCardBackground";
            const posterUrl = field(request, "posterUrl");
            if (posterUrl) {
                poster.style.backgroundImage = cssUrl(posterUrl);
            } else {
                poster.append(icon(isTv(request) ? "tv" : "movie"));
            }

            const body = document.createElement("div");
            body.className = "requestsMine-body";

            const heading = document.createElement("div");
            heading.className = "requestsMine-title";
            heading.textContent = title + (year ? ` (${year})` : "");

            const details = document.createElement("div");
            details.className = "secondaryText";
            details.textContent = [field(request, "mediaType"), scopeLabel(request), field(request, "mediaQuality")]
                .filter(Boolean).join(" · ");

            // The note the user added, on its own line (full text on hover when it's cut off).
            const noteText = String(field(request, "note") || "").trim();
            const note = document.createElement("div");
            note.className = "secondaryText requestsMine-note";
            note.textContent = noteText ? `“${noteText}”` : "";
            note.title = noteText;
            note.hidden = !noteText;

            const date = document.createElement("div");
            date.className = "secondaryText";
            const requested = formatDate(field(request, "requestedAtUtc"));
            date.textContent = requested ? `Requested ${requested}` : "";

            const footer = document.createElement("div");
            footer.className = "requestsMine-footer";
            const badge = document.createElement("span");
            badge.className = `requestsStatusBadge ${statusClass(status)}`;
            badge.textContent = statusLabel(status);
            badge.style.color = statusColor(status);
            footer.append(badge);

            // Cancelling is allowed only while the request is still in the first status.
            if (status === state.initialStatus) {
                const cancel = document.createElement("button");
                cancel.type = "button";
                cancel.className = "paper-icon-button-light requestsMine-cancel";
                cancel.title = "Cancel request";
                cancel.setAttribute("aria-label", `Cancel request for ${title}`);
                cancel.append(icon("delete"));
                cancel.addEventListener("click", function () {
                    cancelRequest(request, cancel);
                });
                footer.append(cancel);
            }

            body.append(heading, details, note, date, footer);
            item.append(poster, body);
            fragment.append(item);
        });
        el.myList.append(fragment);
    }

    async function loadMyRequests() {
        if (!state.myRequests.length) {
            setMessage(el.myStatus, "Loading...");
        }
        try {
            const requests = await api("Requests/my");
            state.myRequests = Array.isArray(requests) ? requests : [];
            state.requested.clear();
            state.myRequests.forEach(function (request) {
                const key = mediaKey(request);
                // Prefer the "whole item" request's status when a show has several requests.
                if (key && (!state.requested.has(key) || field(request, "requestScope") === "EntireSeries")) {
                    state.requested.set(key, field(request, "status") || "Pending");
                }
            });
            renderMyRequests();
            refreshCardStates();
        } catch (error) {
            setMessage(el.myStatus, error.message || "Unable to load your requests.", true);
        }
    }

    async function cancelRequest(request, button) {
        const title = field(request, "title") || "this item";
        if (!window.confirm(`Cancel your request for ${title}?`)) {
            return;
        }
        button.disabled = true;
        try {
            await api(`Requests/${field(request, "id")}`, { method: "DELETE" });
            await loadMyRequests();
        } catch (error) {
            button.disabled = false;
            setMessage(el.myStatus, error.message || "Unable to cancel the request.", true);
        }
    }

    // ---------- search results ----------

    function cardState(item) {
        if (field(item, "inLibrary")) {
            return { inLibrary: true, text: "In library", icon: "check", indicatorClass: "playedIndicator", statusClass: "" };
        }
        // Your own request shows its status; someone else's shows "Already requested"
        // (the server reports that as requestStatus on each search result).
        const mine = state.requested.get(mediaKey(item));
        const status = mine || field(item, "requestStatus");
        if (status) {
            // Same indicator markup Jellyfin uses for its own card badges (countIndicator gets the
            // theme's accent background, like the played check), with a clear icon per status.
            return {
                inLibrary: false,
                mine: Boolean(mine),
                status: status,
                text: mine ? statusLabel(status) : "Already requested",
                icon: STATUS_ICONS[status] || "label",
                indicatorClass: "countIndicator requestsIndicator",
                statusClass: statusClass(status),
                color: mine ? statusColor(status) : ""
            };
        }
        return null;
    }

    // Icons for the built-in statuses; statuses an administrator adds get a generic label icon.
    const STATUS_ICONS = {
        Pending: "schedule",
        Approved: "thumb_up",
        InProgress: "sync",
        Completed: "check",
        Denied: "block",
        Cancelled: "block"
    };

    function updateCard(card, item) {
        const indicators = card.querySelector(".cardIndicators");
        const stateLine = card.querySelector(".requestsCard-state");
        const current = cardState(item);
        indicators.replaceChildren();
        stateLine.textContent = current ? current.text : " ";
        stateLine.className = "cardText cardTextCentered cardText-secondary requestsCard-state"
            + (current && current.statusClass ? " " + current.statusClass : "");
        stateLine.style.color = current && current.color ? current.color : "";
        if (current) {
            const indicator = document.createElement("div");
            indicator.className = `indicator ${current.indicatorClass}`;
            indicator.title = current.text;
            indicator.append(icon(current.icon, "indicatorIcon"));
            indicators.append(indicator);
        }
    }

    function refreshCardStates() {
        el.results.querySelectorAll(".requestsCard").forEach(function (card) {
            const item = state.results[Number(card.dataset.index)];
            if (item) {
                updateCard(card, item);
            }
        });
    }

    function renderCard(item, index) {
        const card = document.createElement("button");
        card.type = "button";
        card.className = "card portraitCard card-hoverable requestsCard";
        card.dataset.index = String(index);
        const year = field(item, "productionYear");
        card.setAttribute("aria-label", `${field(item, "name")}${year ? ` (${year})` : ""}`);

        const box = document.createElement("div");
        box.className = "cardBox cardBox-bottompadded";

        const scalable = document.createElement("div");
        scalable.className = "cardScalable";
        const padder = document.createElement("div");
        padder.className = "cardPadder cardPadder-portrait";
        const image = document.createElement("div");
        const poster = field(item, "posterUrl");
        if (poster) {
            image.className = "cardImageContainer coveredImage cardContent";
            image.style.backgroundImage = cssUrl(poster);
        } else {
            image.className = `cardImageContainer coveredImage cardContent defaultCardBackground defaultCardBackground${(index % 5) + 1}`;
            image.append(icon(isTv(item) ? "tv" : "movie", "cardImageIcon"));
        }
        const indicators = document.createElement("div");
        indicators.className = "cardIndicators";
        scalable.append(padder, image, indicators);

        const name = document.createElement("div");
        name.className = "cardText cardTextCentered cardText-first";
        const bdi = document.createElement("bdi");
        bdi.textContent = field(item, "name") || "Untitled";
        name.append(bdi);

        const secondary = document.createElement("div");
        secondary.className = "cardText cardTextCentered cardText-secondary";
        secondary.textContent = [year, isTv(item) ? "TV Show" : "Movie"].filter(Boolean).join(" · ");

        const stateLine = document.createElement("div");
        stateLine.className = "cardText cardTextCentered cardText-secondary requestsCard-state";

        box.append(scalable, name, secondary, stateLine);
        card.append(box);
        updateCard(card, item);
        return card;
    }

    async function search(query) {
        if (state.searchController) {
            state.searchController.abort();
        }
        const controller = new AbortController();
        state.searchController = controller;

        setMessage(el.status, "Searching...");
        el.searchButton.disabled = true;

        try {
            const type = el.typeContainer.hidden ? (el.type.value || "All") : el.type.value;
            const results = await api(
                `Requests/Search?query=${encodeURIComponent(query)}&mediaType=${encodeURIComponent(type || "All")}`,
                { signal: controller.signal });
            state.results = Array.isArray(results) ? results : [];

            const fragment = document.createDocumentFragment();
            state.results.forEach((item, index) => fragment.append(renderCard(item, index)));
            el.results.replaceChildren(fragment);
            setMessage(el.status, state.results.length
                ? `${state.results.length} result${state.results.length === 1 ? "" : "s"}`
                : `No results found for "${query}".`);
        } catch (error) {
            if (error.name === "AbortError") {
                return;
            }
            el.results.replaceChildren();
            setMessage(el.status, error.message || "Search failed.", true);
        } finally {
            if (state.searchController === controller) {
                state.searchController = null;
                el.searchButton.disabled = false;
            }
        }
    }

    // ---------- request dialog ----------

    function buildSeasonList() {
        if (el.seasonList.childElementCount) {
            el.seasonList.querySelectorAll("input").forEach((box) => { box.checked = false; });
            return;
        }
        const fragment = document.createDocumentFragment();
        for (let season = 1; season <= MAX_SEASONS; season++) {
            const label = document.createElement("label");
            label.className = "requestsChoice";
            const box = document.createElement("input");
            box.type = "checkbox";
            box.value = String(season);
            const text = document.createElement("span");
            text.textContent = `Season ${season}`;
            label.append(box, text);
            fragment.append(label);
        }
        el.seasonList.append(fragment);
    }

    function selectedScope() {
        const checked = dialog.querySelector('input[name="requestsTvScope"]:checked');
        return checked ? checked.value : "EntireSeries";
    }

    function syncScopeVisibility() {
        const scope = selectedScope();
        el.seasonList.hidden = scope !== "SeasonSelection";
        el.episodeList.hidden = scope !== "EpisodeSelection";
    }

    function openDialog(item, trigger) {
        state.dialogItem = item;
        state.lastFocus = trigger || document.activeElement;
        const tv = isTv(item);
        const year = field(item, "productionYear");
        const current = cardState(item);

        el.dialogTitle.textContent = (field(item, "name") || "Request") + (year ? ` (${year})` : "");
        el.dialogMeta.textContent = [tv ? "TV Show" : "Movie", field(item, "sourceProvider")].filter(Boolean).join(" · ");
        el.dialogOverview.textContent = field(item, "overview") || "No overview available.";

        const poster = field(item, "posterUrl");
        el.dialogPoster.style.backgroundImage = poster ? cssUrl(poster) : "";
        el.dialogPoster.classList.toggle("defaultCardBackground", !poster);

        el.tvScope.hidden = !tv;
        if (tv) {
            dialog.querySelector('input[name="requestsTvScope"][value="EntireSeries"]').checked = true;
            buildSeasonList();
            el.episodeIds.value = "";
            syncScopeVisibility();
        }

        fillSelect(el.quality, state.quality);
        el.note.value = "";

        // Movies: one request, and nothing to request once it's in the library.
        // TV shows: still allow requests for missing seasons/episodes.
        let blocked = "";
        let note = "";
        const who = current && current.mine ? "You already requested" : "Someone already requested";
        if (current && !tv) {
            blocked = current.inLibrary
                ? "This movie is already in the library."
                : `${who} this movie (${statusLabel(current.status)}).`;
        } else if (current && tv) {
            note = current.inLibrary
                ? "This show is already in the library. You can still request missing seasons or episodes."
                : `${who} this show (${statusLabel(current.status)}). You can still request other seasons or episodes.`;
        }
        setMessage(el.dialogMessage, blocked || note);
        el.submit.disabled = Boolean(blocked);
        setButtonText(el.submit, "Submit Request");

        dialog.hidden = false;
        (tv ? dialog.querySelector('input[name="requestsTvScope"]:checked') : el.quality).focus();
    }

    function closeDialog() {
        if (dialog.hidden) {
            return;
        }
        dialog.hidden = true;
        state.dialogItem = null;
        if (state.lastFocus && typeof state.lastFocus.focus === "function" && state.lastFocus.isConnected) {
            state.lastFocus.focus();
        }
    }

    function buildPayload(item) {
        const tv = isTv(item);
        const scope = tv ? selectedScope() : "EntireMovie";
        const seasons = scope === "SeasonSelection"
            ? Array.from(el.seasonList.querySelectorAll("input:checked")).map((box) => Number(box.value))
            : [];
        const episodes = scope === "EpisodeSelection"
            ? el.episodeIds.value.split(",").map((value) => value.trim().toUpperCase()).filter(Boolean)
            : [];

        if (scope === "SeasonSelection" && seasons.length === 0) {
            throw new Error("Select at least one season.");
        }
        if (scope === "EpisodeSelection") {
            if (episodes.length === 0) {
                throw new Error("Enter at least one episode, e.g. S01E01.");
            }
            const invalid = episodes.filter((value) => !EPISODE_PATTERN.test(value));
            if (invalid.length) {
                throw new Error(`Use the SxxEyy format for episodes: ${invalid.join(", ")}`);
            }
        }

        return {
            mediaId: field(item, "providerId") || "",
            title: field(item, "name") || "",
            mediaType: tv ? "TV Show" : "Movie",
            overview: field(item, "overview") || "",
            posterUrl: field(item, "posterUrl") || "",
            providerId: field(item, "providerId") || "",
            providerIds: field(item, "providerIds") || {},
            productionYear: field(item, "productionYear") || null,
            sourceProvider: field(item, "sourceProvider") || "",
            mediaCategory: tv ? "TVShows" : "Movies",
            requestScope: scope,
            requestKind: !tv ? "Movie" : scope === "SeasonSelection" ? "Season" : scope === "EpisodeSelection" ? "Episode" : "Series",
            requestedSeasonNumbers: seasons,
            requestedEpisodeIds: episodes,
            requestedEpisodeNames: episodes,
            note: el.note.value.trim(),
            mediaQuality: el.quality.value || ""
        };
    }

    async function submitRequest() {
        const item = state.dialogItem;
        if (!item || state.submitting) {
            return;
        }

        let payload;
        try {
            payload = buildPayload(item);
        } catch (error) {
            setMessage(el.dialogMessage, error.message, true);
            return;
        }

        state.submitting = true;
        el.submit.disabled = true;
        setButtonText(el.submit, "Submitting...");
        try {
            await api("Requests", { method: "POST", body: payload });
            closeDialog();
            setMessage(el.status, `Requested ${payload.title}.`);
            await loadMyRequests();
        } catch (error) {
            setMessage(el.dialogMessage, error.message || "Unable to submit the request.", true);
            el.submit.disabled = false;
        } finally {
            state.submitting = false;
            setButtonText(el.submit, "Submit Request");
        }
    }

    // ---------- wiring ----------

    el.form.addEventListener("submit", function (event) {
        event.preventDefault();
        const query = el.input.value.trim();
        if (query) {
            search(query);
        } else {
            setMessage(el.status, "Enter a title to search.", true);
        }
    });

    // One delegated listener for every result card.
    el.results.addEventListener("click", function (event) {
        const card = event.target instanceof Element ? event.target.closest(".requestsCard") : null;
        const item = card && state.results[Number(card.dataset.index)];
        if (item) {
            openDialog(item, card);
        }
    });

    dialog.addEventListener("click", function (event) {
        if (event.target instanceof Element && event.target.closest("[data-dialog-close]")) {
            closeDialog();
        }
    });
    dialog.addEventListener("change", function (event) {
        if (event.target.name === "requestsTvScope") {
            syncScopeVisibility();
        }
    });
    el.submit.addEventListener("click", submitRequest);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && !dialog.hidden) {
            event.stopPropagation();
            closeDialog();
        }
    }, true);

    // Themes give the search box, type picker and raised button different heights; match the
    // picker and button to the search box (they're already bottom-aligned) so the row lines up.
    function alignSearchControls() {
        const height = el.input.offsetHeight;
        if (!height) {
            return;
        }
        [el.searchButton, el.type].forEach(function (control) {
            control.style.boxSizing = "border-box";
            control.style.height = height + "px";
            control.style.paddingTop = "0";
            control.style.paddingBottom = "0";
        });
    }

    window.addEventListener("resize", alignSearchControls);

    let lastRefresh = 0;
    function show() {
        window.requestAnimationFrame(alignSearchControls);
        window.setTimeout(alignSearchControls, 250);
        // Cheap refresh so admin setting changes and status updates show up when reopening.
        const now = Date.now();
        if (now - lastRefresh > 5000) {
            lastRefresh = now;
            loadSettings();
            loadMyRequests();
        }
        if (!el.input.value) {
            el.input.focus({ preventScroll: true });
        }
    }

    window.RequestsPage = {
        show: show,
        hide: closeDialog,
        hasOpenDialog: () => !dialog.hidden
    };
}());
