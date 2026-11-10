        (function () {
    let requests = [];
    let qualityOptions = [];
    let descending = true;
    let initializedPage;

            function headers() {
                const api = window.ApiClient;
                return api && typeof api.getRequestHeaders === "function"
                    ? api.getRequestHeaders() || {}
                    : {};
            }

            async function get(url) {
                if (window.ApiClient && typeof window.ApiClient.ajax === "function") {
                    return window.ApiClient.ajax({ url: url, type: "GET", dataType: "json" });
                }
                const response = await fetch(url, { credentials: "same-origin", headers: headers() });
                if (!response.ok) throw new Error("Unable to load administrator data.");
                return response.json();
            }

            async function ensureAdministrator() {
                const user = await get("/Users/Me");
                return !!(user.Policy && user.Policy.IsAdministrator);
            }

            async function send(url, method, body) {
                if (window.ApiClient && typeof window.ApiClient.ajax === "function") {
                    return window.ApiClient.ajax({
                        url: url,
                        type: method,
                        dataType: "json",
                        contentType: "application/json",
                        data: body === undefined ? undefined : JSON.stringify(body)
                    });
                }
                const response = await fetch(url, {
                    method: method, credentials: "same-origin",
                    headers: { ...headers(), "Content-Type": "application/json" },
                    body: body === undefined ? undefined : JSON.stringify(body)
                });
                if (!response.ok) throw new Error("Unable to update request.");
                return response.status === 204 ? null : response.json();
            }

            function value(item, name) {
                const upper = name.charAt(0).toUpperCase() + name.slice(1);
                return item[name] !== undefined ? item[name] : item[upper];
            }

            function escape(valueToEscape) {
                const span = document.createElement("span");
                span.textContent = valueToEscape == null ? "" : String(valueToEscape);
                return span.innerHTML;
            }

            function scope(request) {
                const current = value(request, "requestScope");
                if (current === "SeasonSelection") return "Seasons " + (value(request, "requestedSeasonNumbers") || []).join(", ");
                if (current === "EpisodeSelection") return "Episodes " + (value(request, "requestedEpisodeNames") || []).join(", ");
                return current === "EntireMovie" ? "Entire Movie" : "Entire Series";
            }

            function statusClass(status) {
                return "requests-status-" + String(status || "Pending")
                    .replace(/([a-z])([A-Z])/g, "$1-$2")
                    .toLowerCase()
                    .replace(/[^a-z-]/g, "");
            }

            function renderQualityOptions(page) {
                const target = page.querySelector("#requestsQualityOptions");
                target.replaceChildren();
                qualityOptions.forEach(function (option, index) {
                    const row = document.createElement("div");
                    row.className = "requests-quality-row";
                    const input = document.createElement("input");
                    input.type = "text";
                    input.value = option;
                    input.setAttribute("is", "emby-input");
                    input.setAttribute("aria-label", `Media quality option ${index + 1}`);
                    const deleteButton = document.createElement("button");
                    deleteButton.type = "button";
                    deleteButton.className = "raised emby-button requests-quality-delete";
                    deleteButton.textContent = "X";
                    input.onchange = async function () {
                        const value = input.value.trim();
                        if (!value) {
                            input.value = qualityOptions[index];
                            return;
                        }
                        qualityOptions[index] = value;
                        await saveQualityOptions(page);
                    };
                    deleteButton.onclick = async function () {
                        qualityOptions.splice(index, 1);
                        await saveQualityOptions(page);
                    };
                    row.append(input, deleteButton);
                    target.append(row);
                });
            }

            async function loadGeneral(page) {
                const results = await Promise.allSettled([
                    get("/Requests/Configuration/QualityOptions"),
                    get("/Requests/Configuration/Diagnostics"),
                    get("/Requests/Configuration/MediaOptions")
                ]);

                const optionsResult = results[0];
                if (optionsResult.status === "fulfilled") {
                    qualityOptions = optionsResult.value.options || [];
                    renderQualityOptions(page);
                } else {
                    page.querySelector("#requestsQualityOptions").textContent = optionsResult.reason?.message || "Unable to load media quality options.";
                }

                const diagnosticsResult = results[1];
                page.querySelector("#requestsGeneralVersion").textContent = diagnosticsResult.status === "fulfilled"
                    ? diagnosticsResult.value.pluginVersion || "Unknown"
                    : diagnosticsResult.reason?.message || "Unavailable";

                const mediaOptionsResult = results[2];
                if (mediaOptionsResult.status === "fulfilled") {
                    page.querySelector("#requestsEnableMovies").checked = Boolean(mediaOptionsResult.value.movies);
                    page.querySelector("#requestsEnableTvShows").checked = Boolean(mediaOptionsResult.value.tvShows);
                } else {
                    page.querySelector("#requestsEnableMovies").disabled = true;
                    page.querySelector("#requestsEnableTvShows").disabled = true;
                }

                try {
                    const providers = await get("/Requests/Search/Providers");
                    page.querySelector("#requestsGeneralProviderStatus").textContent = providers.status || "Unknown";
                } catch (error) {
                    page.querySelector("#requestsGeneralProviderStatus").textContent = error.message;
                }
            }

            async function saveQualityOptions(page) {
                await send("/Requests/Configuration/QualityOptions", "PUT", { options: qualityOptions });
                renderQualityOptions(page);
            }

            async function saveMediaOptions(page) {
                const movies = page.querySelector("#requestsEnableMovies").checked;
                const tvShows = page.querySelector("#requestsEnableTvShows").checked;
                if (!movies && !tvShows) {
                    page.querySelector("#requestsEnableMovies").checked = true;
                    throw new Error("At least one media type must remain enabled.");
                }
                await send("/Requests/Configuration/MediaOptions", "PUT", { movies: movies, tvShows: tvShows });
            }

            function renderDetails(page, request) {
                const list = page.querySelector("#requestsDetailsList");
                const providerIds = value(request, "providerIds") || {};
                const history = (value(request, "statusHistory") || []).map(function (change) {
                    return `${value(change, "status")} (${new Date(value(change, "changedAtUtc")).toLocaleString()})`;
                }).join(" -> ");
                list.innerHTML = [
                    ["Overview", value(request, "overview")],
                    ["Requester", `${value(request, "userName")} (${value(request, "userId")})`],
                    ["Creation Date", new Date(value(request, "requestedAtUtc")).toLocaleString()],
                    ["Media Type", value(request, "mediaType")],
                    ["Scope", scope(request)],
                    ["Status", value(request, "status")],
                    ["Status History", history || "No history"],
                    ["Provider", value(request, "sourceProvider")],
                    ["Provider IDs", Object.entries(providerIds).map(function (pair) { return `${pair[0]}: ${pair[1]}`; }).join(", ")],
                    ["Notes", value(request, "note")]
                ].map(function (item) { return `<dt>${escape(item[0])}</dt><dd>${escape(item[1] || "Not specified")}</dd>`; }).join("");
                page.querySelector("#requestsDetailsTitle").textContent = value(request, "title") || "Request Details";
                const poster = page.querySelector("#requestsDetailsPoster");
                poster.hidden = !value(request, "posterUrl");
                poster.src = value(request, "posterUrl") || "";
                poster.alt = value(request, "title") || "Requested media";
                page.querySelector("#requestsAdminDetails").hidden = false;
            }

            function renderRequests(page) {
                const query = page.querySelector("#requestsAdminSearch").value.trim().toLowerCase();
                const statusFilter = page.querySelector("#requestsAdminStatus").value;
                const mediaFilter = page.querySelector("#requestsAdminMediaType").value;
                const sort = page.querySelector("#requestsAdminSort").value;
                const filtered = requests.filter(function (request) {
                    const title = String(value(request, "title") || "").toLowerCase();
                    const requester = String(value(request, "userName") || "").toLowerCase();
                    const notes = String(value(request, "note") || "").toLowerCase();
                    const provider = JSON.stringify(value(request, "providerIds") || {}).toLowerCase();
                    const date = value(request, "requestedAtUtc") || "";
                    return (!query || title.includes(query) || requester.includes(query) || notes.includes(query) || provider.includes(query))
                        && (!statusFilter || value(request, "status") === statusFilter)
                        && (!mediaFilter || value(request, "mediaType") === mediaFilter);
                }).sort(function (left, right) {
                    const leftValue = sort === "date" ? value(left, "requestedAtUtc") : sort === "title" ? value(left, "title") : sort === "requester" ? value(left, "userName") : value(left, "status");
                    const rightValue = sort === "date" ? value(right, "requestedAtUtc") : sort === "title" ? value(right, "title") : sort === "requester" ? value(right, "userName") : value(right, "status");
                    return (String(leftValue || "").localeCompare(String(rightValue || ""))) * (descending ? -1 : 1);
                });
                const rows = page.querySelector("#requestsAdminRows");
                rows.replaceChildren();
                filtered.forEach(function (request) {
                    const id = value(request, "id");
                    const poster = value(request, "posterUrl");
                    const row = document.createElement("tr");
                    const status = value(request, "status") || "Pending";
                    const quality = value(request, "mediaQuality") || "";
                    const notes = value(request, "note") || "";
                    const statusOptions = ["Pending", "Approved", "Denied", "InProgress", "Available", "Completed", "Cancelled"];
                    row.innerHTML = `<td>${poster ? `<img src="${escape(poster)}" width="56" alt="">` : ""}</td>`
                        + `<td><button type="button" class="raised emby-button" data-details>${escape(value(request, "title"))}</button></td>`
                        + `<td>${escape(value(request, "userName"))}</td>`
                        + `<td>${escape(new Date(value(request, "requestedAtUtc")).toLocaleString())}</td>`
                        + `<td>${escape(quality)}</td><td>${escape(notes)}</td>`
                        + `<td><select is="emby-select" class="${statusClass(status)}" data-status aria-label="Request status">${statusOptions.map(function (option) {
                            return `<option value="${option}"${option === status ? " selected" : ""}>${option}</option>`;
                        }).join("")}</select></td>`
                        + `<td><button type="button" class="raised emby-button" data-delete aria-label="Delete request" title="Delete request">X</button></td>`;
                    row.querySelector("[data-details]").onclick = function () { renderDetails(page, request); };
                    row.querySelector("[data-status]").onchange = async function (event) {
                        const select = event.target;
                        const previousStatus = status;
                        select.disabled = true;
                        try {
                            await send(`/Requests/${id}/status`, "PUT", { status: select.value });
                            request.status = select.value;
                            select.className = statusClass(select.value);
                        } catch (error) {
                            select.value = previousStatus;
                            window.alert(error.message);
                        } finally {
                            select.disabled = false;
                        }
                    });
                    row.querySelector("[data-delete]").onclick = async function () {
                        const deleteButton = this;
                        deleteButton.disabled = true;
                        try {
                            await send(`/Requests/${id}`, "DELETE");
                            requests = requests.filter(function (candidate) { return value(candidate, "id") !== id; });
                            row.remove();
                        } catch (error) {
                            deleteButton.disabled = false;
                            window.alert(error.message);
                        }
                    };
                    rows.append(row);
                });
            }

            async function loadRequests(page) {
                requests = await get("/Requests");
                renderRequests(page);
            }

            function initialize(page) {
                if (initializedPage === page) {
                    return;
                };

                initializedPage = page;
                page.querySelectorAll(".requests-config-tab").forEach(function (tab) {
                    tab.onclick = function () {
                        page.querySelectorAll("[data-panel]").forEach(function (panel) { panel.hidden = panel.dataset.panel !== tab.dataset.tab; });
                        if (tab.dataset.tab === "requests") {
                            loadRequests(page).catch(function (error) { window.alert(error.message); });
                        }
                    };
                });
                page.querySelectorAll("#requestsAdminSearch,#requestsAdminStatus,#requestsAdminMediaType,#requestsAdminSort").forEach(function (control) {
                    control.oninput = function () { renderRequests(page); };
                    control.onchange = function () { renderRequests(page); };
                });
                page.querySelector("#requestsAdminSortDirection").onclick = function () {
                    descending = !descending;
                    this.textContent = descending ? "Descending" : "Ascending";
                    renderRequests(page);
                };
                page.querySelector("#requestsDetailsClose").onclick = function () { page.querySelector("#requestsAdminDetails").hidden = true; };
                page.querySelector("#requestsQualityAdd").onclick = async function () {
                    qualityOptions.push("new");
                    await saveQualityOptions(page);
                };
                page.querySelector("#requestsEnableMovies").onchange = function () { saveMediaOptions(page).catch(function (error) { window.alert(error.message); }); };
                page.querySelector("#requestsEnableTvShows").onchange = function () { saveMediaOptions(page).catch(function (error) { window.alert(error.message); }); };
                ensureAdministrator().then(function (isAdministrator) {
                    if (!isAdministrator) {
                        page.innerHTML = '<div class="content-primary"><h2 class="sectionTitle">Access denied</h2><p>Administrator access is required.</p></div>';
                        return;
                    }
                    return loadGeneral(page);
                }).catch(function (error) {
                    page.querySelector("#requestsGeneralVersion").textContent = "Unavailable";
                    page.querySelector("#requestsGeneralProviderStatus").textContent = error.message || "Unavailable";
                    window.alert(error.message);
                });
            }

            function findPage() {
                const page = document.querySelector("#requestsConfigPage");
                if (page) {
                    initialize(page);
                }
            }

            findPage();
            new MutationObserver(findPage).observe(document.documentElement, { childList: true, subtree: true });
        }());
