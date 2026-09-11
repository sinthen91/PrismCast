
## Alpha 0.2.0.41 Plex completeness regression

1. Open a Plex library containing more than 600 titles and clear the search field.
2. Scroll continuously past the point where alpha.40 stopped (for example beyond titles beginning with L).
3. Confirm titles through the end of the alphabet remain reachable.
4. Search for a title near the end of the alphabet and confirm the same item also appears in normal unfiltered scrolling.
5. Scroll rapidly through a large library and confirm PrismCast remains responsive while posters load only as rows come into view.

# PrismCast alpha test plan

Use this before publishing `v0.2.0-alpha.1`.

## 1. Host build

1. Remove/disable the old MediaCast-modified sync-plugin dev build so it cannot confuse the test.
2. Keep the normal sync plugin you ordinarily use installed normally.
3. Run `build.cmd`.
4. In Dalamud Dev Plugin Locations, point to:
   `dist\PrismCast-Dev\PrismCast.dll`
5. Scan dev plugins and enable **PrismCast**.
6. `/prismcast` must open the PrismCast window.

## 2. Two-client direct invite test

On the host:

1. Host a short local MP4.
2. Confirm the host sees the world-space screen and hears audio.
3. Copy the PrismCast invite code.

On the viewer:

1. Install/load the same PrismCast build.
2. Keep their ordinary synchronization plugin unchanged.
3. Open `/prismcast` -> **Join**.
4. Paste the invite code and join.

Verify:

- the screen appears on both clients in the same world position
- play/pause propagates
- +/-10 second seeking converges on the viewer
- viewer drift settles without repeated visible jumps
- viewer local volume changes do not alter host volume
- leaving the cast hides the screen
- stopping the host eventually returns the viewer to an offline/idle state

## 3. URL/YouTube test

1. Open **Library** and choose **Web / YouTube** from the media-source selector.
2. Paste a public YouTube URL and select **Load Media**.
3. Confirm title, provider, duration, and thumbnail appear when yt-dlp exposes them.
4. Select **Play** and confirm the host enters the normal PrismCast session flow.
5. Repeat with a direct HTTP(S) video URL. Metadata may be unavailable, but direct playback should still be offered.
6. On a second client, join the Watch Party/Group and confirm both clients resolve/play the web source while using the host only for synchronized session state.

## 4. Plex test

Refresh Plex libraries, select a movie, and verify the path returned by Plex is accessible on the host machine. The current alpha hosts the resolved file path from the host PC.

## 5. Trusted-host relay test (optional)

After deploying the relay Worker/KV:

1. Put the Worker URL in both clients.
2. Viewer saves the host's PrismCast host ID once.
3. Enable **Automatically join trusted hosts**.
4. Start a new cast on the host.
5. Viewer should discover/join it without receiving a new invite code.

## 6. Publish only after the direct two-client test passes

Create release/tag `v0.2.0-alpha.1` and attach `dist\PrismCast.zip` with the exact filename `PrismCast.zip`.
