<p align="center"><img src="images/logo.png" alt="Requests" width="160"></p>

# Jellyfin Requests

Let your users request movies, TV shows and music right inside Jellyfin, and manage every request from the dashboard. No external services or API keys required (trending is optional and uses your own TMDB key).

## Features

- **Requests button in the header** for every signed-in user, opening a search page that follows your theme and custom CSS.
- **Search** movies, TV shows and albums through the metadata providers already configured in Jellyfin (music via MusicBrainz). Duplicate results from different providers are merged, and titles you already have are marked *In library*.
- **Flexible requests:** whole movies, series or albums, or specific seasons and episodes, with a preferred quality and an optional note.
- **Trending** (optional): this week's trending movies and TV shows from TMDB, shown while nothing is searched. Needs your own free TMDB API key.
- **My Requests:** users see the status of everything they asked for and can cancel requests that haven't been processed yet. Titles someone else already requested are marked *Already requested*.
- **Administration** (Dashboard → Requests): filter, sort and update every request; configure media types, quality options and statuses (name, color and icon); optional animations.

## Requirements

- Jellyfin 12.4 or later

## Installation

1. In Jellyfin, go to **Dashboard → Plugins → Repositories** and add:

   ```text
   https://raw.githubusercontent.com/Theonebox/Jellyfin-Requests-Plugin/main/manifest.json
   ```

2. Install **Requests** from the Catalog and restart Jellyfin.
3. Reload the web client. The Requests button appears in the header, next to SyncPlay.
