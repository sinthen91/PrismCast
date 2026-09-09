# PrismCast

Current development package: **0.2.0-alpha.31**.

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

Run the two-client checks in `TEST_PLAN.md` before publishing the first alpha release.

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

The included `pluginmaster.json` is prepared for:

`https://github.com/sinthen91/PrismCast`

The repository is the source of truth for PrismCast development. Matching alpha releases should publish `PrismCast.zip` before that version is advertised through the custom-repository manifest.

## License and attribution

PrismCast is distributed under AGPL-3.0-or-later. Required third-party attribution is preserved in `THIRD_PARTY_NOTICES.md`. See `LICENSE.md`, `NOTICE`, and `THIRD_PARTY_NOTICES.md`.


## Alpha 5 changes

- Fixed local-file hosting failing with `Not on main thread!` by moving Dalamud/game graphics work to the framework thread.
- Replaced raw Plex-only setup with a **Connect to Plex** browser sign-in flow.
- PrismCast now discovers reachable Plex Media Servers and loads their movie libraries.
- Plex playback no longer depends on the Plex server returning a filesystem path that exists on the PrismCast host PC.
- Plex media is proxied through the host's temporary PrismCast session, so viewer clients do not receive the host's Plex token.
- Manual Plex server URL/token fields remain under an Advanced section as a fallback.



## Alpha 0.2.0.8 changes

- Fixed the API-15 `InputFloat3` overload used by the Exact XYZ editor.
- Retains the alpha.7 fine-grained position drag controls.

## Alpha 0.2.0.7 changes

- Replaced the huge-range X/Y/Z sliders with fine-grained drag controls.
- Normal position dragging moves at 0.025 world units per input step.
- Holding Shift while dragging reduces movement to 0.005 world units for precise placement.
- Position values display to three decimal places and still support exact typed XYZ values.

## Alpha 0.2.0.6 changes

- Screen position, rotation, scale, and curvature now update live.
- X/Y/Z sliders plus exact numeric position fields.
- Yaw/pitch/roll controls are shown in degrees.
- Plex movie and TV-show libraries are both listed.
- TV libraries can be browsed show -> season -> episode.


## Alpha 0.2.0.31 changes

- Redesigned the phone Library screen around the approved mobile mockup.
- Search now sits directly beneath the PrismCast header.
- Plex libraries use a 2x2 icon category layout with Movies, TV Shows, Anime, and Anime Movies prioritized.
- Phone media browsing uses a padded three-column poster grid with centered wrapped titles.
- Play buttons show Plex runtime metadata when available and use stronger purple/cyan styling.
- Local Files remains accessible from the compact phone library toolbar.
