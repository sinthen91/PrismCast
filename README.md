# PrismCast

**Your media. Your friends. One screen. Watch together anywhere in Eorzea.**

PrismCast brings synchronized movies, shows, videos, and live screen sharing into Final Fantasy XIV on a screen you place in the game world. Host a quick Watch Party with friends or build a saved theater with its own queue, standby screen, and playback permissions.

[Download releases](https://github.com/Sinnsational/PrismCast/releases) · [Alpha.70 release notes](https://github.com/Sinnsational/PrismCast/releases/tag/v0.2.0-alpha.70) · [Installation](#installation) · [Getting started](#your-first-watch-party) · [Feedback & support](#feedback--support)

This guide covers **0.2.0-alpha.70**, built for **Dalamud API 15**. PrismCast remains alpha software. Hosts and viewers should use the same version.

## What you can do

- **Watch together in the world:** Position, rotate, and resize a synchronized flat or curved screen, choose its aspect ratio and frame, and save screen presets.
- **Choose your media:** Browse Plex movies and episodes, scan local video folders, load supported Web / YouTube links, or share a desktop or visible application window with audio.
- **Host a quick Watch Party:** Share a temporary six-character session code.
- **Keep your group together:** Create a permanent Group with a reusable invite and saved membership.
- **Run a theater:** Save its screen setup and default volume, prepare a persistent queue, keep reusable Saved Media, and play a looping standby video between screenings.
- **Share the controls:** Assign moderators or temporarily pass the remote to one viewer.
- **Personalize your viewing:** Choose your own volume, available audio track, subtitles, and ambient glow.
- **Learn inside the app:** Use the spotlight walkthrough, searchable User Manual, and FAQ.
- **Contact the developer in-app:** Send feedback and private support requests, attach screenshots, and read replies without email or Discord.

The Solution 9-inspired **Mobile** and **Tablet** layouts are two interface styles inside FFXIV, not separate phone or tablet applications. PrismCast is standalone and can run alongside other synchronization plugins.

## Installation

You need the Windows version of FFXIV with XIVLauncher/Dalamud and a compatible Dalamud API version.

1. Open **Dalamud Settings → Experimental → Custom Plugin Repositories**.
2. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/Sinnsational/PrismCast/main/pluginmaster.json
   ```

3. Save, open the **Plugin Installer**, search for **PrismCast**, and install it.
4. Open PrismCast with `/prism` or `/prismcast`, then follow the startup wizard.

**Release availability:** Alpha.70 is available on the [Releases page](https://github.com/Sinnsational/PrismCast/releases/tag/v0.2.0-alpha.70). At this README update, the custom repository feed still advertises Alpha.61. Installing through that feed will provide Alpha.61 until it is updated; the Alpha.70 theater and support features require the newer package.

PrismCast downloads its playback tools automatically as needed. The first playback or hosting attempt can take longer while they are prepared. Viewers do not need to install these tools manually or configure a relay.

## Your first Watch Party

1. Open **Session** and choose **Host Watch Party**.
2. Open **Library** and select **Plex**, **Local Files**, or **Web / YouTube**. Choose a title and press **Play**. For a URL, choose **Load Media** first, then play its preview.
3. Return to **Session** and copy the guest session code.
4. Your friends open PrismCast, enter the code under **Join a Session**, and choose **Join by Code**.
5. Use **Remote** to place the screen and control playback. **End Session** closes the broadcast.

Every viewer needs PrismCast. To watch the same screen together, meet in the same in-game location. The host must keep FFXIV, PrismCast, the computer, and the selected media source available.

A Watch Party code expires when its session ends. For a recurring movie night, create a **Group** instead: friends join with its permanent invite, then find it under **Your Groups** whenever you host again.

## Media sources

| Source | How to use it | What to know |
| --- | --- | --- |
| Plex | Sign in through **Settings → Plex**, choose a server, then browse **Library**. | Movies and show → season → episode browsing are supported. Viewers of your hosted title do not need your Plex login. |
| Local Files | Add folders in **Settings → Local Media**, refresh, then browse **Library**. | Files stay on the host's computer or connected drive and must remain accessible. |
| Web / YouTube | Paste a supported HTTP/HTTPS video URL in **Library → Web / YouTube**. | Not every webpage is playable. Login-required or protected sources may fail. |
| Share Screen / Window | Choose the capture source, audio output, and quality in **Library**. | Captures the selected visible region and Windows playback endpoint. Protected video may appear black. |

### Live screen sharing

Choose **Performance (720p30)**, **High Quality (1080p30)**, or **High Motion (1080p60)**. Higher settings require more host processing power and upload bandwidth per viewer.

Window capture uses the window's visible desktop region. Keep it visible and unobstructed; restart sharing if you move it. Anything visible in that region, including notifications, can appear in the broadcast.

Select the Windows audio output that the source application actually uses. Keep the application and its Windows volume unmuted.

For **Wave Link**, route the browser to **Wave Link Browser** and select that endpoint in PrismCast. To avoid hearing the source early or twice, mute only the Browser row's **Personal Mix / Monitor Mix**, leaving the browser and underlying endpoint active.

PrismCast does not sign in to subscription streaming services or bypass DRM/HDCP. Screen sharing does not guarantee that protected services will play.

## Saved theaters and queues

Create a Group, then open **Session → Your Groups → Manage Theater**.

1. Place and style your screen using **Remote**.
2. Expand **Theater settings**, set the name and default volume, and choose an idle image or video. **Use default** restores the bundled PrismCast standby loop.
3. Choose **Save venue with current screen**.
4. Select **Open Theater** to restore the setup and start standby playback. Manage Theater stays open so you can prepare the program.
5. Add Plex titles, local files, or supported URLs in **Manage Theater Media**.

**Queue** holds upcoming items. Search, move entries up or down, remove an entry, or start it immediately. **Play next** starts the first item; normal finite media advances automatically and returns to standby when the queue finishes. The queue supports up to 100 items.

**Saved Media** keeps reusable favorites. Enable **Also save to theater** when adding an item to keep it for future screenings. Removing a saved entry preserves its queued copies; removing either kind of entry does not delete the original media.

Plex shows and seasons open an episode picker so you can queue selected episodes in order. Authorized moderators and remote holders can request Plex titles from the same server while the owner is hosting; the owner resolves the source with their own credentials.

### Roles and the remote

| Role | Controls |
| --- | --- |
| Owner | Hosts media, edits theater settings and Saved Media, manages roles and members, controls playback and queues, and passes or reclaims the remote. |
| Moderator | Controls playback and queues while the owner's moderator controls are enabled. |
| Viewer | Watches, invites others, and adjusts personal viewing settings. |
| Viewer holding the remote | Temporarily gains playback and queue controls until the owner takes the remote back. |

Shared screen placement and theater setup remain owner-controlled. Passing the remote does not transfer hosting or access to the owner's Plex account and local folders.

Saved theaters require the owner online. They do not provide unattended hosting or automatic host migration.

## Screen, audio, and subtitles

**Remote → Screen Controls** separates **Placement** from **Display**. Adjust position, rotation, size, curvature, aspect ratio, and frame style, or restore a saved Theater Preset.

Screen presets are reusable setups on your device. A saved Group theater additionally remembers its shared setup, permissions, queue, and idle screen.

**Audio** and **CC** choose tracks locally while everyone remains on the synchronized timeline. Available tracks depend on the source. Volume and ambient glow are also personal settings. Live screen sharing carries the captured picture and sound; it cannot expose a browser player's alternate tracks as separate viewer choices.

## Help, feedback, and support

- **Settings → Startup Wizard:** Replay the interactive spotlight tour.
- **Settings → User Manual:** Search step-by-step instructions for playback, theaters, queues, and troubleshooting.
- **Settings → FAQ:** Find answers to common usage and privacy questions.

### Feedback & support

Open **Settings → Support** to report a bug, suggest a feature, or contact the developer privately.

Requests support PNG/JPEG screenshots under 1 MB and an optional, previewed plugin-version/layout summary. **My Requests** holds your conversations and replies; the Support tile indicates unread replies. Refresh manually or keep the app open for periodic updates.

Use **Recover my requests** to save a private recovery code before reinstalling. Anyone with that code can access your support history. Support is asynchronous, not live chat.

## Privacy and account safety

**Plex sign-in happens on Plex's official website.** PrismCast never receives your Plex email address or password through that sign-in. The server token is encrypted locally for your Windows user with DPAPI, is not sent to viewers or the PrismCast directory, and is removed by **Disconnect Plex**.

**There is no YouTube sign-in or browser-cookie import.** PrismCast does not request streaming-service passwords, and its managed yt-dlp calls ignore external configuration.

**Your full library and folders are not uploaded.** Local/Plex hosting provides temporary, token-protected access to the selected media through a Cloudflare Quick Tunnel. Public Web / YouTube playback shares the selected source URL for clients to resolve locally. Authorized viewers receive media data; access controls cannot recall data already received.

**The directory stores data needed for shared features.** This includes PrismCast identifiers, FFXIV first names, Group names and membership, roles, theater settings, selected catalog titles/identifiers, queues, and temporary session connection information. Live records expire unless refreshed; saved Group and theater data persist. Private local paths, Plex credentials, and owner-side source mappings remain local.

**Private support is visible to you and the developer.** Submitted messages, screenshots, and any included version/layout summary are stored by the support service. The developer can also technically access the directory's server-side records and active connection information. These features do not grant access to your account passwords, full Plex library, or unrelated files.

Screen sharing can reveal anything visible in the chosen region. Keep passwords, tokens, private codes, and sensitive media links out of broadcasts and support attachments.

## Troubleshooting

| Problem | First things to check |
| --- | --- |
| Audio plays but no screen is visible | Use Remote to check screen placement and size, or place it in front of you. |
| Screen sharing has no audio | Match Audio Source to the application's active Windows output and leave that source unmuted. |
| Viewers buffer | Try Performance capture quality and check the host's upload connection. |
| A code or Group will not join | Confirm the host is live, refresh Session, and check plugin versions. |
| A queue action is unavailable | Check your role, moderator policy, and whether you hold the remote. |
| Plex titles are missing | Check the selected server and library, confirm access, and refresh. |
| Protected browser video is black | The source may enforce DRM/HDCP; PrismCast does not bypass it. |

Keep your plugin configuration when updating. It contains saved setup and installation identity used for Group ownership. When upgrading older Groups, the owner may need to open the Group first and existing members may need to rejoin with its permanent invite.

## Development

Run `build.cmd` on Windows. It uses an installed .NET 10 SDK or prepares a private copy, locates the XIVLauncher Dalamud development binaries, restores dependencies, and builds Release/x64 for Dalamud API 15.

Build outputs:

- `dist\PrismCast-Dev\PrismCast.dll`
- `dist\PrismCast.zip`

A Dalamud **Dev Plugin Locations** entry points to the DLL itself.

Runtime components are downloaded into the plugin configuration directory as needed: **libmpv**, **yt-dlp**, **Deno**, **cloudflared** for hosting, and **FFmpeg** for screen sharing.

The `directory-service/` folder contains the Railway-compatible directory and support backend. The `relay/` folder retains the Cloudflare Worker implementation. Normal users do not configure either service.

## Releases and license

See [GitHub Releases](https://github.com/Sinnsational/PrismCast/releases) for version history and downloadable packages, including the changes from Alpha.61 to Alpha.70.

PrismCast is distributed under **AGPL-3.0-or-later**. See [LICENSE.md](LICENSE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licensing and attribution.
