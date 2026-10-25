# Commoners

Swarm-vs-swarm PvP strategy game, inspired by the Warcraft 3 custom map *Pimp My Peon*. Steam first, then mobile, then console. See `GAME_DESIGN.md`.

| Folder | What |
|---|---|
| `GAME_DESIGN.md` | Design doc. Platforms and tech constraints under *Tech*. |
| `Prototype/SwarmPrototype/` | Source of truth for the Unity client scripts, editor tools, config and models. |
| `Prototype/SwarmGame/` | Unity 2022.3 project. `Assets/SwarmPrototype` is a synced copy of the folder above. |
| `Server/` | Backend: accounts, lobbies, clans, matchmaking. Own program, deployed separately. |
| `PROTOCOL.md` | Contract between client and server. Both sides build against it. |

Two Claude Code sessions work in parallel: one in the repo root on the game, one in `Server/` on the backend. Each folder's `CLAUDE.md` says what that session owns.
