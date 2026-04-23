# FormCast Glossary

| Term | Definition |
|---|---|
| **BTM** | TCC batch script (`.btm` extension). The primary FormCast caller. |
| **Bound command** | A TCC command line registered with `@FORMBIND` that fires automatically when a named event occurs on a named control. Runs on the CallbackWorker thread. |
| **Callback worker** | The dedicated STA thread (`CallbackWorker.cs`) that runs bound commands. Separates user-script execution from the GUI thread so scripts can call back into `@FORM*` without deadlocking. |
| **Control descriptor** | The `ControlDescriptor` POCO inside a `FormDescriptor`: type, id, position, size, text, prop bag, and (for PANELs) child controls. |
| **Control id** | The user-supplied identifier for a control inside its owning form. Unique within the form (or within its parent panel for nested controls). |
| **Designer mode** | A flag (`design_mode`) on the form descriptor that signals "the user is editing this form in a designer". Doesn't change the realizer's behavior; it's a marker the BTM-side designer reads. |
| **Dispatch surface** | Every public function and command FormCast exposes through TCC's plugin contract: `@FORMOPEN`, `@FORMCLOSE`, `FORMEVENTS`, etc. |
| **ElementHost** | The `WindowsFormsIntegration.ElementHost` control that lets a WPF visual tree live inside a WinForms `Form`. Used by RICHMEMO. |
| **Event queue** | A `ConcurrentQueue<FormEvent>` per realized form. WinForms event handlers push records in; `FORMEVENTS` and bound commands drain them out. |
| **events_pending bit** | Bit `32` on `@FORMSTATE`: set when the form's event queue is non-empty. The polling target for `on condition`-driven scripts. |
| **FORMCAST_DLL** | Environment variable pointing to the full path of `FormCast.dll`. Used by `formcast-check.btm` to find and load the plugin. Set it once in your TCC startup: `set FORMCAST_DLL=C:\path\to\bin\FormCast.dll`. |
| **Forced shutdown** | The contract that `Plugin.Shutdown` MUST destroy every realized form before returning. Without it, surviving windows hold delegate references into the unloaded assembly and crash TCC on the next click. |
| **Form descriptor** | The `FormDescriptor` POCO held in the registry. Pure data, no WinForms references. The realizer turns it into a real Form. |
| **Form handle** | The `L:pid:seq` string returned by `@FORMOPEN`. Identifies a registry entry. The `L` namespace is local to the current TCC session; future `G` is global via `FormCast.Host.exe`. |
| **GuiHostThread** | The dedicated STA thread (`GuiHostThread.cs`) that runs `Application.Run` and owns every realized Form. The only thread allowed to touch a Form's controls. |
| **Headless mode** | The `FORMCAST_HEADLESS=1` environment variable that suppresses every visible window. Used by automation scripts that build forms only to capture images, save templates, or drive synthetic events. |
| **Idle timeout** | `FormCast.Host.exe`'s `--idle-seconds` flag (default 60). The host process exits cleanly after this many seconds with no client connection. |
| **JSONC** | JSON with line comments and trailing commas. The template format. |
| **Layout manager** | A pure-math algorithm (`AbsoluteLayout`, `FlowLayout`, `GridLayout`, `DockLayout`) that consumes a list of `ControlDescriptor` and computes positions. |
| **`Local\` scope** | A form whose handle lives only inside the current TCC session. The default. |
| **`Global\` scope** | A form whose handle is shared across TCC sessions via the `FormCast.Host.exe` daemon. v1 builds the daemon; v1.x ships the global registry decorator. |
| **Marker file** | `%TEMP%\FormCast.init.log`, an append-only log of plugin lifecycle events. Useful for debugging plugin /l / /u sequences. |
| **Mutex (host)** | The well-known per-logon-session mutex (`Local\FormCast.Host.<sid>`) that enforces single-instance for the daemon. |
| **Named pipe** | The IPC channel (`\\.\pipe\FormCast.Host.<sid>`) between the plugin and the host daemon. Length-prefixed framing per `FormCast.Ipc.PipeProtocol`. |
| **Pipe ACL** | The `PipeSecurity` access rule restricting the host's named pipe to the current Windows user. |
| **Plugin** | The `FormCast.dll` class library implementing `ITCCPlugin`. Loaded by TCC via `TC-DotNetPluginHost64.dll`. |
| **Pseudo-prop** | A `@FORMSET` property name that doesn't map to a strongly-typed field on the descriptor. Used by LISTVIEW (`addcolumn`, `additem`), RICHMEMO (`appendcolor`), the designer (`moveby`, `position`), etc. |
| **Property bag** | The `Dictionary<string,string>` field on `FormDescriptor` and `ControlDescriptor` that holds anything not covered by the strongly-typed fields. The extension point for layout hints, prop-driven control config, and forward-compatible attributes. |
| **Realizer** | `FormRealizer.cs`. Walks a `FormDescriptor` and produces a real `System.Windows.Forms.Form` on the GuiHostThread. |
| **Registry** | `LocalFormRegistry` (and the future `RemoteFormRegistry` decorator). The owning collection of `FormDescriptor` instances keyed by sequence id. |
| **Scope** | The handle namespace prefix: `Local\`, `Global\`, `User\<sid>\`. Indicates whether a form lives in this session, the daemon, or a per-user store. |
| **Strict mode (vars)** | The default `${var}` substitution mode where any unresolved placeholder raises an error. Pass `null` vars to `@FORMLOAD` to disable substitution entirely. |
| **TakeCmd.dll** | The native helper DLL TCC ships. FormCast `[DllImport]`s into it for `Command`, `SetEVariable`, `wwriteXP`, `QueryUnicodeOutput`, etc. |
| **MenuStrip** | A menu bar control (`MENUSTRIP`) at the top of a form. Children (via slash-id) become top-level menus; grandchildren become menu items. Text `"-"` creates a separator. Each item fires a `click` event. |
| **ContextMenuStrip** | A right-click popup menu (`CONTEXTMENU`). Same item model as MenuStrip. |
| **ToolStrip** | A toolbar control (`TOOLBAR`). Children with type `BUTTON` become toolbar buttons; text `"-"` creates a separator. |
| **StatusStrip** | A status bar control (`STATUSBAR`). Children become status panels. Set `spring=1` on a panel to fill remaining space. |
| **DataGridView** | A spreadsheet-like grid control (`DATAGRID`). Columns via `addcolumn`, rows via `addrow`. Supports text, checkbox, combobox, button, link, and image column types. |
| **ToolTip** | Hover text on any control, set via the `tooltip` pseudo-prop on `@FORMSET`. One ToolTip component is shared per form. |
| **NotifyIcon** | A system tray icon managed by `@FORMNOTIFY`. Supports show/hide and balloon notifications. |
| **WebBrowser** | An embedded browser control (`WEBBROWSER`). Navigate via the `url` prop or load HTML directly via `text`. |
| **FlowLayoutPanel** | An auto-flowing container (`FLOWPANEL`). Children wrap left-to-right or top-to-bottom. |
| **TableLayoutPanel** | A grid-based container (`TABLEPANEL`). Children specify position via `row` and `col` props. |
| **SplitContainer** | A resizable divider (`SPLITCONTAINER`). First two children go into Panel1 and Panel2. |
| **Anchor** | A control property (`top+bottom+left+right`) that determines which edges the control is pinned to. Anchored to opposite edges, the control stretches when the parent resizes. |
| **formcast-check.btm** | Shared helper script that handles plugin load, unload, and exit cleanup. Replaces the older `formcast-load.btm`. Detects Explorer-launched sessions and performs `plugin /u` + `exit` on close. |
| **FORMEVENTS polling** | The recommended event-handling pattern: `do ev in /p formevents %h` drains the per-form queue one line at a time, each line formatted as `handle kind ctrl data`. Preferred over `@FORMBIND` + `gosub` because gosub from the CallbackWorker thread is unreliable. |
| **FORMICONS** | A streaming command that lists all 216 built-in stock icons with categories. Accepts an optional filter argument. |
| **Font inheritance** | `ApplyFontRecursive` propagates a form-level `font` property to all child controls that lack individual font settings. Compensates for .NET Framework 4.8's limited dynamic font inheritance. |
| **PropertyGrid adapter** | The `designtarget` pseudo-prop on a PROPERTYGRID control that binds it to a form or control descriptor, exposing editable properties in a Visual Studio-style property editor. Used by the designer's Properties window. |
| **Resize handles** | Eight drag handles (four corners + four edge midpoints) drawn around the selected control in design mode. Enable visual resizing by dragging. |
| **SEPARATOR** | A control type for use as a `TOOLBAR` child. Creates a visual divider between toolbar buttons. Equivalent to a LABEL child with text `"-"` but expressed as its own type. |
| **setlocal/endlocal** | Convention used in all FormCast BTM scripts to scope variables to the current script, preventing pollution of the calling environment. |
| **Snap-to-grid** | Designer feature that constrains control placement to a grid (default 8px). Ensures consistent alignment. |
| **Stock icon** | One of 216 built-in icons in 16 categories. Applied to image-capable controls via the `stockicon` property. Enumerated by the `FORMICONS` command. |
| **Theme** | The `theme` property on a form: `system` (default), `dark`, or `light`. Switches live, re-painting all controls. Dark mode uses the DWM `DWMWA_USE_IMMERSIVE_DARK_MODE` attribute for a native dark title bar. |
| **TOGGLE** | An on/off slider switch control. Fires a `change` event with `true`/`false`. State readable via the `checked` property. |
| **Worker thread** | See "callback worker". |
