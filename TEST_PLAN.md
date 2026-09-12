
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
# Alpha 0.2.0.42 startup wizard regression

1. Clear `OnboardingVersion` from the plugin configuration and open PrismCast.
2. Confirm the supplied 3.2-second portrait movie plays in a mobile-sized window and fades fully to black.
3. Confirm the wizard opens on Welcome, cannot pass Layout without choosing Mobile or Tablet, and resumes its saved page after a plugin reload.
4. Choose Mobile and confirm the window remains portrait. Choose Tablet in a fresh configuration and confirm the black window expands around a fixed center before the next page appears.
5. Complete Plex sign-in and choose a local video from Media Setup; confirm the Plex server, selected filename, and local folder are persisted.
6. Create a Group from the wizard, then verify it appears under Session > Your Groups.
7. Finish or skip the tutorial after Layout; confirm it does not automatically return on the next reload.
8. Open Settings > Startup Wizard and replay it; confirm existing Plex, local-folder, Group, and layout settings remain intact.
9. Close and reopen the PrismCast window in the same game session; confirm the startup movie replays and an existing hosted video is not stopped or replaced.

# Alpha 0.2.0.43 interactive tour regression

- Verify Welcome, Layout, and Media Setup text stays inside every card at 100%, 125%, and 150% UI scale.
- Verify Mobile and Tablet descriptions wrap without truncating or crossing their card borders.
- Verify Choose Media Folder opens an opaque folder picker and adds the selected directory to Local Files.
- Verify coach-mark steps show the live Now Playing card, transport controls, and Screen Controls with only the selected element undimmed.
- Verify Library coach marks highlight the real source selector and the connected user's real media area.
- Verify Session coach marks separately highlight Host a Session, Join a Session, and Your Groups.
- Verify Back and Next move the spotlight in both Mobile and Tablet layouts and that progress resumes after closing PrismCast.
- Verify Finish opens the normal Remote page and Settings > Startup Wizard can replay the tour without erasing Plex or Local Media settings.

# Alpha 0.2.0.44 FAQ and account-safety regression

- Verify the former General tile, page header, page heading, and wizard reference all say App Layout.
- Verify the FAQ tile appears in both Mobile and Tablet Settings and every answer wraps inside its card.
- Verify the FAQ page scrolls through all account, session, local-media, Watch Party, Group, analytics, and privacy answers.
- Connect Plex through the browser, restart PrismCast, and verify the selected server still works without retaining `PlexAccountToken`.
- Verify Disconnect Plex clears the locally stored server token and removes library access.
- With a global yt-dlp configuration present, verify PrismCast ignores it for both metadata preview and playback.
- Verify a public YouTube URL still previews and plays, while no YouTube credentials or browser cookies are requested.

# Alpha 0.2.0.45 ImGui crash diagnostic

- Start FFXIV with PrismCast disabled, replace the Alpha.44 DLL with Alpha.45, then enable PrismCast.
- Open PrismCast and verify the startup movie, first-run guide, and normal interface can render without an ImGui assertion or native `cimgui.dll` crash.
- Close and reopen PrismCast, then restart FFXIV with the PrismCast window saved open; verify both paths remain stable.
- Open other Dalamud plugin windows before and after PrismCast and verify they continue to draw and accept input.
- If PrismCast reports a managed draw exception, verify the first exception is logged once rather than repeated every frame and other Dalamud windows remain usable.
- Open the Local Files folder picker and verify it works without the former file-dialog theme wrapper.
- Expect some outer window controls and file-dialog styling to use Dalamud defaults in this diagnostic build.
- If the crash remains, capture `dalamud.log` before restarting and note whether the last PrismCast stack frame still points to `PrismCastWindow.Draw()`.

# Alpha 0.2.0.46 ImGui style-stack repair

- Open PrismCast in Tablet mode and verify no `Size > 0` assertion occurs at `DrawShell()`.
- Open PrismCast in Phone mode and verify the existing rounded phone screen and normal theme still render correctly.
- Verify the startup video plays unchanged before the normal interface appears.
- Verify the folder picker retains its opaque PrismCast styling.
- Open and interact with other Dalamud windows before and after PrismCast; they must remain visible and responsive.
- Close and reopen PrismCast, then restart FFXIV with PrismCast saved open and verify the UI remains stable.

# Alpha 0.2.0.47 session, presence, and shared-screen repair

- From a second client, enter a live Watch Party short code and confirm it joins without an invalid-code or `Not on main thread` error.
- Join a persistent Group using its permanent code; confirm it is bookmarked and automatically enters the active cast when the Group is live.
- Confirm the host changes from `1 WATCHING` to `2 WATCHING` within ten seconds of a viewer joining.
- Leave normally and confirm the count decreases immediately; close/crash the viewer and confirm the stale entry expires within thirty seconds.
- On the viewer, orbit, zoom, and collide the camera around a flat and curved world screen. Confirm the screen remains world-anchored and never expands into sliced rectangles over the camera.
- In Phone and Tablet modes, confirm the viewer-only Screen Controls explanation remains inset and wraps inside its card.
- In Phone and Tablet Library > Web / YouTube, confirm MEDIA URL, the input, and Load Media have consistent side padding, and the button is centered rather than full-card width.
- Replay the startup video and Tablet setup selection to confirm the Alpha.46 ImGui style-stack repair remains intact.

