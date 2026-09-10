# PrismCast

Current confirmed development package: **0.2.0-alpha.41**.

PrismCast is a standalone Dalamud plugin for synchronized, world-space media playback in Final Fantasy XIV.

It can host:

- local video files
- HTTP/HTTPS video URLs
- YouTube links through yt-dlp
- movies, TV shows, anime, and episodes selected from a Plex server

PrismCast is its own plugin and identity. It does not replace or patch other synchronization plugins and can run alongside them normally.

## Viewer experience

PrismCast supports two shared-session flows:

1. **Watch Party** creates a one-time six-character invite code that expires when the hosted session ends.
2. **Groups** use a permanent invite code. Once a viewer joins a Group, it remains saved locally and can be rejoined whenever the host starts a Group session.

Short Watch Party codes and Group state use PrismCast's built-in directory service automatically. Normal users do not configure or enable a relay.

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

## PrismCast directory

Watch Party short codes and persistent Group metadata are resolved through PrismCast's built-in directory service. The production endpoint is embedded in the client, so end users do not need a settings toggle, URL, or separate relay setup.

`directory-service/` contains the Railway-compatible service currently used for development, while `relay/` contains the Cloudflare Worker implementation retained for a future zero-cost production migration.

## Repository publishing

Repository: `https://github.com/sinthen91/PrismCast`

The repository tracks the confirmed PrismCast development baseline. Matching alpha packages should only be advertised after the local Windows/Dalamud build succeeds.

## License and attribution

PrismCast is distributed under AGPL-3.0-or-later. Required third-party attribution is preserved in `THIRD_PARTY_NOTICES.md`.

## Alpha 0.2.0.41 changes

- Removed the hard-coded 600-item Plex display cap that could make large libraries appear to stop partway through the alphabet.
- Normal browsing and search now operate over the same complete Plex item set.
- Added viewport-based poster-grid virtualization in phone mode so only visible rows are rendered/queued, keeping large libraries responsive without hiding content.
- **Confirmed working on the user's Windows/Dalamud development environment.**

## Alpha 0.2.0.40 changes

- Added **Web / YouTube** as a permanent Library source alongside Plex and Local Files.
- Added URL metadata preview using PrismCast's bundled yt-dlp runtime when available.
- Shows title, provider, duration, and thumbnail before playback when the source exposes them.
- Direct HTTP/HTTPS media URLs remain playable even when metadata cannot be resolved.
- Web playback uses the same hosting/session pipeline as Plex and Local Files.
- Removed the old Quick URL / YouTube control from Remote so media selection lives in Library.

## Alpha 0.2.0.39 changes

- PrismCast now uses the live built-in Railway directory endpoint for Watch Party and Group codes.
- No relay/directory setting is exposed to normal users.
- Fixed the `directory.prismcast.app` DNS failure that prevented Group creation.
- Fixed the Railway public-port mismatch that initially caused 502 responses.

## Alpha 0.2.0.38 changes

- Fixed the active-session UI path so Remote, Library, and Session navigation remains visible while hosting/viewing.
- Improved Create Group status/error feedback and directory request timeout handling.

## Alpha 0.2.0.37 changes

- Replaced Open/Private lobbies with **Watch Party** and **Create Group**.
- Watch Parties generate a fresh six-character invite code for each hosted media session and expire when the session ends.
- Groups generate a permanent invite code. Members save the group once, then see Live/Offline status and can join whenever the host starts a group session.
- Session join accepts either a Watch Party code or a Group invite code in one field.
- The Session page is reduced to three containers: Host, Join Watch Party or Group, and Your Groups.
- No Nearby discovery or notification system is used.

## Alpha 0.2.0.36 changes

- Fixed Plex hosting paths that accessed Dalamud character data after async work resumed off the framework thread.
- Host identity is captured on the framework thread before asynchronous hosting work continues.
- Playback errors/status are surfaced directly in the Library UI.

## Alpha 0.2.0.35 changes

- Simplified Sessions to direct lobby flows during the transition to Watch Parties/Groups.
- Fixed phone Plex PLAY actions by returning to native ImGui buttons and routing playback through a dedicated Plex action.
- UI actions queue instead of silently dropping clicks while another UI task is finishing.

## Alpha 0.2.0.34 changes

- Replaced the phone Settings dropdown with individual square settings tiles.

## Alpha 0.2.0.33 changes

- Introduced the first mobile Session hub redesign and removed the old What's New/player-list clutter from Sessions.

## Alpha 0.2.0.32 changes

- Replaced the fixed four-button mobile Plex category grid with a single expandable selector generated from the connected user's Plex libraries.
- Local Files is available at the bottom of the same selector.
- Added the full-width outlined search field and explicit vertical media scrolling.
- Fixed the duplicate Search icon definition that broke the first alpha.32 build.

## Alpha 0.2.0.31 changes

- Redesigned the phone Library screen around the approved mobile mockup.
- Added the padded three-column poster grid with centered wrapped titles and runtime-aware Play buttons.

## Earlier development highlights

- Solution 9-inspired mobile/tablet shell and compact Remote UI.
- Fine-grained world-screen position controls with hold-to-move input.
- Host-authoritative synchronized screen placement and playback state.
- Plex browser sign-in, server/library discovery, and show → season → episode browsing.
- Local/Plex hosting through temporary Cloudflare Quick Tunnels without exposing Plex credentials.
- Mini remote mode, session viewer handling, screen reset/place-in-front controls, and curved/flat screen support.
