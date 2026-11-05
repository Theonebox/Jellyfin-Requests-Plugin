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
        const apiClient = window.ApiClient;
        if (apiClient && typeof apiClient.ajax === "function") {
            try {
                const method = options && options.method ? String(options.method).toUpperCase() : "GET";
                const headers = apiHeaders();
                const ajaxOptions = {
                    type: method,
                    url: apiUrl(url),
                    dataType: "json",
                    headers
                };

                if (options && options.body !== undefined) {
                    ajaxOptions.data = options.body;
                    ajaxOptions.contentType = options.headers && options.headers["Content-Type"]
                        ? options.headers["Content-Type"]
                        : "application/json";
                    ajaxOptions.processData = false;
                }

                return await apiClient.ajax(ajaxOptions);
            } catch (error) {
                const status = error && error.status
                    ? error.status
                    : error && error.statusCode;
                let message = "Unable to contact TMDb.";

                if (error && error.responseJSON && (error.responseJSON.error || error.responseJSON.message)) {
                    message = error.responseJSON.error || error.responseJSON.message;
                } else if (error && error.responseText) {
                    try {
                        const body = JSON.parse(error.responseText);
                        message = body.error || body.message || message;
                    } catch (_) {
                        // Keep the friendly fallback.
                    }
                }

                const suffix = status ? ` (${status})` : "";
                throw new Error(`${message}${suffix}`);
            }
        }

        const response = await fetch(apiUrl(url), {
            ...options,
            headers: {
                ...apiHeaders(),
                ...(options && options.headers ? options.headers : {})
            },
            credentials: "same-origin"
        });

        if (!response.ok) {
            let message = "Unable to contact TMDb.";
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
        return `${item.mediaType}:${item.id}`;
    }

    function releaseYear(item) {
        if (!item.releaseDate) {
            return "";
        }

        const date = new Date(item.releaseDate);
        return Number.isNaN(date.getTime()) ? "" : String(date.getUTCFullYear());
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
        poster.textContent = item.posterPath ? "" : "No poster";

        if (item.posterPath) {
            const image = document.createElement("img");
            image.className = "requests-card-poster";
            image.alt = item.name || "Media poster";
            image.loading = "lazy";
            image.src = item.posterPath;
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
                    mediaId: String(item.id),
                    title: item.name || "",
                    mediaType: mediaLabel(item.mediaType),
                    overview: item.overview || ""
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
        meta.textContent = mediaLabel(item.mediaType);

        const overview = document.createElement("p");
        overview.className = "requests-card-overview";
        overview.textContent = item.overview || "No overview available.";

        const requestButton = document.createElement("button");
        requestButton.type = "button";
        requestButton.className = "raised button-submit emby-button requests-card-button";

        const key = mediaKey(item);
        const normalizedKey = `${String(item.mediaType || "").toLowerCase()}:${item.id}`;
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
            const [movies, tvShows] = await Promise.all([
                apiRequest(`/Requests/Search/Movie?query=${encodedQuery}`, { method: "GET" }),
                apiRequest(`/Requests/Search/TV?query=${encodedQuery}`, { method: "GET" })
            ]);

            const combined = [
                ...(Array.isArray(movies) ? movies : []),
                ...(Array.isArray(tvShows) ? tvShows : [])
            ];

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
            status.textContent = error.message || "Unable to contact TMDb.";
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
