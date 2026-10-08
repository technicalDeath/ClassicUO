# Britannia Renaissance ClassicUO Client Roadmap

This document tracks planned player-client changes for the Britannia Renaissance fork at
`technicalDeath/ClassicUO`. It is not the upstream ClassicUO project roadmap. Implementation
belongs in this repository; shard and server behavior remains owned by its corresponding
repository in the wider Britannia Renaissance workspace.

A roadmap entry describes intent, not shipped behavior. Do not present an entry as available to
players until its implementation, verification, release build, and player-facing documentation are
complete.

## Statuses

| Status | Meaning |
| --- | --- |
| Proposed | The need has been recorded, but scope or design is not yet approved. |
| Planned | The design is approved and decision-complete, but implementation has not started. |
| In Progress | Implementation or verification is underway. |
| Complete | The change is implemented, verified, included in the player build, and documented. |
| Deferred | Work is intentionally postponed; the entry remains for future reconsideration. |

## Roadmap

| Order | Change | Status | Player outcome |
| --- | --- | --- | --- |
| 1 | Default-on, configurable VSync | Planned | Prevent screen tearing by default without requiring a graphics-driver override, while preserving an opt-out. |

## 1. Default-on, configurable VSync

**Status:** Planned

**Implementation repository:** `technicalDeath/ClassicUO`

### Goal and current behavior

ClassicUO currently calls `SetVSync(false)` during startup in
[`GameController.cs`](src/ClassicUO.Client/GameController.cs), overriding FNA's default vertical
synchronization behavior. That requests immediate presentation and can produce horizontal tearing;
players currently have to compensate with a per-application NVIDIA or other graphics-driver
setting.

Enable VSync automatically for new and existing installations, while providing an in-client option
for players whose hardware, input-latency preferences, or frame-pacing behavior requires it to be
disabled.

### Planned implementation

- Add a global `bool` setting serialized as `"vsync"` in
  [`Settings.cs`](src/ClassicUO.Client/Configuration/Settings.cs), with a default value of `true`.
  Deserializing an existing settings file that does not contain the property must also produce
  `true`; an explicit `false` must remain respected and survive subsequent saves.
- Replace the hardcoded startup disable with the loaded global setting. Apply it before initial
  graphics-device creation so FNA selects synchronized presentation for the first frame.
- Support live changes after startup. Changing the value through the UI must update the graphics
  manager and apply the device change only after a graphics device exists, avoiding premature
  device creation from the game constructor.
- Add an **Enable VSync** checkbox to Options -> Video in
  [`OptionsGump.cs`](src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs). Initialize it from the
  global setting, set it to checked when restoring Video defaults, and apply it through the existing
  Apply/OK workflow.
- Add the checkbox text to
  [`ResGumps.resx`](src/ClassicUO.Client/Resources/ResGumps.resx) and regenerate its strongly typed
  resource accessor.
- Keep VSync independent from the existing configurable FPS limiter. Actual presentation may be
  capped by the monitor's refresh rate while VSync is enabled.
- Update the Britannia Renaissance workspace-level `PLAYER_GUIDE.md` so screen-tearing guidance
  points players to Options -> Video instead of requiring an NVIDIA Control Panel or other
  graphics-driver override.
- Publish a Windows x64 release into `bin/dist` using
  [`scripts/build-naot.sh`](scripts/build-naot.sh). The existing Britannia Renaissance player and
  administrator launchers already run the client from that directory.

### Configuration interface

```json
{
  "vsync": true
}
```

The setting is global rather than character-specific because it controls the application's graphics
device. No server protocol, gameplay, account, character-profile, or launcher-argument changes are
planned.

### Verification and acceptance criteria

- Add configuration tests under [`tests/ClassicUO.UnitTests`](tests/ClassicUO.UnitTests) proving:
  - a new settings instance defaults VSync to enabled;
  - JSON without `vsync` defaults it to enabled; and
  - an explicit disabled value survives serialization and deserialization.
- Run the complete ClassicUO unit-test suite successfully.
- Build and publish the Windows x64 release successfully.
- With an existing settings file that lacks `vsync`, verify that the client starts with the checkbox
  checked and synchronized presentation enabled.
- Disable VSync, select Apply or OK, verify that the change takes effect, restart the client, and
  verify that the disabled choice persists.
- Re-enable VSync without restarting and confirm that visible tearing is removed without a driver
  override.
- Exercise both windowed and borderless modes after each live change and confirm that rendering,
  resizing, and input continue to work.
- Update this entry to **Complete** only after the tested Windows player build is in `bin/dist` and
  the player guide describes the shipped behavior accurately.

### Compatibility and rollback

VSync can increase input latency or expose hardware/driver-specific frame-pacing behavior, so the
player opt-out is required. If synchronized presentation causes a regression, setting `"vsync"` to
`false` or clearing the checkbox must restore the current immediate-presentation behavior without a
server or data migration.

### Completion evidence

- [ ] Source and resource changes reviewed.
- [ ] Configuration compatibility tests added and passing.
- [ ] Full unit-test suite passing.
- [ ] Windows x64 release published to `bin/dist`.
- [ ] Windowed and borderless manual checks complete.
- [ ] Existing-settings upgrade and disabled-setting persistence verified.
- [ ] Britannia Renaissance player guide updated.

## Future entry template

Copy this section for each future client change and add the entry to the roadmap table above.

### N. Change name

**Status:** Proposed

**Implementation repository:** `technicalDeath/ClassicUO`

#### Goal

State the problem and the intended outcome without implying that it is already available.

#### Player impact

Describe what players will see, which default behavior changes, and whether an opt-out or migration
is required.

#### Implementation outline

Identify the minimum client subsystems, settings, resources, build integration, and documentation
that must change. Record new configuration or public interfaces explicitly.

#### Compatibility concerns

Document existing-settings behavior, platform or graphics-backend differences, rollback behavior,
and any interaction with upstream ClassicUO changes.

#### Verification and acceptance criteria

List automated tests, release builds, representative manual scenarios, and the objective conditions
required to mark the entry Complete.

#### Completion evidence

- [ ] Implementation reviewed.
- [ ] Automated tests passing.
- [ ] Supported release builds produced.
- [ ] Manual scenarios verified.
- [ ] Player-facing documentation updated.
