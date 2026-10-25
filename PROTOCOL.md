# Client ↔ Server protocol

Contract between the Unity client (`Prototype/`) and the backend (`Server/`). Server agent owns the server sections; game agent owns *Match setup* and *Match result* shapes and reviews everything else. Keep this file current; it is what both sessions build against.

## Transport
- HTTPS REST for everything stateless (auth, profile, clans, store).
- WebSocket for lobby presence, matchmaking status, chat, match start.
- JSON bodies, `camelCase`. Versioned base path `/v1`.
- Auth: `Authorization: Bearer <jwt>` on every call after login.

## Auth
1. Client logs in with a platform token (`steam`, `apple`, `google`, `guest` for dev) → `POST /v1/auth/login { provider, token }`.
2. Server verifies with the platform, finds or creates our account, returns `{ accountId, jwt, displayName, isNew }`.
3. Linking another platform to an existing account: `POST /v1/auth/link`.

## Profile (planned API)
- `GET /v1/me` returns `{ accountId, displayName, ranked: { rating, gamesPlayed, status } }`. `rating` is null in the client response while `status` is `unranked`; the server still uses an internal starting rating of 1000 for matching.
- `PATCH /v1/me { displayName }`: authenticate the account, trim and validate 3–20 letters/numbers/spaces/underscores/hyphens with at least one letter/number. Return the saved profile or a validation error. Rename never changes account ID, friends, clan membership or Elo. Names are display labels, not authentication identifiers; name uniqueness/search policy is still to be decided.
- Unity currently saves only a local display name in `Commoners.DisplayName` PlayerPrefs. This is not a registered online account or a reserved username. Replace that adapter with the authenticated API when accounts exist.

## Ranked matchmaking (planned API; domain core exists in Server/Ranking)
- `POST /v1/ranked/queue { region, playerCount }`, `DELETE /v1/ranked/queue`. Account ID comes from the authenticated session; rating and queued-at time come from the server. No client rating field. FFA player counts: 2/4/6/8/9, all human, no parties/bots in the initial ranked mode.
- Status events: `queued`, `matched { matchId, connection }`, `cancelled`, `failed`. Split queues by region and size, never silently convert ranked into practice. Allocate a trusted game-server instance before handing out a connection.
- Profile status remains `unranked` until the first completed rated match. No local Elo or rank is fabricated in Unity; its ranked launch remains disabled until the authenticated service and trusted game server are connected.

## Lobby
- `POST /v1/lobbies` create, `POST /v1/lobbies/{id}/join`, `DELETE .../leave`, `POST .../ready`, `POST .../start` (host only).
- Lobby state pushed over WebSocket: `{ lobbyId, host, players: [{ accountId, displayName, team, ready, isBot }], settings: { playerCount, map, fillWithBots } }`.
- Empty slots fill with bots at start.

## Match setup (server → client at start, game agent owns the shape)
```json
{
  "matchId": "uuid",
  "seed": 123456789,
  "configVersion": "swarm_config@<hash>",
  "playerCount": 4,
  "players": [
    { "slot": 0, "accountId": "…", "displayName": "…", "team": 0, "isBot": false },
    { "slot": 1, "accountId": null, "displayName": "Bot", "team": 1, "isBot": true }
  ]
}
```
`slot` maps to a base on the grid (`MapLayout`). `seed` drives every random decision in the match so all clients and the server simulate the same game. Until lockstep netcode exists, a match with one human and bots runs entirely on that client.

## Match result (trusted game server → backend; planned API)
`POST /internal/v1/matches/{id}/result { placements: [{ accountId, place }] }`. Authenticate with game-server service credentials and verify the caller is the instance allocated to this exact match. Validate the complete registered roster and competition-ranked placements. The ranked core applies placement Elo once from the recorded pre-match ratings; duplicate identical reports return the stored outcome, conflicting reports fail. In production, persist the result and all rating changes in one transaction with a unique match ID.

Player clients cannot call this result route or submit trusted gold, essences, damage, kills, spawn counts or rating changes. The game server must own simulation and rewards; local practice awards no account progression or Elo. The former client-reported stats payload is not an authority boundary and must not be used for ranked. Trusted server simulation, transport authentication and persistence remain unimplemented.

## Clans
`POST /v1/clans`, `GET /v1/clans/{id}`, `POST .../invite`, `POST .../join`, `DELETE .../members/{accountId}`, roles `leader | officer | member`.

## Later
Store and entitlements, battle pass, talent tree, cosmetics inventory, lockstep input relay.

## Changes

- 2026-10-25 (game agent, at the user's request): rebuilt the splash and home screens from the `commoners-ui` handoff. New files `HubTheme.cs`, `HubArt.cs`, `HubWidgets.cs`, `HubState.cs`, `SplashScreen.cs`, `HomeHub.cs`, `SettingsModal.cs`, Archivo fonts in `Resources/Fonts`. **`MainMenu.cs` was rewritten as a thin host** (canvas, prefs, modals, launch) and `MenuUi.Text` now uses Archivo; `HomeMenuCheck.cs` was rewritten for the new control names. Server agent: your uncommitted ~19:30 `MainMenu.cs` (profile editor, ranked lock) was overwritten before I noticed it was in flight; both features were re-implemented in the new hub against your untouched `LocalPlayerProfile.cs` (`Player identity` card → `Your profile modal` with `Username draft` / `Save username` / `Cancel username` / `Username feedback` / `Profile username`; Settings → `Edit username`; Quick match / Custom are locked with `ONLINE LOCKED` and a `Ranked details` → `Ranked play modal` / `Ranked done`). `ProfileRankedCheck.cs` and `SocialDemoCheck.cs` still target the old home UI (`Ranked mode`, `Match options`, `Friends and chat`, home social drawer); the in-match F6 overlay is unchanged. Please don't restore the old `MainMenu.cs` over this one; edit forward instead.

- 2026-10-25: Added editable saved local usernames throughout Unity profile/chat/clan UI, a visibly locked ranked mode, and tested server-only Elo/queue/result domain logic in `Server/Ranking`. Updated the planned profile/queue/result contract to require authenticated identities and trusted match results. No online service is deployed.

- 2026-10-25: Added an explicitly local social demo (`SocialDemoState` + persistent `SocialOverlay`): seeded direct chats, notifications during matches, incoming/outgoing friend requests, and create/join/leave clan flows. No network calls or real membership changes. Gameplay input is suppressed while using the social panel. Replace demo data with authenticated backend events during integration.

- 2026-10-25: Home-menu UI implementation authorized in this chat: `MainMenu.cs`, `MenuUi.cs`, and `HomeUnitPreview.cs`. Canvas/TMP startup, character preview, offline practice, and social empty states; no backend contract or match simulation changes. Online login, matchmaking, friends and clan/chat data still need server integration.
(Each agent adds a line here when editing a shared file: `GameConfig.cs`, `GameState.cs`, `swarm_config.json`.)

## Requests
(Each agent asks the other for things here instead of editing the other's files.)

- 2026-10-25 game → server: keep scope to auth + lobby + match start + match result until the match is proven. No clans / store yet.
