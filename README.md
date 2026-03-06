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

## Captured firmware behavior (4.1.13)

Validated against provided captures for:
- Connection: `ws://` (non-secure)
- Zones: `Garage Door`, `Speaker Soffit`
- Pattern: `Thanksgiving/Thanksgiving paint`
- Per-zone `runPattern` acknowledgements via both `id` and `zoneName`
- Repeated advanced `runPattern` updates carrying escaped `data` JSON with `runData.speed` and `runData.brightness`

## Notes

- Real socket connect/read/write remains TODO in transport; protocol and payload handling are now aligned with captured real responses.
- Metadata default communication port is set to `80` (legacy arbitrary `2345` removed).

## Next production step

Implement actual websocket I/O in `Jellyfish_Lighting_Transport` and invoke `ReceiveJsonFromSocket(...)` for each inbound text frame.
