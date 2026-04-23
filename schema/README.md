# FormCast template schema

`formcast-template.schema.json` is a JSON Schema (Draft 2020-12)
that describes the FormCast JSONC template format consumed by
`@FORMLOAD` and `@FORMIMPORT`. Reference it from your own templates
via the `$schema` keyword so VS Code, Visual Studio, and other
JSON-Schema-aware editors give you completion and validation as you
author:

```jsonc
{
 "$schema": "https://github.com/Tim-Butterfield/FormCast/schema/formcast-template.schema.json",
 "version": 1,
 "type": "form",
 "name": "settings",
 ...
}
```

For local files inside this repo the example templates use a
relative `$schema` path (`../../schema/formcast-template.schema.json`)
so the editor picks up the schema without network access.

## What it covers

- Top-level form descriptor: `version`, `type`, `name`, `title`,
 `x`, `y`, `width`, `height`, `layout`, `props`, `controls`.
- Controls: `type`, `id`, `x`, `y`, `width`, `height`, `text`,
 `props`, `children`. Open-ended within `props` so layout-manager
 hints (`row`, `col`, `rowspan`, `colspan`, `dock`) and future
 control-type-specific attributes are accepted. Container types
 (PANEL, GROUPBOX, TABCONTROL, etc.) use `children` for nesting.
- Numeric fields are `intOrIntString`: a JSON integer OR a JSON
 string that is either a literal integer (`"400"`) or a single
 `${var}` placeholder (`"${w}"`). The string form is what enables
 load-time substitution because the substituted value is
 always a string and `FormSerializer.ReadInt` falls through to
 `int.TryParse`.

## What it does NOT cover (yet)

- The richer template format from `PLUGIN_DESIGN.md` section 6.17 (nested
 `form: { ... }` block, `bindings: []` array, `vars.defaults`
 block). These are Phase 6+ enhancements; the schema describes
 only the format the current `FormSerializer` actually produces
 and consumes.
- Per-layout configuration sub-objects (e.g. `grid: { columns: [...] }`).
 Layout knobs go through the form-level `props` bag instead
 (`grid_rows`, `grid_cols`, ...). The schema documents the bag as
 open-ended `additionalProperties: { type: string }` rather than
 enumerating known keys; `FormLayoutFactory` is the source of
 truth for which keys it reads.

## Example templates

See `examples/templates/`:

| File | Demonstrates |
|------------------|---------------------------------------------------------------|
| `simple.jsonc` | Minimal absolute-layout form |
| `settings.jsonc` | Grid layout with form-level prop bag and 2x4 control grid |
| `vars.jsonc` | `${var}` placeholders for `@FORMLOAD` parameterization |
| `flow.jsonc` | Flow layout with horizontal packing |

Each example is exercised by `FormTemplateExamplesTests` in the
xUnit project, which loads it via `FormSerializer.Deserialize`,
asserts the resulting `FormDescriptor`, and confirms the
serialize-deserialize-serialize round trip is byte-stable.
