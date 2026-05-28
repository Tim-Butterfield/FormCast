# FormCast Template Reference

A FormCast template is a JSONC file (JSON with line/block comments
and trailing commas) that describes a single form. The schema lives
at `schema/formcast-template.schema.json` and is published as JSON
Schema Draft 2020-12.

## Minimal example

```jsonc
{
 "$schema": "https://github.com/Tim-Butterfield/FormCast/schema/formcast-template.schema.json",
 "version": 1,
 "type": "form",
 "name": "hello",
 "title": "Hello",
 "x": 200, "y": 200, "width": 300, "height": 120,
 "layout": "absolute",
 "controls": [
 { "type": "LABEL", "id": "lbl", "x": 10, "y": 10, "width": 260, "height": 20, "text": "Hello" },
 { "type": "BUTTON", "id": "ok", "x": 90, "y": 40, "width": 80, "height": 25, "text": "OK" }
 ]
}
```

Load it from a script:

```
set h=%@formload[hello.jsonc]
%@formshow[%h]
```

## Top-level fields

| Field | Required | Notes |
|---|---|---|
| `version` | yes | always `1` in v1 |
| `type` | yes | `form` for v1 |
| `name` | yes | logical id; can be scope-qualified (`Local\name`, future `Global\name`) |
| `title` | no | defaults to `name` |
| `x` / `y` / `width` / `height` | yes | integers OR string ints OR `${var}` placeholders |
| `layout` | no | `absolute` (default), `flow`, `grid`, `dock` |
| `props` | no | object of string-valued layout-manager configuration knobs |
| `controls` | yes | array of control objects |

## Control fields

| Field | Required | Notes |
|---|---|---|
| `type` | yes | any recognized type: `LABEL`, `EDIT`, `BUTTON`, `CHECKBOX`, `RADIO`, `TOGGLE`, `PANEL`, `LISTVIEW`, `MEMO`, `RICHMEMO`, `TOOLBAR`, `SEPARATOR`, etc. (39 types total) |
| `id` | yes | unique within the form |
| `x` / `y` / `width` / `height` | yes | integers OR string ints OR `${var}` |
| `text` | no | caption / initial text |
| `props` | no | open-ended string-valued bag |
| `children` | no | nested controls (PANEL, GROUPBOX, TABCONTROL, TABPAGE, SPLITCONTAINER, MENUSTRIP, CONTEXTMENU, TOOLBAR, STATUSBAR, FLOWPANEL, TABLEPANEL) |

## Variable substitution

Numeric and string fields accept `${name}` placeholders that get
replaced at load time when `@FORMLOAD` is called with vars:

```jsonc
{
 "version": 1, "type": "form", "name": "${formname}",
 "title": "${title}",
 "x": 0, "y": 0, "width": "${w}", "height": "${h}",
 "controls": [
 { "type": "LABEL", "id": "lbl", "x": 10, "y": 10, "width": 200, "height": 20, "text": "${prompt}" }
 ]
}
```

```
set h=%@formload[rename.jsonc,formname=rename|title=Rename|w=420|h=140|prompt=New name:]
```

Substitution rules:

* Single pass: `${a}` whose value is `${b}` does NOT recurse
* Strict: an unresolved `${name}` raises `FormatException` (the
 `@FORMLOAD` buffer comes back empty)
* Numeric fields with a `${var}` placeholder work because the
 serializer's `ReadInt` falls through to `int.TryParse` on string
 values

## Layout-manager configuration

The form-level `props` bag holds layout knobs that
`@FORMRELAYOUT` reads to instantiate the right manager:

| Layout | Knobs |
|---|---|
| `absolute` | (none) |
| `grid` | `grid_rows`, `grid_cols`, `grid_hgap`, `grid_vgap`, `grid_padding` |
| `flow` | `flow_hgap`, `flow_vgap`, `flow_padding`, `flow_direction`, `flow_wrap` |
| `dock` | `dock_padding` |

Each control may carry its own per-layout hints in its `props`:
`row`, `col`, `rowspan`, `colspan`, `dock`.

## Property-bag conventions

Some props have meaning to the realizer:

| Prop | Control type | Effect |
|---|---|---|
| `readonly` | EDIT, MEMO, RICHMEMO | flips ReadOnly |
| `nowrap` | MEMO | turns word wrap off |
| `_lv.col.N` | LISTVIEW | column spec, see Function Reference |
| `_lv.item.N` | LISTVIEW | row spec |
| `_lv.multiselect` | LISTVIEW | enables MultiSelect |
| `_lv.sort` | LISTVIEW | initial sort |
| `backcolor` | any | background color (named or `#RRGGBB`) |
| `forecolor` | any | foreground color (named or `#RRGGBB`) |
| `font` | any | `family:size[:style]` |
| `anchor` | any | resize anchoring (`top+bottom+left+right`) |
| `stockicon` | Button, Label, PictureBox, CheckBox, RadioButton, LinkLabel | stock icon name |
| `theme` | form | `system`, `dark`, or `light` |
| `acceptbutton` | form | button id wired to Enter key |
| `cancelbutton` | form | button id wired to Escape key |
| `autoscroll` | PANEL | enable auto-scrolling |
| `checked` | CHECKBOX, RADIO, TOGGLE | initial checked state (`1`, `true`, or `yes`) |

The `_lv.*` keys are managed by the LISTVIEW pseudo-prop dispatch
(`addcolumn`, `additem`, ...) and round-trip through serializer
unchanged.

## Designer and toolbox templates

The visual designer loads its toolbox and properties panels from
templates:

- `templates/toolbox.jsonc` -- the control-type toolbox window
- `templates/properties.jsonc` -- the PropertyGrid + Document Outline window

These templates are standard JSONC files and can be customized.

## Round-trip stability

`@FORMSAVE` followed by `@FORMLOAD` produces a byte-stable JSON file:
the serializer's output is deterministic and the deserializer never
mutates a descriptor on round trip. This is pinned by the regression tests; any change that breaks byte stability will fail
the suite.
