// Administrator settings controller for the Requests plugin.
//
// Jellyfin loads this file as an ES module through the page's
// data-controller="__plugin/RequestsConfigurationjs" attribute and creates
// one controller per view. All listeners are bound to elements inside the
// view, so nothing outlives the page when Jellyfin destroys it.

const STATUS_OPTIONS = ["Pending", "Approved", "Denied", "InProgress", "Available", "Completed", "Cancelled"];

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

function formatDate(input) {
    if (!input) {
        return "Unknown";
    }
    const date = new Date(input);
    return Number.isNaN(date.getTime()) ? "Unknown" : date.toLocaleString();
}

function scopeLabel(request) {
    const current = value(request, "requestScope");
    if (current === "SeasonSelection") {
        return "Seasons " + (value(request, "requestedSeasonNumbers") || []).join(", ");
    }
    if (current === "EpisodeSelection") {
        return "Episodes " + (value(request, "requestedEpisodeNames") || value(request, "requestedEpisodeIds") || []).join(", ");
    }
    return current === "EntireSeries" ? "Entire Series" : "Entire Movie";
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
        statuses: [], // [{ id, name, color }] in display order; the first is given to new requests
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

    function renderQualityOptions() {
        const target = $("#requestsQualityOptions");
        target.replaceChildren();
        state.qualityOptions.forEach(function (option, index) {
            const row = document.createElement("div");
            row.className = "requests-quality-row";

            const input = document.createElement("input");
            input.type = "text";
            input.value = option;
            input.setAttribute("aria-label", `Media quality option ${index + 1}`);
            input.addEventListener("change", function () {
                const next = input.value.trim();
                if (!next) {
                    input.value = state.qualityOptions[index];
                    return;
                }
                const updated = state.qualityOptions.slice();
                updated[index] = next;
                saveQualityOptions(updated);
            });

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "raised emby-button";
            remove.textContent = "Remove";
            remove.disabled = state.qualityOptions.length <= 1;
            remove.title = remove.disabled ? "At least one quality option is required." : "Remove this option";
            remove.addEventListener("click", function () {
                const updated = state.qualityOptions.slice();
                updated.splice(index, 1);
                saveQualityOptions(updated);
            });

            row.append(input, remove);
            target.append(row);
        });
    }

    async function saveQualityOptions(options, focusLast) {
        try {
            const result = await api("Requests/Configuration/QualityOptions", "PUT", { options: options });
            state.qualityOptions = value(result, "options") || options;
            setMessage("#requestsGeneralMessage", "Quality options saved.");
        } catch (error) {
            setMessage("#requestsGeneralMessage", error.message, true);
        }
        renderQualityOptions();
        if (focusLast) {
            const inputs = view.querySelectorAll("#requestsQualityOptions input");
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
            color.setAttribute("aria-label", `Colour for ${status.name}`);
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
            row.append(color, name, remove);
            target.append(row);
        });
        populateStatusFilter();
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
            color: value(option, "color")
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

        const [quality, diagnostics, media, providers, statuses] = await Promise.all([
            settle(api("Requests/Configuration/QualityOptions")),
            settle(api("Requests/Configuration/Diagnostics")),
            settle(api("Requests/Configuration/MediaOptions")),
            settle(api("Requests/Search/Providers")),
            settle(api("Requests/Configuration/StatusOptions"))
        ]);

        if (statuses.ok) {
            state.statuses = normalizeStatuses(value(statuses.value, "options"));
            renderStatusOptions();
        } else {
            $("#requestsStatusOptions").textContent = statuses.error.message;
        }

        if (quality.ok) {
            state.qualityOptions = value(quality.value, "options") || [];
            renderQualityOptions();
        } else {
            $("#requestsQualityOptions").textContent = quality.error.message;
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

        if (media.ok) {
            $("#requestsEnableMovies").checked = Boolean(value(media.value, "movies"));
            $("#requestsEnableTvShows").checked = Boolean(value(media.value, "tvShows"));
        } else {
            $("#requestsEnableMovies").disabled = true;
            $("#requestsEnableTvShows").disabled = true;
            setMessage("#requestsGeneralMessage", media.error.message, true);
        }

        if (providers.ok) {
            const sources = value(providers.value, "metadataSources") || [];
            const status = value(providers.value, "status") || "Unknown";
            $("#requestsGeneralProviderStatus").textContent = sources.length ? `${status} (${sources.join(", ")})` : status;
        } else {
            $("#requestsGeneralProviderStatus").textContent = providers.error.message;
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
            .map((change) => `${statusLabel(value(change, "status"))} (${formatDate(value(change, "changedAtUtc"))})`)
            .join(" → ");
        const year = value(request, "productionYear");

        $("#requestsDetailsList").innerHTML = [
            ["Overview", value(request, "overview")],
            ["Year", year],
            ["Requester", value(request, "userName")],
            ["Request Date", formatDate(value(request, "requestedAtUtc"))],
            ["Media Type", value(request, "mediaType")],
            ["Scope", scopeLabel(request)],
            ["Quality", value(request, "mediaQuality")],
            ["Status", statusLabel(value(request, "status"))],
            ["Status History", history],
            ["Search Provider", value(request, "sourceProvider")],
            ["Provider IDs", Object.entries(providerIds).map((pair) => `${pair[0]}: ${pair[1]}`).join(", ")],
            ["Notes", value(request, "note")]
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
            + `<div class="fieldDescription">${escapeHtml(value(request, "mediaType") || "")}</div></td>`
            + `<td>${escapeHtml(value(request, "userName") || "Unknown")}</td>`
            + `<td>${escapeHtml(formatDate(value(request, "requestedAtUtc")))}</td>`
            + `<td>${escapeHtml(scopeLabel(request))}</td>`
            + `<td>${escapeHtml(value(request, "mediaQuality") || "")}</td>`
            + `<td>${escapeHtml(value(request, "note") || "")}</td>`
            // Each option is coloured individually so the open list shows every status in its own
            // colour; the select's colour only reflects the current status.
            + `<td><select class="requests-status-select ${statusClass(status)}" style="color:${escapeHtml(statusColor(status))}" data-status aria-label="Request status">${choices.map((option) =>
                `<option class="${statusClass(option.id)}" style="color:${escapeHtml(option.color)}" value="${escapeHtml(option.id)}"${option.id === status ? " selected" : ""}>${escapeHtml(option.name)}</option>`).join("")}</select></td>`
            + `<td><button type="button" class="raised emby-button" data-delete title="Delete request">Delete</button></td>`;

        row.querySelector("[data-details]").addEventListener("click", function () {
            renderDetails(request);
        });

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

    $("#requestsQualityAdd").addEventListener("click", function () {
        let name = "New option";
        let suffix = 2;
        const existing = state.qualityOptions.map((option) => option.toLowerCase());
        while (existing.includes(name.toLowerCase())) {
            name = `New option ${suffix++}`;
        }
        saveQualityOptions(state.qualityOptions.concat(name), true);
    });

    $("#requestsStatusAdd").addEventListener("click", function () {
        // New statuses are sent without an id; the server assigns a permanent one.
        saveStatusOptions(state.statuses.concat({ id: "", name: "New status", color: "#bdbdbd" }), true);
    });

    $("#requestsEnableMovies").addEventListener("change", function () { saveMediaOptions(this); });
    $("#requestsEnableTvShows").addEventListener("change", function () { saveMediaOptions(this); });

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
