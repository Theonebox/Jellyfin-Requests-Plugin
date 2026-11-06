# Jellyfin Requests Plugin

A Jellyfin plugin for requesting media.

## Requirements

- Jellyfin 12.4 or later
- .NET 10 runtime

## Search

Media search uses Jellyfin's native metadata identification workflow. Results come from the metadata providers configured in Jellyfin; no external provider API key is required by the plugin.

## Installation

Add this repository URL to Jellyfin's plugin repositories:

```text
https://raw.githubusercontent.com/Theonebox/Jellyfin-Requests-Plugin/main/manifest.json
```

Then install **Requests** from the Catalog and restart Jellyfin.
