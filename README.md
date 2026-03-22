# JellyFish Lighting Crestron Home Driver

This project is a Crestron Home extension driver for JellyFish Lighting controllers using the LAN WebSocket API (`toCtlrGet`, `toCtlrSet`, `fromCtlr`).

## Current implemented behavior

### Transport / connection
- Real WebSocket transport using `ClientWebSocket`
- Configurable controller host and port
- Current implementation forces non-secure `ws://`
- Automatic reconnect/backoff behavior
- Inbound WebSocket text frames are routed directly into protocol parsing

### Polling / synchronization
- Automatic polling is enabled
- Poll requests:
  - `patternFileList`
  - `zones`
- Default poll interval is **180 seconds**
- User-adjustable poll interval is exposed in the Settings UI
- Poll interval is clamped to a minimum of **10 seconds**

### Inbound controller handling
- `patternFileList`
  - caches available scene/category paths
  - counts folders/patterns for status
- `zones`
  - caches known zone names
- `runPattern`
  - tracks active scene/pattern file
  - updates power state from `runPattern.state`
  - handles acknowledgements identified by either `id` or `zoneName`
- `ledPower`
  - updates power feedback when present

### Outbound controller handling
- Basic `runPattern` command for on/off using selected file + zones
- Cached-state restore after reconnect
- Power-off behavior targets known zones when available

### Crestron Home UI
- Main tile with status text and toggle behavior
- Parent scene category selection
- Child scene selection
- Parent/child scene selections concatenate into a full pattern path such as:
  - `Christmas/Mint`
- Selected child scene can be run from the UI and used for normal on/off workflow
- Settings page exposes:
  - SSL toggle (currently transport still forces `ws://`)
  - poll interval

## Observed / validated behavior

Validated against real controller traffic and in-system testing:

- Connection over `ws://<controller>:9000`
- Zone discovery from `zones`
- Pattern/category discovery from `patternFileList`
- Scene execution using parent/child path selection
- Power on/off behavior using selected pattern + selected zones
- `runPattern` acknowledgements may identify the target using:
  - `id`
  - `zoneName`

## Required configuration

Before connecting, configure:

- `ControllerHost`
  - JellyFish controller hostname or IP
- `ControllerPort`
  - WebSocket port on the controller
- `UseSsl`
  - currently exposed in UI, but transport presently forces non-secure `ws://`

## Recommended defaults

- `ControllerPort = 9000`
- `UseSsl = false`
- `PollIntervalSeconds = 180`

For sites where pattern/zone data changes rarely, a longer poll interval such as **300 seconds** may reduce unnecessary list refresh activity.

## Known limitations / current quirks

- Transport currently forces `ws://` even if `UseSsl` is enabled in settings
- Scene-selection UX is functional but may still benefit from UI flow cleanup
- Pattern/zones list handling is now much more stable, but the UI flow has been iterated heavily and should continue to be tested with real processors and mobile clients

## Troubleshooting

### No connection
Check:
- `ControllerHost`
- `ControllerPort`
- controller reachable on `ws://<host>:9000`

### Connected but no scenes appear
Check:
- `patternFileList` is being returned by the controller
- the driver receives `fromCtlr` frames
- a manual refresh / `GetPatternsAndZones` succeeds

### Power toggle works but desired scene does not run
Check:
- selected parent and child scene together form the expected path
- selected zones are valid
- controller sends `runPattern` acknowledgement after selection

### Scene list or tile disappears in user UI
This has historically been tied to listbutton timing and scene-list readiness. If it reappears, inspect:
- parent scene list population timing
- main page listbutton bindings
- scene list readiness gating

## Useful test / capture workflow

Capture traffic from the controller:

```bash
websocat -t ws://<controller-host>:9000 | tee /tmp/jellyfish_ws.log
