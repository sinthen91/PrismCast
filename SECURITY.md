# PrismCast Security and Privacy Model

PrismCast does not contain a browser extension, streaming-service login, cookie reader, password field, or browser-companion bridge.

## Account boundaries

- Plex sign-in happens on Plex's official website. PrismCast never receives the user's Plex email address or password.
- The Plex server token is stored in `PrismCastSecrets.dat`, encrypted with Windows DPAPI `CurrentUser`.
- Existing plaintext Plex tokens are migrated and cleared from ordinary plugin configuration.
- Disconnecting Plex deletes the saved Plex token.
- PrismCast has no YouTube sign-in and does not request or store YouTube credentials or browser cookies.
- PrismCast never asks for Netflix, Prime Video, Disney+, Hulu, Crunchyroll, Spotify, or other streaming-service credentials.

## Screen sharing

- Browser and subscription-service content can be shared only through **Share Screen / Window**.
- Capture begins only after the host explicitly selects **Entire desktop** or a named visible window and presses **Start Screen Share**.
- Screen sharing transmits visible pixels in the selected region. Hosts should finish signing in and remove private notifications, messages, or windows before starting a share.
- Windows loopback audio captures only the playback endpoint selected by the host while sharing is active.
- The selected audio-device ID and friendly name remain local and are not serialized into session state.
- Window titles are used locally for source selection but are replaced with a generic session title before any directory or viewer update.
- The live playlist and segments use the same random per-session bearer token as other hosted media and pass through the temporary session tunnel.
- Capture and encoding stop when the session ends or the plugin unloads.
- DRM-protected content may appear black. PrismCast does not disable, evade, or modify DRM/HDCP behavior.

## Session data

- Local folders and Plex library listings stay on the user's computer.
- For local and Plex hosting, viewers receive a temporary proxy address rather than a local path, Plex address, or Plex token.
- For screen sharing, viewers receive the temporary encoded video/audio feed and a generic share title rather than the selected window title or audio-device name.
- The session directory receives random PrismCast identifiers, FFXIV first names, Group names and membership, the hosted title, and temporary connection information needed for Watch Parties and Groups.
- Live directory records expire unless refreshed. Groups persist until a member leaves or the host deletes them.

## Logging and storage

- Error text shown in the UI redacts common secret-bearing query parameters and email-address patterns.
- Session-network failures are logged by exception type without serializing token-bearing request URLs.
- Temporary screen-capture segments are replaced for each session and removed through the capture lifecycle.
- PrismCast contains no behavioral analytics, advertising, or PrismCast account system.
