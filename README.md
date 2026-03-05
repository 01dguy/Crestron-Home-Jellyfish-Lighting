# JellyFish Lighting Crestron Home Driver

This project is a JellyFish Crestron Home extension scaffold aligned to the LAN WebSocket API (`toCtlrGet`, `toCtlrSet`, `fromCtlr`).

## Current implemented behaviors

- WebSocket transport scaffold with configurable host/port and ws/wss URI generation.
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

## Notes

- Real socket connect/read/write remains TODO in transport; protocol and payload handling are now aligned with captured real responses.
- Environment data gathered so far indicates controller connectivity over `ws://` (non-secure) for current setup.

## Next production step

Implement actual socket I/O in `Jellyfish_Lighting_Transport` and route inbound frames into `HandleInboundWebSocketJson(...)`.
