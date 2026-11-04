# Spin the Spire

Custom-run replacement for Slay the Spire 2. The **Custom** button opens a
Wheel-of-Names-style lobby where characters, ascension, and modifiers are decided by
prize wheels.

## What is implemented

- Standalone mod: no external dependencies, no PCK.
- `Custom` button opens the Spin the Spire lobby in Direct mode; the label changes
  to "Spin the Spire".
- Optional `Start in the vanilla custom screen` setting keeps the normal Custom screen
  and adds a host-only "Spin the Spire" button to it.
- Lobby ready-up flow:
  - Each player has **READY / UNREADY**.
  - Host has **START SPINNING** (disabled until everyone is ready).
  - Host has **VANILLA MODE** until the spin starts.
  - The Steam lobby is closed when the host clicks Start; it is reopened on
    "Maybe Not" / disconnect reset.
- Three automatic wheel stages:
  1. **Characters** – two wheels: characters + player names. One player is paired with
     one character per spin. Players leave the name wheel; characters leave the
     character wheel when "Unique characters" is enabled.
  2. **Ascension** – one 0–10 wheel. Unlocks are ignored.
  3. **Modifiers** – two wheels: good modifiers + curses. Every roll always draws a
     good modifier and has a 50% chance of a curse. Duplicates toggle a modifier off;
     the newest member of a mutually exclusive group evicts the others.
- Modifier count is `(ascension + 1) * 2` rolls.
- Final summary with **LET IT RIDE** / **Maybe Not**.
  - Multiplayer uses the vanilla random-vote behaviour: all players vote, the host
    picks a random vote, and the vanilla split-vote ticker/sound plays if the votes
    differ.
  - Singleplayer shows the two buttons directly.
- "Maybe Not" clears ready, results, ascension and modifiers, resets characters, and
  returns to the mod lobby with the same players.
- A non-host leaving during the spin fades to black and returns the host/remaining
  players to the mod lobby. A host leaving shows clients a "Bummer" screen that returns
  to the main menu.
- Settings (Settings > General):
  - `[Spin the Spire] Roll manually` (off by default)
  - `[Spin the Spire] Modifier roll coefficient` (0-5, default 2; disabled while manual)
  - `[Spin the Spire] Start in the vanilla custom screen`
  - `[Spin the Spire] Spin duration cap` (default 60 s)
- Modifier roll target: `(ascension + 1) × coefficient`. Coefficient 0 skips the modifier
  stage in automatic mode.
- Manual mode: the host clicks the gold SPIN button once per character reveal, ascension
  spin and modifier roll. NEXT only advances sections. Re-spinning a completed section in
  multiplayer opens a voted `Nevermind` / `Let me Spin!` prompt (characters and ascension
  reset, modifiers append another roll).
- Character and ascension memory persist in `user://spin_the_spire.cfg`.
- UI uses the game's own main-menu text buttons, settings tickbox, fuzzy nine-patch
  panels, and vanilla lobby player rows/ready ticks. The vanilla custom-screen children
  are hidden while the mod screen is active and restored for Vanilla Mode.

## Project layout

```
SpinTheSpireCode/
  MainFile.cs              mod initializer / Harmony bootstrap
  SpinConfig.cs         persistent config
  SpinSettings.cs       Settings > General rows
  SpinMessage.cs        custom INetMessage sync
  SpinModels.cs         plan / wheel DTOs
  SpinRules.cs          character/modifier pools, plan generation
  SpinSession.cs        active plan used by the ascension override
  PrizeWheel.cs            procedural Wheel-of-Names control
  SpinUi.cs             fonts, buttons, panels, gradient background
  SpinOverlay.cs        overlay lifecycle, lobby, networking
  SpinOverlay.Sequence.cs  wheel stages, summary, vote
  SpinOverlay.Manual.cs    manual spin flow and re-spin prompts
  Patches.cs               screen/menu/settings/ascension patches
```

`*.wheel` files are the Wheel of Names references the visual style is based on. They
are not shipped or compiled.

## Build / deploy

```powershell
$env:PATH = "C:\Users\Reznolf\.dotnet;$env:PATH"
dotnet build -v m
```

The template target copies the DLL/PDB/manifest to:

`C:\Games\Steam\steamapps\common\Slay the Spire 2\mods\SpinTheSpire`

Close the game before building (the DLL is locked while it runs).

## Test checklist

Singleplayer:

1. Launch the game, enable **Spin the Spire** if needed.
2. Singleplayer → Custom should now say **Spin the Spire**.
3. Open it. The lobby should show you (ready), the unique-character checkbox, and an
   enabled **START SPINNING**.
4. Start. Watch the character wheel, ascension wheel, and the two modifier wheels.
5. The summary should show the same character/ascension/modifiers.
6. Press **LET IT RIDE** and confirm the run starts with the shown ascension and
   modifiers.

Multiplayer:

1. Host: Multiplayer → Host → Custom (or "Spin the Spire" in Direct mode).
2. Invite a second player. Both should see the same lobby and ready states.
3. Both ready. Host Start. Both should see the same wheel results.
4. Vote on the summary. A split vote should show the vanilla split-vote ticker and
   sound, then the random result.
5. Test **Maybe Not** returns both players to the lobby with cleared state.
6. Test a non-host disconnect during the wheels: host/remaining players should fade
   back to the lobby.
7. Test the host leaving: clients should see the Bummer screen and return to the main
   menu.

Vanilla mode:

1. In the mod lobby click **VANILLA MODE**. The normal custom screen should appear.
2. The host-only **Spin the Spire** button should be in the top-right.
3. Click it to return to the mod lobby.

## Known placeholder / open item

- The character wheel currently uses all playable characters and does **not** include
  the reference wheel's "YOUR CHOICE" segment. That would need a player-choice flow
  (and sync) before it can be added.
- Wheels are drawn procedurally. Final art can replace the spinner draw with a wheel
  texture later; the current spinner diameters are 400 px (two-wheel stages) and
  520 px (ascension), inside a 440/560 px root.
