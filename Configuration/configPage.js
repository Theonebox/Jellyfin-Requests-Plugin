// Administrator settings controller for the Requests plugin.
//
// Jellyfin loads this file as an ES module through the page's
// data-controller="__plugin/RequestsConfigurationjs" attribute and creates
// one controller per view. All listeners are bound to elements inside the
// view, so nothing outlives the page when Jellyfin destroys it.

function apiUrl(path) {
    const client = window.ApiClient;
    const relative = String(path).replace(/^\/+/, "");
    return client && typeof client.getUrl === "function" ? client.getUrl(relative) : "/" + relative;
}

function authHeaders() {
    const client = window.ApiClient;
    const token = client && typeof client.accessToken === "function" ? client.accessToken() : "";
    return token ? { Authorization: `MediaBrowser Token="${token}"` } : {};
}

async function api(path, method, body) {
    const headers = { Accept: "application/json", ...authHeaders() };
    if (body !== undefined) {
        headers["Content-Type"] = "application/json";
    }

    const response = await fetch(apiUrl(path), {
        method: method || "GET",
        headers: headers,
        body: body === undefined ? undefined : JSON.stringify(body)
    });

    const text = await response.text();
    let data = null;
    if (text) {
        try {
            data = JSON.parse(text);
        } catch (_) {
            data = text;
        }
    }

    if (!response.ok) {
        const message = data && typeof data === "object" && (data.error || data.Error || data.title || data.message);
        throw new Error(`${message || "Request failed"} (${response.status})`);
    }

    return data;
}

// Jellyfin serializes API responses in PascalCase unless the client asks for
// camelCase, so read either form.
function value(item, name) {
    if (!item) {
        return undefined;
    }
    const upper = name.charAt(0).toUpperCase() + name.slice(1);
    return item[name] !== undefined ? item[name] : item[upper];
}

function escapeHtml(text) {
    const span = document.createElement("span");
    span.textContent = text == null ? "" : String(text);
    return span.innerHTML;
}

function formatDate(input, withTime) {
    if (!input) {
        return "Unknown";
    }
    const date = new Date(input);
    if (Number.isNaN(date.getTime())) {
        return "Unknown";
    }
    return withTime ? date.toLocaleString() : date.toLocaleDateString();
}

function scopeLabel(request) {
    const current = value(request, "requestScope");
    if (current === "SeasonSelection") {
        return "Seasons " + (value(request, "requestedSeasonNumbers") || []).join(", ");
    }
    if (current === "EpisodeSelection") {
        return "Episodes " + (value(request, "requestedEpisodeNames") || value(request, "requestedEpisodeIds") || []).join(", ");
    }
    // Movies are always the whole movie, so there's nothing to say.
    return current === "EntireSeries" ? "Entire series" : "";
}

// Class hook for custom CSS, e.g. .requests-status-in-progress.
function statusClass(status) {
    return "requests-status-" + String(status || "")
        .replace(/([a-z])([A-Z])/g, "$1-$2")
        .toLowerCase()
        .replace(/[^a-z0-9-]/g, "");
}

