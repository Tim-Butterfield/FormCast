# FormCast Architecture

A 1000-foot view of how the plugin is wired. For full design notes
see `.scratch/PLUGIN_DESIGN.md` (developer-only).

## Layered model

```
+----------------------------------+
| TCC tcc.exe (host process) |
| |
| +----------------------------+ |
| | TC-DotNetPluginHost64.dll | | C++/CLI bridge
| +----------------------------+ |
| | |
| v |
| +----------------------------+ |
| | FormCast.dll (this) | |
| | | |
| | Plugin.cs (dispatch) | |
| | | | |
| | v | |
| | ITCCPlugin contract | |
| | +------+------+ | |
| | | | | | |
| | Local Forms CallbackWorker
| | Registry layer worker thread
| | | | |
| | | v |
| | | GuiHostThread (STA)
| | | |
| | | v
| | | FormRealizer ---> WinForms / WPF
| | +-> FormSerializer (JSONC)
| +----------------------------+ |
+----------------------------------+
```

## Threads

FormCast owns three worker threads beyond TCC's own:

| Thread | Owner | Job |
|---|---|---|
| GuiHostThread | `Threading/GuiHostThread.cs` | dedicated STA running `Application.Run`; owns every realized Form |
| CallbackWorker | `Threading/CallbackWorker.cs` | STA queue that runs `@FORMBIND`-bound TCC commands so user scripts never run on the GUI thread |
| (TCC dispatch) | TCC's own | calls into the plugin from BTM scripts |

The threads communicate via `Control.BeginInvoke` (TCC -> GUI),
`CallbackWorker.Enqueue` (anywhere -> worker), and `_realizedFormsLock`
(snapshot reads of the realized-form map and the per-form event
queues).

## Forms data model

```
FormDescriptor POCO, lives in registry
 Type / Name / Title / X / Y / Width / Height / LayoutMode
 Properties : Dictionary<string,string> (form-level prop bag)
 Controls : List<ControlDescriptor>
 |
 v
 ControlDescriptor POCO, lives inside descriptor
 Type / Id / X / Y / Width / Height / Text
 Properties : Dictionary<string,string> (control prop bag)
 Children : List<ControlDescriptor> (PANEL nesting)
```

`FormRealizer.Realize(descriptor, host)` walks the tree on the
GuiHostThread and produces a real `System.Windows.Forms.Form` whose
control hierarchy mirrors the descriptor. Realization is lazy:
nothing happens until `@FORMSHOW` or `@FORMSAVEIMAGE` is called.

## Event flow

```
 user click on a real Button
 |
 v
 EventWiringTable lookup
 (controlType, eventName) --> .NET event + data extractor
 |
 v
 WinForms event handler (GuiHostThread)
 |
 +--> data extractor lambda produces the event data string
 |
 +--> enqueue FormEvent into per-form FormEventQueue
 | |
 | +--> OnEnqueue hook fires synchronously
 | |
 | +--> DispatchBinding looks up
 | (handle, ctrl, event) in
 | _bindings, schedules the
 | bound TCC command on the
 | CallbackWorker
 |
 +--> FORMSTATE bit 32 (events_pending) flips on
 -- visible to scripts polling via on condition
 -- and to the FORMEVENTS streaming command
```

### EventWiringTable

The `EventWiringTable` is the central registry that maps
`(controlType, eventName)` pairs to concrete .NET event
subscriptions. Each entry stores:

1. The .NET event name to subscribe (e.g. `Click`, `TextChanged`)
2. A data-extraction lambda that converts the `EventArgs` into the
   colon-delimited data string that lands in the `FormEvent`

Common events (focus, blur, mousedown, mouseup, mouseenter,
mouseleave, keydown, keyup, resize, dblclick, dragenter, dragdrop)
are registered for all control types. Control-specific events
(click, change, scroll, keypress, columnclick, cellclick,
beforeexpand/afterexpand, beforecollapse/aftercollapse) are
registered only for the control types that support them.

Adding a new event requires a single `Register()` call in the
table -- no changes to the realizer, dispatcher, or event queue.
See `docs/FunctionReference.md` for the full event catalogue and
data formats.

**FormClosing** events now enqueue a `close` event into the
FORMEVENTS queue, so polling scripts see form-close alongside
control events.

