# PrismCast directory v2

Run `npm test`, then `npm start` with Node 20+. Serves authenticated group APIs for alpha.65 while retaining legacy support for groups that have not migrated. The owner authenticates an existing group first; existing viewers rejoin with its permanent invite once.

Production requires a persistent volume, one replica, `DATA_FILE=/data/state.json`, and `PRISMCAST_REQUIRE_STATE=1`. Keep the existing public domain and `/health` healthcheck. Health reports version 2.

For a migration from an ephemeral container, obtain a private snapshot of its state file before any restart. Stage `PRISMCAST_INITIAL_STATE` with that JSON in the same service's environment, together with the new volume and code. On first startup only, a missing data file is initialized atomically from this variable. Existing or corrupted files are never overwritten by the snapshot. Verify the persistent file and group records, then remove the temporary variable. Never commit group databases, invite codes, member credentials or snapshots to this repository.

`PRISMCAST_REQUIRE_STATE=1` prevents silently starting with an empty database after a storage failure. For a brand-new installation, initialize the volume with `{}` once before enabling that guard.

Directory v2 stores role policy, venue settings, public catalog identifiers/titles and queue entries. Owner media paths and Plex source credentials remain on the owner client. All group control actions are authenticated and permission-checked. Group IDs and device identifiers alone are not credentials.

## Theater media manager (alpha.67)

Health and group snapshots now report `mediaVersion: 1`. This is an additive update; group membership, credentials, permanent invites and stored venue settings require no migration.

Owner-only `media-upsert` registers sanitized metadata and can enqueue it atomically. Queue-only entries have `saved:false`. `media-unsave` hides a saved item without removing queued copies. Removing the last queue copy cleans unused queue-only metadata. `queue-move` changes one existing entry by one position, with normal queue permissions. Legacy catalog entries default to saved in new clients.

The owner can advertise a SHA-256 Plex machine-identifier fingerprint. Authorized moderators/remote holders can request `queue-plex` with that fingerprint and a numeric rating key, while the venue is live. The host validates its server and resolves the item using its own credentials. No Plex tokens, server URLs or file paths enter these requests. Private files and arbitrary URLs must be added on the owner installation. Update hosts to alpha.67 before using these requests; earlier hosts do not execute them.

## Private support inbox (Alpha 70)

Set `SUPPORT_ORIGIN` to the service's canonical HTTPS origin and `SUPPORT_OWNER_GITHUB_ID` to the owner's immutable numeric GitHub ID. Visit `/support` and complete GitHub App manifest registration. The server verifies the returned app owner before saving OAuth credentials; subsequent logins verify `/user` against the same ID. No repository permissions are requested. GitHub access tokens are used only for that verification and are not persisted. HTTP-only, Secure, SameSite=Lax cookies expire after seven days. Admin writes require the configured Origin.

All support records use the existing string-valued persistent state store with separate `support:` keys. The plugin sends a separate random 256-bit support credential; only its hash is stored with requests. Recovery codes are those credentials and must stay private. There is no public ticket listing. Screenshots are authenticated PNG/JPEG responses with nosniff and no caching. Text is rendered as text, never HTML. Logs/credentials are not collected automatically.

Limits: 1 MB per screenshot, 10 unresolved tickets per identity, 200 messages per thread, 5,000 tickets, 40 MB content. This is a small-community inbox; add indexed storage, retention/admin deletion and distributed rate limits before substantial growth. Optional request UUIDs make retries idempotent for 24 hours. Run `node --test support.test.mjs groups-v2.test.mjs state-store.test.mjs` before deploying.
