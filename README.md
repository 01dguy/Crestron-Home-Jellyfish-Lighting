# JellyFish Lighting Crestron Home Driver

This project is a JellyFish Crestron Home extension scaffold aligned to the LAN WebSocket API (`toCtlrGet`, `toCtlrSet`, `fromCtlr`).

## Current implemented behaviors

- WebSocket transport scaffold with configurable host/port and `ws`/`wss` URI generation (default is non-secure `ws`).
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
- Main page now surfaces active scene and current speed, aligned to driver properties used in the UI definition.

## Captured firmware behavior (4.1.13)

Validated against provided captures for:
- Connection: `ws://` (non-secure)
- Zones: `Garage Door`, `Speaker Soffit`
- Pattern: `Thanksgiving/Thanksgiving paint`
- Per-zone `runPattern` acknowledgements via both `id` and `zoneName`
- Repeated advanced `runPattern` updates carrying escaped `data` JSON with `runData.speed` and `runData.brightness`

## Notes

- Transport now includes real websocket connect/send/receive handling using `ClientWebSocket`; inbound text frames are routed via `ReceiveJsonFromSocket(...)`.
- Metadata default communication port is set to `9000` for current controller/API Explorer usage.

## Next production step

Harden reconnection/backoff and add integration tests against a mock websocket controller endpoint.

## Working locally (no PR required)

If you are applying changes in your own local repo and want to keep everything on your current branch:

- Use `git apply <patchfile.patch>` when you only want to apply file changes to your working tree/index.
- Use `git am <patchfile.patch>` only when the patch is an email-style commit you want to import *as a commit* with author/message metadata.

For this project workflow, `git apply` is usually the right choice when you are iterating locally and creating files directly in-place.

Typical local flow:

```bash
# from repo root
git apply changes.patch
# resolve rejects if any, then
git add .
git commit -m "Apply local Jellyfish driver updates"
```

If the patch does not apply cleanly, use:

```bash
git apply --reject --whitespace=fix changes.patch
```

This keeps you on the same path and branch without needing to open a PR unless you choose to later.

