# Commoners: server session

You are the **server** agent. A second session works on the Unity game in the repo root at the same time. Stay in your lane.

## What you build
Backend for accounts, lobbies, matchmaking and match results. **Scope for now: auth, lobby, match start, match result. Clans, store, battle pass and progression wait until the match itself is proven** (stress test + multiplayer spike on the game side). Own program in this folder, deployed separately from the game. Platform logins (Steam, Apple, Google, later PSN / Xbox) are *linked identities* on our own account; our account is the source of truth. Entitlements, progression, cosmetics and clans live here. Store SKUs and prices are defined here and fulfilled through each platform's payment rails.

Stack unless told otherwise: .NET 8 minimal API (same language as the client, shared DTOs), PostgreSQL, WebSocket for lobby presence. Keep it boring and testable. Must run locally with one command.

## Ownership
- **Yours**: `Server/**`, `Prototype/SwarmPrototype/Scripts/Net/**` (client networking), `Prototype/SwarmPrototype/Scripts/MainMenu.cs`, lobby / login / clan UI, the boot sequence in `Bootstrap.cs`.
- **Game agent's**: everything else under `Prototype/`. Don't edit; leave a note in `PROTOCOL.md` under *Requests*.
- **Shared, edit with a one-line note in `PROTOCOL.md` *Changes***: `GameConfig.cs`, `GameState.cs`, `swarm_config.json`.
- `PROTOCOL.md` is the contract. You own its server-side sections; the game agent reviews the match-start payload.

## Client-side rules (when you touch Unity files)
- Source of truth is `Prototype/SwarmPrototype/`. After every edit: `cd Prototype && rsync -a --exclude '.DS_Store' SwarmPrototype/ SwarmGame/Assets/SwarmPrototype/`.
- Compile without the editor using the bundled Roslyn: see the root `CLAUDE.md` and memory `unity-compile-check`. Unity 2022.3.5f1.
- UI you build must be Canvas + TextMeshPro, work with mouse, touch and gamepad, respect safe areas. No IMGUI.

## Git
Commit on `main`, small descriptive messages. Pull before you push; the game agent pushes too.
