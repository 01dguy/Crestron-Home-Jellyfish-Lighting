# CODEX_CONTEXT.md

## Purpose

This file gives Codex the project-specific context it needs to work effectively on this repository without relying on prior chat history.

Codex should read this file before making changes.

---

## Project Summary

This repository contains a **Crestron Home extension driver** for **Jellyfish Lighting**.

The driver communicates with a Jellyfish Lighting LAN controller over **WebSocket** and exposes device status and controls to Crestron Home.

This project is developed with a split workflow:

* **Edit/refactor** in VS Code, Codespaces, or Codex local
* **Build/package/final verify** in Visual Studio 2022 using the Crestron SDK installed locally
* **GitHub** is the source of truth for the repo

---

## Codex Working Rules

### Use local/workspace-attached context only

Codex should work from the **real opened workspace** and not from a cloud sandbox snapshot unless explicitly requested.

Preferred context:

* local VS Code repo
* local Codex CLI from repo root
* Codespaces attached to the real `/workspaces/...` repo

Avoid relying on:

* Codex Cloud task snapshots
* synthetic branches like `work`
* isolated cloud runtimes with missing remotes

### Git / branch safety

* Do **not** push changes without the user's review/approval.
* Do **not** create PRs unless explicitly asked.
* Do **not** change branches unless explicitly asked.
* Before major edits, confirm:

  * current working directory
  * current branch
  * git status
  * git remotes

### Repo-first context

Do not assume prior chat history is available.
Use the repository contents, docs, and this file as the source of project memory.

---

## Core Architecture

This project uses a **transport/protocol split**.

### Transport layer

Primary responsibility:

* manage WebSocket connectivity
* connect/disconnect lifecycle
* reconnect behavior
* raw JSON send/receive

Known transport class:

* `Jellyfish_Lighting_Transport : ATransportDriver`

Transport responsibilities include:

* maintaining socket state
* reconnect attempts
* handling inbound JSON
* reporting connection events/errors upward

Important implementation details remembered from development:

* reconnect backoff seconds: **1, 2, 5**
* transport has flags/state such as connection tracking and reconnect guards
* WebSocket connectivity has been a recurring debugging focus

### Protocol layer

Primary responsibility:

* application-level logic
* polling
* parsing Jellyfish JSON responses
* mapping values to Crestron Home properties/UI

Known protocol class:

* `Jellyfish_Lighting_Protocol : ABaseDriverProtocol, IDisposable`

Protocol responsibilities include:

* request pattern list
* request zone list
* handle scene/brightness/speed related data
* update driver properties exposed to Crestron Home

---

## Connection / API Assumptions

### WebSocket transport

Use:

* **`ws://` only**

Do not assume:

* `wss://`

### Default controller port

Use:

* **port 9000**

### Poll interval

Expected default:

* **60 seconds**

Minimum enforced in prior work:

* **10 seconds**

---

## Known Jellyfish API command patterns

These commands were used during development and should be treated as important known-good patterns unless the code says otherwise.

### Get pattern file list

```json
{"cmd":"toCtlrGet","get":[["patternFileList"]]}
```

### Get zone list

```json
{"cmd":"toCtlrGet","get":[["zones"]]}
```

There are also command flows related to:

* runPattern
* brightness changes
* speed changes
* zone/pattern summaries

Codex should inspect the actual code for the current exact implementations before changing these.

---

## Important Driver/UI Rules

### UiDefinition.xml is the contract

Treat `UiDefinition.xml` as the **contract** between the Crestron Home UI and the driver code.

That means:

* property keys must match exactly
* command keys must match exactly
* UI bindings must match exactly
* if UI behavior is broken, always verify the XML bindings against the driver properties first

This is a critical rule for this project.

### Translation/UI content

Translation files and UI definition files are required for packaging and runtime UI.

Typical required included assets:

* `UiDefinition.xml`
* translation files such as `en-US.json`

---

## Packaging / Build Notes

This project is ultimately built and packaged in **Visual Studio 2022** using the Crestron SDK.

Typical local SDK path used by the user:

* `C:\crestron_drivers_sdk\Libraries`

### Important packaging requirements from prior work

The following have previously caused packaging/build issues and should be treated carefully:

* driver manifest/programming JSON must be embedded as an **Embedded Resource**
* `UiDefinition.xml` must be included correctly for packaging
* translation files must be included correctly for packaging
* `IncludeInPkg` content matters
* `ManifestUtil` is used to generate the `.pkg`

Codex should avoid casually changing packaging/resource settings unless necessary and should preserve working packaging structure.

---

## Known Debugging Workflow

The user has an established debugging workflow for Crestron Home drivers.

Useful tools/workflows include:

* Crestron Toolbox console logging
* `CrestronConsole.PrintLine(...)`
* `ErrorLog` messages
* console commands for driver diagnostics
* `pyng rpc extensions list`
* driver property inspection workflows

Codex should preserve or improve observability where practical.

---

## Known Real-World Testing Context

This driver has already been tested against a real Crestron Home system and Jellyfish controller.

Previously verified areas include:

* WebSocket connectivity to controller
* property feedback such as:

  * `StatusText`
  * `ZoneSummary`
  * `ActiveScene`
  * `Brightness`
  * `Speed`
* UI binding behavior through `UiDefinition.xml`

That means this is not purely a scaffold project; it already has real integration history.

---

## Persistent Project Pain Points / Known Issues

Codex should keep these in mind when analyzing bugs or proposing refactors:

1. **WebSocket persistence / reconnect behavior**

   * reconnect logic has been a major focus
   * avoid designs that create duplicate reconnect loops
   * avoid unstable socket lifecycle behavior

2. **Startup state population**

   * some fields do not populate immediately on initial driver load
   * driver may need explicit startup requests to fetch current controller state

3. **Online/offline truthfulness**

   * prior behavior showed the system appearing online even when the configured IP was wrong
   * connection status reporting should reflect reality

4. **Settings/UI persistence quirks**

   * programmer-entered/user-visible IP field behavior has been problematic before

5. **Do not casually switch protocol/security assumptions**

   * this project intentionally targets `ws://` and port `9000`

---

## Preferred Codex Behavior on This Repo

When asked to help, Codex should generally:

1. inspect existing code first
2. preserve current architecture unless change is justified
3. make targeted edits rather than broad rewrites
4. state uncertainty explicitly instead of guessing
5. keep Crestron Home constraints in mind
6. avoid introducing unnecessary abstractions
7. avoid changing packaging/resource metadata unless required

For this project, **surgical and grounded** is better than clever.

---

## Suggested Session Bootstrap

At the start of a new Codex session, use a prompt like:

```text
Read README.md, docs/ARCHITECTURE.md, and docs/CODEX_CONTEXT.md first.
Work only from this current workspace.
Do not use Codex Cloud for this task.
Do not fetch, push, create PRs, or change branches without approval.
Before making changes, tell me:
- current working directory
- current branch
- git status --short
- git remote -v
```

---

## Notes for Future Documentation

If project docs are missing or incomplete, Codex may help create or improve:

* `README.md`
* `docs/ARCHITECTURE.md`
* troubleshooting notes
* packaging/build notes
* API examples from real controller responses

But documentation should be based on the **actual current repo state**, not assumptions from old cloud tasks.

---

## Final Reminder

This project should be treated as a real hardware-integrated Crestron Home driver with existing design decisions and a preferred workflow.

The main priority is:

* work from the real repo
* preserve stable behavior
* keep changes reviewable
* do not push without approval
