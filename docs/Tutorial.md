# FormCast Tutorial

A walk through the things you'll actually do in BTM scripts. Each
section is self-contained; copy any block into a `.btm` file and
run it after loading the plugin (via `call formcast-check.btm load`
or `plugin /l FormCast.dll`). All examples use `setlocal`/`endlocal`
for clean variable scoping.

## 1. The simplest dialog

```
setlocal
set h=%@formopen[form,box,200,200,260,100]
%@formadd[%h,lbl,LABEL,10,10,240,20,Hello]
%@formadd[%h,btn,BUTTON,90,40,80,25,OK]
%@formshow[%h]

:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  iff "%KIND" == "click" .or. "%KIND" == "close" then
    goto done
  endiff
enddo
goto loop

:done
%@formclose[%h]
endlocal
```

The `FORMEVENTS` polling loop is the recommended event pattern.
Each event is one line (`handle kind ctrl data`). The loop drains
the queue, dispatches by kind, and uses `goto` / `gosub` for
control flow. This avoids the thread-safety issues of `@FORMBIND`
with `gosub` from a callback thread.

## 2. Reading values back

```
setlocal
set h=%@formopen[form,name,200,200,300,120]
%@formadd[%h,lbl,LABEL,10,10,260,20,Your name?]
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
  iff "%KIND" == "close" goto done
enddo
goto loop

:done
set NAME=%@formget[%h,txt,text]
%@formclose[%h]
echo Hello, %NAME
endlocal
```

After the click event arrives, `@FORMGET` reads the textbox content
before closing the form.

## 3. Polling instead of binding

If you'd rather drive the loop yourself than use bound commands:

```
set h=%@formopen[form,box,200,200,260,100]
%@formadd[%h,btn,BUTTON,90,40,80,25,Go]
%@formshow[%h]

on condition %@formstate[%h] == 35 set FIRED=1

set FIRED=0
do while %FIRED == 0
  delay /m 50
enddo
echo button was pressed
%@formclose[%h]
quit
```

`35` = `1` (visible) + `2` (enabled) + `32` (events_pending). When
the user clicks Go, `events_pending` flips on and `on condition`
fires.

## 4. Streaming events with `do ... in /p`

```
set h=%@formopen[form,log,200,200,400,300]
%@formadd[%h,btn,BUTTON,160,250,80,25,Stop]
%@formshow[%h]

do ev in /p formevents %h
  echo got: %ev
  set KIND=%@word[" ",2,%ev]
  iff "%KIND" == "click" leave
enddo

%@formclose[%h]
quit
```

`formevents` is a streaming command, not a variable function. The
`do ... in /p` form runs it as a child process, reads its stdout
line by line, and assigns each line to `%ev`.

## 5. Layouts

The form-level `layout` property picks the layout manager:

```
%@formset[%h,.,layout,grid]
%@formset[%h,.,grid_rows,4]
%@formset[%h,.,grid_cols,2]
%@formset[%h,.,grid_hgap,8]
```

then add controls with `row` / `col` props:

```
%@formadd[%h,lbl,LABEL,0,0,80,20,Name:]
%@formset[%h,lbl,row,0]
%@formset[%h,lbl,col,0]
```

After adding, call `%@formrelayout[%h]` to compute final positions.

## 6. LISTVIEW

```
%@formadd[%h,lst,LISTVIEW,10,10,400,300,]
%@formset[%h,lst,addcolumn,Name:200:text]
%@formset[%h,lst,addcolumn,Size:100:size]
%@formset[%h,lst,addcolumn,Modified:160:date]
%@formset[%h,lst,additem,readme.txt:1.2 KB:2026-04-01]
%@formset[%h,lst,additem,test.bin:5 MB:2026-04-09]
%@formset[%h,lst,sort,Name:asc]
%@formshow[%h]
```

Click any column header to re-sort; the comparison is type-aware
based on the column type token.

## 7. Templates

Save the form descriptor to a JSONC file:

```
%@formsave[%h,settings.jsonc]
```

Reload it later:

```
set h=%@formload[settings.jsonc]
```

With variables for parameterization:

```
set h=%@formload[picker.jsonc,prompt=Choose|width=500]
```

The template author writes `${prompt}` and `${width}` references
inside string values; FormCast substitutes at load time.

## 8. Saving an image without showing the form

`@FORMSAVEIMAGE[h,path]` renders the form to a PNG file via
`Control.DrawToBitmap`, no window required. Useful for documentation
generation:

```
set h=%@formload[picker.jsonc]
%@formsaveimage[%h,picker.png]
%@formclose[%h]
```

The form's HWND is forced into existence on the GUI thread, then
the image is captured and the handle is freed. Nothing flashes on
screen.

## 9. Loading from a template

Many examples have been converted to JSONC templates in the
`templates/` directory (examples 04, 05, 09, 11, 12, 13, 14, 15).
Loading from a template is simpler than imperative construction:

```
setlocal
call formcast-check.btm load
set h=%@formload[templates\settings.jsonc]
%@formshow[%h]

:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  set CTRL=%@word[" ",2,%ev]
  iff "%KIND" == "click" .and. "%CTRL" == "btnOK" then
    goto done
  endiff
  iff "%KIND" == "close" goto done
enddo
goto loop

:done
%@formclose[%h]
endlocal
```

## 10. Appearance: colors, fonts, and themes

```
setlocal
set h=%@formopen[form,styled,200,200,400,300]
%@formset[%h,.,title,Styled Form]
%@formset[%h,.,theme,dark]
%@formset[%h,.,font,Segoe UI:11]
%@formadd[%h,lbl,LABEL,12,12,360,24,Dark theme with custom font]
%@formset[%h,lbl,forecolor,#00AAFF]
%@formadd[%h,btn,BUTTON,150,60,100,30,OK]
%@formset[%h,btn,stockicon,StatusInfo]
%@formset[%h,.,acceptbutton,btn]
%@formshow[%h]

:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  iff "%KIND" == "click" .or. "%KIND" == "close" goto done
enddo
goto loop

:done
%@formclose[%h]
endlocal
```

Theme can be `system`, `dark`, or `light` and switches live.
`acceptbutton` wires Enter to the named button. Stock icons from
the built-in library can be applied to buttons, labels, and other
image-capable controls.

## 11. Anchored controls that resize with the form

```
setlocal
set h=%@formopen[form,anchor,200,200,500,300]
%@formadd[%h,memo,MEMO,8,8,480,240,Resize the form]
%@formset[%h,memo,anchor,top+bottom+left+right]
%@formadd[%h,btn,BUTTON,400,255,80,25,Close]
%@formset[%h,btn,anchor,bottom+right]
%@formshow[%h]

:loop
do ev in /p formevents %h
  set KIND=%@word[" ",1,%ev]
  iff "%KIND" == "click" .or. "%KIND" == "close" goto done
enddo
goto loop

:done
%@formclose[%h]
endlocal
```

Anchor values are combined with `+`: `top+left` (default),
`top+bottom+left+right` (stretch both axes), `bottom+right`
(float with the bottom-right corner).

## 12. Browsing stock icons

The `FORMICONS` command lists all 216 built-in icons. The icon
browser example (`16-icon-browser.btm`) shows them in a grid:

```
do icon in /p formicons
  echo %icon
enddo
```

Filter by category:

```
do icon in /p formicons File
  echo %icon
enddo
```
