# PrismCast Development Notes

## Current baseline

- Version: `0.2.0-alpha.61`
- Canonical project: `PrismCast/PrismCast.csproj`
- Dalamud API: 15
- Target: .NET 10 / Windows x64
- Source of truth: GitHub `main`

## Media sources

PrismCast supports Plex libraries discovered dynamically from the connected server, Local Files, Web / YouTube URLs, and host-selected screen/window capture. There is no browser companion or streaming-service account integration. Browser content uses Share Screen / Window. Media selection belongs in Library; Remote is for playback, session transport, and world-screen controls.

## Sessions

- Watch Party: one-time short invite code for a single hosted session.
- Groups: persistent membership through a permanent invite code; members can rejoin when the group is live.
- No Nearby discovery.
- No user-facing relay configuration.
- Active participant names are only shown inside the active session.

Short-code and Group resolution use the built-in PrismCast directory service. The current development deployment is on Railway; production should use durable zero-cost storage/hosting where practical.

## UI

The approved mobile UI direction is a dark Solution 9-inspired phone interface with purple/cyan neon framing and bottom navigation for Remote, Library, and Session.

## Alpha.41 state

- Plex PLAY path is working.
- Plex libraries are dynamically enumerated.
- The old 600-item Plex display cap is removed.
- Large Plex poster grids are viewport-virtualized.
- Web / YouTube is a permanent Library source.
- Watch Party / Group lookups use the live built-in directory endpoint.
- Session bottom navigation remains visible during active sessions.

## Workflow

1. Start from GitHub `main`.
2. Make source changes locally.
3. Build and test.
4. Only promote a tested build to `main`.
5. Generated ZIPs are test/release artifacts, not canonical source.
