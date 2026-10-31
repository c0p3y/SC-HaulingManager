# Changelog

## 0.10.0
- Dockable panels: drag a panel by its title to move it anywhere (live preview + drop shadow), drag the gaps to resize, minimize / close / restore any panel, double-click a title to fill the window, Panels ▾ menu and Reset layout. Replaces the fixed grid / classic layouts.
- Session timer & income log: starts with your first contract, pause for breaks, stops when everything is delivered. Summary window with aUEC earned, aUEC per hour, active / total time, contracts taken / completed, boxes and SCU delivered, max SCU aboard, leg times, a timestamped log, earlier sessions and CSV export.
- Named custom colour themes (Save as / Delete); unsaved custom colours are no longer lost when you switch themes.

## 0.9.2
- All column headers and cell text are left-aligned in every table (numbers included).

## 0.9.1
- One shared place list for Start at, pickup and drop-off; places read from contracts are remembered.
- Fuzzy best-match of every contract location against the catalogue (OCR-tolerant).
- Added the distribution centres from the Star Citizen Wiki (HDPC-Cassillo, HDPC-Farnesway, Covalex S1DC06, Dupree, Greycat Stanton I-B, Cry-Astro 19-02 / 34-12) and Pyro salvage yards / trading posts (Seer's Canyon, Last Landings, Supply Gap, Rough Landing).
- Prompt for the reward when it can't be read; the edit window highlights a blank reward. Fixed the "Reward" label wrapping.

## 0.9.0
- In-game route overlay (semi-transparent, always on top, never takes focus). Hotkeys to complete or undo the current step and to show or hide the overlay.

## 0.8.x
- 0.8.4: one pickup spelled two ways by OCR ("Patch City" / "Petch City") is no longer split into two pickup points; existing contracts are repaired on start.
- 0.8.3: full location catalogue (every Lagrange station by full name, outposts, cities, distribution centres) from UEX Corp; fuzzy matching of OCR slips; "Update locations (UEX)".
- 0.8.2: angled cargo bays drawn correctly (Railen, Hull B, Hull C, M80).
- 0.8.1: four equal panels by default; your panel proportions persist through resizing; Expand / Restore for the 3D hold.
- 0.8.0: aligned panel grid layout (Classic layout still available); FRONT / REAR / PORT / STARBOARD labels in the 3D hold.

## 0.7.x
- Colors & Themes: six themes and fully custom colours, editable cargo colour order. Deduplicated Start at list.

## 0.6.x
- Packing modes (tightest fit, grouped by cargo / destination, 1 SCU gaps), stack-up-first packing, canonical destination names, contracts always numbered 1..N, pink highlight theme.

## 0.5.x
- Four-panel layout, 3D cargo hold with ship grids, manifest / delivered lists, drag-and-drop placement, off-grid area, crate labels, route steps with undo.

## 0.1 - 0.4
- OCR contract capture with in-game hotkey, route planner, station / outpost detection, multi-pickup contracts, container entry.
