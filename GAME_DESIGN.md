# Game Design Doc (working title TBD)

Swarm-vs-swarm PvP strategy game inspired by the Warcraft 3 custom map *Pimp My Peon*. Goal is to take the concept off WC3 (where custom maps were free) and ship it as a standalone monetized game. Launch on **Steam first**, then port to **mobile** if it does well.

No standalone version currently exists. A Dota 2 custom game version was pulled from the Steam Workshop and players still ask for it back, so there's proven nostalgia and a real gap.

Avoid the "Pimp My Peon" name and any "peon" or Blizzard branding.

## Design pillars

1. **Swarm vs swarm.** You don't build an army of diverse units. You have one swarm that spawns automatically, and you make it better than everyone else's.
2. **Stats are the game.** Matches are won mainly by stacking the right upgrades: attack, attack speed, move speed, armor, HP.
3. **Strategy is in when and where you fight.** Hitting a base while its owner's swarm is away, teaming up on whoever's leading, holding chokepoints.
4. **Keep it simple.** This is NOT a full RTS. Don't rebuild Warcraft with a big unit roster and combos.
5. **Every game plays differently** because of the build you choose that match.

## Core loop

- Each player starts with the same central spawner building.
- It auto-spawns basic melee units on a timer. Units run to a rally point.
- Killing enemy units earns gold. Gold buys upgrades.
- Gold mostly comes from fighting, so you have to attack, not just turtle. But you also have to defend or someone rushes and kills your base.
- Last base standing (or last team) wins.

## Match setup

- Bases laid out on a grid: 3x3 for 9 players, 3x4 for 12. Scales with player count.
- Teams supported. Teammates' bases spawn next to each other.
- Bots and real multiplayer. Bots are needed from day one (to fill empty lobbies and keep matches running when the community is small).
- Simple "join a game" lobby to start.

## Map

- Each base sits on raised ground with entrances only at north, south, east and west. No diagonal access.
- Chokepoints let players body block with their units to defend against rushes. Units need real collision for this.
- Bases spaced further apart than the original.
- Neutral monster camps between bases. Optional bonus gold / rare drops, never required for core upgrades (contested required camps would lock players out and snowball).

## Units

- **Basic melee unit** is the bulk of every swarm.
- **One special unit type per match**, chosen by the player (e.g. archer, healer, or a defensive/tank unit).
  - By default it replaces every 5th or 10th spawn.
  - Investing upgrades into it increases how often it spawns (e.g. up to every other spawn).
- That's it for unit variety in the base game. Keep the roster tiny.

## In-match upgrades (resets every match)

- Bought with gold during the match.
- Gear-style upgrades applied to the whole swarm: weapons, helms, shoulders, shields, wings, etc. Each boosts attack, attack speed, move speed, armor or HP.
- Spawn rate upgrades.
- Special unit frequency upgrades.
- Upgrades need real trade-offs so reading and countering an opponent's build is a skill. Ideas: escalating costs on stacking one stat, and stats that counter each other (armor beats many small hits, raw damage punches through armor).

Possible carryovers from the original, to evaluate later:
- Leader unit with support abilities every couple of minutes
- Early "super" unit
- Destroying an enemy base gives you a building that pays out gold over time

## Resources

- **Gold only** to start. Add a second currency later only if the economy feels flat.

## Pacing

