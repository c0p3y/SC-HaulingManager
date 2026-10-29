# SC Hauling Manager - User Guide
C0P3Y | Icarus Interstellar Inc.

Reads accepted hauling contracts straight off the Star Citizen screen and plans the best pickup / drop-off order across all of them.

## Requirements
- Windows 10 or 11 (uses the .NET Framework 4.8 and the OCR engine that are already part of Windows)
- No installer, no registry changes, no downloads. Settings and contracts are saved as JSON files next to the exe.

## Getting started
1. Put `SCHaulingManager.exe` in any folder you like and run it. (Windows SmartScreen may warn because the exe is unsigned. If you'd rather not run a prebuilt exe, run `build.bat` to compile it yourself from `src\` with the compiler that comes with Windows.)
2. In Star Citizen, use **Borderless** or **Windowed** display mode. (If captures come out black, switch the capture method in Settings.)
3. Accept a hauling contract, open its details in mobiGlas, and press the hotkey (default **Ctrl + Shift + H**; you can change it in Settings).
   The hotkey uses a keyboard hook, so it works while the game has focus. You can also click **Read in 5s** and switch back to the game.
   A popup over the game shows what was read, and warns you if the contract uses a planetary outpost.
4. **Select Region** (recommended): after your first capture, drag a box around the contract details pane. This keeps the contract list and your wallet balance out of the read.
5. Pick your ship in **Route Plan** (110 ships, with each ship's real cargo grid SCU), then press **PLAN ROUTE**. Tick each pickup or drop-off as you do it in game.

## Screen layout
Accepted Contracts (top left) · Route Plan (top right) · Cargo to Move (bottom left) · Cargo Hold 3D (bottom right, the biggest panel).
- The four panels sit on one shared grid and start out the same size. Drag the line between columns or rows to resize. Your proportions stay the same when you resize, minimize or maximize the window, and the app remembers them.
- **Expand** (cargo hold toolbar) makes the 3D hold fill the window; **Restore** puts the other panels back.
- Prefer the old layout? Settings → General → Window layout → **Classic (v0.7.1)**.

## Route steps
- Ticking a step doesn't remove it. Done steps stay at the top of the route, greyed out, so you can see what's happened.
- Un-ticking a done step undoes it (like a back button). That puts the cargo, the container boxes and the 3D hold back the way they were.

## In-game route overlay
- Click **Overlay** (top bar) or press **Ctrl + Delete** to show a small see-through list of your route steps on top of Star Citizen. It shows each stop and what to pick up or drop off there. The ▶ step is the next one; done steps are greyed with ✓.
- **Ctrl + End** ticks off the ▶ step and moves to the next, without leaving the game. **Ctrl + Home** un-ticks the last step. Change these in Settings.
- Ticking a pickup from the hotkey records the planned amount (no container dialog, so the game keeps focus). If you loaded something different, fix it later in Cargo to Move.
- Drag the overlay to move it and drag an edge to resize; it remembers where you put it. Settings has its opacity and a click-through option.
- Star Citizen must be in **Borderless** or **Windowed** mode. Overlays can't show over exclusive fullscreen.

## Cargo Hold (3D)
- When you tick a **PICK UP**, enter the containers that came up the elevator. They appear in the hold's **MANIFEST** (left side). Containers you enter in Cargo to Move appear there too.
- **PACK CARGO** places everything in the manifest onto your ship's real cargo grid. Cargo for later stops goes deepest; cargo for the next stop sits nearest the exit and on top.
- The **packing** list next to PACK CARGO sets the layout. Every mode stacks up before spreading out, and turns containers whichever way uses less floor:
  - **Tightest fit**: least space; cargo for later stops goes deepest.
  - **Group by cargo + destination**: each cargo for each stop is stacked together, groups in drop-off order (first drop nearest the exit).
  - **Cargo groups + 1 SCU gaps**: as above, with a one-cell gap between every group.
  - **Destination groups + 1 SCU gaps**: everything for one stop sits together (still sorted by cargo), with a gap only between stops. It's a little tighter.
  - Gaps are only used when the hold still has room for everything left to pick up on the route. Otherwise you get a warning, and it packs tightly by cargo in drop-off order.
- Destination colours go to the cargo in the hold, first drop-off first, always starting from the top of the cargo colour list (Settings → Colors & Themes).
- **Repack…** asks whether to repack just the **last load** or **all cargo for the whole route**.
- Drag any container to move it. It drops onto the floor or stacks on top of other boxes. R or right-click while dragging turns it. Right-click a container for "Turn 90°" or "Send back to the manifest". You can also drag from the manifest list onto the ship, or double-click a manifest line to auto-place one box.
- Containers are drawn as dark grey crates with destination-coloured corner brackets (see the legend). Every face shows the size (e.g. 4 SCU, top-left), the cargo (centre) and → destination (bottom); text scales with the crate so the three never overlap.
- **OFF GRID** (dashed area beside the ship) is for crates that aren't on the cargo grid. Drag them there, or right-click a crate and choose **Move off grid**. The packer never fills it, and the header shows on-grid and off-grid SCU separately.
- FRONT / REAR / PORT / STARBOARD labels around the ship show which way it faces.
- Drag empty space to rotate the view. The mouse wheel zooms, and Shift+drag or middle-drag pans. Q / E turn the view.
- Ticking a **DROP OFF** moves those containers from the ship to **DELIVERED** (size, quantity and where). **Undeliver** there, or un-ticking the step, puts them back on the ship in the same spot.
- Containers only leave the hold when their contract is cleared (Clear Completed / Remove) or you clear the delivered list.
- Ship and cargo-grid data come from SuperCargo 0.9.1 (MIT, © 2026 Travis; grid positions via sc-cargo.space / Ratjack). Angled bays (Railen, Hull B, Hull C, M80) are drawn at their real angle.

## Pickups with unknown amounts
Some contracts collect one cargo total from several pickup points without saying how much waits at each. **Cargo to Move** shows these as a group with estimated shares (**~48**).
- When you load at one of those pickups, click its **LOADED ✎** cell and type the real amount. Typing **0** there clears it, as if you'd never loaded there: the estimate comes back, the containers leave the hold and the route history is corrected. (Ticking the PICK UP in the route and entering 0 means "nothing was waiting there".) Or tick the PICK UP in the route, and it asks you.
- The rest of the contract total is spread over the pickups you haven't visited yet (a pickup marked "skip" has nothing left to collect), and the route is re-planned from where you are.

## Containers
- Click a **CONTAINERS** cell in Cargo to Move to record what came up the freight elevator (for example 3 × 8 SCU, 1 × 4, 1 × 2). The total goes into LOADED for you, and the route updates.
- Ticking a PICK UP in the route asks for the containers too. You can turn that off in Settings. If you don't want to count boxes, just type a total.
- Drop-offs in the route show the containers for that cargo (▣ 3×8 1×4) so you can find them on your ship's grid.

## Stations vs planetary outposts
- **Cargo to Move** shows the type of each pickup and drop-off: Station, City, Outpost (Dist. Center) or Planetary Outpost.
- **Check Outposts** highlights and selects every active contract that uses a planetary outpost. You can then press **Remove**, or keep them knowingly.
- Anything "on <planet/moon>" that isn't a known city counts as a planetary outpost. You can choose in Settings whether distribution centers count. Known names live in `locations.json`.

## Colors & Themes
**Settings → Colors & Themes**
- **Themes**: dark: Navy & Pink (default), Graphite & Orange, Slate Grays, Abyss Teal & Coral. Light: Daylight Blue, Paper Mint.
- **Custom colors**: set any of the 25 colours (background, panels, primary/secondary text, accent, highlight, selection, crate body/text, …). Click a swatch or type a hex value. Changing a colour switches to the Custom theme. A preview shows the result.
- **Cargo colors (fill order)**: reorder, change, add or remove the destination colours used in the cargo hold.
- Theme changes apply after a restart (the app offers to restart when you save). Cargo colours apply straight away.

## If the hotkey does nothing in game
- If Star Citizen runs as administrator, Windows blocks keys from reaching normal apps. The status bar tells you when that's the case: click **Restart as Admin**.

## Good to know
- The **Start at** list has every station, Lagrange station (HUR-L1 … MIC-L5 by full name, e.g. "CRU-L4 Shallow Fields Station"), city, outpost and distribution centre once, plus any other places from your contracts. Small OCR slips ("Gastight", "Petch City") are matched to the right place, so one pickup read two ways is no longer split into two pickup points.
- The place list comes from UEX Corp (uexcorp.space). **Settings → Update locations (UEX)** downloads the latest list, for example after a patch adds new stations or outposts.
- Contracts are always numbered 1, 2, 3… in list order. Removing one renumbers the rest.
- Capturing the same contract again (for example after a partial delivery, when it shows 12/24) updates its progress instead of adding a duplicate.
- If the OCR misreads something, double-click the contract to fix it. You can also edit the captured text and press **Parse text**.
- **Last Capture...** shows the exact image and text the OCR saw.
- The planner splits cargo bigger than your hold into several trips, never overloads the ship, and drops cargo off before picking up at each stop.
- Travel costs come from `locations.json`, which has planets, moons, stations and travel effort per tier. Edit it to add new stations or systems.
- Troubleshooting: turn on *Save captures + OCR text* in Settings. The `debug` folder then gets every capture, which helps when tuning the parser.

## Files
| File | Purpose |
|---|---|
| `SCHaulingManager.exe` | The app |
| `settings.json` | Hotkey, capture region, options |
| `contracts.json` | Your tracked contracts and progress |
| `locations.json` | Location / travel-cost data, which you can edit |
| `build.bat` + `src\` | Source, rebuildable with Windows' built-in csc.exe |
| `sample-contract.txt` | Example text to try **Add Contract Manually → Parse text** without the game |

Read-only tool: it only takes screenshots and reads keyboard state for the hotkey. It never touches the game's memory or files. Not affiliated with Cloud Imperium Games.
