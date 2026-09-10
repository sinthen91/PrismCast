# PrismCast

Current tested development package: **0.2.0-alpha.32**.

PrismCast is a standalone Dalamud plugin for synchronized, world-space media playback in Final Fantasy XIV.

It can host:

- local video files
- HTTP/HTTPS video URLs
- YouTube links through yt-dlp
- movies, TV shows, anime, and episodes selected from a Plex server

PrismCast is its own plugin and identity. It does not replace or patch other synchronization plugins and can run alongside them normally.

## Viewer experience

The basic flow is deliberately small:

1. Install PrismCast once from its Dalamud custom repository.
2. Paste a host invite code and join.
3. Optionally save a trusted host ID once so future active casts can be discovered and joined automatically.

An optional PrismCast rendezvous relay is included for trusted-host discovery. When configured, a viewer can save a host ID once and PrismCast will automatically discover and join that host's active session without needing a fresh invite code every movie night.

## How hosting works

For local/Plex media, PrismCast keeps the original file on the host PC. It starts a loopback byte-range HTTP server and creates a temporary Cloudflare Quick Tunnel. Viewers receive a short-lived session location and token, not the host's Plex credentials.

For public HTTP/YouTube sources, the session state contains the source URL while each PrismCast client handles playback locally.

Playback state is synchronized periodically. Viewers hard-seek when drift is large and make small temporary playback-speed corrections when drift is minor.

## Commands

- `/prismcast`
- `/prism`

## Local development

Run:

`build.cmd`

The build script:

- uses an installed .NET 10 SDK when available, otherwise installs a private copy
- preloads `Dalamud.NET.Sdk/15.0.0` into a private NuGet cache
- locates the XIVLauncher Dalamud development binaries
- restores and builds PrismCast in Release/x64
- packages a dev build and release ZIP

Outputs:

- `dist\PrismCast-Dev\PrismCast.dll`
- `dist\PrismCast.zip`

Dalamud's **Dev Plugin Locations** entry must point to the DLL itself.

## Runtime dependencies

PrismCast downloads its media/runtime tools into its plugin configuration directory as needed:

- libmpv
- yt-dlp
- Deno
- cloudflared (host only, downloaded when hosting is first used)

Viewers do not manually install any of these. Playback dependencies can warm up in the background after PrismCast loads.

## Optional relay

`relay/worker.js` contains a small Cloudflare Worker for trusted-host discovery. It stores only temporary PrismCast invite codes under cryptographic host IDs and expires announcements automatically.

Direct invite codes do not require this relay.

## Repository publishing

Repository: `https://github.com/sinthen91/PrismCast`

The repository tracks the tested PrismCast development baseline. Matching alpha packages should only be advertised after the local Windows/Dalamud build succeeds.

## License and attribution

PrismCast is distributed under AGPL-3.0-or-later. Required third-party attribution is preserved in `THIRD_PARTY_NOTICES.md`.

## Alpha 0.2.0.32 changes

- Replaced the hardcoded four-library mobile Plex layout with a **single dynamic library selector**.
- The selector is populated from the libraries actually returned by the connected Plex server, so users can have arbitrary names and library counts.
- **Local Files** is permanently placed at the bottom of the selector instead of being hidden behind an unlabeled toolbar icon.
- There is deliberately **no All Plex Libraries option**, keeping mobile browsing scoped to one selected library at a time.
- Added a defined Solution 9-style search field with a magnifying-glass icon and explanatory placeholder text.
- Search applies to the currently selected source/library.
- Kept the phone poster browser as a padded three-column grid with wrapped, centered titles.
- Media buttons retain runtime text such as `PLAY · 1:35:45` when Plex duration metadata is available.
- The poster/media region is vertically scrollable so every item in the selected library is reachable.
- Fixed the alpha.32 build failure caused by the duplicate `PrismCastWindow.UiIcon.Search` enum/switch definition.
- **Build verified successfully on the user's Windows/Dalamud development environment.**

## Alpha 0.2.0.31 changes

- Redesigned the phone Library screen around the approved mobile mockup.
- Search moved beneath the PrismCast header.
- Added a padded three-column poster grid with centered wrapped titles.
- Play buttons show Plex runtime metadata when available and use stronger purple/cyan styling.

## Earlier development highlights

- Fine-grained world-screen position controls with Shift precision movement.
- Live screen position, yaw/pitch/roll, scale, and curvature updates.
- Plex browser sign-in and server/library discovery.
- Plex show → season → episode browsing.
- Local/Plex hosting through temporary PrismCast sessions without exposing Plex credentials.
- Synchronized host/viewer screen placement and playback state.
