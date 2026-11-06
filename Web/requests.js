(function () {
    "use strict";

    const page = document.querySelector("#requestsPage");
    if (!page || page.dataset.initialized === "true") {
        return;
    }
    page.dataset.initialized = "true";

    const form = page.querySelector("#requestsSearchForm");
    const input = page.querySelector("#requestsSearchInput");
    const button = page.querySelector("#requestsSearchButton");
    const status = page.querySelector("#requestsStatus");
    const results = page.querySelector("#requestsResults");
    const requestedKeys = new Set();

    function apiUrl(url) {
        const apiClient = window.ApiClient;
        let serverAddress = "";

        if (apiClient && typeof apiClient.serverAddress === "function") {
            serverAddress = apiClient.serverAddress() || "";
        }

        return serverAddress
            ? `${serverAddress.replace(/\/+$/, "")}${url}`
            : url;
    }

    function apiHeaders() {
        const apiClient = window.ApiClient;
        const token = apiClient && typeof apiClient.accessToken === "function"
            ? apiClient.accessToken()
            : null;
        const headers = {};

        if (apiClient && typeof apiClient.getRequestHeaders === "function") {
            try {
                Object.assign(headers, apiClient.getRequestHeaders() || {});
            } catch (_) {
                // Fall back to the API token header below.
            }
        }

        if (token) {
            headers["X-Emby-Token"] = token;
        }

        if (!headers.Authorization && apiClient && typeof apiClient.authorizationHeader === "function") {
            try {
                const authorization = apiClient.authorizationHeader();
                if (authorization) {
                    headers.Authorization = authorization;
                }
            } catch (_) {
                // The X-Emby-Token header remains the fallback.
            }
        }

        return headers;
    }

    async function apiRequest(url, options) {
        const response = await fetch(apiUrl(url), {
            ...options,
            headers: {
                ...apiHeaders(),
                ...(options && options.headers ? options.headers : {})
            },
            credentials: "same-origin"
        });

        if (!response.ok) {
            let message = "Unable to search Jellyfin metadata providers.";
            try {
                const body = await response.json();
                if (body.error || body.message) {
                    message = body.error || body.message;
                }
            } catch (_) {
                // Keep the friendly fallback for non-JSON responses.
            }
            throw new Error(`${message} (${response.status} ${response.statusText})`);
        }

        return response.status === 204 ? null : response.json();
    }

    function escapeHtml(value) {
        const element = document.createElement("span");
        element.textContent = value == null ? "" : String(value);
        return element.innerHTML;
    }

    function mediaKey(item) {
        return `${String(item.mediaType || "").toLowerCase()}:${item.providerId}`;
    }

    function releaseYear(item) {
        return item.productionYear ? String(item.productionYear) : "";
    }

    function mediaLabel(mediaType) {
        return String(mediaType || "").toLowerCase() === "tv" ? "TV Show" : "Movie";
    }

    async function loadExistingRequests() {
        try {
            const requests = await apiRequest("/Requests", { method: "GET" });
            for (const request of requests || []) {
                requestedKeys.add(`${String(request.mediaType || "").toLowerCase()}:${request.mediaId}`);
            }
        } catch (_) {
            // Searching can still work even if the existing-request list is unavailable.
        }
    }

    function createPoster(item) {
        const poster = document.createElement("div");
        poster.className = "requests-card-poster requests-card-poster-placeholder";
        poster.textContent = item.posterUrl ? "" : "No poster";

        if (item.posterUrl) {
            const image = document.createElement("img");
            image.className = "requests-card-poster";
            image.alt = item.name || "Media poster";
            image.loading = "lazy";
            image.src = item.posterUrl;
            image.addEventListener("error", function () {
                image.replaceWith(poster);
            });
            return image;
        }

        return poster;
    }

    async function submitRequest(item, requestButton) {
        requestButton.disabled = true;
        requestButton.textContent = "Requesting...";

        try {
            await apiRequest("/Requests", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    mediaId: item.providerId || "",
                    title: item.name || "",
                    mediaType: mediaLabel(item.mediaType),
                    overview: item.overview || "",
                    providerId: item.providerId || "",
                    providerIds: item.providerIds || {},
                    productionYear: item.productionYear || null,
                    sourceProvider: item.sourceProvider || ""
                })
            });

            requestedKeys.add(mediaKey(item));
            requestButton.textContent = "✓ Requested";
            requestButton.classList.add("requested");
        } catch (error) {
            requestButton.disabled = false;
            requestButton.textContent = "Request";
            status.className = "requests-status error";
            status.textContent = error.message || "Unable to submit the request.";
        }
    }

    function renderResult(item) {
        const card = document.createElement("article");
        card.className = "requests-card";

        const poster = createPoster(item);
        const body = document.createElement("div");
        body.className = "requests-card-body";

        const year = releaseYear(item);
        const heading = document.createElement("h2");
        heading.className = "requests-card-heading";
        heading.textContent = year ? `${item.name || "Untitled"} (${year})` : item.name || "Untitled";

        const meta = document.createElement("div");
        meta.className = "requests-card-meta";
        meta.textContent = [
            mediaLabel(item.mediaType),
            item.sourceProvider ? `Source: ${item.sourceProvider}` : ""
        ].filter(Boolean).join(" · ");

        const overview = document.createElement("p");
        overview.className = "requests-card-overview";
        overview.textContent = item.overview || "No overview available.";

        const requestButton = document.createElement("button");
        requestButton.type = "button";
        requestButton.className = "raised button-submit emby-button requests-card-button";

        const key = mediaKey(item);
        const normalizedKey = `${String(item.mediaType || "").toLowerCase()}:${item.providerId}`;
        if (requestedKeys.has(key) || requestedKeys.has(normalizedKey)) {
            requestButton.textContent = "✓ Requested";
            requestButton.disabled = true;
            requestButton.classList.add("requested");
        } else {
            requestButton.textContent = "Request";
            requestButton.addEventListener("click", function () {
                submitRequest(item, requestButton);
            });
        }

        body.append(heading, meta, overview, requestButton);
        card.append(poster, body);
        return card;
    }

    async function search(query) {
        status.className = "requests-status";
        status.textContent = "Searching...";
        results.replaceChildren();
        button.disabled = true;
        input.disabled = true;

        try {
            await loadExistingRequests();
            const encodedQuery = encodeURIComponent(query);
            const combined = await apiRequest(`/Requests/Search?query=${encodedQuery}`, { method: "GET" });

            if (combined.length === 0) {
                status.textContent = "No results found.";
                return;
            }

            status.textContent = `${combined.length} result${combined.length === 1 ? "" : "s"}`;
            combined.forEach(function (item) {
                results.append(createResultFragment(item));
            });
        } catch (error) {
            status.className = "requests-status error";
            status.textContent = error.message || "Unable to search Jellyfin metadata providers.";
        } finally {
            button.disabled = false;
            input.disabled = false;
        }
    }

    function createResultFragment(item) {
        return renderResult(item);
    }

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        const query = input.value.trim();
        if (!query) {
            status.className = "requests-status error";
            status.textContent = "Enter a title to search.";
            return;
        }
        search(query);
    });
})();
