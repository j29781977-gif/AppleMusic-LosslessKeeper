# Changelog

## 1.2.0

* **Instant window**: default mode is now `direct` — Apple Music opens at its
  normal speed (measured 1.19–1.24 s) and the setting is applied in the
  background; the window is never moved or hidden.
* **Portable**: the MSIX package folder and the music library are discovered at
  runtime instead of being hard-coded, so the tool works on any machine with
  Apple Music for Windows.
* **Localization**: UI labels are matched case-insensitively by substring from
  `src/strings.json` (11 locales), with a structural fallback (4th switch,
  first combo box, last dropdown entry) for languages that are not listed.
* **Compiled in memory**: `Keeper.ps1` builds the C# in memory, so the release
  contains no DLL and no compiler output.
* **Window guard**: if the app parks its own window off screen (observed at
  `-21333,-21333`), it is moved back within ~15 ms; documents the stale window
  position/state in the app's settings hive are repaired too.
* Added `Switch-Mode.cmd` and `Edit-Config.cmd`.

## 1.1.0

* `stealth` mode: the window is moved off screen before it is painted, kept there
  with a WinEvent-hook driven guard while the setting is applied, and moved back
  afterwards — 0–1 frames of exposure.
* Content-loaded gate so the sidebar menu is opened once instead of on every
  retry.
* Per-attempt retry loop, timings in the log (`--verbose`).

## 1.0.0

* First working version: window hook, UI Automation driven change of
  `Lossless audio` + `Streaming quality`, install/uninstall, logon autostart.
