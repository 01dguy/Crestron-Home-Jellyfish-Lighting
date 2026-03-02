# JellyFish Lighting Crestron Home Driver

This project is a JellyFish Crestron Home extension scaffold aligned to the LAN WebSocket API spec (`toCtlrGet`, `toCtlrSet`, `fromCtlr`).

## Aligned behaviors from the provided spec

- WebSocket/LAN messaging model with no auth assumptions.
- Message envelope support for:
  - `patternFileList`
  - `zones`
  - `patternFileData`
  - `runPattern` (basic)
  - `runPattern` (advanced)
- Poll cycle requests `patternFileList` and `zones`.
- Inbound `fromCtlr` parsing updates online/status/scene/brightness and tracks pattern+zone summary counts.

## Request validation now included

- Basic `runPattern` requires:
  - `file`
  - `state` (0/1)
  - at least one `zoneName`
- Advanced `runPattern` validates:
  - required advanced schema keys
  - required `runData` keys
  - `effect = "No Effect"`
  - `effectValue = 0`
  - `rgbAdj = [100,100,100]`
  - `brightness` range 0..100
  - `colors` multiple-of-3, max 90 values, each 0..255

## Notes

- Transport remains a scaffold that validates and logs websocket URI/messages.
- Real socket connect/read/write should be implemented in `Device_Name_Transport` and inbound frames should be routed to `HandleInboundWebSocketJson(...)`.
