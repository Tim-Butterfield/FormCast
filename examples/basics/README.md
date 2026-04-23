# FormCast basics

Self-contained example BTM scripts. Each exercises one feature and
is documented inline so the whole script reads like a tutorial step.
Start with `00-everything.btm` to see every control type at a glance.

Run any of them after `plugin /l C:\path\to\FormCast.dll`:

```
plugin /l C:\path\to\FormCast.dll
call 01-hello.btm
```

| File | What it shows | Controls used |
|---|---|---|
| [`00-everything.btm`](00-everything.btm) | Every visual control type on one form: labels, edits, buttons, checkboxes, radios, groupbox, numeric/domain spinners, date picker, progress bar, trackbar, combobox, listbox, checked listbox, listview, treeview, memo, masked textbox, picture box, panel, tabs, calendar, scrollbars, datagrid, status bar. A visual reference sheet. | All 39 types |
| [`01-hello.btm`](01-hello.btm) | The simplest possible dialog: label + edit + OK button. Reads the textbox value back on click. | LABEL, EDIT, BUTTON |
| [`02-confirm.btm`](02-confirm.btm) | A native yes/no/cancel task dialog with the question icon. The dispatch returns the clicked button index. | `@FORMTASKDIALOG` |
| [`03-progress.btm`](03-progress.btm) | A progress bar driven from a `do while` loop, walking from 0 to 100 in 10 steps. | LABEL, PROGRESSBAR |
| [`04-file-picker.btm`](04-file-picker.btm) | Navigable file picker: folders show first with `[DIR]`, double-click to navigate, `..` to go up. Path label updates on every navigation. Double-click a file or click Open to pick it. | LISTVIEW with text/size/date columns, `FORMEVENTS dblclick` |
| [`05-settings.btm`](05-settings.btm) | A multi-control "Preferences" dialog: text field + radios + checkboxes + OK / Cancel. Reads every value back on OK. | LABEL, EDIT, RADIO, CHECKBOX, BUTTON |
| [`06-template.btm`](06-template.btm) | Build a form, save it to a JSONC template via `@FORMSAVE`, reload it via `@FORMLOAD`, verify the round trip. | `@FORMSAVE`, `@FORMLOAD` |
| [`07-events.btm`](07-events.btm) | Stream events from a live form through `do ev in /p formevents` and parse them with `@word`. | `FORMEVENTS` streaming command |
| [`08-memo-notepad.btm`](08-memo-notepad.btm) | A simple multiline text editor. Type a note, click Save, the text is read back. | MEMO |
| [`09-modal-dialog.btm`](09-modal-dialog.btm) | A modal dialog that blocks the script until the user clicks OK or a 3-second auto-dismiss timer fires. | Modal `@FORMSHOW` |
| [`10-designer-drag.btm`](10-designer-drag.btm) | Interactive drag-to-move in design mode. Click a control to select (red dashed border). Drag to move. Final positions committed to the descriptor. | `design_mode=1`, `DesignModeHandler` |
| [`11-pipe-output.btm`](11-pipe-output.btm) | Pipe command output (`dir /b`) into a MEMO or RICHMEMO control via `FORMPIPE`. Each line of stdout becomes a line in the control. RICHMEMO variant adds color. | `FORMPIPE` command, MEMO, RICHMEMO |
| [`12-log-viewer.btm`](12-log-viewer.btm) | Two-window workflow: pick a file from a LISTVIEW, then view its contents in a RICHMEMO with per-line syntax coloring (errors red, warnings orange). | LISTVIEW + RICHMEMO, multi-window |
| [`13-ide-layout.btm`](13-ide-layout.btm) | IDE-shaped layout with nested PANELs: toolbar (New/Open/Save), sidebar LISTVIEW file browser, center MEMO editor, status bar. Click Open to load a file into the editor. | Nested PANELs, LISTVIEW, MEMO, multi-panel |
| [`14-tabbed-settings.btm`](14-tabbed-settings.btm) | Three-tab preferences dialog with GROUPBOX radio grouping, COMBOBOX dropdown, NUMERICUPDOWN spinner, and DATETIMEPICKER. Shows two independent radio groups working correctly via separate GROUPBOXes. | TABCONTROL, TABPAGE, GROUPBOX, COMBOBOX, NUMERICUPDOWN, DATETIMEPICKER |
| [`15-app-window.btm`](15-app-window.btm) | Full application window: menu bar (File/Help), toolbar (New/Open/Save), MEMO editor, status bar, system tray icon. The "BTM as a Windows app" pattern. Uses @FORMOPENDIALOG, @FORMSAVEDIALOG, @FORMTASKDIALOG, @FORMNOTIFY, and tooltips. | MENUSTRIP, TOOLBAR, STATUSBAR, MEMO, @FORMNOTIFY, tooltip |
| [`16-icon-browser.btm`](16-icon-browser.btm) | Interactive stock icon browser. Enumerates all FormCast stock icons via FORMICONS, displays them in a categorized grid, and shows usage instructions when clicked. | FORMICONS, SPLITCONTAINER, PANEL, dynamic control creation, stockicon |

## What's NOT here

These scripts intentionally stay narrow. For deeper material:

* **MEMO and RICHMEMO**: see the [Tutorial](../../docs/Tutorial.md)
  section 8 for the read-only / styled-text patterns.
* **Nested PANEL layouts**: see
  [`../screenshots/capture-readme-images.btm`](../screenshots/capture-readme-images.btm)
  for the IDE-shaped layout (toolbar + sidebar + editor + status).
* **The visual designer**: see
  [`../designer/formcast-visual-designer.btm`](../designer/formcast-visual-designer.btm)
  for the interactive visual designer and the
  [Designer Guide](../../docs/DesignerGuide.md) for the conceptual
  walkthrough.
* **JSONC templates with `${var}` substitution**: see
  [`../templates/`](../templates/) for the four canonical template
  shapes and the [Template Reference](../../docs/TemplateReference.md)
  for the schema.

## Running them under the headless test harness

Set `FORMCAST_HEADLESS=1` to suppress every visible window. The
scripts will still run their full logic; nothing pops up. This is
useful for CI smoke tests:

```
set FORMCAST_HEADLESS=1
plugin /l C:\path\to\FormCast.dll
call 01-hello.btm
```

Examples that depend on user interaction (01, 02, 04, 05, 07)
will print "scaffold suppressed" markers instead of actually
showing dialogs and will exit after the first cycle.
