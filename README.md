# JellyFish Lighting Crestron Home Driver

This project is a JellyFish Crestron Home extension scaffold aligned to the LAN WebSocket API (`toCtlrGet`, `toCtlrSet`, `fromCtlr`).

## Current implemented behaviors

- WebSocket transport scaffold with configurable host/port and `ws`/`wss` URI generation.
- Transport now exposes an inbound frame callback (`ReceiveJsonFromSocket(...)` -> `InboundJsonReceived`) so received websocket text frames can be routed directly into protocol parsing.
- Poll flow sends:
  - `patternFileList`
  - `zones`
- Inbound `fromCtlr` handling for:
  - `patternFileList` (folder/pattern counts)
  - `zones` (cached known zone names)
  - `patternFileData` (caches `jsonData`, parses speed/brightness)
  - `runPattern` acks (including per-zone `id` responses)
  - `ledPower` updates
- Outbound command helpers:
  - Basic `runPattern` (on/off)
  - Advanced `runPattern` with full stringified data payload
  - Pattern file data request
- Pattern data cache + update helpers:
  - `SetBrightness(...)`
  - `SetSpeed(...)`
- Status aggregation separates the latest power status from command/ack status to reduce rapid status churn in UI.

## Compatibility and observed firmware behavior

Validated against firmware version **4.1.13** and captured controller traffic:

- Transport compatibility: `ws://` observed and validated as working in captures (non-secure WebSocket).
- Device/zone behavior: discovered zones included `Garage Door` and `Speaker Soffit`.
- Pattern behavior: `Thanksgiving/Thanksgiving paint` payloads parse correctly.
- Acknowledgement behavior: `runPattern` responses may identify target zones by either `id` or `zoneName`.
- Realtime update behavior: repeated advanced `runPattern` updates can include escaped nested JSON in `data`, including `runData.speed` and `runData.brightness`.

## Required integration attributes and defaults

The integration should always be configured with these values before connect:

- `ControllerHost` (**required**): JellyFish controller hostname or IP.
- `ControllerPort` (**required**): WebSocket port on the controller.
- `UseSsl` (**required**): security mode selector that controls URI scheme.

Recommended defaults (unless the site requires an override):

- URI scheme: **`ws://`** (`UseSsl = false`)
- Port: **`80`**

Use `wss://` (`UseSsl = true`) only when the controller/site deployment explicitly supports secure WebSocket termination.

## Troubleshooting quick matrix

| Symptom | Likely cause | What to verify/fix |
|---|---|---|
| Connect fails immediately | Host/port/scheme mismatch | Confirm `ControllerHost`, `ControllerPort`, and `UseSsl`; default to `ws://<host>:80` first. |
| Connected but no frames | Polling/request flow not triggered or capture source issue | Trigger `GetPatternsAndZones` / refresh; verify controller is emitting `fromCtlr` frames. |
| Malformed data status/errors | Escaped JSON payload in advanced `runPattern` data not preserved | Validate string escaping in `data` and replay captured frames through parser workflow below. |
| Zone mismatch (action applies to wrong/zero zones) | `id` vs `zoneName` mapping mismatch or stale zone cache | Refresh zones, then compare outgoing selected zones with incoming `runPattern` ack identifiers. |

## Sample capture + replay workflow

Use `websocat` to capture real frames and replay them through the parser helper.

1. Capture a session to a log file (example):

   ```bash
   websocat -t ws://<controller-host>:80 | tee /tmp/jellyfish_ws.log
   ```

2. (Optional) Keep only JSON frame lines if your capture includes non-frame noise.

3. Replay captured frames through the helper:

   ```bash
   python3 tools/replay_websocat_log.py /tmp/jellyfish_ws.log
   ```

4. Review emitted parse/status output to confirm:
   - `fromCtlr` messages are parsed.
   - `zones`, `patternFileList`, and `runPattern` events are recognized.
   - advanced `runPattern.data` values produce expected speed/brightness extraction.

## Notes

- Real socket connect/read/write remains TODO in transport; protocol and payload handling are now aligned with captured real responses.
- Metadata default communication port is set to `9000` for current controller/API Explorer usage.

## Next production step

Implement actual websocket I/O in `Jellyfish_Lighting_Transport` and invoke `ReceiveJsonFromSocket(...)` for each inbound text frame.