# Alpha 0.2.0.48 world-screen visibility hotfix

- Start media on the host and confirm both the video surface and audio are present locally.
- Join from a second client and confirm the viewer sees the video surface at the host-owned placement while hearing synchronized audio.
- Orbit and zoom the viewer camera through the screen location; confirm camera intersection hides the surface briefly instead of producing giant sliced rectangles.
- Confirm the Alpha.47 Group join, People Watching, Screen Controls padding, and Web / YouTube form fixes remain intact.

# Alpha 0.2.0.49 viewer mesh and Twitch live repair

- Join from a second client, orbit and zoom around both flat and curved screens, and confirm no detached slabs or duplicated-looking screen pieces appear.
- Confirm the screen may disappear briefly when the camera physically crosses its plane, then returns intact after the camera clears it.
- Host a public Twitch channel URL, join from a second client, and allow the initial live buffer to fill without pausing the host.
- Confirm viewer video and audio both begin, playback is no longer repeatedly seeked, and pause/resume still follows the host.
- Confirm ordinary local files, Plex, YouTube VODs, and direct seekable URLs retain normal timestamp synchronization.

# Alpha 0.2.0.50 machine-specific render isolation

- Retest on the machine that showed detached video fragments while the host and control viewer remain in the same scene.
- Confirm the screen is one continuous surface at the expected world position with both Flat and Curved selected by the host.
- Orbit and zoom the affected viewer camera; confirm no fragment remains in the upper-left portion of the screen.
- Open GShade/ReShade and other overlay windows during playback and confirm PrismCast remains intact and those tools continue rendering after PrismCast restores their shader stages.
- Recheck Twitch live playback and all Alpha.49 synchronization behavior.

# Alpha.51 Plex credential-storage migration

1. Upgrade from Alpha.50 with Plex connected. Confirm `PlexToken` is empty in the normal plugin configuration, `PrismCastSecrets.dat` exists, and Plex still browses.
2. Confirm **Disconnect Plex** removes the encrypted token.

# Alpha.52 screen/window sharing checks

1. Open **Library → Share Screen / Window**, refresh the target list, and confirm it contains **Entire desktop** plus currently visible top-level windows.
2. Select a harmless application window and start sharing. Confirm PrismCast downloads FFmpeg only on the first use, shows the capture on the host's world screen, and includes Windows output audio.
3. Join from a second PC running the same PrismCast build. Confirm video and audio begin near the live edge and remain stable for at least ten minutes.
4. Confirm the directory/session title says only **Application Window Share** or **Entire Desktop Share**, never the real selected window title.
5. End the session and confirm FFmpeg exits, audio-loopback capture stops, the tunnel closes, and the world screen disappears for host and viewer.
6. Close or minimize the selected window during a session. Confirm PrismCast fails safely or freezes/goes black without switching to a different private window.
7. Test a DRM-protected page only to confirm the warning is accurate. A black protected-video area is expected; do not add or test a protection bypass.
8. Regression-test local files, Plex, YouTube, Twitch, direct URLs, Watch Parties, Groups, and shared-screen placement.

# Alpha.53 WASAPI compatibility repair

1. Test on the machine that produced the `MMDeviceEnumeratorComObject` invalid-cast error and confirm **Start Screen Share** proceeds past audio initialization.
2. Confirm Windows output audio is present with the user's normal default playback device.
3. Enable other audio-related Dalamud plugins before starting the share and verify they cannot cause a Core Audio wrapper collision.
4. Start and stop three screen-share sessions consecutively, confirming the Windows audio client and FFmpeg process are released each time.

# Alpha.54 browser-window capture repair

1. Open an ordinary YouTube video in Edge or Chrome, keep the window visible, select it under **Share Screen / Window**, and confirm both video and mouse movement appear instead of a black background.
2. Repeat using **Entire desktop** and confirm all visible non-protected desktop content appears.
3. Move another window over the selected browser region and confirm the overlap is captured, as disclosed by the UI; restart after moving/resizing the selected window.
4. Confirm the encoded picture never exceeds 1280x720 and a 21:9 source retains its aspect ratio rather than stretching.
5. Treat a black rectangle limited to DRM-protected video as expected protection behavior, not a screen-capture regression.

# Alpha.55 Windows build-launcher repair

1. Extract the complete source ZIP and double-click `Build.cmd`. Confirm the banner and **Starting PowerShell build script** appear immediately.
2. On a PC without the .NET 10 SDK, confirm the window identifies the download/install step and continues showing progress.
3. On a PC with a .NET 10 SDK, confirm the builder selects it without pointing `DOTNET_ROOT` at the private fallback directory.
4. Confirm a failure remains visible until a key is pressed and displays the `build.log` location.
5. Confirm the generated `dist\PrismCast.zip` contains runtime DLL/JSON/PDB files and excludes nested build-package folders.

