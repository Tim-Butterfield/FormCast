# FormCast Designer Guide

FormCast v1 ships the descriptor-side designer **primitives**, an
**interactive visual designer** in `examples/designer/formcast-visual-designer.btm`,
and a **headless regression** that pins the primitive semantics. A
scripted smoke test of the original designer demo lives in
`examples/smoke/formcast-designer-smoke.btm`.

## What "designer" means in v1

A designer in FormCast is a BTM script that uses the same `@FORM*`
surface every other script uses, plus a small set of **designer
pseudo-properties** that mutate a target form's descriptor in
deterministic ways.

| Pseudo-prop | Where | Format | Effect |
|---|---|---|---|
| `design_mode` | form (`.`) | `1`/`0` | enable/disable designer mode flag |
| `selected` | form (`.`) | id string | currently selected control id |
| `position` | control | `x:y` | absolute Position |
| `size` | control | `w:h` | absolute Size |
| `moveby` | control | `dx:dy` | delta move |
| `resizeby` | control | `dw:dh` | delta resize |

`position` and `size` round-trip via `@FORMGET` -- a get-then-set
preserves the value.

## Why colon, not pipe or comma

The pair separator went through three iterations:

* `,` (comma) -- broken, because `@FORMSET` uses `,` to split arg
  positions; a `position` value of `100,200` becomes two extra
  positional args.
* `|` (pipe) -- broken in BTM, because `set X=%@formget[h,c,position]`
  receives the value through TCC's command parser, which treats
  bare `|` as a pipe operator and truncates at the first one.
* `:` (colon) -- the safe residual choice. Does not collide with
  the FORMSET arg parser and survives BTM SET assignment.

## The reference BTM

`examples/designer/formcast-visual-designer.btm` is the primary designer.
The original scripted demo now lives at
`examples/smoke/formcast-designer-smoke.btm` as a smoke test. It:

1. Opens a target form with two buttons
2. Enables `design_mode`
3. Sets `selected` = `btn1`
4. Calls `moveby` and `resizeby` against the selected control
5. Switches selection to `btn2`
6. Calls `position` to absolute-place it
7. Reads every modified position/size back via `@FORMGET`
8. Saves to `%TEMP%\formcast-designer-out.jsonc` via `@FORMSAVE`
9. Closes the form, reloads from disk, verifies every value
   survived the round trip

Run it after `plugin /l FormCast.dll`. It's headless by design --
no window flashes on screen -- because the v1 deliverable is the
primitive semantics, not the visible UI.

## Building your own

The simplest interactive shell sits inside an `inkey` / `do while`
loop:

```
%@formset[%h,.,design_mode,1]
%@formset[%h,.,selected,btn1]

do forever
  inkey /k"asdwq" %k
  set sel=%@formget[%h,.,selected]
  iff "%k" == "a" %@formset[%h,%sel,moveby,-5:0]
  iff "%k" == "d" %@formset[%h,%sel,moveby,5:0]
  iff "%k" == "w" %@formset[%h,%sel,moveby,0:-5]
  iff "%k" == "s" %@formset[%h,%sel,moveby,0:5]
  iff "%k" == "q" leave
enddo

%@formsave[%h,out.jsonc]
%@formclose[%h]
```

That's a working WASD-driven nudge tool in 14 lines. Real drag/
resize via the on-screen WinForms canvas waits on a future
`@FORMHITTEST[h,x,y]` and `@FORMBIND[h,.,mousedown,...]` surface.

## The visual designer

[`examples/designer/formcast-visual-designer.btm`](../examples/designer/formcast-visual-designer.btm)
is the full interactive designer. It opens **three windows**:

- **Toolbox** (left): loaded from `templates/toolbox.jsonc`. Lists
  all available control types as clickable buttons. Clicking a type
  adds a new control to the canvas at the next available position
  with a type-based ID (Label1, Edit1, Button1, etc.).
- **Canvas** (center): the form being designed, with
  `design_mode=1`. Controls display type labels painted on each
  control in design mode. Click a control to select it -- selection
  shows **8-point resize handles** (corners and edge midpoints).
  Drag to move or resize. **Snap-to-grid** (default 8px) constrains
  placement.
- **Properties** (right): loaded from `templates/properties.jsonc`.
  Contains a **PropertyGrid** (bound via the `designtarget` prop)
  and a **Document Outline** listing all controls. The PropertyGrid
  auto-updates when a different control is selected on the canvas,
  and editable properties apply live back to the canvas control.

**Adding controls**: click a type button in the Toolbox. The control
appears on the canvas with a type-based ID. Drag it where you want,
then resize with the handles.

