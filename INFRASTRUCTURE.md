# Commoners: infrastructure requirements

Everything the game needs that is not playing the match: accounts, social, matchmaking, match hosting, anti-cheat, ranking, progression, store, website, operations, compliance. Written from the design doc (`GAME_DESIGN.md`), the client/server contract (`PROTOCOL.md`), the server skeleton (`Server/`) and the home hub as built on 2026-10-25.

Conventions:

- **MUST** = needed before the first online test with strangers. **SHOULD** = needed before the Steam launch. **LATER** = after launch or after a dependency lands.
- Every requirement has an id (`ACC-3`, `MM-2`…) so PROTOCOL.md, tickets and tests can reference it.
- The build order from `GAME_DESIGN.md` still applies: stress test → multiplayer spike → pick the networking model → then everything below beyond what a lobby needs. Sections marked **[after model]** depend on that decision.

Decisions already made that this document inherits: own account system with platform logins as linked identities; .NET 8 minimal API, PostgreSQL, WebSocket for presence; store SKUs and prices defined server-side and fulfilled through each platform's payment rails; Matchmaking is human-only, Freeplay may bot-fill; FFA sizes 2–9 plus shared-castle duos; seasons every few weeks; cosmetics persist across seasons.

---

## 1. Principles

- **P-1 The server owns every fact that has value.** Ratings, currency, entitlements, inventory, progression, match results, clan membership. The client renders and requests; it never reports a number the server could compute or verify. (The ranking prototype already states this: "clients must never submit ratings, queue timestamps, currency awards, damage, kills, unit spawns or final placement as trusted facts.")
- **P-2 One account, many logins.** `accountId` is the identity everywhere (DB keys, chat, clans, results). Steam / Apple / Google / PSN / Xbox ids are rows in a link table.
- **P-3 Data-driven balance.** Unit stats, upgrades, perks, talents, drop tables and prices live in versioned config the server serves and the client caches. A balance patch must not require a client release.
- **P-4 Boring, testable, one command to run locally.** `docker compose up` (Postgres + API + a match host) and every domain rule has an executable test, like `Server/Ranking.Tests`.
- **P-5 Cross-play and cross-progression from day one.** No table, endpoint or store flow may assume a single platform.
- **P-6 Fail safe for the player.** Any outage degrades to local practice. Nothing a player paid for can be lost because a service was down.

---

## 2. Accounts and identity

| Id | Req | Requirement |
|---|---|---|
| ACC-1 | MUST | Account record: `accountId` (UUID), display name, created at, region, status (active / suspended / deleted), privacy flags. |
| ACC-2 | MUST | Platform login: `POST /v1/auth/login { provider, token }` verifies the token **with the platform** (Steam session ticket via `ISteamUserAuth/AuthenticateUserTicket`, Apple Sign-In JWT, Google id token; PSN / Xbox later), finds or creates the account, returns a short-lived JWT plus a refresh token. `guest` provider is dev-only and disabled in production builds. |
| ACC-3 | MUST | Linking: `POST /v1/auth/link` attaches another provider to the signed-in account; refuse if that provider id already belongs to a different account and return a merge path (support ticket at first, self-serve merge LATER). |
| ACC-4 | MUST | Session tokens: JWT (15 min) + rotating refresh token (30 days, revocable). All calls after login carry `Authorization: Bearer`. Revoke all sessions on password/link change, ban, or player request. |
| ACC-5 | MUST | Display names: 3–20 chars, letters / digits / space / `_` / `-` (matches `LocalPlayerProfile.Validate`), unique case-insensitively **or** suffixed with a discriminator (`Copperpot#2210`, the friend-code format the hub already shows). Pick one; the friend code implies the discriminator. Profanity filter, rename cooldown (e.g. 30 days), name history kept for moderation. |
| ACC-6 | MUST | Profile read: `GET /v1/profile` returns name, avatar/initial, clan tag, rating summary, owned cosmetics summary, settings blob (see ACC-9). |
| ACC-7 | SHOULD | Account deletion and data export (GDPR / CCPA): request, 30-day grace, then purge PII while keeping anonymised match rows. |
| ACC-8 | SHOULD | Age gate at account creation (date of birth or platform age); under-13 (or regional threshold) accounts get chat, purchases and friend requests disabled (COPPA). |
| ACC-9 | SHOULD | Cloud-synced settings (graphics tier, volumes, keybinds, hub preferences) so cross-progression carries settings, with device-local override. |
| ACC-10 | LATER | Two-factor for email-linked accounts; login alerts on new device. |