**Recommended pattern**: `FORMEVENTS` polling with `goto`/`gosub`
dispatch is preferred over `@FORMBIND` with `gosub`, because
`gosub` from the CallbackWorker thread does not work reliably.
All current examples use the polling pattern.

## Font and theme system

**Font inheritance**: `ApplyFontRecursive` propagates a form-level
`font` property to all child controls that have not been
individually styled. This compensates for .NET Framework 4.8's
limited font inheritance when controls are added dynamically.

**Theme switching**: the `theme` property (`system`/`dark`/`light`)
applies live. The dark theme sets control colors and invokes the
DWM `DWMWA_USE_IMMERSIVE_DARK_MODE` attribute for a native dark
title bar on Windows 10 build 18985+. The `darkmode` property is a
legacy alias for `theme=dark`.

## Stock icon library

216 built-in icons across 16 categories (File, Edit, Navigation,
Actions, Formatting, Status, Objects, Controls, Alignment, Layout,
Arrows, Media, Development, Communication, Shapes, Misc). Icons are
rendered as `Bitmap` resources and applied via the `stockicon`
property on any image-capable control (Button, Label, PictureBox,
CheckBox, RadioButton, LinkLabel). The `FORMICONS` command
enumerates the full library.

## Designer architecture

The visual designer (`formcast-visual-designer.btm`) uses a
**three-window layout**:

1. **Toolbox** (left) -- loaded from `templates/toolbox.jsonc`. Lists
   all available control types as clickable buttons.
2. **Canvas** (center) -- the form being designed, with
   `design_mode=1`. Controls show type labels, selection uses 8-point
   resize handles, and snap-to-grid (default 8px) constrains
   placement.
3. **Properties** (right) -- loaded from `templates/properties.jsonc`.
   Contains a PropertyGrid (via the `designtarget` prop) and a
   Document Outline. The PropertyGrid auto-updates when a different
   control is selected, and editable properties apply live to the
   canvas.

Type-based IDs are auto-generated: `Label1`, `Edit1`, `Button1`, etc.

The designer uses the `FORMEVENTS` polling pattern for its event
loop, not `@FORMBIND`. A shared `formcast-check.btm` handles plugin
load/unload/exit.

## Cross-process (Phase 10)

`FormCast.Host.exe` is the daemon that will eventually own
`Global\` scoped forms so they survive the TCC session that created
them. v1 ships the host scaffolding, named-pipe IPC, version
handshake, ACL, and idle-out timer. The actual `IRemoteFormRegistry`
op codes that wire `@FORMOPEN[Global\name]` through to the host
are deferred to v1.x.

The plugin reaches the host via the `FormCast.Ipc.PipeProtocol`
length-prefixed wire format, shared via `ProjectReference` between
the plugin and host projects (the host has
`InternalsVisibleTo("FormCast.Host")` from the plugin so the
internal type is reachable).

## Forced shutdown contract

`Plugin.Shutdown` (called by TCC on `plugin /u`) MUST tear down
every realized form before returning. Surviving HWNDs would hold
delegate references into the (now-unloaded) plugin assembly and
the next click would crash TCC. The contract is implemented in
three layers:

1. `_forcedShutdown` flag set on the GuiHostThread before the sweep
2. Every Form has a `FormClosing` handler that clears any user-set
 `e.Cancel` while the flag is on
3. `FormRealizer.Destroy` runs against every entry in
 `_realizedForms` on the GUI thread; the RICHMEMO realizer
 wires `ElementHost.HandleDestroyed` to null `Child` first, so
 the WPF teardown order does not race the WinForms host

## Test surface

```
xUnit (FormCast.Tests, net48)
 +-- pure-logic tests (POCO, JSON, layout managers)
 +-- realizer tests (FormRealizer with a real GuiHostThread)
 +-- dispatch tests (Plugin.f_FORM* through StringBuilder)
 +-- end-to-end visible tests (real Form.Show() in non-headless mode)

bridge (Windows VM runner, not committed)
 +-- build + dotnet test
 +-- per-milestone .btm probes that load the plugin in real TCC v36
 +-- examples-smoke covers the 18 PLUGIN_DESIGN section 6 examples
```

The xUnit suite is the inner loop; bridge BTMs prove every
milestone holds up inside an actual `tcc.exe` host process,
because nothing inside `dotnet test` exercises TC's
P/Invoke contract or the `TC-DotNetPluginHost64.dll` reflection
load path.