- At 15 minutes, **berserk mode**: all units get -20% armor and +20% damage, so fights resolve faster.
- Target match length around 20 minutes max. Tune from playtesting.
- Consider a comeback mechanic (e.g. bonus gold for killing a stronger player's units) since berserk can favor whoever's ahead.

## Persistent progression (carries between matches)

Two layers: the in-match gear defines your build; the persistent layer adds a little extra oomph.

### Talent tree
- WoW-style talent tree with small perks (crit, etc.).
- **Capped**: you can't fill the whole tree, you pick a spec.
- **Respecs (rerolls) cost money**, with occasional free rerolls.
- **Free reroll after any balance change** that affects the tree.
- **Seasons** every few weeks: everyone's tree resets to zero. You unlock talents again by playing, or pay to unlock faster.

### Perks (Dead by Daylight style)
- A small number of perk slots (around 4), chosen before the match, locked in once it starts.
- Perks shape playstyle rather than just adding raw stats. Examples: units deal more damage near your base; killing a monster camp boosts spawn rate for a minute; a swarm perk that doubles unit count but halves HP, with damage scaling per living unit in the group.
- Some perks rotate each season to shake up the meta.
- Earned from timed free chests (e.g. every 8 hours) and the battle pass.

### What persists across seasons
- Cosmetics (always kept)
- Account level / season rewards, so a reset never feels like losing everything

Watch for perks, talents and gear overlapping so much it gets confusing.

## Monetization

Blend of Dead by Daylight and Clash Royale. Some pay-to-win is intended, but capped with strong balancing.

- Paid base game (Steam)
- Large cosmetics store (persistent across seasons)
- Seasonal battle pass with free and premium tracks (premium gives more perks/chests)
- Timed free chests
- Paid talent tree unlock acceleration and paid rerolls
- Later: new races as paid content drops (like DBD killers/chapters)

Rules:
- Anything that affects power can always be earned for free at a reasonable pace.
- Never launch paid content overpowered and nerf it later (the Pokemon Unite / Mewtwo FOMO problem).
- No licensed IP crossovers / revenue-share deals planned.
- Detailed balancing of pay-to-win is a later-stage task.

## Races (later)

- Launch with **one race**.
- New races can come post-launch as paid content drops, each with its own look and flavor. Earnable by grinding, never launched overpowered.
- Can't use Blizzard's races; design original ones.

## Art direction

3D, Warcraft 3 style: cartoony but semi-realistic fantasy units.

## Controls

- Units spawn and run to a rally point.
- Players must be able to split units into groups and command them independently (e.g. one group defends, another attacks). A single rally point or one mass selector isn't enough.
- Steam first means mouse/keyboard RTS controls (box select, control groups) at launch. Solve touch controls for the mobile port.
- Exact scheme undecided; decide by prototyping what feels good. One mobile idea: a few fixed squads (attack, defend, flank), each with its own draggable flag, and the player picks which squad new spawns join.

## Tech

- **Engine: Unity** (good 3D performance with many units, stylized fantasy asset packs, mature multiplayer networking).
- **All unit stats, upgrades, perks and talents are data-driven** (config/data files, not hardcoded), so progression, caps, seasonal changes and balance patches can be layered on without rewriting combat.
- Biggest technical risks: collision/body blocking with lots of units in networked multiplayer; group controls on touch.

### Platforms (decided 2026-10-25)

Targets: **Steam (PC/Mac) at launch, mobile (iOS/Android) next, console if it earns it.** Cross-platform play and cross-progression are the goal. Nothing built from here on may block any of the three, but nothing gets built *for* mobile or console until the match is proven on PC.

What is settled:

- **Graphics scale per device, gameplay doesn't.** High / medium / low quality tiers (shadows, post-processing, LODs, texture size, particle density, resolution scale). Same art, same rules, same UI layout everywhere. Stylized low-poly art scales down well.
- **Minimum supported phone** is a named device (pick one: a 2022 mid-range Android), not "the weakest phone". Performance work targets it.
- **Expected load** on a 3x3 map: 9 × 30 player units + up to ~100 camp monsters + a few elites, ~380 units worst case, ~250–300 typical.
- **Controls**: PC keeps selection controls (box select, control groups) as the design doc says. Squad / flag commands are the touch and gamepad candidate; test whether they express the same decisions before committing.
- **Own account system on our backend**; Steam / Apple / Google / PSN / Xbox IDs are linked logins, never the source of truth. Store goes through each platform's payment rails (~30% cut each), SKUs and prices defined server-side, chest odds disclosed where required.
- **Rendering: URP**, Input System package (mouse, touch, gamepad from one map), Canvas + TextMeshPro UI with safe areas and controller navigation. IMGUI is prototype-only.
- **Console is real engineering, not a checkbox**: platform SDKs, presence, suspend/resume, privilege checks, cross-play approval, certification. Decide after the Steam launch; keep the architecture from blocking it and nothing more.

What is **not** settled, and gets decided by tests:

- **Networking model.** Candidates: server-authoritative (server simulates, clients render; ~30 KB/s per client at 380 units before compression; validates results for free; costs server CPU per match) vs deterministic lockstep (tiny bandwidth, free replays, needs a deterministic sim and a rewrite of the physics-based movement). Pick after a host-authoritative multiplayer spike shows how command latency feels.
- **Whether the current simulation is fast enough.** `Unit.cs` scans all combatants and uses Rigidbody + NavMesh. Run a representative maximum-load battle (9 teams at cap, all camps, elites) in the editor profiler, then on the minimum phone. Rewrite only what the profile says to.

Order: stress test → multiplayer spike → choose model → then accounts / clans / store beyond what a lobby needs.

## First prototype priorities

1. One base, spawner, rally point, units walking and fighting.
2. Raised base with four entrances and working body blocking.
3. Group control (mouse first), test a couple of schemes.
4. Gold from kills and a small set of stat upgrades.
5. One special unit with spawn frequency upgrade.
6. Bot opponents.
7. Berserk mode timer.

## Open questions

- Game name and branding
- Full list of in-match upgrades, costs and trade-offs
- Which special units exist at launch
- Talent tree contents and cap
- Perk list
- Control scheme
- Comeback mechanic, if any
- Which carryovers from the original to keep (leader unit, super unit, captured-base income)

## Reference: the original map

Researched from public map pages and changelogs (latest seen version 12.178):
- Gear upgrades (weapons, helms, shoulders, shields, wings) boosting the whole swarm
- Garage spawner that sets the rally point
- Hero that grants upgrades
- Leader unit with support spells every few minutes
- Super peon arriving around 2 minutes in
- Roaming trader swapping gold and lumber
- Captured enemy base pays gold over time
- Races (human, undead, blood elf, likely orc; later Clockwork replaced Night Elves) with racial abilities
- Ranked free-for-all for up to 12 players; a PvE mode called Hellfire