# Alpha.56 audio routing and latency repair

1. Open **Share Screen / Window** and confirm Audio Source lists **Default Windows output** plus active physical and virtual playback endpoints.
2. Route a browser to a dedicated virtual endpoint such as **Wave Link Browser**, select the same endpoint in PrismCast, and confirm browser audio is present in the in-game screen stream.
3. In Wave Link, mute the Browser row only under **Personal Mix** (called Monitor Mix in older versions). Keep the Browser row enabled under Stream Mix, and keep the browser tab, Windows app volume, and Wave Link Browser endpoint active. Confirm direct browser sound stops while the audio embedded in PrismCast remains audible and synchronized with its video.
4. Mute the browser tab or its Windows app volume and confirm PrismCast captures silence; restore it before continuing. This verifies that the UI warning distinguishes source mute from Monitor Mix routing.
5. Confirm the selected endpoint persists after reopening PrismCast but its ID/name never appears in Watch Party, Group, state, or directory payloads.
6. Compare Alpha.55 and Alpha.56 latency using a visible timer/video. Confirm half-second HLS segments and the four-segment playlist reduce delay without repeated buffering on the host and one remote viewer.

# Alpha.57 source-selector visibility repair

1. Connect a Plex server containing four libraries matching the reported setup: Anime Movies, Movies, Anime, and TV Shows.
2. Open the Library source selector at the default PrismCast window size and confirm all four Plex libraries plus Local Files, Web / YouTube, and Share Screen / Window are visible without scrolling.
3. Select Share Screen / Window and confirm the capture-target and Audio Source controls appear.
4. Select each ordinary media source and confirm all selector actions still close the popup and navigate correctly.
5. Test with more than seven Plex libraries and confirm the popup shows ten rows with a visible scrollbar for the remaining entries.

# Alpha.58 screen-share-only browser media

1. Confirm the Library selector contains Plex libraries, Local Files, Web / YouTube, and Share Screen / Window, with no Streaming Services entry.
2. Confirm the Settings hub contains no Companion tile or pairing controls.
3. Confirm the compiled plugin does not start a listener on the retired companion port and the release ZIP contains no browser-extension folder.
4. Select Share Screen / Window and confirm target selection, Audio Source selection, Wave Link routing, and Start Screen Share remain available.
5. Host an ordinary browser video and join from a second Alpha.58 client. Confirm the viewer needs only PrismCast and receives synchronized encoded video/audio.
6. Confirm selected window titles and audio-device names remain local and the session uses a generic share title.

# Alpha.59 Windows audio-endpoint discovery repair

1. Open Share Screen / Window on the machine that produced `E_NOINTERFACE` for `IMMDeviceCollection` and confirm the error no longer appears.
2. Confirm Capture Source lists Entire desktop and visible windows while Audio Source lists Default Windows output plus active physical and virtual render endpoints.
3. Select Wave Link Browser and start a share. Confirm the selected browser audio is embedded in the live feed.
4. Simulate or force an optional endpoint-enumeration failure and confirm Capture Source remains usable while Audio Source falls back to Default Windows output.
5. Start and stop three shares and confirm no stale COM objects, FFmpeg processes, named pipes, or capture segments remain.

# Alpha.60 command aliases and window restoration

1. Run `/prism` while PrismCast is closed and confirm the window opens and receives focus.
2. Close it, run `/prismcast`, and confirm the same behavior.
3. Minimize PrismCast into compact mode, run either command, and confirm the full window is restored, resized, and brought forward.
4. Put another Dalamud window over PrismCast, run either command, and confirm PrismCast is brought to the front.
5. Run `/xlhelp` and confirm both `/prism` and `/prismcast` are listed as `Open or restore PrismCast.`
6. Use the plugin installer's Open button and confirm it follows the same restore-and-focus behavior.

# Alpha.61 Screen Share quality, subtitles, and Session layout

1. Open Share Screen / Window and confirm the Stream Quality selector offers Performance (1280x720, 30 FPS), High Quality (1920x1080, 30 FPS), and High Motion (1920x1080, 60 FPS).
2. Start one share with each preset and inspect the generated HLS stream. Confirm its frame rate, maximum dimensions, target bitrate, and maximum bitrate match the selected profile.
3. Join each share from a second Alpha.61 client and confirm the viewer switches between the 1280x720 and 1920x1080 render textures without restarting the plugin.
4. Confirm the selected quality persists after closing and reopening PrismCast.
5. While hosting and while viewing, open Session in Mobile and Tablet layouts. Confirm no bottom Now Playing player appears and the recovered space is available to Groups.
6. Confirm the Remote page retains its normal playback controls and that other non-Session pages still show the footer player where intended.
7. In Settings > Playback, enable Show subtitles when available and play a Plex item containing an embedded subtitle track. Confirm subtitles render on the in-world screen.
8. Disable the setting during playback and confirm subtitles disappear; enable it again and confirm an available track returns.
9. Join the Plex session from a second client with the opposite subtitle preference and confirm each device independently respects its own setting.
