# Project Notes

## Current State (User-Provided Context)
- This project was developed from scratch using Codex Cloud.
- The repo workflow is: developed locally, pushed to GitHub, then pulled into Visual Studio.
- The solution builds successfully in Visual Studio.
- Crestron SDK `ManifestUtil.exe` successfully packages build output from `/bin/release` into a `.pkg` file.
- The driver is currently running successfully on a Crestron Home processor.
- Confirmed working behavior in current deployment:
  - Turn lights on/off
  - Change lighting scenes/patterns
  - Manually refresh patterns and zones to pick up new changes
- Next phase focus:
  - Improve UI
  - Fix buggy issues

## Collaboration Notes
- Keep architecture and notes docs updated as behavior changes.
- Prefer incremental, low-risk changes to avoid regressions in currently working transport/protocol behavior.

## Crestron UI Definition Conventions
- In `IncludeInPkg/UiDefinitions/UiDefinition.xml`, prefer hiding controls with `visible="#false"` instead of deleting them.
- Rationale: in Crestron Home, `UiDefinition.xml` acts as both UI layout and a functional contract surface for the driver.
- Practical guidance: when iterating UI, keep existing IDs/bindings where possible and disable visibility first to reduce integration regressions.
- Brittleness warning: invalid/missing/mismatched bindings can cause the entire tile/UI surface to disappear even while transport/protocol communication is still working.
- Regression note: binding a UI field directly to `ControllerHost` caused a UI regression; use a dedicated UI display property (for example `ControllerHostDisplay`) and map it to protocol/user-attribute values in code.