Server-side validation: never trust the platform id from the client, only the verified ticket; rate-limit login (10/min per IP, 30/hour per account); log every link/unlink with IP and device.

---

## 3. Social: friends, presence, chat, parties

The hub already presents friends with In match / Online / Offline groups, requests, add-by-name-or-code, DM tabs, clan chat, party chat and party slots. Backend must supply exactly that.

| Id | Req | Requirement |
|---|---|---|
| SOC-1 | MUST | Friend graph: request / accept / decline / remove / block. Block hides presence and chat both ways and prevents future requests. Cap friends (e.g. 200) and pending requests. |
| SOC-2 | MUST | Add by display name + discriminator or by friend code; never by platform id. Platform friends import (Steam friends who own the game) SHOULD. |
| SOC-3 | MUST | Presence over the WebSocket: `offline / online / in hub / in queue / in match (elapsed) / in party (n of 3)`. Presence is fan-out to friends only, debounced (≤1 update/sec per account). |
| SOC-4 | MUST | Chat channels: direct (friend pairs), party, clan. Messages are stored server-side (DM/party: last 200 per channel, 30 days; clan: 500, 30 days) so tabs rehydrate after a restart and moderation can read them. Max 500 chars, rate limit 5/5s, markup is never interpreted (the client already renders chat as literal text). |
| SOC-5 | MUST | Chat is unavailable between non-friends and to under-age or chat-banned accounts. Profanity filter configurable per region. |
| SOC-6 | MUST | Parties: create, invite (only online friends, only by the leader, only while a slot is free, max 3 now, 4 LATER), accept, decline, leave, kick, transfer leader. Party state is pushed to every member. The leader chooses mode and map size and queues; members see READY and must ready up before the leader can queue. |
| SOC-7 | MUST | Party invite expires (60 s), one outstanding invite per target, invites are dropped if the inviter enters a match. |
| SOC-8 | SHOULD | Report player (from chat, match, clan) with reason and message ids; feeds moderation (section 12). |
| SOC-9 | SHOULD | Notifications feed (friend request, party invite, clan invite, chest ready, season start) with read state, so the client never has to poll. |
| SOC-10 | LATER | Voice is out of scope; rely on platform voice (Steam / console). |

Anti-abuse: friend request rate limit (20/hour), block list honoured in matchmaking (never place blocked players together in Freeplay; Matchmaking best effort).

---

## 4. Clans

The handoff defines the clan screens (My Clan, Find a clan, Create a clan): name 3–24, tag exactly 4 letters A–Z, country, play style Casual / Competitive, joining Open / Request, roles, invite by name or code, delete by typing the tag.

| Id | Req | Requirement |
|---|---|---|
| CLN-1 | SHOULD | Clan record: name (unique, filtered), tag (unique, 4 letters), country, style, joining policy, description, created by, crest color. Soft delete with 30-day restore. |
| CLN-2 | SHOULD | Membership: roles `leader / officer / member`, max members (12 shown in the hub; make it a config value), one clan per account, leave / kick / promote / transfer leadership, leader cannot be removed. |
| CLN-3 | SHOULD | Join flows: Open (immediate), Request (officer approves), Invite (officer sends, 7-day expiry, cancellable). Joining a new clan leaves the old one; the client warns first. |
| CLN-4 | SHOULD | Finder: search by name or tag, filters for style, joining, country; paginated; "CLAN FULL" state. |
| CLN-5 | SHOULD | Clan chat (SOC-4) and a members list with role and presence. |
| CLN-6 | LATER | Clan stats, clan seasons, clan cosmetics. |

Validation: all name/tag rules enforced server-side with the same regexes the client uses; tag collisions return a specific error so the create screen can show it inline.

---

## 5. Matchmaking and lobbies

Modes and rules from the design doc: **Freeplay** (humans first, bot fill after a wait threshold) and **Matchmaking** (human only, ranked). FFA sizes 2–9, duos share a castle, random castle assignment, parties stay together.

