# PrismCast alpha.15 UI / control overlay

Extract this overlay over the current working PrismCast alpha.14 source tree.

## Changes

- Minimized PrismCast is now a portrait pocket remote instead of a horizontal strip.
- Active mini remote includes artwork when a Plex poster is available, play/pause, seek, a compact movement pad, forward/back, rotate, center, and power.
- Idle mini remote shows PrismCast branding and a power button.
- Screen movement, forward/back, rotate, tilt, roll, and scale buttons now support click-and-hold repeat.
- Starting a new hosted session from Plex, Local Files, or URL places the world-space screen directly in front of the host by default.
- Remote Control includes explicit Place in Front of Me and Reset Rotation buttons.
- Changelog entries in the lower-left sidebar are clickable. They open a full Recent Changes page with detailed notes for recent alpha versions.
- Existing first-name-only viewer list from alpha.14 remains intact.

## Deferred renderer verification

The two-client alpha.10 remote camera/fragmentation verification remains pending until another viewer is available.


## 0.2.0.16
- Widened the portrait mini remote.
- Centered control rows and fixed right-edge clipping.


## 0.2.0.17
- Added Tablet, Phone, and Automatic interface modes.
- Added bottom-navigation phone shell.
- Unified the Library page so Plex and Local Files live together.


## 0.2.0.18
- Hotfix: constructor no longer reads ImGui window state.
- Fixes crash when enabling the plugin after alpha.17.


## 0.2.0.19
- Full Solution 9 visual redesign.
- Neon angular panel chrome and cyan/violet HUD accents.
- Compact phone Remote, Library, Session, Settings, and What's New pages.
- Unified Plex + Local Files library retained.


## 0.2.0.20
- Remote page overhaul for phone mode.
- Removed Session block from Remote in phone layout.
- Added circular transport buttons and cleaner Screen Controls.
- Removed duplicate phone-mode bottom playback bar on Remote.


## 0.2.0.21
- Remote fidelity pass matching the approved Solution 9 mobile mockup more closely.
- Added phone status bar and separate PrismCast app header.
- Fixed Now Playing padding, purple progress bar, transport alignment/glow.
- Added monitor/control icons and icon bottom navigation.


## 0.2.0.22
- Repaired malformed alpha.21 generated source.
- Preserved the Remote fidelity design changes.


## 0.2.0.23
- Expanded phone Remote controls to fill the page and removed both Remote-page scrollbars.
- Embedded and used the supplied PrismCast logo in the phone header.
- Embedded white play/pause icon assets and used them in the purple media control.
- Removed the redundant Remote/Live labels beside the Settings cog.


## 0.2.0.24
- Enlarged the PrismCast phone header logo.
- Added Stop Session beside Settings when hosting.
- Moved Scale/Flat/Curved controls to the top of Screen Controls.
- Fixed stale Now Playing poster after ending a session.
- Redrew Settings as a cog outline.


## 0.2.0.25
- Larger minimize/close targets in the phone status bar.
- Slightly larger PrismCast header logo.
- Transport controls remain visible in the Now Playing card while idle/viewing, but are only interactive for hosts.
- Phone default size changed to 440x940 with a 430x918 minimum so Place in Front / Reset Rotation are visible at launch.


## 0.2.0.26
- Enlarged the PrismCast phone header wordmark to 320 x 105 logical pixels.
- Replaced tiny punctuation-style window controls with 42 x 34 high-contrast minimize/close controls.
- Increased phone status/header space and launch height so the Remote page remains fully visible.


## 0.2.0.27
- Increased default phone launch height so both Screen Controls helper lines fit.
- Added a thicker, layered embossed outer device frame with purple/blue highlights and recessed shadow edges.


## 0.2.0.28
- Replaced the embossed shell rim with a neon tube-style border.
- Added layered violet/cyan glow outlines to better match the provided border reference.
- Retained the larger default phone size so the helper text remains visible.


## 0.2.0.29
- Added a top-layer shell cap so child-window corners no longer poke through the rounded app corners.
- Re-drew the neon border over the content edges for a cleaner rounded silhouette.
- Preserved the existing neon shell look and larger default phone size.


