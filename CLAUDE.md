# Commoners: game session

You are the **game** agent. A second session works in `Server/` at the same time. Stay in your lane.

## Ownership
- **Yours**: `Prototype/SwarmPrototype/**` except the files listed under the server agent, `GAME_DESIGN.md` gameplay sections.
- **Server agent's**: `Server/**`, `Prototype/SwarmPrototype/Scripts/Net/**`, `MainMenu.cs`, lobby / login / clan UI, `Bootstrap.cs` boot sequence. Don't edit; leave a note in `PROTOCOL.md` under *Requests* instead.
- **Shared, edit with a one-line note at the top of `PROTOCOL.md` *Changes* list**: `GameConfig.cs`, `GameState.cs`, `Resources/swarm_config.json`.
- `PROTOCOL.md` is the contract. Match setup data (players, teams, seed, map) comes from the server in the shape it defines.

## Workflow
- Source of truth is `Prototype/SwarmPrototype/`. After every edit: `cd Prototype && rsync -a --exclude '.DS_Store' SwarmPrototype/ SwarmGame/Assets/SwarmPrototype/`.
- Compile without the editor using the bundled Roslyn (see `~/.claude` memory `unity-compile-check`). Unity 2022.3.5f1 at `/Applications/Unity/Hub/Editor/2022.3.5f1`.
- Headless renders only when the editor is closed (`pgrep -fl "MacOS/Unity"`).
- Commit on `main` with small, descriptive messages. Pull before you push; the server agent pushes too.

## Platform constraints (from GAME_DESIGN.md > Tech > Platforms)
Steam, mobile and console with cross-play. Settled: graphics quality tiers, Input System package, Canvas + TextMeshPro UI, URP, own-account backend. **Not settled**: networking model (server-authoritative vs lockstep) and whether `Unit.cs` needs rewriting. Those wait on a max-load stress test and a multiplayer spike. Don't start a deterministic-sim rewrite on your own.
