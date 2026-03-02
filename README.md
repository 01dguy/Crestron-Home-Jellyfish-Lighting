# Crestron-Home-Extension-Driver-Template

These files make up a template for writing extension drivers for Crestron Home. Normally this project is shared as a full Visual Studio solution zip, but the complete solution is larger than GitHub's file-size limits.

## Converting this template into a production Crestron Home driver

This repository is the starting point. A functioning driver still requires device-specific transport logic, protocol parsing, command/feedback wiring, and metadata/UI completion.

## Documentation status

### Received

- Driver SDK v1 landing page:  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Driver-SDK-V1.htm
- Create a Project (Simpl# required interfaces/classes starting point):  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Create-a-Driver/Create-a-Project.htm
- Extension Drivers overview:  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Create-a-Driver/Device-Types/Extensions/Extension-Drivers.htm
- UI Files reference (`UiDefinition.xml` structure and supported definitions):  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Create-a-Driver/Device-Types/Extensions/UI-Files.htm
- UI translation guidance for `en-US.json` (same UI Files page, Translations section):  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/Create-a-Driver/Device-Types/Extensions/UI-Files.htm#Translat
- Crestron Device Driver SDK API reference (full class/member docs):  
  https://sdkcon78221.crestron.com/downloads/CrestronCertifiedDriversAPI/html/R_Project_Crestron_Certified_Drivers_SDK_Documentation.htm
- Driver JSON schema reference:  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/API-Reference/Driver-JSON-Schema/Driver-JSON-Schema.htm
- Driver JSON schema API details (property/member-level reference):  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/API-Reference/Driver-JSON-Schema/API/API.htm
- SDK Architecture (framework and best-practice guidance):  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V1/SDK-Framework/SDK-Architecture.htm
- Crestron Driver SDK Best Practices:  
  https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Best-Practices/Best-Practices.htm

### Still needed (specific links)

Please share direct links to the exact pages for:

1. Optional: transport implementation examples for your specific target protocol/device class (helpful for production behavior, but not required for using this basic template).

> Note: Packaging/Manifest Utility steps can be handled at the end and are not required to start core driver implementation.

## Implementation plan mapped to this template

1. Define v1 feature scope for a target device (power, inputs, volume, mute, feedback/events).
2. Build device transport/session handling in `Device_Name_Transport.cs`.
3. Implement command format + response parsing in `Device_Name_Protocol.cs`.
4. Connect lifecycle, actions, and feedback publication in `Device_Name.cs`.
5. Finalize runtime settings/metadata in `Settings_Data.cs` and `Device_Name.json`, and add UI translation strings in `en-US.json`.
6. Finalize UI metadata in `UiDefinition.xml`.
7. Validate on hardware (or an emulator) and iterate on timing/error-handling edge cases.
8. Perform end-of-cycle packaging/manifest steps after functional behavior is complete.

## Immediate next step from the docs provided

Using the "Create a Project," "Extension Drivers," "UI Files," and full SDK API reference, we can now begin implementation by replacing template placeholders with concrete driver identity values and scaffolding required driver classes/methods directly against the API docs before filling in transport/protocol specifics.

## Device details needed before coding

- Manufacturer + exact model number(s).
- Firmware version(s) to support.
- Official protocol/API reference for the device.
- Required commands/feedback for v1.
- Known quirks (rate limits, login/auth flow, warmup timing, unsolicited feedback behavior).