export default function (view, params) {
    // "RequestsManage" is the dashboard sidebar entry for this same page (see Plugin.GetPages).
    const pageName = (params && params.name)
        || new URLSearchParams(window.location.hash.split("?")[1] || window.location.search).get("name");
    let initialTab = pageName === "RequestsManage" ? "requests" : null;

    const $ = (selector) => view.querySelector(selector);

    const state = {
        requests: [],
        qualityOptions: [],
        statuses: [], // [{ id, name, color, icon }] in display order; the first is given to new requests
        icons: [], // [{ key, label, path }] the icons a status can use (from the server)
        descending: true,
        generalLoaded: false,
        isAdministrator: null
    };

    function setMessage(selector, text, isError) {
        const element = $(selector);
        element.textContent = text || "";
        element.classList.toggle("error", Boolean(isError));
    }

    function showLoading(show) {
        const loading = window.Dashboard;
        if (loading && typeof loading.showLoadingMsg === "function" && typeof loading.hideLoadingMsg === "function") {
            if (show) {
                loading.showLoadingMsg();
            } else {
                loading.hideLoadingMsg();
            }
        }
    }

    function findStatus(id) {
        return state.statuses.find((status) => status.id === id);
    }

    // Display name for a stored status id (ids never change; names are editable).
    function statusLabel(id) {
        const status = findStatus(id);
        return status ? status.name : String(id || "").replace(/([a-z])([A-Z])/g, "$1 $2");
    }

    function statusColor(id) {
        const status = findStatus(id);
        return status ? status.color : "";
    }

    // ---------- Tabs ----------

    function selectTab(name) {
        view.querySelectorAll(".requests-config-tab").forEach(function (tab) {
            const active = tab.dataset.tab === name;
            tab.classList.toggle("requests-config-tab-active", active);
            tab.setAttribute("aria-selected", String(active));
        });
        view.querySelectorAll(".requests-config-panel").forEach(function (panel) {
            panel.hidden = panel.dataset.panel !== name;
        });
        if (name === "requests") {
            loadRequests();
        }
    }

    // ---------- General tab ----------

    function normalizeQuality(options) {
        return (options || []).map((option) => (typeof option === "string"
            ? { name: option, description: "" }
            : { name: value(option, "name") || "", description: value(option, "description") || "" }));
    }

    function renderQualityOptions() {
        const target = $("#requestsQualityOptions");
        target.replaceChildren();
        state.qualityOptions.forEach(function (option, index) {
            const row = document.createElement("div");
            row.className = "requests-quality-row";

            const update = (changes) => saveQualityOptions(state.qualityOptions.map((item, i) => (i === index ? { ...item, ...changes } : item)));

            const name = document.createElement("input");
            name.type = "text";
            name.value = option.name;
            name.className = "requests-quality-name";
            name.setAttribute("aria-label", `Media quality option ${index + 1}`);
            name.addEventListener("change", function () {
                const next = name.value.trim();
                if (!next) {
                    name.value = option.name;
                    return;
                }
                update({ name: next });
            });

            // Optional detail shown to users next to the quality (not in the admin requests table).
            const description = document.createElement("input");
            description.type = "text";
            description.value = option.description;
            description.className = "requests-quality-description";
            description.placeholder = "Description (optional)";
            description.maxLength = 200;
            description.setAttribute("aria-label", `Description for ${option.name}`);
            description.addEventListener("change", () => update({ description: description.value.trim() }));

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "raised emby-button";
            remove.textContent = "Remove";
            remove.disabled = state.qualityOptions.length <= 1;
            remove.title = remove.disabled ? "At least one quality option is required." : "Remove this option";
            remove.addEventListener("click", function () {
                saveQualityOptions(state.qualityOptions.filter((_, i) => i !== index));
            });

            const move = createMoveButtons(index, state.qualityOptions.length, `"${option.name}"`,
                (from, to) => saveQualityOptions(moved(state.qualityOptions, from, to)));
            row.append(name, description, move, remove);
            target.append(row);
        });
    }

    async function saveQualityOptions(options, focusLast) {
        try {
            const result = await api("Requests/Configuration/QualityOptions", "PUT", { options: options });
            state.qualityOptions = normalizeQuality(value(result, "options") || options);
            setMessage("#requestsGeneralMessage", "Quality options saved.");
        } catch (error) {
            setMessage("#requestsGeneralMessage", error.message, true);
        }
        renderQualityOptions();
        if (focusLast) {
            const inputs = view.querySelectorAll("#requestsQualityOptions .requests-quality-name");
            const last = inputs[inputs.length - 1];
            if (last) {
                last.focus();
                last.select();
            }
        }
    }

    // ---------- Status options ----------

    function renderStatusOptions() {
        const target = $("#requestsStatusOptions");
        target.replaceChildren();
        state.statuses.forEach(function (status, index) {
            const row = document.createElement("div");
            row.className = "requests-quality-row requests-status-row";

            const color = document.createElement("input");
            color.type = "color";
            color.value = /^#[0-9a-f]{6}$/i.test(status.color) ? status.color : "#bdbdbd";
            color.className = "requests-status-color";
            color.setAttribute("aria-label", `Color for ${status.name}`);
            color.title = "Color";
            color.addEventListener("change", function () {
                saveStatusOptions(state.statuses.map((item, i) => (i === index ? { ...item, color: color.value } : item)));
            });

            const name = document.createElement("input");
            name.type = "text";
            name.value = status.name;
            name.style.color = status.color;
            name.setAttribute("aria-label", `Status ${index + 1} name`);
            name.addEventListener("change", function () {
                const next = name.value.trim();
                if (!next) {
                    name.value = status.name;
                    return;
                }
                saveStatusOptions(state.statuses.map((item, i) => (i === index ? { ...item, name: next } : item)));
            });

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "raised emby-button";
            remove.textContent = "Remove";
            remove.disabled = state.statuses.length <= 1;
            remove.title = remove.disabled ? "At least one status is required." : "Remove this status";
            remove.addEventListener("click", async function () {
                // A removed status would leave its requests with no name to show, so only allow
                // removing statuses no request is using.
                let inUse = 0;
                try {
                    inUse = ((await api("Requests")) || []).filter((request) => value(request, "status") === status.id).length;
                } catch (error) {
                    setMessage("#requestsGeneralMessage", error.message, true);
                    return;
                }
                if (inUse) {
                    setMessage("#requestsGeneralMessage",
                        `"${status.name}" is used by ${inUse} request${inUse === 1 ? "" : "s"}. Move ${inUse === 1 ? "it" : "them"} to another status on the Requests tab first.`, true);
                    return;
                }
                if (window.confirm(`Remove the "${status.name}" status?`)) {
                    saveStatusOptions(state.statuses.filter((_, i) => i !== index));
                }
            });

            if (index === 0) {
                name.title = "New requests start in this status.";
            }
            const move = createMoveButtons(index, state.statuses.length, `"${status.name}"`,
                (from, to) => saveStatusOptions(moved(state.statuses, from, to)));
            row.append(color, name, createIconButton(status, index), move, remove);
            target.append(row);
        });
        populateStatusFilter();
    }

    const ARROW_UP = "M7.41 15.41 12 10.83l4.59 4.58L18 14l-6-6-6 6z";
    const ARROW_DOWN = "M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6z";

    // Up/down buttons for reordering a list row. onMove(from, to) saves the new order.
    function createMoveButtons(index, count, label, onMove) {
        const group = document.createElement("div");
        group.className = "requests-move-buttons";
        [["up", ARROW_UP, index - 1], ["down", ARROW_DOWN, index + 1]].forEach(function ([direction, path, target]) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "requests-move-button";
            button.title = `Move ${direction}`;
            button.setAttribute("aria-label", `Move ${label} ${direction}`);
            button.disabled = target < 0 || target >= count;
            button.append(svg(path, "requests-icon-svg"));
            button.addEventListener("click", () => onMove(index, target));
            group.append(button);
        });
        return group;
    }

    function moved(list, from, to) {
        const copy = list.slice();
        const [item] = copy.splice(from, 1);
        copy.splice(to, 0, item);
        return copy;
    }

    function svg(path, className) {
        const ns = "http://www.w3.org/2000/svg";
        const element = document.createElementNS(ns, "svg");
        element.setAttribute("viewBox", "0 0 24 24");
        element.setAttribute("aria-hidden", "true");
        element.setAttribute("class", className);
        const shape = document.createElementNS(ns, "path");
        shape.setAttribute("d", path || "");
        element.append(shape);
        return element;
    }

    function iconFor(key) {
        return state.icons.find((icon) => icon.key === key) || state.icons.find((icon) => icon.key === "bookmark");
    }

    // The icon button shows the status's current icon (in its color); clicking it opens a grid
    // of the available icons. Picking one saves immediately.
    function createIconButton(status, index) {
        const wrapper = document.createElement("div");
        wrapper.className = "requests-icon-picker";

        const current = iconFor(status.icon);
        const button = document.createElement("button");
        button.type = "button";
        button.className = "requests-icon-button";
        button.style.color = status.color;
        button.title = `Icon: ${current ? current.label : "none"}`;
        button.setAttribute("aria-label", `Icon for ${status.name}`);
        button.setAttribute("aria-haspopup", "true");
        button.setAttribute("aria-expanded", "false");
        button.append(svg(current && current.path, "requests-icon-svg"));

        const menu = document.createElement("div");
        menu.className = "requests-icon-menu";
        menu.setAttribute("role", "menu");
        menu.hidden = true;
        state.icons.forEach(function (icon) {
            const choice = document.createElement("button");
            choice.type = "button";
            choice.className = "requests-icon-choice" + (icon.key === (current && current.key) ? " is-selected" : "");
            choice.title = icon.label;
            choice.setAttribute("role", "menuitemradio");
            choice.setAttribute("aria-checked", String(icon.key === (current && current.key)));
            choice.setAttribute("aria-label", icon.label);
            choice.append(svg(icon.path, "requests-icon-svg"));
            choice.addEventListener("click", function () {
                closeIconMenus();
                if (icon.key !== status.icon) {
                    saveStatusOptions(state.statuses.map((item, i) => (i === index ? { ...item, icon: icon.key } : item)));
                }
            });
            menu.append(choice);
        });

        button.addEventListener("click", function (event) {
            event.stopPropagation();
            const opening = menu.hidden;
            closeIconMenus();
            if (opening) {
                menu.hidden = false;
                button.setAttribute("aria-expanded", "true");
                (menu.querySelector(".is-selected") || menu.firstElementChild)?.focus();
            }
        });

        wrapper.append(button, menu);
        return wrapper;
    }

    function closeIconMenus() {
        view.querySelectorAll(".requests-icon-menu").forEach((menu) => { menu.hidden = true; });
        view.querySelectorAll(".requests-icon-button").forEach((button) => button.setAttribute("aria-expanded", "false"));
    }

    async function saveStatusOptions(options, focusLast) {
        try {
            const result = await api("Requests/Configuration/StatusOptions", "PUT", { options: options });
            state.statuses = normalizeStatuses(value(result, "options"));
            setMessage("#requestsGeneralMessage", "Status options saved.");
        } catch (error) {
            setMessage("#requestsGeneralMessage", error.message, true);
        }
        renderStatusOptions();
        if (focusLast) {
            const inputs = view.querySelectorAll("#requestsStatusOptions input[type=text]");
            const last = inputs[inputs.length - 1];
            if (last) {
                last.focus();
                last.select();
            }
        }
    }

    function normalizeStatuses(options) {
        return (options || []).map((option) => ({
            id: value(option, "id"),
            name: value(option, "name"),
            color: value(option, "color"),
            icon: value(option, "icon")
        }));
    }

    function populateStatusFilter() {
        const select = $("#requestsAdminStatus");
        const current = select.value;
        select.replaceChildren(new Option("All statuses", ""), ...state.statuses.map(function (status) {
            const option = new Option(status.name, status.id);
            option.style.color = status.color;
            return option;
        }));
        select.value = state.statuses.some((status) => status.id === current) ? current : "";
    }

    async function saveMediaOptions(changed) {
        const movies = $("#requestsEnableMovies");
        const tvShows = $("#requestsEnableTvShows");
        if (!movies.checked && !tvShows.checked) {
            changed.checked = true;
            setMessage("#requestsGeneralMessage", "At least one media type must remain enabled.", true);
            return;
        }

        movies.disabled = true;
        tvShows.disabled = true;
        try {
            await api("Requests/Configuration/MediaOptions", "PUT", { movies: movies.checked, tvShows: tvShows.checked });
            setMessage("#requestsGeneralMessage", "Media options saved.");
        } catch (error) {
            changed.checked = !changed.checked;
            setMessage("#requestsGeneralMessage", error.message, true);
        } finally {
            movies.disabled = false;
            tvShows.disabled = false;
        }
    }

    async function loadGeneral() {
        const settle = (promise) => promise.then(
            (result) => ({ ok: true, value: result }),
            (error) => ({ ok: false, error: error }));

        const [settings, diagnostics, providers] = await Promise.all([
            settle(api("Requests/Configuration/Client")),
            settle(api("Requests/Configuration/Diagnostics")),
            settle(api("Requests/Search/Providers"))
        ]);

        if (settings.ok) {
            const s = settings.value;
            state.icons = (value(s, "statusIcons") || []).map((icon) => ({ key: value(icon, "key"), label: value(icon, "label"), path: value(icon, "path") }));
            state.statuses = normalizeStatuses(value(s, "statusOptions"));
            renderStatusOptions();
            state.qualityOptions = normalizeQuality(value(s, "qualityOptions"));
            renderQualityOptions();
            $("#requestsEnableMovies").checked = Boolean(value(s, "movies"));
            $("#requestsEnableTvShows").checked = Boolean(value(s, "tvShows"));
            $("#requestsEnableAnimations").checked = value(s, "animations") !== false;
        } else {
            ["#requestsEnableMovies", "#requestsEnableTvShows", "#requestsEnableAnimations"].forEach((id) => { $(id).disabled = true; });
            $("#requestsQualityOptions").textContent = settings.error.message;
            $("#requestsStatusOptions").textContent = settings.error.message;
        }

        if (diagnostics.ok) {
            const d = diagnostics.value;
            $("#requestsGeneralVersion").textContent = value(d, "pluginVersion") || "Unknown";
            const counts = value(d, "statusCounts") || {};
            $("#requestsGeneralTotals").textContent = [`${value(d, "totalRequests") || 0} total`]
                .concat(Object.entries(counts).map(([id, count]) => `${count} ${statusLabel(id)}`))
                .join(" · ");
        } else {
            $("#requestsGeneralVersion").textContent = diagnostics.error.message;
            $("#requestsGeneralTotals").textContent = "Unavailable";
        }

        if (providers.ok) {
            const sources = value(providers.value, "metadataSources") || [];
            const status = value(providers.value, "status") || "Unknown";
            $("#requestsGeneralProviderStatus").textContent = sources.length ? `${status} (${sources.join(", ")})` : status;
        } else {
            $("#requestsGeneralProviderStatus").textContent = providers.error.message;
        }
    }

    async function saveAnimations(checkbox) {
        checkbox.disabled = true;
        try {
            await api("Requests/Configuration/Animations", "PUT", { enabled: checkbox.checked });
            setMessage("#requestsGeneralMessage", `Animations ${checkbox.checked ? "on" : "off"}.`);
        } catch (error) {
            checkbox.checked = !checkbox.checked;
            setMessage("#requestsGeneralMessage", error.message, true);
        } finally {
            checkbox.disabled = false;
        }
    }

    // ---------- Requests tab ----------

    function sortValue(request, sort) {
        switch (sort) {
            case "title": return String(value(request, "title") || "").toLowerCase();
            case "requester": return String(value(request, "userName") || "").toLowerCase();
            case "status": return String(value(request, "status") || "");
            default: return String(value(request, "requestedAtUtc") || "");
        }
    }

    function renderDetails(request) {
        const providerIds = value(request, "providerIds") || {};
        const history = (value(request, "statusHistory") || [])
            .map((change) => `${statusLabel(value(change, "status"))} (${formatDate(value(change, "changedAtUtc"), true)})`)
            .join(" → ");
        const year = value(request, "productionYear");

        $("#requestsDetailsList").innerHTML = [
            ["Overview", value(request, "overview")],
            ["Year", year],
            ["Requester", value(request, "userName")],
            ["Request Date", formatDate(value(request, "requestedAtUtc"))],
            ["Media Type", [value(request, "mediaType"), scopeLabel(request)].filter(Boolean).join(" · ")],
            ["Quality", value(request, "mediaQuality")],
            ["Status", statusLabel(value(request, "status"))],
            ["Status History", history],
            ["Search Provider", value(request, "sourceProvider")],
            ["Provider IDs", Object.entries(providerIds).map((pair) => `${pair[0]}: ${pair[1]}`).join(", ")],
            ["Notes", value(request, "note")],
            ["Response", value(request, "adminNote")]
        ].map((item) => `<dt>${escapeHtml(item[0])}</dt><dd>${escapeHtml(item[1] || "Not specified")}</dd>`).join("");

        $("#requestsDetailsTitle").textContent = value(request, "title") || "Request Details";
        const poster = $("#requestsDetailsPoster");
        const posterUrl = value(request, "posterUrl");
        poster.hidden = !posterUrl;
        if (posterUrl) {
            poster.src = posterUrl;
        } else {
            poster.removeAttribute("src");
        }
        poster.alt = value(request, "title") || "Requested media";
        $("#requestsAdminDetails").hidden = false;
    }

    // ---------- Replies to requesters ----------

    let responseTarget = null; // { request, button } while the reply dialog is open

    function updateResponseButton(button, request) {
        const note = value(request, "adminNote") || "";
        button.textContent = note ? "Edit reply" : "Reply";
        button.title = note ? `Your reply: ${note}` : "Send the requester a note (e.g. why it was denied)";
        button.classList.toggle("requests-response-sent", Boolean(note));
    }

    function openResponseDialog(request, button) {
        responseTarget = { request: request, button: button };
        const note = value(request, "adminNote") || "";
        $("#requestsResponseTitle").textContent = `Reply about "${value(request, "title") || "this request"}"`;
        $("#requestsResponseFor").textContent = `Shown to ${value(request, "userName") || "the requester"} next to the status in My Requests.`;
        $("#requestsResponseText").value = note;
        $("#requestsResponseClear").hidden = !note;
        setMessage("#requestsResponseMessage", "");
        $("#requestsResponseDialog").hidden = false;
        $("#requestsResponseText").focus();
    }

    function closeResponseDialog() {
        $("#requestsResponseDialog").hidden = true;
        if (responseTarget && responseTarget.button.isConnected) {
            responseTarget.button.focus();
        }
        responseTarget = null;
    }

    async function saveResponse(note) {
        if (!responseTarget) {
            return;
        }
        const { request, button } = responseTarget;
        const buttons = view.querySelectorAll("#requestsResponseDialog button");
        buttons.forEach((element) => { element.disabled = true; });
        try {
            await api(`Requests/${value(request, "id")}/response`, "PUT", { note: note });
            request.adminNote = note;
            request.AdminNote = note;
            updateResponseButton(button, request);
            closeResponseDialog();
            setMessage("#requestsAdminMessage", note ? `Reply saved for "${value(request, "title")}".` : `Reply removed from "${value(request, "title")}".`);
        } catch (error) {
            setMessage("#requestsResponseMessage", error.message, true);
        } finally {
            buttons.forEach((element) => { element.disabled = false; });
        }
    }

    function buildRow(request) {
        const id = value(request, "id");
        const posterUrl = value(request, "posterUrl");
        const status = value(request, "status") || "";
        const row = document.createElement("tr");
        // Configured statuses, plus the request's own if it was since removed from the list.
        const choices = state.statuses.concat(findStatus(status) ? [] : [{ id: status, name: statusLabel(status), color: "" }]);

        row.innerHTML =
            `<td>${posterUrl ? `<img src="${escapeHtml(posterUrl)}" alt="" loading="lazy">` : ""}</td>`
            + `<td><button type="button" class="requests-admin-title" data-details>${escapeHtml(value(request, "title") || "Untitled")}</button>`
            // Type, plus what was requested for TV (seasons/episodes); movies need no scope.
            + `<div class="fieldDescription">${escapeHtml([value(request, "mediaType"), scopeLabel(request)].filter(Boolean).join(" · "))}</div></td>`
            + `<td>${escapeHtml(value(request, "userName") || "Unknown")}</td>`
            + `<td>${escapeHtml(formatDate(value(request, "requestedAtUtc")))}</td>`
            + `<td>${escapeHtml(value(request, "mediaQuality") || "")}</td>`
            + `<td>${escapeHtml(value(request, "note") || "")}</td>`
            + `<td><button type="button" class="raised emby-button requests-response-button" data-response></button></td>`
            // Each option is colored individually so the open list shows every status in its own
            // color; the select's color only reflects the current status.
            + `<td><select class="requests-status-select ${statusClass(status)}" style="color:${escapeHtml(statusColor(status))}" data-status aria-label="Request status">${choices.map((option) =>
                `<option class="${statusClass(option.id)}" style="color:${escapeHtml(option.color)}" value="${escapeHtml(option.id)}"${option.id === status ? " selected" : ""}>${escapeHtml(option.name)}</option>`).join("")}</select></td>`
            + `<td><button type="button" class="raised emby-button" data-delete title="Delete request">Delete</button></td>`;

        row.querySelector("[data-details]").addEventListener("click", function () {
            renderDetails(request);
        });

        const responseButton = row.querySelector("[data-response]");
        updateResponseButton(responseButton, request);
        responseButton.addEventListener("click", () => openResponseDialog(request, responseButton));

        const select = row.querySelector("[data-status]");
        select.addEventListener("change", async function () {
            const previous = value(request, "status") || "";
            const next = select.value;
            select.disabled = true;
            try {
                const updated = await api(`Requests/${id}/status`, "PUT", { status: next });
                Object.assign(request, updated || {});
                request.status = next;
                request.Status = next;
                select.className = `requests-status-select ${statusClass(next)}`;
                select.style.color = statusColor(next);
                setMessage("#requestsAdminMessage", `"${value(request, "title")}" marked ${statusLabel(next)}.`);
            } catch (error) {
                select.value = previous;
                setMessage("#requestsAdminMessage", error.message, true);
            } finally {
                select.disabled = false;
            }
        });

        row.querySelector("[data-delete]").addEventListener("click", async function () {
            const button = this;
            if (!window.confirm(`Delete the request for "${value(request, "title")}"? This cannot be undone.`)) {
                return;
            }
            button.disabled = true;
            try {
                await api(`Requests/${id}`, "DELETE");
                state.requests = state.requests.filter((candidate) => value(candidate, "id") !== id);
                renderRequests();
                setMessage("#requestsAdminMessage", "Request deleted.");
            } catch (error) {
                button.disabled = false;
                setMessage("#requestsAdminMessage", error.message, true);
            }
        });

        return row;
    }

    function renderRequests() {
        const query = $("#requestsAdminSearch").value.trim().toLowerCase();
        const statusFilter = $("#requestsAdminStatus").value;
        const mediaFilter = $("#requestsAdminMediaType").value;
        const sort = $("#requestsAdminSort").value;
        const direction = state.descending ? -1 : 1;

        const filtered = state.requests.filter(function (request) {
            const haystack = [
                value(request, "title"),
                value(request, "userName"),
                value(request, "note"),
                Object.values(value(request, "providerIds") || {}).join(" ")
            ].join(" ").toLowerCase();
            return (!query || haystack.includes(query))
                && (!statusFilter || value(request, "status") === statusFilter)
                && (!mediaFilter || value(request, "mediaType") === mediaFilter);
        }).sort((left, right) => sortValue(left, sort).localeCompare(sortValue(right, sort)) * direction);

        const rows = $("#requestsAdminRows");
        rows.replaceChildren();
        if (filtered.length === 0) {
            const empty = document.createElement("tr");
            empty.innerHTML = `<td colspan="9" class="requests-admin-empty">${state.requests.length ? "No requests match the current filters." : "No requests have been submitted yet."}</td>`;
            rows.append(empty);
            return;
        }
        filtered.forEach((request) => rows.append(buildRow(request)));
    }

    async function loadRequests() {
        setMessage("#requestsAdminMessage", "Loading requests...");
        try {
            state.requests = (await api("Requests")) || [];
            setMessage("#requestsAdminMessage", `${state.requests.length} request${state.requests.length === 1 ? "" : "s"}.`);
        } catch (error) {
            state.requests = [];
            setMessage("#requestsAdminMessage", error.message, true);
        }
        renderRequests();
    }

    // ---------- Wiring (runs once per view instance) ----------

    view.querySelectorAll(".requests-config-tab").forEach(function (tab) {
        tab.addEventListener("click", () => selectTab(tab.dataset.tab));
    });

    ["#requestsAdminSearch", "#requestsAdminStatus", "#requestsAdminMediaType", "#requestsAdminSort"].forEach(function (selector) {
        const control = $(selector);
        control.addEventListener(control.tagName === "SELECT" ? "change" : "input", renderRequests);
    });

    $("#requestsAdminSortDirection").addEventListener("click", function () {
        state.descending = !state.descending;
        this.textContent = state.descending ? "Descending" : "Ascending";
        renderRequests();
    });

    $("#requestsAdminRefresh").addEventListener("click", loadRequests);

    view.querySelectorAll("[data-close-details]").forEach(function (element) {
        element.addEventListener("click", () => { $("#requestsAdminDetails").hidden = true; });
    });

    view.querySelectorAll("[data-close-response]").forEach((element) => element.addEventListener("click", closeResponseDialog));
    $("#requestsResponseSave").addEventListener("click", () => saveResponse($("#requestsResponseText").value.trim()));
    $("#requestsResponseClear").addEventListener("click", () => saveResponse(""));

    $("#requestsQualityAdd").addEventListener("click", function () {
        let name = "New option";
        let suffix = 2;
        const existing = state.qualityOptions.map((option) => option.name.toLowerCase());
        while (existing.includes(name.toLowerCase())) {
            name = `New option ${suffix++}`;
        }
        saveQualityOptions(state.qualityOptions.concat({ name: name, description: "" }), true);
    });

    $("#requestsStatusAdd").addEventListener("click", function () {
        // New statuses are sent without an id; the server assigns a permanent one.
        saveStatusOptions(state.statuses.concat({ id: "", name: "New status", color: "#bdbdbd", icon: "bookmark" }), true);
    });

    // Icon menus close on any outside click or Escape.
    view.addEventListener("click", function (event) {
        if (!event.target.closest(".requests-icon-picker")) {
            closeIconMenus();
        }
    });
    // Escape closes whatever is on top: an icon menu, the reply dialog or the details dialog.
    view.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") {
            return;
        }
        if (view.querySelector(".requests-icon-menu:not([hidden])")) {
            closeIconMenus();
        } else if (!$("#requestsResponseDialog").hidden) {
            closeResponseDialog();
        } else if (!$("#requestsAdminDetails").hidden) {
            $("#requestsAdminDetails").hidden = true;
        }
    });

    $("#requestsEnableMovies").addEventListener("change", function () { saveMediaOptions(this); });
    $("#requestsEnableTvShows").addEventListener("change", function () { saveMediaOptions(this); });
    $("#requestsEnableAnimations").addEventListener("change", function () { saveAnimations(this); });

    view.addEventListener("viewshow", async function () {
        showLoading(true);
        try {
            if (state.isAdministrator === null) {
                const user = await api("Users/Me");
                const policy = value(user, "policy") || {};
                state.isAdministrator = Boolean(value(policy, "isAdministrator"));
            }

            if (!state.isAdministrator) {
                $(".content-primary").innerHTML = '<h2 class="sectionTitle">Access denied</h2><p>Administrator access is required.</p>';
                return;
            }

            if (!state.generalLoaded) {
                state.generalLoaded = true;
                await loadGeneral();
            }

            if (initialTab) {
                // Opened from the dashboard sidebar entry: start on the Requests tab.
                const tab = initialTab;
                initialTab = null;
                selectTab(tab);
            } else if (view.querySelector(".requests-config-tab-active")?.dataset.tab === "requests") {
                await loadRequests();
            }
        } catch (error) {
            state.generalLoaded = false;
            $("#requestsGeneralVersion").textContent = "Unavailable";
            $("#requestsGeneralProviderStatus").textContent = "Unavailable";
            setMessage("#requestsGeneralMessage", error.message, true);
        } finally {
            showLoading(false);
        }
    });
}
