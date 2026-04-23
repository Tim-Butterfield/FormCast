# FormCast FAQ

Answers to the questions you'll hit first, organized by category.
Each answer includes a short explanation and links to the detailed
docs.

## Categories

- [Getting started](#getting-started)
- [Controls and layout](#controls-and-layout)
- [Events and interaction](#events-and-interaction)
- [Data and templates](#data-and-templates)
- [Piping and streaming](#piping-and-streaming)
- [Visual designer](#visual-designer)
- [Troubleshooting](#troubleshooting)
- [Advanced topics](#advanced-topics)

---

## Getting started

### How do I install the plugin?

Copy `FormCast.dll` into your TCC plugins directory, or load it on
demand from any path:

```
plugin /l C:\path\to\FormCast.dll
```

See the [Quickstart](Quickstart.md) for the full walkthrough.

### How do I verify the plugin loaded correctly?

Check `@formversion[]` — it returns the version string when loaded,
or empty when not:

```
set ver=%@formversion[]
iff "%ver" == "" then
  echo Plugin not loaded!
endiff
```

Every [example BTM](../examples/basics/) includes this guard clause.

### What's the simplest possible FormCast script?

[`01-hello.btm`](../examples/basics/01-hello.btm) — a label, a text
box, and an OK button in 15 lines. See also the [Quickstart](Quickstart.md).

### What control types are available?

39 types covering basic input (including TOGGLE), numeric/date,
lists/trees/grids, text, containers, app chrome (including
SEPARATOR for toolbars), and specialized controls. See the
[Function Reference](FunctionReference.md#build) for the categorized
list including MENUSTRIP, TOOLBAR, STATUSBAR, CONTEXTMENU,
DATAGRID, TREEVIEW, WEBBROWSER, and more.

---

## Controls and layout

### How do I create a form with multiple controls?

Call `@FORMADD` once per control, passing the control type, position,
and size. See [`05-settings.btm`](../examples/basics/05-settings.btm)
for a dialog with labels, text fields, radios, checkboxes, and buttons.

### How do I make a multi-line text box?

Use the `MEMO` control type instead of `EDIT`. It's a `TextBox` with
`Multiline=true`. See [`08-memo-notepad.btm`](../examples/basics/08-memo-notepad.btm).

### How do I create a read-only text display?

Set the `readonly` prop on a `MEMO` or `RICHMEMO`:

```
%@formset[%h,memo,readonly,1]
```

### How do I show a sortable list of items with columns?

Use `LISTVIEW` with `addcolumn` (name, width, and sort type) then
`additem` for each row. Column types (`text`, `number`, `date`,
`size`) control how sorting works. See
[`04-file-picker.btm`](../examples/basics/04-file-picker.btm) for a
complete navigable file picker.

### How do I nest controls inside a panel?

Use the slash syntax in the control id: `panel1/btn` adds `btn` as a
child of `panel1`. The parent must be a `PANEL` type. See the
[Architecture doc](Architecture.md) and the nested-layout screenshot
for the IDE-shaped pattern (toolbar + sidebar + editor + status bar).

### How do I show a progress bar?

Use the `PROGRESSBAR` control type with `min`, `max`, and `value`
props. See [`03-progress.btm`](../examples/basics/03-progress.btm).

### How do I make a dropdown selection list?

Use the `COMBOBOX` control type with `additem` for each option:

```
%@formadd[%h,cmb,COMBOBOX,100,12,120,24,]
%@formset[%h,cmb,style,list]
%@formset[%h,cmb,additem,Small]
%@formset[%h,cmb,additem,Medium]
%@formset[%h,cmb,additem,Large]
%@formset[%h,cmb,selectedindex,1]
```

Read the selection with `%@formget[%h,cmb,selecteditem]`. See
[Function Reference: COMBOBOX](FunctionReference.md#combobox).

### How do I group radio buttons so multiple groups work independently?

Wrap each group in a `GROUPBOX`. Without GroupBox, WinForms treats
all RadioButtons on the same parent as one group — selecting one
deselects all others:

```
%@formadd[%h,grpTheme,GROUPBOX,12,12,180,80,Theme]
%@formadd[%h,grpTheme/rLight,RADIO,12,22,80,20,Light]
%@formadd[%h,grpTheme/rDark,RADIO,12,46,80,20,Dark]

%@formadd[%h,grpLang,GROUPBOX,200,12,180,80,Language]
%@formadd[%h,grpLang/rEN,RADIO,12,22,80,20,English]
%@formadd[%h,grpLang/rFR,RADIO,12,46,80,20,French]
```

Now selecting "Dark" doesn't deselect "English". See
[`14-tabbed-settings.btm`](../examples/basics/14-tabbed-settings.btm).

### How do I create a tabbed dialog?

Use `TABCONTROL` with `TABPAGE` children:

```
%@formadd[%h,tabs,TABCONTROL,8,8,400,260,]
%@formadd[%h,tabs/general,TABPAGE,0,0,0,0,General]
%@formadd[%h,tabs/general/lbl,LABEL,12,12,200,20,Some option]
%@formadd[%h,tabs/advanced,TABPAGE,0,0,0,0,Advanced]
```

See [`14-tabbed-settings.btm`](../examples/basics/14-tabbed-settings.btm).

### How do I add a numeric spinner?

Use `NUMERICUPDOWN` with `min`, `max`, and `value` props:

```
%@formadd[%h,nud,NUMERICUPDOWN,100,12,60,24,]
%@formset[%h,nud,min,1]
%@formset[%h,nud,max,100]
%@formset[%h,nud,value,4]
```

### How do I add a date picker?

Use `DATETIMEPICKER`:

```
%@formadd[%h,dtp,DATETIMEPICKER,100,12,200,24,]
%@formset[%h,dtp,format,short]
```

### How do I show colored or styled text?

Use `RICHMEMO` (a WPF RichTextBox) with the live-operation props:

```
%@formset[%h,rm,appendcolor,Error: file not found|Red]
%@formset[%h,rm,appendstyle,Build complete|bold]
%@formset[%h,rm,loadrules,ERROR|Red,WARN|Orange]
```

See [Function Reference: RICHMEMO](FunctionReference.md#memo-and-richmemo).

---

## Events and interaction

### How do I respond to a button click?

Use `@FORMBIND` to register a TCC command that runs when the event
fires:

```
%@formbind[%h,btnOK,click,gosub :on_ok]
```

The bound command runs on a worker thread so the script can call back
into `@FORM*` functions without deadlocking. See
[`01-hello.btm`](../examples/basics/01-hello.btm).

### What events can I bind to?

`click`, `change`, `focus`, `blur`, `keypress`, `dblclick`, `close`,
`mousedown`, `mouseup`, `mouseenter`, `mouseleave`, `keydown`,
`keyup`, `resize`, `dragenter`, `dragdrop`, `scroll`, `columnclick`,
`cellclick`, `beforeexpand`, `afterexpand`, `beforecollapse`,
`aftercollapse`. See
[Function Reference: @FORMBIND](FunctionReference.md#state-and-events).

### How do I wait for user input without busy-looping?

Two patterns:

**Pattern 1: `do forever` + bound commands** (most common)

```
%@formbind[%h,btn,click,gosub :done]
%@formshow[%h]
do forever
  delay 1
enddo
```

**Pattern 2: `on condition` polling the events_pending bit**

```
on condition %@formstate[%h] == 35 gosub :handle_event
```

Bit 32 = events_pending. See
[`07-events.btm`](../examples/basics/07-events.btm) and
[Function Reference: @FORMSTATE](FunctionReference.md#state-and-events).

### How do I show a modal dialog that blocks until the user responds?

Use `@FORMSHOW[%h, modal]` for wait-forever or `@FORMSHOW[%h, modal:3000]`
for a 3-second auto-dismiss. The return value is the `DialogResult`
integer. See [`09-modal-dialog.btm`](../examples/basics/09-modal-dialog.btm).

### How do I show a simple yes/no/cancel confirmation?

Use `@FORMTASKDIALOG` — no need to build a form:

```
set rc=%@formtaskdialog[Delete file?,Are you sure?,yesnocancel,question]
```

Returns 0=yes, 1=no, 2=cancel. See
[`02-confirm.btm`](../examples/basics/02-confirm.btm).

### How do I read back what the user typed or selected?

Use `@FORMGET`:

```
set name=%@formget[%h,txtName,text]
set picked=%@formget[%h,lst,selecteditem]
```

### How do I navigate folders in a LISTVIEW file picker?

Bind `dblclick` on the LISTVIEW, read `selecteditem`, check `isdir`,
call `cd` + repopulate. See
[`04-file-picker.btm`](../examples/basics/04-file-picker.btm) for the
full navigable pattern.

---

### How do I run a BTM as a standalone Windows app (no visible console)?

1. Set the `FORMCAST_DLL` environment variable (once, in your
   TCC startup or system environment):
   ```
   set FORMCAST_DLL=C:\path\to\bin\FormCast.dll
   ```

2. Add the `/app` pattern at the top of your BTM (after loading
   the plugin):
   ```btm
   call formcast-check.btm load
   if "%1" == "/app" set RC=%@formconsole[hide]
   ```

3. The `formcast-check.btm` shared helper loads the plugin (or
   verifies it is already loaded), and handles unload/exit cleanup.
   When `/app` is passed, `@FORMCONSOLE[hide]` hides the TCC
   console window. Everything runs in a single TCC process.

**From TCC** (normal): `myapp.btm` -- runs in the current session.

**As a standalone app**: `myapp.btm /app` -- hides the TCC console.
The user sees only the FormCast window.

**Desktop shortcut**: For zero-flash launch, set the Target to:
`"C:\path\to\tcc.exe" /c start /inv /pgm "C:\path\to\tcc.exe" /c "myapp.btm" /app`,
Run "Minimized". The shortcut must target `tcc.exe /c` -- pointing
directly at a `.btm` file will not pass arguments correctly.

See [`15-app-window.btm`](../examples/basics/15-app-window.btm),
[`formcast-check.btm`](../examples/formcast-check.btm), and
[Function Reference: @FORMCONSOLE](FunctionReference.md#formconsoleaction).

### How do I set a custom icon on the title bar and taskbar?

Set the `icon` prop on the form before showing it:

```
%@formset[%h,.,icon,C:\path\to\myapp.ico]
```

Forms are hidden from the taskbar by default (most usage is utility
dialogs under TCC). For standalone app windows, opt in:

```
%@formset[%h,.,showintaskbar,1]
```

### How do I add a menu bar (File / Edit / View)?

Use `MENUSTRIP` with nested children via the slash-id syntax:

```
%@formadd[%h,menu,MENUSTRIP,0,0,0,0,]
%@formadd[%h,menu/file,LABEL,0,0,0,0,File]
%@formadd[%h,menu/file/open,LABEL,0,0,0,0,Open...]
%@formadd[%h,menu/file/exit,LABEL,0,0,0,0,Exit]
%@formbind[%h,exit,click,gosub :on_exit]
```

Text `"-"` creates a separator. See
[Function Reference: MENUSTRIP](FunctionReference.md#menustrip-app-menu-bar).

### How do I add a toolbar?

Use `TOOLBAR` with button children:

```
%@formadd[%h,tb,TOOLBAR,0,0,0,0,]
%@formadd[%h,tb/btnNew,BUTTON,0,0,0,0,New]
%@formadd[%h,tb/sep1,LABEL,0,0,0,0,-]
%@formadd[%h,tb/btnOpen,BUTTON,0,0,0,0,Open]
```

### How do I add a status bar?

Use `STATUSBAR` with label children:

```
%@formadd[%h,sb,STATUSBAR,0,0,0,0,]
%@formadd[%h,sb/msg,LABEL,0,0,0,0,Ready]
%@formset[%h,sb/msg,spring,1]
```

Set `spring=1` on a panel to make it fill remaining space.

### How do I put an icon in the system tray?

```
%@formnotify[show,My App]
%@formnotify[balloon,Alert,Build complete,info]
%@formnotify[hide]
```

### How do I add a right-click menu?

Use `CONTEXTMENU` the same way as `MENUSTRIP`.

### How do I show a native Open File dialog without building a form?

Use `@FORMOPENDIALOG` — one function call:

```
set file=%@formopendialog[Select a file,Text files:*.txt:All files:*.*]
iff "%file" != "" echo You picked: %file
```

Also available: `@FORMSAVEDIALOG`, `@FORMFOLDERDIALOG`,
`@FORMCOLORDIALOG`, `@FORMFONTDIALOG`. See
[Function Reference: Common dialogs](FunctionReference.md#common-dialogs).

---

## Data and templates

### How do I save a form to a file?

```
%@formsave[%h,myform.jsonc]
```

### How do I load a form from a template file?

```
set h=%@formload[myform.jsonc]
```

With variable substitution:

```
set h=%@formload[picker.jsonc,prompt=Choose|width=500]
```

See [`06-template.btm`](../examples/basics/06-template.btm) and
[Template Reference](TemplateReference.md).

### What is the template file format?

JSONC (JSON with comments and trailing commas). The schema is at
[`schema/formcast-template.schema.json`](../schema/formcast-template.schema.json).
See [Template Reference](TemplateReference.md) for the full spec.

### How do I capture a form as an image without showing it?

```
%@formsaveimage[%h,screenshot.png]
```

Works in headless mode — no window flashes. See
[Tutorial section 8](Tutorial.md#8-saving-an-image-without-showing-the-form).

---

## Piping and streaming

### How do I pipe command output into a form control?

Use the `FORMPIPE` streaming command:

```
dir /s /b | FORMPIPE %h memo
type build.log | FORMPIPE %h rm Blue
```

For RICHMEMO, the optional third argument sets the text color. See
[`11-pipe-output.btm`](../examples/basics/11-pipe-output.btm) and
[Function Reference: FORMPIPE](FunctionReference.md#formpipe-handle-ctrlid-color-command).

### How do I append text to a control without replacing the content?

Use the `appendtext` pseudo-prop on `@FORMSET`:

```
%@formset[%h,memo,appendtext,new line of text]
```

Works on both `MEMO` and `RICHMEMO`. Live-only (requires the form to
be realized via `@FORMSHOW` first).

### How do I stream events out of a form?

Use the `FORMEVENTS` streaming command with `do ... in /p`:

```
do ev in /p formevents %h
  echo got: %ev
enddo
```

Each event is one line: `handle kind ctrl data`. See
[`07-events.btm`](../examples/basics/07-events.btm).

---

## Visual designer

### Is there a visual form designer?

Yes. [`examples/designer/formcast-visual-designer.btm`](../examples/designer/formcast-visual-designer.btm)
is a full interactive designer with a three-window layout: Toolbox
(loaded from template), Canvas with 8-point resize handles and
snap-to-grid, and a Properties window with a PropertyGrid that
auto-updates on selection and applies edits live. Click a control
type in the Toolbox to add it to the canvas with a type-based ID
(Label1, Button1, etc.). Save the result as a JSONC template. See
the [Designer Guide](DesignerGuide.md).

### Can I visually drag controls around on a form?

Yes. Set `design_mode=1` on the form before showing it:

```
%@formset[%h,.,design_mode,1]
%@formshow[%h]
```

Click a control to select it (red dashed border). Drag to move it.
The final position is committed to the descriptor so `@FORMSAVE`
reflects the drag. See
[`10-designer-drag.btm`](../examples/basics/10-designer-drag.btm)
and the [Designer Guide](DesignerGuide.md).

### How do I add controls to a form that's already showing?

`@FORMADD` now works after `@FORMSHOW`. The new control appears
on the visible form immediately. This is what the visual designer
uses to add controls from the toolbox.

### How do I delete a control?

```
%@formset[%h,ctrl,delete,]
```

Removes the control from both the descriptor and the realized form.

### How do I change Z-order (bring to front / send to back)?

```
%@formset[%h,ctrl,bringtofront,]
%@formset[%h,ctrl,sendtoback,]
```

### Can I save event bindings in a template?

Yes. Store bindings as `_bind.*` props:

```
%@formset[%h,btnOK,_bind.click,gosub :on_ok]
%@formsave[%h,myform.jsonc]
```

The loading BTM activates them explicitly:

```
set h=%@formload[myform.jsonc]
%@formapplybindings[%h]
%@formshow[%h]
```

The designer does NOT call `@FORMAPPLYBINDINGS`, so it can load
templates without having the target subroutines. See
[Function Reference: Template event bindings](FunctionReference.md#template-event-bindings).

### How do I move a control into or out of a container?

```
%@formset[%h,ctrl,reparent,panel1]    :: move into panel1
%@formset[%h,ctrl,reparent,]          :: move to form root
```

### Can I resize controls by dragging?

Yes. In design mode, selecting a control shows 8-point resize handles
(four corners + four edge midpoints). Drag any handle to resize.
Snap-to-grid (default 8px) constrains the result. You can also
resize programmatically:

```
%@formset[%h,btn,resizeby,20:10]
```

### How do I read or set a control's position programmatically?

```
set pos=%@formget[%h,btn,position]   :: returns "x:y"
%@formset[%h,btn,position,100:200]   :: absolute set
%@formset[%h,btn,moveby,5:-3]        :: delta move
```

Note the colon separator (not comma or pipe). See
[Function Reference: Designer primitives](FunctionReference.md#designer-primitives-form-level-ctrl).

### How do I select multiple controls?

Ctrl+Click or Shift+Click additional controls after selecting the
first one. The primary selection shows resize handles; secondary
selections show blue dashed borders. Click the canvas background
to deselect all. Right-clicking preserves the selection and opens
the context menu.

### How do I align or distribute controls?

Select 2+ controls (Ctrl+Click), then use:
- **Edit > Align** (or right-click > Align): Left, Right, Top,
  Bottom, Center Horizontally, Center Vertically
- **Edit > Layout** (or right-click > Layout): Distribute
  Horizontally/Vertically (3+ controls), Make Same Width/Height/Size
  (2+ controls)

Single-select alignment aligns to the canvas edge. Multi-select
alignment aligns to the first selected control. The same operations
are available from the toolbar (tb2 row), Edit menu, and context menu.

### How do I change the snap grid?

View > Grid Size in the designer menu. Options: Off (no snap), 4px
(fine), 8px (default), 16px (coarse). The grid dots update
immediately. Controls snap to the grid when dragged.

### How do I copy and paste controls?

Select one or more controls (Ctrl+Click for multi-select), then:
- **Ctrl+C** (or Edit > Copy / toolbar Copy): copies to clipboard
- **Ctrl+X** (or Edit > Cut): copies and deletes the originals
- **Ctrl+V** (or Edit > Paste): pastes from clipboard

Copy/paste handles containers with children -- copying a Panel
copies everything inside it. Pasted controls get new IDs
automatically (`Label1` -> `Label2`). Cut+paste reuses the
original ID since it was removed from the form.

Multi-select copy/cut copies all selected controls as a set.
Paste adds them all at once with an offset from the originals.

### How do I delete multiple controls at once?

Ctrl+Click to select multiple controls, then press Delete or use
Edit > Delete. All selected controls are removed in one operation
with a single undo snapshot.

---

## Troubleshooting

### The plugin loads but @FORMVERSION returns empty

Check that `plugin /l` actually succeeded — TCC may return exit=0
even when the .NET host fails silently. Look for error messages in
stderr. Common causes:

- **Wrong .NET Framework version**: FormCast requires net48 (4.8).
- **Missing dependencies**: `System.Text.Json.dll` and its transitive
  deps must be in the same directory as `FormCast.dll`. The build
  copies them via `CopyLocalLockFileAssemblies`.
- **`subst`'d drive**: .NET's `Assembly.LoadFrom` rejects paths
  through `subst`'d drives. Use `%@truename[path]` to resolve to the
  real path before passing to `plugin /l`.

### I get "Unknown command 0" errors

Every `@FORM*` variable function returns a value. If you write it
as a bare statement without capturing the return, TCC expands it to
`0` (success) and tries to execute `0` as a command:

```btm
:: WRONG - produces "Unknown command 0"
%@formset[%h,.,title,Hello]

:: CORRECT - capture the return value
set RC=%@formset[%h,.,title,Hello]
```

This applies to `%@formset`, `%@formbind`, `%@formshow`,
`%@formnotify`, and any other `%@form*` variable function.

### FORMSET returns 20101 (bad arguments)

Your value probably contains a comma, which `ArgParser.Split` treats
as an argument separator. Use colon (`:`) for structured values
(position `100:200`, LISTVIEW `addcolumn Name:280:text`). Never use
pipe (`|`) in values that pass through `set rc=%@formset[...]` — TCC
eats it as a pipe operator.

### LISTVIEW columns show literal separator characters

You're using the wrong field separator. LISTVIEW accepts both `|` and
`:`, but `:` is the only one safe for BTM scripts that use
`set rc=%@formset[...]`. See
[Function Reference: LISTVIEW](FunctionReference.md#listview-pseudo-props-set-on-a-control-with-typelistview).

### My form shows but controls are invisible

Usually this means `@FORMSHOW` was called before all `@FORMADD`
calls. The realizer builds WinForms controls from the descriptor at
show time. Note: `@FORMADD` after `@FORMSHOW` does work -- the new
control appears on the visible form immediately (this is what the
designer uses). If controls still don't appear, check position/size
values.

### The bound command doesn't seem to fire

- Verify the event name is correct (`click`, not `onclick`).
- Verify the control id matches exactly (case-insensitive).
- Remember that bound commands run on the worker thread, not the
  script thread. The script needs a `do forever / delay 1` loop to
  stay alive while waiting for events.

### How do I run scripts without any windows appearing?

Set the `FORMCAST_HEADLESS` environment variable:

```
set FORMCAST_HEADLESS=1
```

All `@FORMSHOW` calls become no-ops, modal dialogs return immediately,
and `@FORMSAVEIMAGE` still works (off-screen rendering). See
[Glossary: Headless mode](Glossary.md).

---

## Advanced topics

### How do I focus a form or switch back to the TCC console?

```
%@formfocus[%h]        :: focus the form
%@formfocus[TCC]       :: focus the TCC console
```

See [Function Reference: @FORMFOCUS](FunctionReference.md#formfocush--formfocustcc).

### How do I send a raw Win32 message to a form?

```
set result=%@formsendmessage[%h,0x10,0,0]
:: 0x10 = WM_CLOSE
```

See [Function Reference: @FORMSENDMESSAGE](FunctionReference.md#formsendmessagehmsgwparamlparam).

### How do I find which control is at a specific coordinate?

```
set id=%@formhittest[%h,150,80]
```

Returns the control id at that form-relative pixel position, or empty
if nothing is there. Used by the designer to map mouse clicks to
controls. See
[Function Reference: @FORMHITTEST](FunctionReference.md#formhittesthxy).

### Can forms survive after the TCC session exits?

Not yet in v1. The `FormCast.Host.exe` daemon is built and ships, but
the `Global\` scope wiring that keeps forms alive across sessions is
planned for v1.1. All v1 forms are `Local\` scoped and are destroyed
when the plugin unloads.

### What happens when the plugin unloads?

Every realized window is force-closed before `Plugin.Shutdown`
returns. This is the forced-shutdown contract -- a surviving window
would hold references into the unloaded assembly and crash TCC on
the next click. See [Architecture: Forced shutdown](Architecture.md#forced-shutdown-contract).

### How do I apply a dark theme to my form?

```
%@formset[%h,.,theme,dark]
```

Theme values: `system` (default), `dark`, `light`. Switching is
live -- the form and all controls repaint immediately. The dark
title bar uses the DWM immersive dark mode attribute on Windows 10+.
See [Function Reference: Theme](FunctionReference.md#theme-and-dark-mode).

### How do I set colors on a control?

```
%@formset[%h,lbl,forecolor,#00AAFF]
%@formset[%h,panel,backcolor,DarkBlue]
```

Use named colors (any `System.Drawing.Color` name or system color
like `Highlight`, `HighlightText`) or hex `#RRGGBB`. See
[Function Reference: Appearance](FunctionReference.md#appearance-properties).

### How do I set a custom font?

```
%@formset[%h,.,font,Segoe UI:11:bold]
%@formset[%h,memo,font,Consolas:10]
```

Format: `family:size[:style]`. Style is optional (`bold`, `italic`,
`bold+italic`). Setting the font on the form (`.`) propagates to
all child controls via `ApplyFontRecursive`.

### How do I make a control resize with the form?

Use the `anchor` property:

```
%@formset[%h,memo,anchor,top+bottom+left+right]
%@formset[%h,btn,anchor,bottom+right]
```

Anchored to opposite edges, the control stretches when the parent
resizes. See [Tutorial section 11](Tutorial.md#11-anchored-controls-that-resize-with-the-form).

### How do I add a stock icon to a button?

```
%@formset[%h,btn,stockicon,FileNew]
```

Works on Button, Label, PictureBox, CheckBox, RadioButton, and
LinkLabel. List all available icons with `FORMICONS`. See
[Function Reference: Stock icons](FunctionReference.md#stock-icons).

### How do I wire Enter/Escape to buttons?

```
%@formset[%h,.,acceptbutton,btnOK]
%@formset[%h,.,cancelbutton,btnCancel]
```

### What is formcast-check.btm?

The shared helper that replaces `formcast-load.btm`. It handles
plugin load (checking if already loaded), unload, and exit cleanup.
When a BTM is launched from Explorer (`%_parent` contains
`explorer`), it performs `plugin /u` + `exit` on close for clean
teardown. All example BTMs use `call formcast-check.btm load`
instead of `plugin /l` directly.

### What is the FORMEVENTS polling pattern?

The recommended way to handle events:

```
:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  set CTRL=%@word[" ",2,%ev]
  iff "%KIND" == "click" .and. "%CTRL" == "btnOK" then
    goto done
  endiff
enddo
goto loop
```

This is preferred over `@FORMBIND` with `gosub` because `gosub`
from the CallbackWorker thread does not work reliably. All current
examples use this pattern.

### How do I create a toggle switch?

```
%@formadd[%h,tgl,TOGGLE,12,12,50,24,]
%@formset[%h,tgl,checked,true]
```

TOGGLE is an on/off slider switch. It fires a `change` event with
`true` or `false`. Read state with `%@formget[%h,tgl,checked]`.

### How do I set control text from a file without TCC expansion?

```
%@formset[%h,memo,textfromfile,C:\data\log.txt]
```

This loads the file content directly, bypassing TCC's variable
expansion. Useful when the file contains `%` characters that would
otherwise be interpreted as variables.

### How do I set control text from a TCC variable?

```
%@formset[%h,lbl,textfromvar,MYVAR]
```

Reads the TCC variable at set-time and applies it as the control's
text.