## 0.2.0.30
- Rounded the actual phone-screen child window instead of relying only on an overlay cap.
- Increased the phone screen inset so the content stays inside the neon chassis.
- Kept the neon border drawn above the screen for a clean uninterrupted corner silhouette.


## 0.2.0.31
- Phone Library now starts with search directly beneath the PrismCast header.
- Plex libraries render as 2x2 icon category buttons with Movies, TV Shows, Anime, and Anime Movies prioritized.
- Phone Plex media renders in a padded 3-column poster grid.
- Titles are centered and wrapped inside each card.
- Play buttons display Plex runtimes when available and use stronger purple/cyan styling.
- Local Files remains accessible through the compact source control beside search.


## 0.2.0.32
- Mobile Library now uses a single dynamic Plex library dropdown instead of hardcoded category buttons.
- Local Files is permanently placed at the bottom of that dropdown.
- Added a full-width visible search container with search icon and placeholder.
- Added a styled vertical scrollbar for Plex and local media result regions while keeping search/source selection fixed.


## 0.2.0.33
- Rebuilt the mobile Session tab around three compact views: Rooms, Temporary, and Nearby.
- Rooms are permanent memberships with Open/Private privacy modes; Private rooms require an invite code only for initial membership.
- Temporary casts generate a fresh one-time session code on every host start.
- Nearby is pull-only and intersects relay advertisements with characters currently loaded by the local FFXIV client; there are no notifications.
- Removed What's New and the global Players block from Sessions. Active player names render only inside a live room card.
- Added collapsed host-only member management for kicking permanent room members.


## Alpha 0.2.0.34 changes

- Replaced the phone Settings dropdown with individual square settings tiles.
- Added a dedicated PrismCast Relay settings page with status, URL, host ID, and feature guidance.
- Rooms/Nearby relay warnings now link directly to PrismCast Relay settings.
- Kept trusted-host options under Networking and moved relay configuration out of that page.


## Alpha 0.2.0.35 changes

- Removed the relay-based Rooms, Nearby discovery, short-code, trusted-host auto-join, and notification-adjacent infrastructure from the active PrismCast workflow.
- Sessions now use simple direct Open Lobby / Private Lobby flows with one-time direct invite codes.
- Private lobby codes are clearly marked invite-only; ending a lobby invalidates the underlying direct session.
- Fixed phone Plex PLAY actions by returning to native ImGui buttons and routing playback through a dedicated Plex action.
- UI actions now queue instead of silently dropping clicks while another UI task is still finishing.


## 0.2.0.36
- Fixed host-name capture so Plex/local/URL sessions do not access Dalamud object data from a background thread.
- Mobile Library now surfaces playback status/errors above the poster grid.


## 0.2.0.37
- Session mobile UI now uses three containers instead of lobby-mode tabs.
- Container 1: Watch Party / Create Group.
- Container 2: one invite field for Watch Party or Group codes.
- Container 3: saved Groups with Live/Offline state and Join Lobby.
- Removed Nearby and notification concepts from the active Session flow.
- Short-code/group directory is automatic and has no user-facing relay setting.


## 0.2.0.38
- Fixed phone bottom navigation disappearing while a Watch Party or Group session is active.
- Added inline group-name validation and Create Group progress/error feedback.
- Added an 8-second directory timeout with a clear infrastructure error instead of an apparently dead button.



## 0.2.0.41

- Removed the 600-item Plex display cap.
- Search and normal browsing now expose the same complete Plex result set.
- Phone poster rows are virtualized so large libraries do not render or queue every poster in one frame.

## 0.2.0.40

- Added Web / YouTube to the unified media-source selector.
- Added a dedicated URL input card with metadata resolution and media preview.
- Added thumbnail, title, provider, duration, and a prominent Play action for resolved web media.
- Moved URL/YouTube selection out of Remote and into Library.

## 0.2.0.39
- Switched the invisible PrismCast directory backend to the live Railway deployment.
- No user configuration is required for Watch Party or Group code resolution.
