# Architecture

## Scope
This document is derived only from files in this local repository. It describes what is present in code today and calls out uncertainty explicitly where behavior cannot be confirmed from source alone.

## Main Project Structure
- `Crestron-Home-Jellyfish-Lighting.csproj`: .NET Framework 4.8 class library project for a Crestron Home extension driver.
- `Jellyfish_Lighting.cs`: top-level extension-device class (`AExtensionDevice`) that defines UI-facing properties, command handlers, settings save/load, and driver lifecycle (`Connect`/`Disconnect`).
- `Jellyfish_Lighting_Protocol.cs`: protocol/state layer (`ABaseDriverProtocol`) that builds outbound JSON, parses inbound `fromCtlr` JSON, manages poll cadence, and tracks cached scene/zone/power state.
- `Jellyfish_Lighting_Transport.cs`: transport layer (`ATransportDriver`) using `ClientWebSocket` with reconnect loop/backoff and inbound text frame routing.
- `Settings_Data.cs`: persisted driver settings model (currently `UseSsl` compatibility field and `PollIntervalSeconds` with min clamp).
- `Jellyfish_Lighting.json`: driver manifest (device metadata, communication defaults, user attributes like `ControllerHost`/`ControllerPort`).
- `IncludeInPkg/UiDefinitions/UiDefinition.xml`: Crestron Home UI layouts, controls, and command bindings.
- `IncludeInPkg/Translations/en-US.json`: UI label strings.
- `tests/`: local Python parser/behavior fixtures for inbound message handling expectations.
- `tools/replay_websocat_log.py`: local analyzer for captured WebSocket logs.

## Key Classes And Responsibilities

### `Jellyfish_Lighting` (device/UI orchestration)
- Creates driver properties and object lists used by the UI (`StatusText`, `PowerState`, parent/child scene lists, etc.).
- Instantiates and wires:
  - `Transport.InboundJsonReceived -> Protocol.HandleInboundWebSocketJson`
  - `Protocol.UI_Update -> Jellyfish_Lighting.Update_UI`
- Handles Crestron commands (`RefreshNow`, `GetPatternsAndZones`, `TogglePatternPower`, `SelectParentScene`, `RunChildScene`, `PowerOff`, settings commands).
- Converts UI selections into protocol calls (`RunPattern`, `SetPowerState`) and scene path composition (`parent/child`).
- Maintains fallback behavior:
  - If selected zones are blank, uses `Protocol.GetKnownZonesSnapshot()`.
  - If scene path is blank, attempts first available parent/child auto-selection.

### `Jellyfish_Lighting_Protocol` (protocol + state machine)
- Owns runtime state exposed to UI: online status, last scene, brightness/speed, power status, zone summary, ack text.
- Handles lifecycle (`Start`, `Stop`) and polling interval control (`PollingInterval` in ms, minimum 10s).
- Sends outbound controller JSON:
  - `toCtlrGet` for `patternFileList` and `zones`
  - `toCtlrSet` `runPattern` (basic by file, advanced by cached `data`)
- Parses inbound `fromCtlr` using regex helpers:
  - `runPattern` (scene path, optional `data`, zone ack by `id` or `zoneName[0]`)
  - `ledPower`
  - `patternFileList`
  - `zones`
- Caches:
  - known zones
  - pattern paths (derived from `patternFileList` folder+name pairs)
  - last pattern file / run data for reconnect restore
- On reconnect:
  - polls immediately
  - optionally restores cached prior state (advanced data preferred over basic file restore)
  - skips restore if a newer outbound command was sent after reconnect start.

### `Jellyfish_Lighting_Transport` (WebSocket transport)
- Uses `ClientWebSocket` to connect to `ws://<host>:<port>`.
- Explicitly forces non-secure WebSocket (`ws://`) even if `useSsl` is requested.
- Emits callbacks for connection established/lost and inbound JSON frames.
- Runs async receive loop for text frames, reassembles fragmented frames, forwards complete JSON string.
- Implements reconnect loop with backoff: `1s, 2s, 5s, 10s, 30s, 60s` then `60s` indefinitely.
- Handles stop/cleanup with cancellation tokens and socket close.

### `Settings_Data` (persisted settings contract)
- Fields: `UseSsl`, `PollIntervalSeconds`.
- `UseSsl` is preserved for compatibility but forced false on save.
- Poll interval is clamped to minimum 10 seconds.

## Transport / Protocol Flow
1. Driver `Connect()` loads persisted settings and starts protocol.
2. Protocol applies host/port/ssl config to transport (ssl currently ignored by transport).
3. Transport opens WebSocket to `ws://host:port`.
4. On connection established, protocol marks online, calls `PollNow()`, and attempts cached-state restore.
5. Poll sends two `toCtlrGet` requests:
- `patternFileList`
- `zones`
6. Inbound text frames are parsed only when `cmd == "fromCtlr"`.
7. `patternFileList` updates cached paths, then device rebuilds parent/child scene lists for UI.
8. `zones` updates known zones and zone summary string.
9. UI commands (`RunChildScene`, toggle power, etc.) send `toCtlrSet/runPattern`.
10. On disconnect/error, transport marks offline and starts reconnect attempts; protocol updates status via callbacks.

## Build Requirements
- Language/runtime:
  - C# targeting `.NET Framework v4.8`.
  - Output type: class library.
- Solution/project:
  - `Crestron-Home-Jellyfish-Lighting.sln`
  - `Crestron-Home-Jellyfish-Lighting.csproj`
- NuGet packages (`packages.config`):
  - `Crestron.SimplSharp.SDK.Library` `2.21.226`
  - `Crestron.SimplSharp.SDK.ProgramLibrary` `2.21.226`
- Additional required references in `.csproj`:
  - `Crestron.DeviceDrivers.API.dll`
  - `RADCommon.dll`
  - `RADProTransports.dll`
  - These are referenced via relative `..\..\..\..\..\crestron_drivers_sdk\Libraries\...` paths and are expected to exist outside this repo.
- The project includes an `EnsureNuGetPackageBuildImports` target that fails build when package target files are missing.

## Conventions And Constraints Observed In Code
- Protocol parsing/building is manual string/regex based (not strongly typed JSON model).
- Status aggregation preference:
  - If `LastPowerStatus` exists, it dominates `LastStatus`, optionally appending `LastAckStatus`.
  - Else `LastAckStatus` becomes status.
- Zone validation for `RunPattern`:
  - If known zones exist, requested zones must match one of them (case-insensitive).
- Scene model convention:
  - Parent/child scene lists derive from paths in `patternFileList` as `folder/name`.
- Polling convention:
  - Auto polling enabled; interval controlled by settings with minimum 10 seconds.
- Transport convention:
  - `wss://` is intentionally disabled in current implementation.

## Uncertainties / Explicitly Unverified Items
- The C# protocol parser has helper paths for `patternFileData` in fixtures tooling, but runtime `HandleInboundWebSocketJson` in C# does not currently process `patternFileData` explicitly.
- Local Python test harness status strings for LED power (`"LED power is ON/OFF"`) differ from C# runtime strings (`"Jellyfish is ON/OFF"`), so tests are not a literal mirror of current C# status text behavior.
- `UiDefinition.xml` settings page includes a host text entry bound to `{ControllerHost}`, but `Jellyfish_Lighting.cs` does not define a corresponding property key in `CreateDeviceDefinition()`. Whether this works via platform-level user-attribute plumbing rather than local property definition is not provable from this repo alone.
- Build execution was not performed during this documentation pass, so compile/runtime validation against a real Crestron SDK environment is not confirmed here.
