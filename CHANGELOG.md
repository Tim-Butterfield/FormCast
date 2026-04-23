# Changelog

All notable changes to FormCast are documented in this file.

The format follows [Keep a Changelog 1.1](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html).

See [README.md](README.md) for the feature overview and install instructions.

## [Unreleased]

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

[Unreleased]: https://github.com/Tim-Butterfield/FormCast/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Tim-Butterfield/FormCast/releases/tag/v1.0.0