**Editing**: select a control, then use the Edit menu to Delete,
Bring to Front, or Send to Back. Edit properties directly in the
PropertyGrid -- changes apply live. The `reparent` pseudo-prop
(via Edit menu or script) moves controls into or out of containers.

**Saving**: File > Save writes the canvas form to a JSONC template
via `@FORMSAVE`. The `design_mode` flag is cleared before saving
so the template is clean. File > Open loads a template back onto
the canvas.

**Event loop**: the designer uses the `FORMEVENTS` polling pattern
(not `@FORMBIND`) for all three windows. A shared
`formcast-check.btm` handles plugin load/unload/exit.

**Plugin primitives the designer uses**:
- `design_mode=1` + `DesignModeHandler` for click-to-select + drag
- 8-point resize handles on the selected control
- Snap-to-grid (configurable via View > Grid Size: Off/4/8/16 px)
- Type labels painted on controls in design mode
- Multi-select via Ctrl+Click or Shift+Click (blue dashed borders
  on secondary selections)
- Live `@FORMADD` after `@FORMSHOW` for adding controls to a
  visible form
- `delete` pseudo-prop for removing controls (single or multi-select)
- `bringtofront` / `sendtoback` for Z-order
- `position` / `size` reads and live updates
- Arrow key nudge (move by grid size, or 1px if grid off)
- Undo/redo stack (Ctrl+Z / Ctrl+Y)
- Cut/Copy/Paste (Ctrl+X / Ctrl+C / Ctrl+V) -- works with single
  or multi-selected controls. Container children are included
  automatically. Pasted IDs are auto-incremented to avoid
  collisions; cut+paste reuses original IDs.
- `designtarget` to bind PropertyGrid to the selected control
- Document Outline via `controllist` JSON and TreeView live-apply
- `@FORMSAVE` / `@FORMLOAD` for persistence
- `@FORMOPENDIALOG` / `@FORMSAVEDIALOG` for file selection
- `@FORMLOG` for debug logging

**Multi-select operations** (require Ctrl+Click to select 2+ controls):

| Operation | Menu | Context Menu | Toolbar | Min. Controls |
|---|---|---|---|---|
| Delete | Edit > Delete | Delete | tb1 | 1+ |
| Copy | Edit > Copy | Copy | tb1 | 1+ |
| Cut | Edit > Cut | Cut | tb1 | 1+ |
| Paste | Edit > Paste | Paste | tb1 | clipboard non-empty |
| Align Left/Right/Top/Bottom | Edit > Align | Align | tb2 | 1 (canvas edge) or 2+ (to first selected) |
| Center Horizontal/Vertical | Edit > Align | Align | tb2 | 1 or 2+ |
| Distribute Horizontal/Vertical | Edit > Layout | Layout | tb2 | 3+ |
| Make Same Width/Height/Size | Edit > Layout | Layout | tb2 | 2+ |

Single-select alignment aligns the control to the canvas edge.
Multi-select alignment aligns all controls to the first selected
control's edge.

## Running the designer as a standalone app

The visual designer supports two launch modes:

**From a TCC prompt** (normal development):
```
plugin /l FormCast.dll
formcast-visual-designer.btm
```
Runs in the current session. Console stays visible alongside the
designer windows.

**As a standalone app** (no visible console):
```
formcast-visual-designer.btm /app
```
Hides the TCC console via `@FORMCONSOLE[hide]`. The user sees
only the three designer windows -- no console at all. The Toolbox
window appears in the taskbar as "FormCast Designer"; clicking it
activates all three windows together (via the `owner` property).

**Desktop shortcut:**

For zero-flash launch, use `start /inv` in the shortcut target to
spawn the designer in a fully invisible TCC session:

| Field | Value |
|---|---|
| Target | `"C:\path\to\tcc.exe" /c start /inv /pgm "C:\path\to\tcc.exe" /c "C:\path\to\formcast-visual-designer.btm" /app` |
| Start in | Directory containing `formcast-visual-designer.btm` |
| Run | Minimized |
| Icon | Your designer's `.ico` file |

The shortcut must target `tcc.exe /c` -- pointing directly at the
`.btm` file will not pass arguments correctly.

---

## What's coming after v1

* Alignment guides (snap-to-sibling in addition to snap-to-grid)
* Template gallery / starter templates
* Single-window docked layout (MDI-style, like Visual Studio)
* Click-to-select in Document Outline

The v1 designer includes: three-window layout, PropertyGrid editing,
resize handles, configurable snap-to-grid, undo/redo, cut/copy/paste,
multi-select with align/distribute/size, Document Outline, context
menus, keyboard shortcuts, and live property updates.