| Id | Req | Requirement |
|---|---|---|
| MM-1 | MUST | Queue ticket: `{ accountIds[] (party), mode, size, region, partyRating, createdAt }`. One ticket per account; queueing while in a match is rejected. Ticket state is pushed over the WebSocket (`searching / found / allocating / starting / cancelled`). |
| MM-2 | MUST | Freeplay: fill a size-N match from compatible tickets in the same region; after `freeplayBotFillSeconds` (config, to be tuned) start with bots in the empty slots (`isBot: true` in the setup). Blocked players are never placed together. |
| MM-3 | MUST | Matchmaking (ranked): human only, rating window starts at 100 and widens 5/s to 400 (from `Server/Ranking`), party rating = max member rating (anti-smurf) and parties only face parties of the same size, never bots. If the queue is too thin, keep searching; never degrade to bots. |
| MM-4 | MUST | Duo handling: a party of two occupies one castle slot and both players appear in the roster with the same `castle`. Castle count = solo players + parties. Team ids are per castle. |
| MM-5 | MUST | Custom lobbies: `POST /v1/lobbies` creates an invite-only lobby (the hub's "Custom → FIND LOBBY"); host sets size and bot fill; everyone readies; host starts. Lobby state as already defined in PROTOCOL.md. Custom matches are unrated. |
| MM-6 | MUST | Match setup payload (PROTOCOL.md shape, game agent owns it): `matchId`, `seed`, `configVersion`, `playerCount`, `players[{ slot, accountId, displayName, team, isBot }]`, plus `castle` per player and `mode`. Castle assignment is random on the server; clients never choose. |
| MM-7 | MUST | Reconnect: a disconnected player can rejoin the same match for `reconnectGraceSeconds` (e.g. 120); after that the slot is marked forfeit (section 8). Queue tokens and match tokens survive a client restart. |
| MM-8 | SHOULD | Regions: at least NA-East, NA-West, EU, with ping-based selection; cross-region fallback after a wait in Freeplay only. |
| MM-9 | SHOULD | Queue telemetry: time to match, bot-fill rate, size distribution, dodge rate, so the Freeplay threshold and the 3/5/7 sizes can be tuned with data. |
| MM-10 | SHOULD | Dodge / leave penalties: leaving a found ranked match before start increments a timed queue lock (5, 15, 60 min). |
| MM-11 | LATER | Parties of three and four with their dedicated maps; placement matches for new accounts; map vote for Freeplay. |

---

## 6. Match hosting **[after model]**

Two candidate models, decided by the spike. Infrastructure requirements for each; the shared ones come first.

| Id | Req | Requirement |
|---|---|---|
| HOST-1 | MUST | A match allocator: given a ready ticket group, picks a region, spins up or reserves a match host, hands the client a `{ matchId, host, port, matchToken }`. The match token is signed, single-use, bound to `accountId` + `matchId`, 60 s to connect. |
| HOST-2 | MUST | Match host registration: hosts hold service credentials (mTLS or a signed service JWT) and report `allocated / running / finished / crashed` to the API. The API only accepts match results from the host that was allocated that `matchId` (`authenticatedServerId`, as the ranking prototype already requires). |
| HOST-3 | MUST | Fleet: start with a fixed pool of small VMs per region running N hosts each; autoscale on queue depth LATER. Target cost per match-hour is a tracked metric. |
| HOST-4 | MUST | Config pinning: the host and every client load the same `configVersion` (section 10); mismatch aborts the join with a clear "update required" error. |
| HOST-5 | SHOULD | Spectator / observer slot for moderation and for recording replays. |

**If server-authoritative** (server simulates, clients send commands and render):

| Id | Req | Requirement |
|---|---|---|
| SA-1 | MUST | Headless Unity (or a C# extraction of the sim) runs the match at a fixed tick (e.g. 20 Hz); clients send only commands (rally point, purchases, consumables, respec) and receive state snapshots or deltas. Budget: ~30 KB/s per client at 380 units before compression, so delta compression and interest management are required, not optional. |
| SA-2 | MUST | Command validation on the host: purchases checked against the player's server-side gold/skulls/essences, cooldowns and once-per-match items; rally points inside the map; one command stream per connected account. |
| SA-3 | MUST | Host CPU budget per match measured in the stress test (9 teams at cap, all camps, elites) and used to size HOST-3. |
| SA-4 | SHOULD | Snapshot recording on the host for replays and dispute review. |

**If deterministic lockstep** (every client simulates, server relays inputs):

| Id | Req | Requirement |
|---|---|---|
| LS-1 | MUST | A deterministic simulation (fixed-point or carefully fenced float math, no Rigidbody/NavMesh nondeterminism); this is the `Unit.cs` rewrite the design doc defers until the profile says so. |
| LS-2 | MUST | Input relay server: receives each player's inputs per tick, broadcasts the ordered input set, stores the full input log. Tiny bandwidth; latency = round trip + tick delay. |
| LS-3 | MUST | Result verification: the server replays the stored inputs with the same seed and config in a headless sim and derives placements itself. Client-reported placements are only a hint. Desync detection via periodic state hashes from every client; a desynced client is dropped and the majority hash wins. |
| LS-4 | MUST | Replays come free from the input log; keep them 30 days (ranked) for disputes and highlights. |

Either way: the **client never reports kills, gold, damage, spawns or placement as facts** (P-1). Under lockstep the server recomputes; under server-authoritative it already knows.

---

## 7. Match results and ranking

| Id | Req | Requirement |
|---|---|---|
| RES-1 | MUST | Result payload is produced by the host (SA) or by server replay (LS): `{ matchId, mode, size, configVersion, durationSeconds, placements[{ accountId, castle, placement, eliminatedAt }], perPlayer[{ kills, deaths, gold, skulls, essences, upgrades[], special, consumables[] }] }`. The `POST /v1/matches/{id}/result` in PROTOCOL.md becomes host-to-API, not client-to-API. |
| RES-2 | MUST | Idempotent: the same result twice returns the same rating changes; a conflicting result for the same `matchId` is rejected and alerted (already in `Server/Ranking`). |
| RES-3 | MUST | One database transaction per match: unique match-result row + every rating update + every reward grant. |
| RES-4 | MUST | Elo as prototyped: start 1000, display Unranked until the first completed ranked match, K=32, pairwise average by placement, zero-sum, fractional storage, round on display, ties by competition ranking. Rated only for Matchmaking; Freeplay, Custom and Practice never touch rating. |
| RES-5 | MUST | Forfeit policy: a player disconnected past the grace (MM-7) is placed last among the remaining players at the moment of forfeit; repeated forfeits add queue locks (MM-10). |
| RES-6 | SHOULD | Match history: `GET /v1/profile/matches` (last 50), per-match detail for the results screen, and a public share link LATER. |
| RES-7 | SHOULD | Leaderboards per season and region (top 100 cached, player rank by query), updated by the result transaction, not by a cron. |
| RES-8 | SHOULD | Rank bands (names and thresholds) defined in config once the FFA Elo extension is playtested. |
| RES-9 | SHOULD | Anomaly flags on results: impossible durations, gold per minute beyond the config ceiling, 0-damage winners, same IP across opponents in ranked, repeated opponents (win trading). Flagged results still apply, but are queued for review (section 12) and can be reverted with full audit. |

---

## 8. Anti-cheat and server validation

What is protected: ratings, currencies, entitlements, progression, cosmetics, and the fairness of a live match. The design doc's "some pay-to-win, capped" makes power items valuable, so grants must be airtight.

| Id | Req | Requirement |
|---|---|---|
| AC-1 | MUST | **All grants originate server-side**: match rewards from RES-1, chests from a server timer, battle-pass progress from server-counted XP, purchases from verified receipts. There is no "add currency" endpoint for clients. |
| AC-2 | MUST | **Authoritative match outcome** per section 6; clients cannot submit results. Practice matches (bots, offline) report nothing and grant nothing beyond flat, capped participation XP if any. |
| AC-3 | MUST | **Signed config.** The balance/config bundle is signed by the server; the client refuses to start an online match with a tampered or mismatched `configVersion` (HOST-4). Local edits to `swarm_config.json` only affect Practice. |
| AC-4 | MUST | **Pre-match loadout is server-stored.** Perks (4 slots) and talent spec are chosen in the hub, saved via API, and included in the match setup by the server; the client never sends its loadout to the host. |
| AC-5 | MUST | **Input sanity on the host / relay**: command rate limits per tick, rally positions inside the map bounds, purchases only from the live price table, consumables respecting once-per-match and cooldowns, no commands for units you do not own (duo ally armies stay separate), no commands after elimination. |
| AC-6 | MUST | **Session binding**: match token tied to account + match; one connection per account per match; a second connection kicks the first and is logged. |
| AC-7 | MUST | **Rate limiting and abuse controls** on every endpoint (per IP and per account), request body size caps, WebSocket message caps, idempotency keys on all writes. |
| AC-8 | SHOULD | **Client integrity (defence in depth, not trust)**: Steam ticket + VAC/ban status check at login; build version and hash reported and gated (old builds get "update required"); console platforms enforce their own integrity. Never rely on this instead of server validation. |
| AC-9 | SHOULD | **Behavioural detection** on results (RES-9), on queue behaviour (win trading: same opponents in ranked repeatedly, alternating placements), on economy (impossible resource curves vs. config ceilings), with a review queue, not auto-bans at first. |
| AC-10 | SHOULD | **Replays for disputes** (SA-4 / LS-4) retained 30 days for ranked. |
| AC-11 | SHOULD | **Smurf / boost mitigation**: party rating = max member, new-account placement, hardware/platform id clustering for ban evasion (platform ids only, no fingerprinting beyond what platforms provide). |
| AC-12 | LATER | Kernel anti-cheat is **not** planned; the game is not twitch-based and server authority covers the valuable surface. Revisit only with evidence. |

---

## 9. Progression, seasons, chests, battle pass

From the design doc: talent tree (capped, spec, paid rerolls, free reroll after balance change, resets each season), perks (≈4 slots, from chests and battle pass, some rotate per season), timed free chests (e.g. every 8 h), seasonal battle pass with free and premium tracks, account level and season rewards that persist, cosmetics always kept.

| Id | Req | Requirement |
|---|---|---|
| PROG-1 | SHOULD | Season table: id, start, end, talent tree version, perk rotation, pass contents, reset rules. Season rollover is a server job that snapshots standings, grants end-of-season rewards, resets talents, and bumps the active config. Rollover must be idempotent and rehearsed on staging. |
| PROG-2 | SHOULD | Account XP and level: XP from server-computed match participation (capped per day to blunt grinding bots), level thresholds in config, level never resets. |
| PROG-3 | SHOULD | Talent tree: nodes and prerequisites in config; unlock points earned by play or purchased (paid acceleration); spec cap enforced server-side; reroll endpoint with price, free-reroll token granted automatically when a balance patch changes any node (`treeVersion` diff). |
| PROG-4 | SHOULD | Perks inventory and loadout: owned perks per account, 4-slot loadout saved via API (AC-4), rotation handled by season config (a rotated-out perk stays owned but unslottable). |
| PROG-5 | SHOULD | Timed chests: server timer per account (`nextChestAt`), claim endpoint rolls on a server-side drop table with disclosed odds (section 13), results logged. Claim is idempotent and never client-timed. |
| PROG-6 | SHOULD | Battle pass: tiers in config, XP contributions from matches, premium flag from the store, claim-per-tier with idempotency, catch-up rules; buying premium retroactively unlocks earned premium tiers. |
| PROG-7 | SHOULD | Entitlement model: one `grants` ledger (who, what, why, source, timestamp) that every grant path writes to; inventories are projections of the ledger so support can replay or reverse any grant. |
| PROG-8 | LATER | Races as paid content: entitlement per race, "earnable by grinding" track, launch-balance guard (section 13). |

---

## 10. Config and content pipeline

| Id | Req | Requirement |
|---|---|---|
| CFG-1 | MUST | A versioned config bundle (`swarm_config.json` plus talents, perks, prices, drop tables, queue thresholds, feature flags) served by `GET /v1/config/{version}` with ETag caching and signed (AC-3). `configVersion = swarm_config@<hash>` as PROTOCOL.md already names it. |
| CFG-2 | MUST | Compatibility matrix: which client builds accept which config versions; the login response tells the client the active version and the minimum build. |
| CFG-3 | SHOULD | Config authoring lives in the repo, validated by tests (schema, no negative prices, special unlock reachable in 2–4 min by simulation), promoted dev → staging → prod by CI, with rollback. |
| CFG-4 | SHOULD | Feature flags and kill switches (ranked on/off, bot fill threshold, chat on/off, store on/off) changeable without deploy. |
| CFG-5 | SHOULD | Localisation strings served the same way; client ships a fallback copy. |

---

## 11. Store and entitlements

Platform rails (Steam, App Store, Google Play, later PSN / Xbox, ~30% each), SKUs and prices defined server-side, odds disclosed, power always earnable, nothing launched overpowered.

| Id | Req | Requirement |
|---|---|---|
| STO-1 | SHOULD | Catalog: SKUs with per-platform product ids and prices, region pricing, availability windows, bundle contents; served via `GET /v1/store`. |
| STO-2 | SHOULD | Purchase flow: client buys through the platform, sends the receipt / order id; the server verifies with the platform (Steam Microtransactions `FinalizeTxn`, App Store Server API, Google Play Developer API), records the transaction, writes grants (PROG-7). Receipts are verified exactly once; replays are idempotent. |
| STO-3 | SHOULD | Refund and chargeback handling: platform webhooks revoke grants; consumed items leave a negative balance that must be settled before new purchases. |
| STO-4 | SHOULD | Premium currency ledger (if a soft currency exists): all credits/debits in one ledger with balances as projections; tax/VAT handled by the platforms. |
| STO-5 | SHOULD | Cosmetics inventory and equip state per account; equipped cosmetics appear in the match setup so other clients can render them. |
| STO-6 | LATER | Gifting, promo codes, creator codes, regional promotions, storefront on the website (section 14) that deep-links into the platform purchase. |

---

## 12. Moderation, support, admin

| Id | Req | Requirement |
|---|---|---|
| MOD-1 | SHOULD | Admin API behind staff SSO with roles (support, moderator, admin) and a full audit log of staff actions. |
| MOD-2 | SHOULD | Player lookup by name, id, platform id, email; view sessions, links, match history, chat history, grants ledger, reports filed and received. |
| MOD-3 | SHOULD | Actions: warn, chat mute (timed), queue lock, rating revert on a flagged match, suspend, ban (with evasion linkage by platform ids), rename, restore deleted clan, manual grant / revoke with reason. |
| MOD-4 | SHOULD | Report queue fed by SOC-8 and RES-9 with status and outcome; players get an outcome notification. |
| MOD-5 | SHOULD | Support tooling for account merges (ACC-3), purchase lookups and restores (STO-2). |
| MOD-6 | LATER | Automated chat moderation beyond a word filter. |

---

## 13. Legal and compliance

| Id | Req | Requirement |
|---|---|---|
| LEG-1 | MUST | Privacy policy and terms of service accepted at account creation, versioned; re-acceptance on material change. |
| LEG-2 | MUST | GDPR / CCPA: export and deletion (ACC-7), data retention table (chat 30 days, match telemetry 1 year, financial records as required by law), DPA with every processor, EU data residency option for EU accounts. |
| LEG-3 | MUST | COPPA / age gating (ACC-8); no behavioural ads; platform family settings honoured on console. |
| LEG-4 | SHOULD | Loot-box / chest odds disclosed in-game and on the website; comply with regional rules (Belgium / Netherlands restrictions, Japan kompu gacha, China odds publication, Apple/Google disclosure requirements). Keep a flag per region to disable randomised paid items. |
| LEG-5 | SHOULD | Platform certification inputs: Steam (Steamworks, VAC decisions), Apple / Google store review (purchase restore, account deletion in-app), console cross-play approvals later. |
| LEG-6 | SHOULD | Accessibility statements and the EU Accessibility Act for the storefront and website. |

---

## 14. Website

| Id | Req | Requirement |
|---|---|---|
| WEB-1 | SHOULD | Marketing site (static, CDN): game pitch, trailer, Steam / store links, press kit, newsletter. |
| WEB-2 | SHOULD | Account portal: log in with any linked provider, view and manage links (ACC-3), rename, privacy controls, data export / deletion, purchase history, chest odds page. |
| WEB-3 | SHOULD | Patch notes and season pages generated from the config/changelog pipeline (CFG-3) so balance changes are published automatically with each config promotion. |
| WEB-4 | SHOULD | Public status page (API, matchmaking, store, per region) driven by the health checks in section 15. |
| WEB-5 | LATER | Leaderboards (RES-7), public profiles and match pages (RES-6), clan pages (CLN-4), replay viewer (AC-10). |
| WEB-6 | LATER | Support centre with ticketing tied to MOD-5. |

---

## 15. Platform and operations

| Id | Req | Requirement |
|---|---|---|
| OPS-1 | MUST | Environments: local (`docker compose`), staging (full stack, fake platform verifiers, test SKUs), production. Separate databases and secrets per environment. |
| OPS-2 | MUST | PostgreSQL managed service with daily backups, point-in-time recovery, restore drill once per season. Migrations are versioned, forward-only, run by CI before deploy. |
| OPS-3 | MUST | CI: build + tests for `Server/*` and the Unity client compile check on every push; deploy API and match hosts from tagged builds; config promotion pipeline (CFG-3). |
| OPS-4 | MUST | Observability: structured logs with `accountId` / `matchId` correlation, metrics (queue depth, time-to-match, bot-fill rate, match host CPU, API latency, WebSocket connections, error rate), traces on the request path, alerting on SLOs. |
| OPS-5 | MUST | Secrets in a manager (never in the repo), TLS everywhere, JWT signing keys rotated, service-to-service auth for match hosts (HOST-2). |
| OPS-6 | SHOULD | SLOs: login p99 < 1 s, match found-to-start < 10 s after allocation, API availability 99.9%, chat delivery < 1 s. |
| OPS-7 | SHOULD | Load testing: synthetic clients for queue + WebSocket at 10× expected concurrency before launch; match host soak test with the stress-test scenario. |
| OPS-8 | SHOULD | Cost tracking per match-hour and per MAU; autoscaling policy for hosts after the fleet is stable. |
| OPS-9 | SHOULD | Incident runbooks: ranked rollback, config rollback, store outage (queue receipts, grant later), region failover, mass-disconnect handling (pause ratings for affected matches). |
| OPS-10 | LATER | Multi-region active-active for the API; database read replicas per region. |

---

## 16. Client-facing contract summary

What the Unity client needs from all of the above, so the hub stops using sample data (`HubState.cs`):

| Hub feature | Backend source |
|---|---|
| Player card (name, tag, status) | ACC-6, CLN-2, SOC-3 |
| Friends groups, requests, add by name/code | SOC-1, SOC-2, SOC-3 |
| DM / party / clan chat tabs | SOC-4, CLN-5 |
| Party invite card, slots, READY / LEAVE | SOC-6, SOC-7 |
| Mode (Quick match = Matchmaking, Custom = lobby, Practice = local) | MM-3, MM-5, local only |
| Map size tiles → `size` | MM-1 |
| PLAY / FIND LOBBY / READY | MM-1, MM-5, SOC-6 |
| Settings sync, username | ACC-9, ACC-5 |
| Ranked details ("Unranked", Elo rules) | RES-4, RES-8 |
| Clan screens | CLN-1 … CLN-5 |
| Customize / Store placeholders | STO-1, STO-5 |

API surface (grouped, final shapes in PROTOCOL.md): `/v1/auth/*`, `/v1/profile`, `/v1/friends/*`, `/v1/parties/*`, `/v1/clans/*`, `/v1/queue`, `/v1/lobbies/*`, `/v1/matches/*`, `/v1/config/*`, `/v1/progression/*`, `/v1/store/*`, `/v1/reports`, plus one WebSocket (`/v1/ws`) carrying presence, chat, party, queue and match-start events.

---

## 17. Phasing

1. **Prove the match** (already the plan): stress test, multiplayer spike, choose the networking model. Infrastructure work in parallel that does not depend on the model: ACC-1…6, SOC-1…7, MM-1, MM-5, MM-6, CFG-1…2, OPS-1…5, LEG-1…3.
2. **First online tests with friends**: HOST-1…4 for the chosen model, MM-2, MM-7, RES-1…5, AC-1…7.
3. **Steam launch**: everything marked SHOULD, in this order: ranking + leaderboards, seasons + progression, store, clans, moderation, website, load testing, runbooks.
4. **After launch**: LATER items, mobile store review requirements, console certification.

## 18. Open decisions

- Networking model (drives section 6 and the `Unit.cs` rewrite).
- Display-name uniqueness vs discriminator (ACC-5).
- Freeplay bot-fill threshold and whether Freeplay affects any visible rating (it should not).
- Whether a soft currency exists at all, or everything is cosmetics + pass + talent acceleration (STO-4).
- Hosting provider and regions for the first external test.
- Whether Practice grants any XP (recommendation: a flat, capped daily amount, never currency).
