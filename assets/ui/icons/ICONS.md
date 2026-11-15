# UI icons

116 SVG icons exported from the Figma file "Underfunded Space Agency - UI" (`H8hAm4dZXNMaLDSdJKK3BO`), page `01 Components` > `Section Icons`, plus the eight Set 2 16 px icons that live on page `20 Comms (Set 2)` (`Cell Icon/*` frames). Node ids link back to the Figma components (`https://www.figma.com/design/H8hAm4dZXNMaLDSdJKK3BO?node-id=<id with - for :>`).

## Conventions

- Path: `<group>/<id>.svg`, `<group>` = second segment of the component name, `<id>` = the full component name in snake_case (`Icon/Family/thruster` -> `family/icon_family_thruster.svg`). Code looks icons up by id through `UiIcons` (below) or `icons.json` (schema `icons/1`: id -> `path`, `size`).
- Grid: 24 px icons use 2 px strokes, 16 px icons use 1.5 px strokes (as drawn in Figma); round caps and joins. `viewBox` is `0 0 24 24` or `0 0 16 16`.
- Colour: every stroke and fill is `#FFFFFF`. Tint at runtime with `modulate` / `self_modulate` (or a theme colour) from the UI tokens. The Figma tint is noted in the table where it is not the default `text/mid` (#A9B4C0); status icons must still be paired with a word (UI.md 7: no status by colour alone).
- No raster, ids, metadata or groups: each file is `<svg>` + `<path>` elements only.

## Godot import preset

Each SVG has a hand-written `.import` next to it: importer `texture`, `CompressedTexture2D`, `compress/mode=0` (lossless), `mipmaps/generate=false`, `svg/scale=2.0` (24 px -> 48 px texture, 16 px -> 32 px), `process/fix_alpha_border=true`, `editor/*` colour conversion off, `detect_3d/compress_to=0`. Godot adds `uid`/`path`/`dest_files` on the first import and keeps the `[params]`; `build_icons.py` leaves an `.import` that already has the preset untouched, so uids survive a re-export. If a file ever imports with other settings, select the SVGs in the FileSystem dock > Import tab and set: Compress Mode Lossless, Mipmaps Generate off, SVG Scale 2.0, Editor Scale With Editor Scale off, Convert Colors With Editor Theme off, then Reimport (or save it as the default `Texture2D` preset for this folder).

Why this preset: the console draws icons at their Figma size (16/20/24 logical px) under `canvas_items` stretch, so they land on 16-32 physical px (1x at 1080p, 1.333x at 1440p, 0.667x at 720p). A 2x raster is always minified (2:1 at 1x, 1.5:1 at 1.333x), which keeps 1.5-2 px strokes smooth with the default linear filter and needs no mipmaps; lossless keeps the white alpha edges clean, so `modulate`/`self_modulate` tints exactly. Pre-rendered PNGs @1x/@2x would give the same pixels with two files per icon. Godot's `DPITexture` (Import As > DPITexture, re-rasterised per oversampling) is the candidate for pixel-exact output at 1.333x; switch the folder over only after checking it in the editor, as it changes the importer of every file.

## Using an icon from C#

`Usa.Game.UI.Theme.UiIcons` (generated with this table): one `const string` per id (`UiIcons.StatusCaution` = `"icon_status_caution"`), `UiIcons.PathOf(id)` -> `res://` path, `UiIcons.All` (id -> `UiIcon(Id, Path, SizePx)`). Load with `ResourceLoader.Load<Texture2D>(UiIcons.PathOf(id))` into a `TextureRect` (`ExpandMode = IgnoreSize`, `StretchMode = KeepAspectCentered`, `CustomMinimumSize` = the Figma size) and tint with `SelfModulate` from the theme token. `tests/Usa.UiTheme.Tests/UiIconsTests.cs` checks every id against `icons.json` and the files on disk.

## Licence

All icons in this folder are original designs drawn for this game in the user's own Figma file "Underfunded Space Agency - UI" (`H8hAm4dZXNMaLDSdJKK3BO`); they belong to the project like the rest of its art. No third-party icon set, font glyph or traced artwork was used, so no attribution or third-party licence applies. Keep it that way: new icons are drawn in Figma (`01 Components` > `Section Icons`) and exported with `tools/icons`.

## Icons

| File | Figma component · node | Size | UI.md 8.1 item | Intended use |
|---|---|---|---|---|
| `action/icon_action_advance.svg` | Icon/Action/Advance · `3:75` | 16 | 8.1 Actions: advance | Advance time |
| `action/icon_action_ask.svg` | Icon/Action/Ask · `3:58` | 16 | 8.1 Actions: talk | Ask / talk to crew (speech bubble, tint #5FB3D9) |
| `action/icon_action_assign.svg` | Icon/Action/Assign · `30:2418` | 24 | 8.1 Actions: assign | Assign task to crew (Set 5) |
| `action/icon_action_back.svg` | Icon/Action/Back · `30:2618` | 24 | - | Front-end back navigation (Set 6) |
| `action/icon_action_check.svg` | Icon/Action/Check · `3:55` | 16 | 8.1 Actions: acknowledge | Acknowledge alarm / confirm |
| `action/icon_action_chevron.svg` | Icon/Action/Chevron · `3:52` | 16 | - | Expand / collapse, dropdowns |
| `action/icon_action_close.svg` | Icon/Action/Close · `3:49` | 16 | - | Close dialog / overlay |
| `action/icon_action_crew.svg` | Icon/Action/Crew · `3:62` | 16 | - | Crew addressee / crew tab |
| `action/icon_action_edit.svg` | Icon/Action/Edit · `30:1707` | 16 | - | Comms input: edit transcript (Set 2) |
| `action/icon_action_encyclopedia.svg` | Icon/Action/Encyclopedia · `30:2610` | 24 | 8.1 Actions: encyclopedia | Open encyclopedia (Set 6) |
| `action/icon_action_exit.svg` | Icon/Action/Exit · `30:2625` | 24 | - | Front-end exit / quit (Set 6) |
| `action/icon_action_focus.svg` | Icon/Action/Focus · `30:490` | 16 | 8.1 Actions: focus | Focus camera / view on component (Set 3) |
| `action/icon_action_inspect.svg` | Icon/Action/Inspect · `30:499` | 16 | 8.1 Actions: inspect | Inspect component (Set 3) |
| `action/icon_action_keyboard.svg` | Icon/Action/Keyboard · `30:1738` | 16 | - | Comms input: switch to keyboard (Set 2) |
| `action/icon_action_labels.svg` | Icon/Action/Labels · `30:2367` | 24 | - | Orbit map label toggle (Set 4) |
| `action/icon_action_layers.svg` | Icon/Action/Layers · `30:507` | 16 | - | Diagram layer toggles (Set 3) |
| `action/icon_action_menu.svg` | Icon/Action/Menu · `3:78` | 16 | 8.1 Actions: menu | Main menu |
| `action/icon_action_mic.svg` | Icon/Action/Mic · `3:66` | 16 | 8.1 Actions: mic | Push-to-talk / mic toggle |
| `action/icon_action_play.svg` | Icon/Action/Play · `30:2621` | 24 | - | Front-end play / start mission (Set 6) |
| `action/icon_action_repair.svg` | Icon/Action/Repair · `30:503` | 16 | 8.1 Actions: repair | Repair / open workspace (Set 3) |
| `action/icon_action_request.svg` | Icon/Action/Request · `41:401` | 24 | 8.1 Actions: request | Request a reading / report from the crew (incoming tray) |
| `action/icon_action_resend.svg` | Icon/Action/Resend · `30:1713` | 16 | - | Comms: resend message (Set 2) |
| `action/icon_action_reset.svg` | Icon/Action/Reset · `30:494` | 16 | - | Reset view (Set 3) |
| `action/icon_action_search.svg` | Icon/Action/Search · `30:2606` | 24 | 8.1 Actions: search | Search field (Set 6) |
| `action/icon_action_send.svg` | Icon/Action/Send · `3:69` | 16 | 8.1 Actions: send | Send comms message |
| `action/icon_action_settings.svg` | Icon/Action/Settings · `30:2615` | 24 | 8.1 Actions: settings | Settings (Set 6) |
| `action/icon_action_stop.svg` | Icon/Action/Stop · `3:72` | 16 | 8.1 Actions: stop | Stop / cancel crew task |
| `action/icon_action_test.svg` | Icon/Action/Test · `30:2414` | 24 | 8.1 Actions: test | Workspace: test connection / part (Set 5) |
| `action/icon_action_undo.svg` | Icon/Action/Undo · `30:1719` | 16 | - | Comms: undo sent message (Set 2) |
| `action/icon_action_zoom_in.svg` | Icon/Action/Zoom in · `30:2359` | 24 | - | Orbit map zoom in (Set 4) |
| `action/icon_action_zoom_out.svg` | Icon/Action/Zoom out · `30:2363` | 24 | - | Orbit map zoom out (Set 4) |
| `category/icon_category_command.svg` | Icon/Category/Command · `3:81` | 16 | - | Event log category: command |
| `category/icon_category_decision.svg` | Icon/Category/Decision · `3:84` | 16 | - | Event log category: decision |
| `category/icon_category_system.svg` | Icon/Category/System · `3:88` | 16 | - | Event log category: system |
| `content/icon_content_image.svg` | Icon/Content/Image · `30:2630` | 24 | - | Front-end image placeholder (Set 6) |
| `crew/icon_crew_fatigue.svg` | Icon/Crew/Fatigue · `30:1696` | 16 | 8.1 Crew: fatigue | Crew card: fatigue (Set 2) |
| `crew/icon_crew_health.svg` | Icon/Crew/Health · `41:405` | 24 | 8.1 Crew: health | Crew card: health (24 px; fatigue/hydration are 16 px) |
| `crew/icon_crew_hydration.svg` | Icon/Crew/Hydration · `30:1701` | 16 | 8.1 Crew: hydration | Crew card: hydration (Set 2) |
| `crew/icon_crew_nutrition.svg` | Icon/Crew/Nutrition · `41:411` | 24 | 8.1 Crew: nutrition | Crew card: nutrition (24 px) |
| `crew/icon_crew_task.svg` | Icon/Crew/Task · `41:416` | 24 | 8.1 Crew: task | Crew card: current task (24 px) |
| `family/icon_family_battery.svg` | Icon/Family/battery · `30:446` | 24 | - | Internal diagram node / component inspector: family `battery` (Set 3, scales to 20 px) |
| `family/icon_family_breaker.svg` | Icon/Family/breaker · `30:426` | 24 | - | Internal diagram node / component inspector: family `breaker` (Set 3, scales to 20 px) |
| `family/icon_family_bus.svg` | Icon/Family/bus · `30:434` | 24 | - | Internal diagram node / component inspector: family `bus` (Set 3, scales to 20 px) |
| `family/icon_family_compartment.svg` | Icon/Family/compartment · `30:450` | 24 | - | Internal diagram node / component inspector: family `compartment` (Set 3, scales to 20 px) |
| `family/icon_family_docking.svg` | Icon/Family/docking · `30:383` | 24 | - | Internal diagram node / component inspector: family `docking` (Set 3, scales to 20 px) |
| `family/icon_family_driver.svg` | Icon/Family/driver · `30:418` | 24 | - | Internal diagram node / component inspector: family `driver` (Set 3, scales to 20 px) |
| `family/icon_family_fan.svg` | Icon/Family/fan · `30:459` | 24 | - | Internal diagram node / component inspector: family `fan` (Set 3, scales to 20 px) |
| `family/icon_family_fuel_cell.svg` | Icon/Family/fuel_cell · `30:442` | 24 | - | Internal diagram node / component inspector: family `fuel_cell` (Set 3, scales to 20 px) |
| `family/icon_family_harness.svg` | Icon/Family/harness · `30:430` | 24 | - | Internal diagram node / component inspector: family `harness` (Set 3, scales to 20 px) |
| `family/icon_family_hatch.svg` | Icon/Family/hatch · `30:374` | 24 | - | Internal diagram node / component inspector: family `hatch` (Set 3, scales to 20 px) |
| `family/icon_family_heat_shield.svg` | Icon/Family/heat_shield · `30:379` | 24 | - | Internal diagram node / component inspector: family `heat_shield` (Set 3, scales to 20 px) |
| `family/icon_family_heater.svg` | Icon/Family/heater · `30:477` | 24 | - | Internal diagram node / component inspector: family `heater` (Set 3, scales to 20 px) |
| `family/icon_family_load.svg` | Icon/Family/load · `30:387` | 24 | - | Internal diagram node / component inspector: family `load` (Set 3, scales to 20 px) |
| `family/icon_family_outlet.svg` | Icon/Family/outlet · `30:438` | 24 | - | Internal diagram node / component inspector: family `outlet` (Set 3, scales to 20 px) |
| `family/icon_family_parachute.svg` | Icon/Family/parachute · `30:485` | 24 | - | Internal diagram node / component inspector: family `parachute` (Set 3, scales to 20 px) |
| `family/icon_family_payload.svg` | Icon/Family/payload · `30:395` | 24 | - | Internal diagram node / component inspector: family `payload` (Set 3, scales to 20 px) |
| `family/icon_family_pump.svg` | Icon/Family/pump · `30:473` | 24 | - | Internal diagram node / component inspector: family `pump` (Set 3, scales to 20 px) |
| `family/icon_family_radiator.svg` | Icon/Family/radiator · `30:391` | 24 | - | Internal diagram node / component inspector: family `radiator` (Set 3, scales to 20 px) |
| `family/icon_family_regulator.svg` | Icon/Family/regulator · `30:454` | 24 | - | Internal diagram node / component inspector: family `regulator` (Set 3, scales to 20 px) |
| `family/icon_family_retro_rocket.svg` | Icon/Family/retro_rocket · `30:481` | 24 | - | Internal diagram node / component inspector: family `retro_rocket` (Set 3, scales to 20 px) |
| `family/icon_family_scrubber.svg` | Icon/Family/scrubber · `30:463` | 24 | - | Internal diagram node / component inspector: family `scrubber` (Set 3, scales to 20 px) |
| `family/icon_family_sensor.svg` | Icon/Family/sensor · `30:422` | 24 | - | Internal diagram node / component inspector: family `sensor` (Set 3, scales to 20 px) |
| `family/icon_family_structure.svg` | Icon/Family/structure · `30:370` | 24 | - | Internal diagram node / component inspector: family `structure` (Set 3, scales to 20 px) |
| `family/icon_family_switch.svg` | Icon/Family/switch · `30:405` | 24 | - | Internal diagram node / component inspector: family `switch` (Set 3, scales to 20 px) |
| `family/icon_family_tank.svg` | Icon/Family/tank · `30:399` | 24 | - | Internal diagram node / component inspector: family `tank` (Set 3, scales to 20 px) |
| `family/icon_family_thermal_node.svg` | Icon/Family/thermal_node · `30:468` | 24 | - | Internal diagram node / component inspector: family `thermal_node` (Set 3, scales to 20 px) |
| `family/icon_family_thruster.svg` | Icon/Family/thruster · `30:409` | 24 | - | Internal diagram node / component inspector: family `thruster` (Set 3, scales to 20 px) |
| `family/icon_family_thruster_group.svg` | Icon/Family/thruster_group · `30:414` | 24 | - | Internal diagram node / component inspector: family `thruster_group` (Set 3, scales to 20 px) |
| `map/icon_map_ap.svg` | Icon/Map/Ap · `30:2337` | 24 | 8.1 Map: Ap | Orbit map: apoapsis marker (Set 4) |
| `map/icon_map_impact.svg` | Icon/Map/Impact · `30:2343` | 24 | 8.1 Map: impact | Orbit map: predicted impact (Set 4) |
| `map/icon_map_lodestar.svg` | Icon/Map/Lodestar · `30:2349` | 24 | - | Orbit map: Lodestar vehicle marker (Set 4) |
| `map/icon_map_node.svg` | Icon/Map/Node · `30:2313` | 24 | 8.1 Map: node | Orbit map: manoeuvre node (Set 4) |
| `map/icon_map_pe.svg` | Icon/Map/Pe · `30:2334` | 24 | 8.1 Map: Pe | Orbit map: periapsis marker (Set 4) |
| `map/icon_map_prograde.svg` | Icon/Map/Prograde · `30:2318` | 24 | 8.1 Map: prograde | Burn planner: prograde handle (Set 4) |
| `map/icon_map_radial_in.svg` | Icon/Map/Radial in · `30:2326` | 24 | 8.1 Map: radial in | Burn planner: radial-in handle (Set 4) |
| `map/icon_map_radial_out.svg` | Icon/Map/Radial out · `30:2331` | 24 | 8.1 Map: radial out | Burn planner: radial-out handle (Set 4) |
| `map/icon_map_retrograde.svg` | Icon/Map/Retrograde · `30:2322` | 24 | 8.1 Map: retrograde | Burn planner: retrograde handle (Set 4) |
| `map/icon_map_tandem.svg` | Icon/Map/Tandem · `30:2346` | 24 | - | Orbit map: Tandem vehicle marker (Set 4) |
| `map/icon_map_zone.svg` | Icon/Map/Zone · `30:2340` | 24 | 8.1 Map: zone | Orbit map: recovery zone (Set 4) |
| `part/icon_part_adapter.svg` | Icon/Part/Adapter · `30:2379` | 24 | 8.1 Parts: adapter | Workspace part tray: adapter (Set 5) |
| `part/icon_part_breaker.svg` | Icon/Part/Breaker · `30:2385` | 24 | 8.1 Parts: breaker | Workspace part tray: breaker (Set 5) |
| `part/icon_part_extension.svg` | Icon/Part/Extension · `30:2375` | 24 | 8.1 Parts: extension | Workspace part tray: extension (Set 5) |
| `part/icon_part_fuse.svg` | Icon/Part/Fuse · `30:2382` | 24 | 8.1 Parts: fuse | Workspace part tray: fuse (Set 5) |
| `part/icon_part_harness.svg` | Icon/Part/Harness · `30:2372` | 24 | 8.1 Parts: harness | Workspace part tray: harness (Set 5) |
| `part/icon_part_lioh_canister.svg` | Icon/Part/LiOH canister · `30:2393` | 24 | 8.1 Parts: LiOH canister | Workspace part tray: LiOH canister (Set 5) |
| `part/icon_part_spare.svg` | Icon/Part/Spare · `30:2396` | 24 | 8.1 Parts: generic spare | Workspace part tray: generic spare (Set 5) |
| `part/icon_part_tape.svg` | Icon/Part/Tape · `30:2390` | 24 | 8.1 Parts: tape | Workspace part tray: tape (Set 5) |
| `port/icon_port_d_type.svg` | Icon/Port/D-type · `30:2404` | 24 | - | Workspace connector port: D-type (Set 5) |
| `port/icon_port_round.svg` | Icon/Port/Round · `30:2411` | 24 | - | Workspace connector port: round (Set 5) |
| `recovery/icon_recovery_helicopter.svg` | Icon/Recovery/Helicopter · `30:2355` | 24 | - | Recovery forces: helicopter (Set 4) |
| `recovery/icon_recovery_ship.svg` | Icon/Recovery/Ship · `30:2352` | 24 | - | Recovery forces: ship (Set 4) |
| `state/icon_state_conflict.svg` | Icon/State/Conflict · `3:36` | 16 | 8.1 Status: conflict (⇄) | Knowledge badge / fact chip: conflict (tint #C97CDD) |
| `state/icon_state_estimated.svg` | Icon/State/Estimated · `41:388` | 24 | 8.1 Status: estimated (≈) | Knowledge badge / fact chip: estimated (EST) |
| `state/icon_state_ground_band.svg` | Icon/State/Ground band · `41:395` | 24 | 8.1 Status: ground band | Knowledge badge / fact chip: ground band (GND, value from the ground station) |
| `state/icon_state_info.svg` | Icon/State/Info · `3:40` | 16 | - | Info / dependency tooltip trigger (tint #5FB3D9) |
| `state/icon_state_link_lost.svg` | Icon/State/Link lost · `30:1732` | 16 | - | Comms service status: link lost (Set 2) |
| `state/icon_state_locked.svg` | Icon/State/Locked · `30:2602` | 24 | - | Front-end: locked mission / content (Set 6) |
| `state/icon_state_report.svg` | Icon/State/Report · `30:1725` | 16 | - | Comms: crew report marker (Set 2) |
| `state/icon_state_stale.svg` | Icon/State/Stale · `3:33` | 16 | 8.1 Status: stale (clock) | Knowledge badge / fact chip: stale |
| `status/icon_status_caution.svg` | Icon/Status/Caution · `3:16` | 16 | 8.1 Status: caution | Status badge / alarm row: caution (tint #E8C547) |
| `status/icon_status_failed.svg` | Icon/Status/Failed · `3:19` | 16 | 8.1 Status: failed | Status badge / alarm row: failed (tint #EA6E72) |
| `status/icon_status_isolated.svg` | Icon/Status/Isolated · `3:23` | 16 | 8.1 Status: isolated | Status badge: isolated |
| `status/icon_status_nominal.svg` | Icon/Status/Nominal · `3:13` | 16 | 8.1 Status: nominal | Status badge / alarm row: nominal (Figma tint status/nominal #4FB37A) |
| `status/icon_status_suspect.svg` | Icon/Status/Suspect · `3:26` | 16 | 8.1 Status: suspect | Status badge: suspect, dashed (tint #E8C547) |
| `status/icon_status_unknown.svg` | Icon/Status/Unknown · `3:29` | 16 | 8.1 Status: unknown | Status badge: unknown, dashed (tint #8E98A4) |
| `system/icon_system_attitude.svg` | Icon/System/Attitude · `3:104` | 24 | 8.1 Systems: attitude | Systems board / system tab: attitude |
| `system/icon_system_comms.svg` | Icon/System/Comms · `3:118` | 24 | 8.1 Systems: comms | Systems board / system tab: comms |
| `system/icon_system_inventory.svg` | Icon/System/Inventory · `3:124` | 24 | 8.1 Systems: inventory | Systems board / inventory tab |
| `system/icon_system_life_support.svg` | Icon/System/Life support · `3:96` | 24 | 8.1 Systems: life support | Systems board / system tab: life support |
| `system/icon_system_navigation.svg` | Icon/System/Navigation · `3:113` | 24 | 8.1 Systems: navigation | Systems board / system tab: navigation |
| `system/icon_system_power.svg` | Icon/System/Power · `3:91` | 24 | 8.1 Systems: power | Systems board / system tab: power |
| `system/icon_system_propulsion.svg` | Icon/System/Propulsion · `3:100` | 24 | 8.1 Systems: propulsion | Systems board / system tab: propulsion |
| `system/icon_system_structure.svg` | Icon/System/Structure · `3:121` | 24 | 8.1 Systems: structure | Systems board / system tab: structure |
| `system/icon_system_thermal.svg` | Icon/System/Thermal · `3:109` | 24 | 8.1 Systems: thermal | Systems board / system tab: thermal |
| `trend/icon_trend_down.svg` | Icon/Trend/Down · `3:46` | 16 | - | Readout trend arrow: falling |
| `trend/icon_trend_up.svg` | Icon/Trend/Up · `3:43` | 16 | - | Readout trend arrow: rising |

## UI.md 8.1 coverage

Every UI.md 8.1 item has an icon. The last six (estimated, ground band, request, crew health, crew nutrition, crew task) were drawn at 24 px in `01 Components` > `Section Icons` > `Icons 24 · UI.md 8.1 gaps (G6 rev 2)`. The crew health/nutrition/task icons are 24 px while fatigue and hydration are 16 px (Set 2); scale down in the crew card or redraw at 16 if they must sit in the same row.
