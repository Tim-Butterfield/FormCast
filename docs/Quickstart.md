# FormCast Quickstart

FormCast lets a TCC v36+ batch script (`.btm`) build native Windows GUI
forms with the same handle-based vocabulary you already use for
`@SM*` and `@B*` handles. This page walks you from "plugin loaded"
to "form on screen with a button that calls a script function" in
about a minute.

## 1. Install

1. Download the latest FormCast release zip from the [releases page](https://github.com/Tim-Butterfield/FormCast/releases).
2. Extract the zip to a permanent location (e.g. `C:\FormCast\`). Keep all DLLs together — the plugin needs its dependency DLLs in the same directory as `FormCast.dll`.
3. Set the `FORMCAST_DLL` environment variable so BTM scripts can find it:
   ```
   set FORMCAST_DLL=C:\FormCast\FormCast.dll
   ```
   (Add this to your TCC startup file or system environment for persistence.)
4. Load it with `plugin /l %FORMCAST_DLL`.

`@FORMVERSION` returns the plugin version when the load succeeded:

```
plugin /l FormCast.dll
echo %@formversion[]
```

## 2. Hello, button

Save as `hello.btm` and run it from a TCC prompt:

```
@echo off
setlocal
call formcast-check.btm load

set h=%@formopen[form,hello,200,200,300,120]
%@formset[%h,.,title,Hello]
%@formadd[%h,lbl,LABEL,10,10,260,20,What is your name?]
%@formadd[%h,txt,EDIT,10,35,260,22,]
%@formadd[%h,btn,BUTTON,200,65,70,25,OK]

%@formshow[%h]

:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  set CTRL=%@word[" ",2,%ev]
  iff "%KIND" == "click" .and. "%CTRL" == "btn" then
    goto done
  endiff
  iff "%KIND" == "close" then
    goto done
  endiff
enddo
goto loop

:done
set name=%@formget[%h,txt,text]
%@formclose[%h]
echo Hello, %name
endlocal
```

`formcast-check.btm` is the shared helper that loads the plugin (or
verifies it is already loaded). It replaces the older
`formcast-load.btm`. The `FORMEVENTS` polling loop is the recommended
event-handling pattern -- it avoids the thread-safety pitfalls of
`@FORMBIND` + `gosub` from a callback thread.

## 3. The four moves you need to know

Every FormCast form follows the same four-step pattern:

1. **Open** a handle: `set h=%@formopen[form,name,x,y,w,h]`
2. **Add** controls: `%@formadd[%h,id,TYPE,x,y,w,h,text]`
3. **Show** the form: `%@formshow[%h]`
4. **Poll** for events: `do ev in /p formevents %h`

When you're done, `%@formclose[%h]` tears it down. The forced-shutdown
contract guarantees that `plugin /u FormCast` cleans up every form
FormCast created, so you can always recover with a fresh load.

**Templates**: instead of building forms imperatively, you can load a
JSONC template: `set h=%@formload[myform.jsonc]`. Many of the example
BTMs (04, 05, 09, 11, 12, 13, 14, 15) have been converted to load
from templates in the `templates/` directory.

## 4. Where to go next

| Topic | Read |
|---|---|
| Common questions by category | [FAQ](FAQ.md) |
| All variable functions and commands | [Function Reference](FunctionReference.md) |
| Step-by-step worked examples | [Tutorial](Tutorial.md) |
| Saving and loading templates | [Template Reference](TemplateReference.md) |
| Building forms in a designer | [Designer Guide](DesignerGuide.md) |
| How the plugin is wired | [Architecture](Architecture.md) |
| What every term means | [Glossary](Glossary.md) |
