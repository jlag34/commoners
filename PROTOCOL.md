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

## Match result (client → server)
`POST /v1/matches/{id}/result { winnerTeam, durationSeconds, perPlayer: [{ slot, kills, gold, upgrades, survived }] }`. Server validates (later: by replaying inputs) and applies progression.

## Clans
`POST /v1/clans`, `GET /v1/clans/{id}`, `POST .../invite`, `POST .../join`, `DELETE .../members/{accountId}`, roles `leader | officer | member`.

## Later
Store and entitlements, battle pass, talent tree, cosmetics inventory, lockstep input relay.

## Changes
(Each agent adds a line here when editing a shared file: `GameConfig.cs`, `GameState.cs`, `swarm_config.json`.)

## Requests
(Each agent asks the other for things here instead of editing the other's files.)

- 2026-10-25 game → server: keep scope to auth + lobby + match start + match result until the match is proven. No clans / store yet.
