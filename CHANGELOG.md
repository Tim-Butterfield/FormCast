# Changelog

All notable changes to FormCast are documented in this file.

The format follows [Keep a Changelog 1.1](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html).

See [README.md](README.md) for the feature overview and install instructions.

## [1.1.2] -- 2026-06-01

### Fixed

- **NUMERICUPDOWN and TRACKBAR value get**: `@FORMGET` for the `value`
  property now reads the live control's current value instead of the
  initial descriptor value, so changes the user makes in the form are
  reflected. (issue #5)

### Changed

- **Release zip**: the release workflow now publishes an identically
  named `FormCast.zip` alongside the versioned `FormCast-v<tag>.zip`, so
  the version-independent
  `releases/latest/download/FormCast.zip` URL resolves to the latest
  release.

## [1.1.1] -- 2026-05-27

### Fixed

- **Path normalization in template I/O**: `@FORMLOAD`, `@FORMIMPORT`,
  `@FORMSAVE`, `@FORMSAVEIMAGE`, and `@FORMSAVECOMPOSITE` now normalize
  paths via `Path.GetFullPath()` before calling .NET file APIs, handling
  double backslashes, SUBST drives, and mapped shares without requiring
  `%@truename[]` on the BTM side. (issue #1)
- **CHECKBOX and RADIO pre-show property**: `checked` property is now
  read from the descriptor property bag during control realization, so
  `@FORMSET` before `@FORMSHOW` works correctly. (issues #2, #4)
- **DATETIMEPICKER value get/set**: new `value` property for setting
  and reading the date (ISO 8601 format) both before and after
  `@FORMSHOW`. (issue #3)
- **RADIO checked get/set**: `@FORMGET` and `@FORMSET` for the `checked`
  property now work on RADIO controls, matching CHECKBOX and TOGGLE
  behavior. (issue #4)
- **Test suite**: catch `BadImageFormatException` in P/Invoke error
  handlers so all 579 tests pass regardless of test runner bitness.

### Changed

- **Centralized version**: version number moved to `Directory.Build.props`
  so all projects inherit from one place.

## [1.1.0] -- 2026-05-27

### Fixed

- **Unload no longer kills persistent TCC sessions**: `formcast-check.btm`
  now checks `%_transient` instead of `%_parent` containing `explorer`,
  so only double-click-spawned windows auto-close on unload.
- **Template loading on SUBST/mapped drives**: all `@FORMLOAD` calls in
  example BTMs now resolve paths through `@truename` before passing to
  .NET, matching the pattern already used for `plugin /l`.

### Changed

- **Flat release zip**: DLLs and `FormCast.Host.exe` are now in the zip
  root instead of a `bin/` subfolder. Install docs updated to match
  (e.g. `C:\FormCast\FormCast.dll` instead of `C:\FormCast\bin\FormCast.dll`).

## [1.0.0] -- 2026-04-22

First public release.

### Added

- **Control surface**: 39 control types including TOGGLE, MENUSTRIP,
  TOOLBAR, STATUSBAR, CONTEXTMENU, SEPARATOR, TABCONTROL, DATAGRID,
  TREEVIEW, SPLITCONTAINER, FLOWPANEL, TABLEPANEL, WEBBROWSER, and
  RICHMEMO. 6 common dialogs: Open File, Save File, Browse Folder,
  Color Picker, Font Picker, Task Dialog.
- **Layout managers**: `absolute`, `flow`, `grid`, `dock`, with nested
  `PANEL` containers that carry their own layout.
- **Events**: `FORMEVENTS` polling pipe; `@FORMBIND` declarative event
  bindings (click, change, focus, blur, dblclick, keypress, close);
  the `events_pending` bit for `ON CONDITION`-driven polling.
- **Modal forms** via `@FORMSHOW[h,modal]` with optional timer
  auto-dismiss.
- **FORMPIPE** streaming command for piping command output into a MEMO
  or RICHMEMO control (`dir /b | FORMPIPE %h memo`).
- **JSONC templates** with `${var}` substitution, `_bind.click` props
  activated by `@FORMAPPLYBINDINGS`, and a JSON Schema for editor
  validation.
- **Appearance**: `backcolor`, `forecolor`, `font`, and `theme`
  (system/dark/light) with DWM dark title bar; `anchor` for resize
  behavior; 216 stock icons across 16 categories.
- **Standalone app mode** via `myapp.btm /app` and `@FORMCONSOLE[hide]`,
  with zero-flash desktop-shortcut support.
- **Visual designer** (Toolbox / Canvas / Properties) with multi-select,
  align / distribute / same-size, undo / redo, cut / copy / paste,
  context menus, keyboard shortcuts, and JSONC save/load.
- **`FormCast.Host.exe`** scaffolding for cross-process `Global\`
  form handles (`IRemoteFormRegistry` integration in v1.x).
- **Forced-shutdown contract**: `plugin /u FormCast` closes every
  realized window before returning.
- **Test coverage**: 579 xUnit tests across the plugin and host; a
  119-case TCC integration + smoke suite covering the designer,
  templates, events, and plugin lifecycle.

[1.1.1]: https://github.com/Tim-Butterfield/FormCast/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Tim-Butterfield/FormCast/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Tim-Butterfield/FormCast/releases/tag/v1.0.0
