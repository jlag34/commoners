# Swarm prototype, step 1

Covers prototype priority 1 from GAME_DESIGN.md: one base, a spawner, a rally point, and units walking and fighting. A bot base on the other side of the map fights back so there's something to fight. Gold from kills is counted and shown, but there's nothing to spend it on yet (that's step 4).

The match and the Canvas/TextMeshPro home screen are built from code, so there is no scene or prefab setup.

## Home screen

Open `SwarmGame` in Unity and press Play. The screens follow the `commoners-ui` handoff (1600x900 reference, scaled with the window): a splash with the wordmark, tagline and two swarms marching on a ridge, then the home hub.

**Hub layout.** Top bar: logo, HOME / CUSTOMIZE / CLANS / STORE pill (Customize and Store are placeholders; Clans opens the clan chat tab), SETTINGS. Left column: your player card (click it to edit your local username), a party invite card, and the friends panel with add-by-name, search, requests (accept / decline) and In match / Online / Offline groups; each row has message, invite-to-party and a ⋯ menu (invite to clan, remove with confirm). Centre: the live Warrior model on its pedestal, drag to rotate. Right column: chat with Clan, Party and closable DM tabs (Enter or the send button posts), and the play panel: mode (Quick Match = Freeplay, Ranked, Custom, Practice), map size tiles (Duel 2, Small 4, Medium 6, Large 9; the gold cell is your base), party slots (`HubState.PartySize`, 2 while duos share a castle, crown on the leader) and the PLAY button. Joining Bramblewick's invite makes you a member: the button reads READY and LEAVE PARTY appears.

Friends, chat, party and clan are **local sample data** (`HubState.cs`), kept in memory for the play session and never sent anywhere. Quick Match, Ranked and Custom are **locked** (`ONLINE LOCKED`, with a DETAILS explainer) until authenticated server matches exist; Practice launches the local bot match for the selected map size. M in a match returns home; the hub state survives the round trip.

Settings: Low / Medium / High Unity quality presets, Master / Music / Effects volume (master drives `AudioListener`, the others are stored), EDIT USERNAME, DONE. Usernames go through `LocalPlayerProfile` (3–20 characters) and update the card, your chat lines and the in-match social overlay.

The in-match social panel (**F6** or the people button, `SocialOverlay.cs`) is a card above the bottom-right buttons built from the same widget kit: Friends (add by name, requests with accept / decline, friends with their last message and unread badge, tap a row to chat), Clan (browse and join or create a demo clan; once in one, its header, members card and chat) and direct chats with bubbles. It uses its own seeded demo data (`SocialDemoState.cs`) and is hidden while the hub is open.

Type is Archivo (static Expanded / SemiExpanded / normal cuts in `Resources/Fonts`, built into dynamic TextMeshPro font assets at startup). Every sprite the screens use is painted in code (`HubArt.cs`). The project includes uGUI 1.0.0, TextMeshPro 3.0.6 and the TMP essential resources. The source folder remains `SwarmPrototype`; sync it into `SwarmGame/Assets/SwarmPrototype` after changes. Retain `.meta` files when copying assets.

For a focused integration check, stop Play and choose **Commoners > Check home flow** (or touch `SwarmGame/home-menu-check.trigger`). It walks splash, home, friend actions, DM tab, party invite / join / leave, the username editor, the online lock, quality / volume, map selection, launch and return; restores saved settings; and leaves home open. Results go to `Prototype/home-menu-check.log` with `screenshot-home-menu-*.png` next to it. **Check social demo** and **Check profile and ranked** still target the previous home UI and need updating to the hub's control names.

## Setup (about 2 minutes)

1. Open Unity Hub and choose **New project**. Pick the **Universal 3D** template (any 3D template works) and create it.
2. Copy the `SwarmPrototype` folder into the new project's `Assets` folder.
3. Go back to Unity, let it compile, and press **Play** in any scene (the default SampleScene is fine).

## Controls

- There is no rally point at the start: units gather in rings around your tower, clockwise, nearest free spot first.
- **Right-click** the ground to set your rally point (the blue flag). New and idle units walk there.
- **Left-click your tower**, press **B**, or hit the Shop button in the banner to open the shop. Two shops as tabs. **Stats** (gold only): Strength (+1 damage), Atk Speed (+10%), Health (+10 HP, heals the same), Armor (+1 flat), Move Speed (+8%, max 10). **Greater** (gold + skulls + an essence per level, capped; laid out as three rows, one per essence tier): Lifesteal (+3%), Evasion (+4% dodge), Unit Regen (+1 HP/s), Tower Regen (+3 HP/s, all your towers), Tower Damage (+15%), Tower Range (+1 m), Crit Chance (+5%, double damage), Splash (+10% of each hit to enemies within 1.6 m of the target). **Currencies.** Gold from fighting, plus **skulls** from neutral kills (easy 1, medium 4, hard 10, elite 50) and three **essences** that neutrals have a chance to drop (easy 10% lesser; medium 25% lesser, 4% greater; hard 20% lesser, 35% greater, 5% elite; the elite always drops a greater and an elite). Stats tab is gold only. Special and Economy upgrades cost gold + skulls + one essence per level, tier by how strong the stat is: elite essence for Spawn Rate and Splash; greater for Lifesteal, Evasion, Crit and Reincarnate; lesser for Bounty and the tower upgrades. Drop rates are `tiers` in the config, prices are `skullCost` / `essence` on each upgrade.

**Elites.** Every 2 to 3 minutes (`eliteIntervalMin/Max`) one of the `elites` in the config appears on a random road that doesn't already have one, scaled by the match clock, pinged on the minimap. Each has abilities (`EliteAbilities.cs`): Ancient Treant heals itself and knocks units flying; Venom Hydra poisons; Headsman has a 12% chance to execute a unit outright; Thunder Ogre stuns and attacks faster the lower it gets; Siege Golem hits everything within 3 m; Plague Shaman poisons and heals. Whoever kills one gets its bounty, 50 skulls, a greater and an elite essence. Your buildings taking damage also pings the minimap (red) and shouts.

Also on Greater: Spawn Rate (spawn interval ÷ (1 + 10% per level), max 10), Bounty (+10% gold on every kill reward per level, max 10), Reincarnate (+6% chance per level that a unit of yours that dies comes straight back at your tower, max 5 = 30%). The `tab` field on each entry in `upgrades` picks where it shows. Level n costs `baseCost * costGrowth^(n-1)` gold; all start at 15. Esc or click away to close.
- **WASD / arrow keys** pan, **scroll** zooms, **Space** jumps back to your base, **E** to the enemy base. The view starts centered on your tower, steep like Warcraft 3.
- **R** restarts the match.

The home screen's Match Options panel selects 2 (1v1), 4 (2x2), 6 (2x3), 8 (2x4) or 9 (3x3) players. Free-for-all: you are blue in the south-west, bots hold the other bases. The Play button launches the match; M in-game returns home. Each bot gathers round its tower, sends a wave at a random living opponent on a timer, and spends its kill gold in the shop as it goes (cheapest upgrade first). You win when all three enemy towers are down, lose when yours is.

Monster camps are laid out from the grid: one between every four bases (medium or hard), one outside each boundary road (easy; in a 1v1 both sides of the single road are medium/hard), and one behind every base (easy). Each site rolls a template of its tier from nine: Wild Boars, Goblin Scouts, Skeletons (easy); Wolf Pack, Bandit Camp, Troll Hunters (medium); Ogre Den, Stone Golems, Dark Knights (hard). Each monster pays a bounty to whoever kills it, the last one pays a clear bonus. Every spawn is scaled by the match clock, +5% HP, damage and gold per minute (`campScalePerMinute`), shown as +N% on the camp label. Monsters attack anyone who comes close and walk back to camp if lured too far. 30 seconds after the camp is cleared a new random one appears (`campRespawnDelay`). Templates (count, stats, bounty, size) are in `swarm_config.json` under `camps`; counts run 4–6 so camps feed enough essences and kills.

## Tuning

All numbers live in `SwarmPrototype/Resources/swarm_config.json`: spawn rate (one unit every 5 s), unit cap (0 = none), unit HP/attack/attack speed/move speed/armor/ranges, base HP, gold per kill, map size, and bot timing. Edit, save, and press Play again.

Damage uses flat armor (`damage - armor`, minimum 1), so armor beats many small hits and big hits punch through, as the design doc suggests.

## Files

- Click your top-left player card, or Settings → Edit Username, to change your local username. Names persist on this device and update the home card, own chat messages and clan roster. Ranked displays Unranked and remains locked until authenticated online matches are integrated. Practice is available normally; it never changes Elo.
- `Commoners > Check profile and ranked` exercises validation, save/cancel, identity propagation, menu recreation, practice launch and the ranked guard. Existing `Check home flow` and `Check social demo` cover their broader UI paths. The server's Elo/queue checks run with `dotnet run --project Server/Ranking.Tests` from the repository root.

- `Bootstrap.cs` builds the map, both bases, the flag and the camera when you press Play. Set `AutoStart` to false once you have real scenes.
- `Tower.cs` is a secondary corner tower (four per base, 600 HP, pays 50 gold when destroyed) plus `TowerGun`, the ranged attack both tower types use, and `Bolt`, its projectile. Tower stats are `mainTower` / `secondaryTower` in the config; `attack` 0 makes a tower passive.
- `Camp.cs` spawns the monster camp and pays the clear bonus.
- `Spawner.cs` is a base: the 5 x 5 tower, HP, spawn timer, rally point and the ring spawn used when there is none.
- `Unit.cs` is a melee unit: Rigidbody movement (so units collide and can body block), targeting, attacking.
- `Combatant.cs` is the shared health/team/armor logic for units and bases.
- `EnemyBrain.cs` is the simple bot.
- `PlayerCommander.cs` handles right-click rally, tower clicks, restart, HUD and health bars.
- `UnitGear.cs` dresses units to match their team's levels: Strength changes the sword (bronze at 1-5, steel at 6-10, glowing gold at 11+), Armor adds leather / iron / gold pauldrons and a helmet cap at the same brackets, Health grows the model 5% per bracket. Updates on every purchase, for units already alive too.
- `MapLayout.cs` places bases, roads and camps for a player count and sizes the map. `MainMenu.cs` hosts the menu (canvas, prefs, modals, launch); `SplashScreen.cs` and `HomeHub.cs` build the two screens from `HubWidgets.cs` (layout + controls), `HubTheme.cs` (tokens + fonts), `HubArt.cs` (painted sprites) and `HubState.cs` (sample social / play state); `SettingsModal.cs` holds the settings, profile and ranked dialogs. `HomeUnitPreview.cs` owns the isolated character stage; `MenuUi.cs` keeps the safe-area helper the social panel's canvas uses.
- `Icons.cs` paints every icon in code; `FloatingText.cs` is the "+5 coin" popups on kills and the big essence-drop popups.
- `MiniMap.cs` is the bottom-left overview: terrain, live dots for every unit, tower and monster, the camera's view outline; click to look there, right-click to rally there.
- `UiSkin.cs` is the in-match IMGUI look (dark rounded surfaces, Archivo faces, gold primary button, keycaps, icon buttons), shared by the HUD, shop and pause menu. `Hud.cs` draws the resource pill top-left (gold, skulls, essences with tooltips), the match clock top centre (m:ss, stops while paused), the help / social / menu buttons bottom-right (help lists the controls; social opens the F6 panel above them, with an attention badge) and the bottom action bar (units alive with a segmented cap bar, Home, Next enemy, Shop). `ScoreBoard.cs` is the STANDINGS card top-right (swatch, name, level, kills); compact shows four rows plus +N more, Tab or the footer expands it, X hides it. Camp and elite labels are rounded pills over the world. `PauseMenu.cs` is the Esc / Menu-button overlay (Resume, Restart, Quit to home); local matches pause while it is open, and the Victory / Defeat banner offers the same two exits. `SpawnPoof.cs` is the puff and pop-in when a unit appears.
- `Shop.cs` draws the tower shop; upgrade levels and the team's live unit stats live on `Spawner`, the list of upgrades in `swarm_config.json` (`upgrades`).
- `RtsCamera.cs` is the camera; `InputShim.cs` works with both the old and new Unity input systems.
- `GameConfig.cs` / `GameState.cs` hold config loading, gold, kills and win/loss.

## Not in this step

Raised bases with four entrances (step 2), selecting and splitting groups (step 3), the upgrade shop (step 4), special units (step 5), smarter bots and more players (step 6), and the berserk timer (step 7).
