// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/StockIcons.cs
// ============================
//
// Comprehensive library of programmatically drawn 16x16 stock icons
// embedded in FormCast.dll. All icons use saturated colors that are
// visible on both light and dark backgrounds.
//
// Access from BTM via: set RC=%@formset[%h,ctrl,stockicon,name]
//
// Categories:
//   File operations:  new, open, save, saveas, close, print, export, import
//   Edit operations:  cut, copy, paste, delete, undo, redo, find, replace
//   Navigation:       up, down, left, right, home, end, back, forward,
//                     refresh, stop, zoomin, zoomout
//   Common actions:   add, remove, ok, cancel, apply, help, info, warning,
//                     error, question, settings, run, pause, stop_action
//   Formatting:       bold, italic, underline, alignleft, aligncenter,
//                     alignright, indent, outdent, font, color
//   Status:           check, cross, star, flag, lock, unlock, pin,
//                     visible, hidden, online, offline
//   Objects:          folder, file, image, database, table, chart, clock,
//                     calendar, mail, user, users, key, link, attachment
//   Control types:    ctrl_label, ctrl_edit, ctrl_button, ctrl_checkbox,
//                     ctrl_radio, ctrl_panel, ctrl_listbox, ctrl_combobox,
//                     ctrl_memo, ctrl_groupbox, ctrl_progress, ctrl_picture,
//                     ctrl_toggle, ctrl_numeric, ctrl_datepicker, ctrl_trackbar,
//                     ctrl_treeview, ctrl_checkedlist, ctrl_linklabel,
//                     ctrl_richmemo, ctrl_maskedit, ctrl_tabcontrol,
//                     ctrl_splitter, ctrl_listview, ctrl_datagrid,
//                     ctrl_menustrip, ctrl_toolbar, ctrl_statusbar,
//                     ctrl_monthcalendar, ctrl_hscrollbar, ctrl_vscrollbar,
//                     ctrl_flowpanel, ctrl_tablepanel, ctrl_propertygrid,
//                     ctrl_webbrowser, ctrl_contextmenu, ctrl_domainupdown

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// Comprehensive library of programmatically drawn 16x16 stock icons.
    /// Every icon is rendered to a <see cref="Bitmap"/> via GDI+ primitives
    /// (lines, arcs, fills) rather than embedded image resources, keeping the
    /// assembly size small and enabling dual-theme visibility (all colors are
    /// chosen to be legible on both light and dark backgrounds).
    /// </summary>
    /// <remarks>
    /// <para>Icons are organized into categories (File, Edit, Navigation,
    /// Controls, etc.) via the <see cref="Cat"/> / <see cref="Reg"/> pattern
    /// in the static constructor. BTM scripts access icons via
    /// <c>%@formset[%h,ctrl,stockicon,name]</c>.</para>
    ///
    /// <para>The cache is process-lifetime: once an icon is drawn it is
    /// never redrawn. The cache key is case-insensitive so
    /// <c>stockicon=Save</c> and <c>stockicon=save</c> return the same
    /// <see cref="Bitmap"/> instance.</para>
    /// </remarks>
    internal static class StockIcons
    {
        /// <summary>
        /// Process-lifetime cache of rendered icons. Keyed by icon name
        /// (case-insensitive). Once populated, entries are never evicted.
        /// </summary>
        private static readonly Dictionary<string, Bitmap> _cache =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get a stock icon by name. Returns null if unknown.
        /// </summary>
        public static Image? Get(string name)
        {
            if (_cache.TryGetValue(name, out Bitmap? cached))
                return cached;
            Bitmap? bmp = Draw(name.ToLowerInvariant());
            if (bmp is not null)
                _cache[name] = bmp;
            return bmp;
        }

        /// <summary>
        /// Returns all known icon names for enumeration.
        /// </summary>
        public static IReadOnlyCollection<string> AllNames => _drawers.Keys;

        /// <summary>
        /// Returns icons grouped by category in registration order.
        /// Each entry is (category, name).
        /// </summary>
        public static IReadOnlyList<(string Category, string Name)> CategorizedNames => _categorized;

        /// <summary>
        /// Registry of drawing functions keyed by icon name. Populated
        /// once in the static constructor; never mutated after that.
        /// </summary>
        private static readonly Dictionary<string, Func<Bitmap>> _drawers =
            new Dictionary<string, Func<Bitmap>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Ordered list of (category, name) pairs preserving registration
        /// order. Used by the FORMICONS command and the designer's icon
        /// picker to enumerate icons grouped by category.
        /// </summary>
        private static readonly List<(string Category, string Name)> _categorized =
            new List<(string, string)>();
        /// <summary>Current category label set by <see cref="Cat"/>.</summary>
        private static string _currentCategory = "";

        static StockIcons()
        {
            Cat("File");
            Reg("new", () => Icon(DrawDocument));
            Reg("open", () => Icon(DrawFolder));
            Reg("save", () => Icon(DrawFloppy));
            Reg("saveas", () => Icon((g) => { DrawFloppy(g); DrawPencilSmall(g, 9, 9); }));
            Reg("close", () => Icon((g) => { DrawDocument(g); DrawXSmall(g, 9, 9); }));
            Reg("print", () => Icon(DrawPrinter));
            Reg("export", () => Icon((g) => { DrawDocument(g); DrawArrowRight(g, 8, 7); }));
            Reg("import", () => Icon((g) => { DrawDocument(g); DrawArrowLeft(g, 8, 7); }));

            Cat("Edit");
            Reg("cut", () => Icon(DrawScissors));
            Reg("copy", () => Icon(DrawCopyDocs));
            Reg("paste", () => Icon(DrawClipboard));
            Reg("delete", () => Icon(DrawTrash));
            Reg("undo", () => Icon(DrawUndoArrow));
            Reg("redo", () => Icon(DrawRedoArrow));
            Reg("find", () => Icon(DrawMagnifier));
            Reg("replace", () => Icon((g) => { DrawMagnifier(g); DrawPencilSmall(g, 9, 9); }));

            Cat("Navigation");
            Reg("up", () => Icon((g) => DrawChevron(g, 0)));
            Reg("down", () => Icon((g) => DrawChevron(g, 2)));
            Reg("left", () => Icon((g) => DrawChevron(g, 3)));
            Reg("right", () => Icon((g) => DrawChevron(g, 1)));
            Reg("home", () => Icon(DrawHome));
            Reg("back", () => Icon((g) => DrawCurvedArrow(g, true)));
            Reg("forward", () => Icon((g) => DrawCurvedArrow(g, false)));
            Reg("refresh", () => Icon(DrawRefresh));
            Reg("zoomin", () => Icon((g) => { DrawMagnifier(g); DrawPlus(g, 3, 5); }));
            Reg("zoomout", () => Icon((g) => { DrawMagnifier(g); DrawMinus(g, 3, 5); }));

            Cat("Actions");
            Reg("add", () => Icon((g) => DrawPlus(g, 4, 4)));
            Reg("remove", () => Icon((g) => DrawMinus(g, 4, 6)));
            Reg("ok", () => Icon(DrawCheckmark));
            Reg("cancel", () => Icon(DrawXLarge));
            Reg("apply", () => Icon(DrawCheckmark));
            Reg("help", () => Icon((g) => DrawCircleLetter(g, "?")));
            Reg("info", () => Icon((g) => DrawCircleLetter(g, "i")));
            Reg("warning", () => Icon(DrawWarningTriangle));
            Reg("error", () => Icon(DrawErrorCircle));
            Reg("question", () => Icon((g) => DrawCircleLetter(g, "?")));
            Reg("settings", () => Icon(DrawGear));
            Reg("run", () => Icon(DrawPlayTriangle));
            Reg("pause", () => Icon(DrawPauseBars));
            Reg("stop_action", () => Icon(DrawStopSquare));

            Cat("Formatting");
            Reg("bold", () => Icon((g) => DrawFormatLetter(g, "B", true)));
            Reg("italic", () => Icon((g) => DrawFormatLetter(g, "I", false, true)));
            Reg("underline", () => Icon((g) => DrawFormatLetterUnderline(g)));
            Reg("alignleft", () => Icon((g) => DrawAlignLines(g, 0)));
            Reg("aligncenter", () => Icon((g) => DrawAlignLines(g, 1)));
            Reg("alignright", () => Icon((g) => DrawAlignLines(g, 2)));
            Reg("font", () => Icon((g) => DrawFormatLetter(g, "A", false)));
            Reg("color", () => Icon(DrawColorPalette));

            Cat("Status");
            Reg("check", () => Icon(DrawCheckmark));
            Reg("cross", () => Icon(DrawXLarge));
            Reg("star", () => Icon(DrawStar));
            Reg("flag", () => Icon(DrawFlag));
            Reg("lock", () => Icon((g) => DrawLock(g)));
            Reg("unlock", () => Icon((g) => DrawLock(g, true)));
            Reg("pin", () => Icon(DrawPin));
            Reg("visible", () => Icon(DrawEye));
            Reg("hidden", () => Icon((g) => { DrawEye(g); DrawStrikethrough(g); }));
            Reg("online", () => Icon((g) => DrawDot(g, C.Green)));
            Reg("offline", () => Icon((g) => DrawDot(g, C.Gray)));

            Cat("Objects");
            Reg("folder", () => Icon(DrawFolder));
            Reg("file", () => Icon(DrawDocument));
            Reg("image", () => Icon(DrawImageIcon));
            Reg("database", () => Icon(DrawDatabase));
            Reg("table", () => Icon(DrawTableGrid));
            Reg("chart", () => Icon(DrawBarChart));
            Reg("clock", () => Icon(DrawClock));
            Reg("calendar", () => Icon(DrawCalendarIcon));
            Reg("mail", () => Icon(DrawEnvelope));
            Reg("user", () => Icon((g) => DrawUser(g)));
            Reg("users", () => Icon((g) => { DrawUser(g, 2); DrawUser(g, 8); }));
            Reg("key", () => Icon(DrawKey));
            Reg("link", () => Icon(DrawLinkChain));
            Reg("attachment", () => Icon(DrawPaperclip));

            Cat("Controls");
            Reg("ctrl_label", () => Icon((g) => DrawCtrlBox(g, "Aa", C.Blue)));
            Reg("ctrl_edit", () => Icon((g) => DrawCtrlBox(g, "ab|", C.Teal)));
            Reg("ctrl_button", () => Icon((g) => DrawCtrlRoundBox(g, "Btn", C.Blue)));
            Reg("ctrl_checkbox", () => Icon(DrawCtrlCheckbox));
            Reg("ctrl_radio", () => Icon(DrawCtrlRadio));
            Reg("ctrl_panel", () => Icon((g) => DrawCtrlDashedBox(g, C.Gray)));
            Reg("ctrl_listbox", () => Icon((g) => DrawCtrlListLines(g, C.Teal)));
            Reg("ctrl_combobox", () => Icon(DrawCtrlCombobox));
            Reg("ctrl_memo", () => Icon((g) => DrawCtrlTextLines(g, C.Teal)));
            Reg("ctrl_groupbox", () => Icon((g) => DrawCtrlTitledBox(g, "G", C.Purple)));
            Reg("ctrl_progress", () => Icon(DrawCtrlProgress));
            Reg("ctrl_picture", () => Icon(DrawImageIcon));
            Reg("ctrl_toggle", () => Icon(DrawCtrlToggle));
            Reg("ctrl_numeric", () => Icon((g) => DrawCtrlBox(g, "1^", C.Orange)));
            Reg("ctrl_datepicker", () => Icon(DrawCalendarIcon));
            Reg("ctrl_trackbar", () => Icon(DrawCtrlTrackbar));
            Reg("ctrl_treeview", () => Icon(DrawCtrlTree));
            Reg("ctrl_checkedlist", () => Icon(DrawCtrlCheckedList));
            Reg("ctrl_linklabel", () => Icon((g) => DrawCtrlBox(g, "Lk", C.LinkBlue)));
            Reg("ctrl_richmemo", () => Icon((g) => DrawCtrlTextLines(g, C.Purple, true)));
            Reg("ctrl_maskedit", () => Icon((g) => DrawCtrlBox(g, "_#", C.Teal)));
            Reg("ctrl_tabcontrol", () => Icon(DrawCtrlTabs));
            Reg("ctrl_splitter", () => Icon(DrawCtrlSplitter));
            Reg("ctrl_listview", () => Icon(DrawCtrlListViewGrid));
            Reg("ctrl_datagrid", () => Icon(DrawTableGrid));
            Reg("ctrl_menustrip", () => Icon(DrawCtrlMenuStrip));
            Reg("ctrl_toolbar", () => Icon(DrawCtrlToolbar));
            Reg("ctrl_statusbar", () => Icon(DrawCtrlStatusbar));
            Reg("ctrl_monthcalendar", () => Icon(DrawCalendarIcon));
            Reg("ctrl_hscrollbar", () => Icon((g) => DrawCtrlScrollbar(g, true)));
            Reg("ctrl_vscrollbar", () => Icon((g) => DrawCtrlScrollbar(g, false)));
            Reg("ctrl_flowpanel", () => Icon((g) => DrawCtrlFlowArrows(g)));
            Reg("ctrl_tablepanel", () => Icon(DrawTableGrid));
            Reg("ctrl_propertygrid", () => Icon(DrawCtrlPropertyGrid));
            Reg("ctrl_webbrowser", () => Icon(DrawCtrlGlobe));
            Reg("ctrl_contextmenu", () => Icon(DrawCtrlContextMenu));
            Reg("ctrl_domainupdown", () => Icon((g) => DrawCtrlBox(g, "A^", C.Orange)));

            // ---- Designer Alignment ----
            Cat("Alignment");
            Reg("align_left", () => Icon((g) => DrawAlignGuide(g, 0)));
            Reg("align_right", () => Icon((g) => DrawAlignGuide(g, 1)));
            Reg("align_top", () => Icon((g) => DrawAlignGuide(g, 2)));
            Reg("align_bottom", () => Icon((g) => DrawAlignGuide(g, 3)));
            Reg("align_hcenter", () => Icon((g) => DrawAlignGuide(g, 4)));
            Reg("align_vcenter", () => Icon((g) => DrawAlignGuide(g, 5)));
            Reg("dist_horizontal", () => Icon(DrawDistHorizontal));
            Reg("dist_vertical", () => Icon(DrawDistVertical));
            Reg("same_width", () => Icon((g) => DrawSameSize(g, true, false)));
            Reg("same_height", () => Icon((g) => DrawSameSize(g, false, true)));
            Reg("same_size", () => Icon((g) => DrawSameSize(g, true, true)));
            Reg("snap_grid", () => Icon(DrawSnapGrid));
            Reg("snap_lines", () => Icon(DrawSnapLines));

            // ---- Layout & Grouping ----
            Cat("Layout");
            Reg("group", () => Icon(DrawGroup));
            Reg("ungroup", () => Icon(DrawUngroup));
            Reg("bringfront", () => Icon((g) => DrawZOrder(g, true)));
            Reg("sendback", () => Icon((g) => DrawZOrder(g, false)));
            Reg("dock_left", () => Icon((g) => DrawDockRegion(g, 0)));
            Reg("dock_right", () => Icon((g) => DrawDockRegion(g, 1)));
            Reg("dock_top", () => Icon((g) => DrawDockRegion(g, 2)));
            Reg("dock_bottom", () => Icon((g) => DrawDockRegion(g, 3)));
            Reg("dock_fill", () => Icon((g) => DrawDockRegion(g, 4)));
            Reg("tile_h", () => Icon((g) => DrawTile(g, true)));
            Reg("tile_v", () => Icon((g) => DrawTile(g, false)));
            Reg("splitpane", () => Icon(DrawCtrlSplitter));

            // ---- Arrows & Directions ----
            Cat("Arrows");
            Reg("arrow_up", () => Icon((g) => DrawSolidArrow(g, 0)));
            Reg("arrow_down", () => Icon((g) => DrawSolidArrow(g, 2)));
            Reg("arrow_left", () => Icon((g) => DrawSolidArrow(g, 3)));
            Reg("arrow_right", () => Icon((g) => DrawSolidArrow(g, 1)));
            Reg("arrow_upleft", () => Icon((g) => DrawDiagArrow(g, 0)));
            Reg("arrow_upright", () => Icon((g) => DrawDiagArrow(g, 1)));
            Reg("arrow_downleft", () => Icon((g) => DrawDiagArrow(g, 2)));
            Reg("arrow_downright", () => Icon((g) => DrawDiagArrow(g, 3)));
            Reg("expand", () => Icon(DrawExpand));
            Reg("collapse", () => Icon(DrawCollapse));
            Reg("sort_asc", () => Icon((g) => DrawSortArrow(g, true)));
            Reg("sort_desc", () => Icon((g) => DrawSortArrow(g, false)));
            Reg("swap", () => Icon(DrawSwap));
            Reg("move_icon", () => Icon(DrawMoveIcon));

            // ---- Media ----
            Cat("Media");
            Reg("play", () => Icon(DrawPlayTriangle));
            Reg("record", () => Icon((g) => DrawDot(g, C.Red)));
            Reg("rewind", () => Icon(DrawRewind));
            Reg("fastforward", () => Icon(DrawFastForward));
            Reg("skip_prev", () => Icon((g) => DrawSkip(g, true)));
            Reg("skip_next", () => Icon((g) => DrawSkip(g, false)));
            Reg("volume", () => Icon(DrawVolume));
            Reg("mute", () => Icon(DrawMute));

            // ---- Data & Development ----
            Cat("Development");
            Reg("code", () => Icon(DrawCodeBrackets));
            Reg("terminal", () => Icon(DrawTerminal));
            Reg("bug", () => Icon(DrawBug));
            Reg("build", () => Icon(DrawHammer));
            Reg("package", () => Icon(DrawPackage));
            Reg("plugin", () => Icon(DrawPluginIcon));
            Reg("api", () => Icon(DrawApiIcon));
            Reg("branch", () => Icon(DrawBranch));
            Reg("merge", () => Icon(DrawMerge));
            Reg("commit", () => Icon(DrawCommit));
            Reg("tag", () => Icon(DrawTagIcon));
            Reg("variable", () => Icon((g) => DrawCtrlBox(g, "x=", C.Teal)));

            // ---- Communication ----
            Cat("Communication");
            Reg("chat", () => Icon(DrawChatBubble));
            Reg("phone", () => Icon(DrawPhone));
            Reg("notification", () => Icon(DrawBell));
            Reg("share", () => Icon(DrawShareIcon));
            Reg("download", () => Icon((g) => DrawVertArrowBox(g, true)));
            Reg("upload", () => Icon((g) => DrawVertArrowBox(g, false)));
            Reg("cloud", () => Icon(DrawCloud));
            Reg("sync", () => Icon(DrawRefresh));
            Reg("rss", () => Icon(DrawRss));

            // ---- Shapes & Symbols ----
            Cat("Shapes");
            Reg("circle", () => Icon((g) => { using var p = new Pen(C.Blue, 1.5f); g.DrawEllipse(p, 2, 2, 11, 11); }));
            Reg("square", () => Icon((g) => { using var p = new Pen(C.Blue, 1.5f); g.DrawRectangle(p, 2, 2, 11, 11); }));
            Reg("triangle", () => Icon((g) => { using var p = new Pen(C.Blue, 1.5f); g.DrawPolygon(p, new Point[] { new(8, 2), new(14, 13), new(2, 13) }); }));
            Reg("diamond", () => Icon((g) => { using var p = new Pen(C.Blue, 1.5f); g.DrawPolygon(p, new Point[] { new(8, 1), new(14, 8), new(8, 15), new(2, 8) }); }));
            Reg("heart", () => Icon(DrawHeart));
            Reg("lightning", () => Icon(DrawLightning));
            Reg("sun", () => Icon(DrawSun));
            Reg("moon", () => Icon(DrawMoon));
            Reg("target", () => Icon(DrawTarget));
            Reg("shield", () => Icon(DrawShield));
            Reg("trophy", () => Icon(DrawTrophy));
            Reg("gift", () => Icon(DrawGift));
            Reg("bookmark", () => Icon(DrawBookmark));
            Reg("tag_label", () => Icon(DrawTagLabel));
            Reg("hash", () => Icon((g) => DrawFormatLetter(g, "#", true)));
            Reg("at", () => Icon((g) => DrawFormatLetter(g, "@", false)));
            Reg("percent", () => Icon((g) => DrawFormatLetter(g, "%", true)));
            Reg("power", () => Icon(DrawPower));

            // ---- Misc Extras ----
            Cat("Misc");
            Reg("filter", () => Icon(DrawFunnel));
            Reg("layers", () => Icon(DrawLayers));
            Reg("ruler", () => Icon(DrawRuler));
            Reg("palette", () => Icon(DrawColorPalette));
            Reg("wrench", () => Icon(DrawWrench));
            Reg("magic", () => Icon(DrawMagicWand));
            Reg("clipboard_text", () => Icon(DrawClipboard));
            Reg("history", () => Icon(DrawClock));
            Reg("connection", () => Icon(DrawLinkChain));
            Reg("disconnect", () => Icon((g) => { DrawLinkChain(g); DrawStrikethrough(g); }));
            Reg("recycle", () => Icon(DrawRecycle));
            Reg("trash_empty", () => Icon(DrawTrash));
            Reg("fullscreen", () => Icon(DrawFullscreen));
            Reg("exitfullscreen", () => Icon(DrawExitFullscreen));
            Reg("minimize", () => Icon((g) => DrawMinus(g, 8, 12)));
            Reg("maximize", () => Icon((g) => { using var p = new Pen(C.Blue, 1.5f); g.DrawRectangle(p, 2, 2, 11, 11); }));
            Reg("restore", () => Icon(DrawRestore));
            Reg("pin_off", () => Icon((g) => { DrawPin(g); DrawStrikethrough(g); }));
            Reg("thumbsup", () => Icon(DrawThumbsUp));
            Reg("thumbsdown", () => Icon(DrawThumbsDown));
        }

        /// <summary>Set the current category for subsequent <see cref="Reg"/> calls.</summary>
        private static void Cat(string category)
        {
            _currentCategory = category;
        }

        /// <summary>
        /// Register an icon drawing function under the current category.
        /// The drawer is lazy: it runs only when <see cref="Get"/> is first
        /// called for this name. The result is then cached.
        /// </summary>
        private static void Reg(string name, Func<Bitmap> drawer)
        {
            _drawers[name] = drawer;
            _categorized.Add((_currentCategory, name));
        }

        /// <summary>
        /// Execute the registered drawing function for the given name.
        /// Returns <c>null</c> if no drawer is registered (unknown icon name).
        /// </summary>
        private static Bitmap? Draw(string name)
        {
            return _drawers.TryGetValue(name, out var drawer) ? drawer() : null;
        }

        /// <summary>
        /// Create a 16x16 bitmap, configure antialiased rendering, run the
        /// painter delegate, and return the finished bitmap. This is the
        /// common factory for every icon in the library.
        /// </summary>
        private static Bitmap Icon(Action<Graphics> painter)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                painter(g);
            }
            return bmp;
        }

        // ========== Color palette (visible on light and dark) ==========

        /// <summary>
        /// Saturated color constants chosen for visibility on both light
        /// (white/F3F3F3) and dark (202020/2D2D2D) backgrounds. Each icon
        /// drawing function references these instead of hard-coding colors
        /// so the palette can be adjusted in one place.
        /// </summary>
        private static class C
        {
            public static readonly Color Blue = Color.FromArgb(60, 140, 220);
            public static readonly Color DarkBlue = Color.FromArgb(40, 100, 180);
            public static readonly Color LinkBlue = Color.FromArgb(80, 160, 240);
            public static readonly Color Teal = Color.FromArgb(0, 160, 150);
            public static readonly Color Green = Color.FromArgb(80, 180, 80);
            public static readonly Color Red = Color.FromArgb(220, 60, 60);
            public static readonly Color Orange = Color.FromArgb(220, 150, 40);
            public static readonly Color Yellow = Color.FromArgb(240, 200, 40);
            public static readonly Color Purple = Color.FromArgb(150, 100, 200);
            public static readonly Color Gray = Color.FromArgb(140, 140, 140);
            public static readonly Color LightGray = Color.FromArgb(180, 180, 180);
            public static readonly Color White = Color.White;
            public static readonly Color Gold = Color.FromArgb(230, 180, 50);
            public static readonly Color GoldDark = Color.FromArgb(190, 140, 30);
            public static readonly Color Metal = Color.FromArgb(190, 195, 200);
            public static readonly Color Amber = Color.FromArgb(180, 140, 50);
        }

        // ========== Primitive drawing helpers ==========

        private static void DrawPlus(Graphics g, int cx, int cy)
        {
            using var p = new Pen(C.Green, 2f);
            g.DrawLine(p, cx, cy - 3, cx, cy + 3);
            g.DrawLine(p, cx - 3, cy, cx + 3, cy);
        }

        private static void DrawMinus(Graphics g, int cx, int cy)
        {
            using var p = new Pen(C.Red, 2f);
            g.DrawLine(p, cx - 3, cy, cx + 3, cy);
        }

        private static void DrawXSmall(Graphics g, int cx, int cy)
        {
            using var p = new Pen(C.Red, 1.5f);
            g.DrawLine(p, cx - 2, cy - 2, cx + 2, cy + 2);
            g.DrawLine(p, cx + 2, cy - 2, cx - 2, cy + 2);
        }

        private static void DrawXLarge(Graphics g)
        {
            using var p = new Pen(C.Red, 2.5f);
            g.DrawLine(p, 3, 3, 12, 12);
            g.DrawLine(p, 12, 3, 3, 12);
        }

        private static void DrawPencilSmall(Graphics g, int x, int y)
        {
            using var p = new Pen(C.Orange, 1.2f);
            g.DrawLine(p, x, y + 5, x + 5, y);
            g.DrawLine(p, x, y + 5, x + 1, y + 4);
        }

        private static void DrawArrowRight(Graphics g, int x, int y)
        {
            using var p = new Pen(C.Green, 1.8f);
            g.DrawLine(p, x, y, x + 5, y);
            g.DrawLine(p, x + 3, y - 2, x + 5, y);
            g.DrawLine(p, x + 3, y + 2, x + 5, y);
        }

        private static void DrawArrowLeft(Graphics g, int x, int y)
        {
            using var p = new Pen(C.Blue, 1.8f);
            g.DrawLine(p, x + 5, y, x, y);
            g.DrawLine(p, x + 2, y - 2, x, y);
            g.DrawLine(p, x + 2, y + 2, x, y);
        }

        private static void DrawStrikethrough(Graphics g)
        {
            using var p = new Pen(C.Red, 1.8f);
            g.DrawLine(p, 2, 13, 13, 2);
        }

        private static void DrawDot(Graphics g, Color c)
        {
            using var b = new SolidBrush(c);
            g.FillEllipse(b, 4, 4, 8, 8);
        }

        private static void DrawChevron(Graphics g, int dir)
        {
            // 0=up 1=right 2=down 3=left
            using var p = new Pen(C.Blue, 2.2f);
            switch (dir)
            {
                case 0: g.DrawLine(p, 3, 10, 8, 4); g.DrawLine(p, 8, 4, 13, 10); break;
                case 1: g.DrawLine(p, 5, 3, 11, 8); g.DrawLine(p, 11, 8, 5, 13); break;
                case 2: g.DrawLine(p, 3, 5, 8, 11); g.DrawLine(p, 8, 11, 13, 5); break;
                case 3: g.DrawLine(p, 11, 3, 5, 8); g.DrawLine(p, 5, 8, 11, 13); break;
            }
        }

        // ========== File operations ==========

        private static void DrawDocument(Graphics g)
        {
            using var fill = new SolidBrush(Color.FromArgb(230, 240, 255));
            var pts = new Point[] { new(3, 1), new(9, 1), new(12, 4), new(12, 14), new(3, 14) };
            g.FillPolygon(fill, pts);
            using var pen = new Pen(C.Blue, 1.4f);
            g.DrawLine(pen, 3, 1, 3, 14); g.DrawLine(pen, 3, 14, 12, 14);
            g.DrawLine(pen, 12, 14, 12, 4); g.DrawLine(pen, 12, 4, 9, 1);
            g.DrawLine(pen, 9, 1, 3, 1); g.DrawLine(pen, 9, 1, 9, 4); g.DrawLine(pen, 9, 4, 12, 4);
        }

        private static void DrawFolder(Graphics g)
        {
            using var brush = new SolidBrush(C.Gold);
            g.FillPolygon(brush, new Point[] {
                new(1, 5), new(6, 5), new(8, 3), new(14, 3), new(14, 13), new(1, 13) });
            using var pen = new Pen(C.GoldDark, 1.2f);
            g.DrawLine(pen, 1, 5, 14, 5);
        }

        private static void DrawFloppy(Graphics g)
        {
            using var brush = new SolidBrush(C.Blue);
            g.FillRectangle(brush, 2, 1, 12, 13);
            using var wh = new SolidBrush(C.White);
            g.FillRectangle(wh, 4, 8, 8, 6);
            using var metal = new SolidBrush(C.Metal);
            g.FillRectangle(metal, 5, 1, 6, 5);
        }

        private static void DrawPrinter(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.3f);
            g.FillRectangle(Brushes.White, 4, 1, 8, 4);
            g.DrawRectangle(pen, 4, 1, 8, 4);
            using var body = new SolidBrush(C.LightGray);
            g.FillRectangle(body, 1, 5, 14, 6);
            g.DrawRectangle(pen, 1, 5, 14, 6);
            g.FillRectangle(Brushes.White, 4, 10, 8, 5);
            g.DrawRectangle(pen, 4, 10, 8, 5);
        }

        // ========== Edit operations ==========

        private static void DrawScissors(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.6f);
            g.DrawEllipse(pen, 2, 9, 5, 5);
            g.DrawEllipse(pen, 9, 9, 5, 5);
            g.DrawLine(pen, 5, 10, 11, 2);
            g.DrawLine(pen, 11, 10, 5, 2);
        }

        private static void DrawCopyDocs(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.4f);
            g.DrawRectangle(pen, 4, 1, 10, 10);
            using var fill = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            g.FillRectangle(fill, 1, 4, 10, 10);
            g.DrawRectangle(pen, 1, 4, 10, 10);
        }

        private static void DrawClipboard(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            using var fill = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            g.FillRectangle(fill, 2, 3, 12, 12);
            g.DrawRectangle(pen, 2, 3, 12, 12);
            using var clip = new SolidBrush(C.Amber);
            g.FillRectangle(clip, 5, 1, 6, 4);
            g.DrawRectangle(pen, 5, 1, 6, 4);
            g.DrawLine(pen, 5, 8, 11, 8);
            g.DrawLine(pen, 5, 11, 11, 11);
        }

        private static void DrawTrash(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.3f);
            g.DrawLine(pen, 3, 3, 13, 3);
            g.DrawLine(pen, 6, 1, 10, 1);
            g.DrawLine(pen, 6, 1, 6, 3);
            g.DrawLine(pen, 10, 1, 10, 3);
            g.DrawLine(pen, 4, 3, 5, 14);
            g.DrawLine(pen, 12, 3, 11, 14);
            g.DrawLine(pen, 5, 14, 11, 14);
            g.DrawLine(pen, 8, 5, 8, 12);
        }

        private static void DrawUndoArrow(Graphics g)
        {
            using var pen = new Pen(C.Blue, 2f);
            g.DrawArc(pen, 3, 4, 10, 8, 180, 180);
            g.DrawLine(pen, 3, 4, 3, 8);
            g.DrawLine(pen, 3, 8, 7, 8);
        }

        private static void DrawRedoArrow(Graphics g)
        {
            using var pen = new Pen(C.Blue, 2f);
            g.DrawArc(pen, 3, 4, 10, 8, 180, -180);
            g.DrawLine(pen, 13, 4, 13, 8);
            g.DrawLine(pen, 13, 8, 9, 8);
        }

        private static void DrawMagnifier(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.8f);
            g.DrawEllipse(pen, 2, 1, 8, 8);
            g.DrawLine(pen, 9, 8, 14, 13);
        }

        // ========== Navigation ==========

        private static void DrawHome(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            // Roof
            g.DrawLine(pen, 2, 8, 8, 2);
            g.DrawLine(pen, 8, 2, 14, 8);
            // Walls
            g.DrawLine(pen, 4, 8, 4, 14);
            g.DrawLine(pen, 12, 8, 12, 14);
            g.DrawLine(pen, 4, 14, 12, 14);
            // Door
            g.DrawLine(pen, 7, 14, 7, 10);
            g.DrawLine(pen, 9, 14, 9, 10);
            g.DrawLine(pen, 7, 10, 9, 10);
        }

        private static void DrawCurvedArrow(Graphics g, bool left)
        {
            using var pen = new Pen(C.Blue, 2f);
            if (left)
            {
                g.DrawArc(pen, 2, 3, 12, 10, 200, -160);
                g.DrawLine(pen, 2, 6, 2, 10);
                g.DrawLine(pen, 2, 10, 6, 10);
            }
            else
            {
                g.DrawArc(pen, 2, 3, 12, 10, -20, 160);
                g.DrawLine(pen, 14, 6, 14, 10);
                g.DrawLine(pen, 14, 10, 10, 10);
            }
        }

        private static void DrawRefresh(Graphics g)
        {
            using var pen = new Pen(C.Green, 2f);
            g.DrawArc(pen, 2, 2, 12, 12, 0, 270);
            g.DrawLine(pen, 8, 0, 14, 2);
            g.DrawLine(pen, 8, 0, 8, 5);
        }

        // ========== Common actions ==========

        private static void DrawCheckmark(Graphics g)
        {
            using var pen = new Pen(C.Green, 2.5f);
            g.DrawLine(pen, 2, 8, 6, 12);
            g.DrawLine(pen, 6, 12, 13, 3);
        }

        private static void DrawCircleLetter(Graphics g, string letter)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawEllipse(pen, 2, 2, 12, 12);
            using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var brush = new SolidBrush(C.Blue);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(letter, font, brush, new RectangleF(2, 2, 12, 12), sf);
        }

        private static void DrawWarningTriangle(Graphics g)
        {
            using var fill = new SolidBrush(C.Yellow);
            var pts = new Point[] { new(8, 1), new(15, 14), new(1, 14) };
            g.FillPolygon(fill, pts);
            using var pen = new Pen(C.Orange, 1.3f);
            g.DrawPolygon(pen, pts);
            using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(80, 60, 0));
            g.DrawString("!", font, brush, 5, 4);
        }

        private static void DrawErrorCircle(Graphics g)
        {
            using var fill = new SolidBrush(C.Red);
            g.FillEllipse(fill, 1, 1, 14, 14);
            using var pen = new Pen(C.White, 2f);
            g.DrawLine(pen, 5, 5, 11, 11);
            g.DrawLine(pen, 11, 5, 5, 11);
        }

        private static void DrawGear(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.5f);
            g.DrawEllipse(pen, 4, 4, 8, 8);
            // Teeth (simplified as short lines radiating out)
            for (int angle = 0; angle < 360; angle += 45)
            {
                double rad = angle * Math.PI / 180.0;
                int x1 = 8 + (int)(5 * Math.Cos(rad));
                int y1 = 8 + (int)(5 * Math.Sin(rad));
                int x2 = 8 + (int)(7 * Math.Cos(rad));
                int y2 = 8 + (int)(7 * Math.Sin(rad));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private static void DrawPlayTriangle(Graphics g)
        {
            using var brush = new SolidBrush(C.Green);
            g.FillPolygon(brush, new Point[] { new(4, 2), new(13, 8), new(4, 14) });
        }

        private static void DrawPauseBars(Graphics g)
        {
            using var brush = new SolidBrush(C.Orange);
            g.FillRectangle(brush, 3, 2, 4, 12);
            g.FillRectangle(brush, 9, 2, 4, 12);
        }

        private static void DrawStopSquare(Graphics g)
        {
            using var brush = new SolidBrush(C.Red);
            g.FillRectangle(brush, 3, 3, 10, 10);
        }

        // ========== Formatting ==========

        private static void DrawFormatLetter(Graphics g, string letter, bool bold, bool italic = false)
        {
            FontStyle style = FontStyle.Regular;
            if (bold) style |= FontStyle.Bold;
            if (italic) style |= FontStyle.Italic;
            using var font = new Font("Segoe UI", 10f, style);
            using var brush = new SolidBrush(C.Blue);
            g.DrawString(letter, font, brush, 2, 0);
        }

        private static void DrawFormatLetterUnderline(Graphics g)
        {
            using var font = new Font("Segoe UI", 10f, FontStyle.Underline);
            using var brush = new SolidBrush(C.Blue);
            g.DrawString("U", font, brush, 2, 0);
        }

        private static void DrawAlignLines(Graphics g, int align)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            int[] widths = align == 0 ? new[] { 12, 8, 11, 6 }
                         : align == 2 ? new[] { 12, 8, 11, 6 }
                         : new[] { 10, 6, 12, 8 };
            for (int i = 0; i < 4; i++)
            {
                int y = 3 + i * 3;
                int x0 = align == 0 ? 2 : align == 2 ? 14 - widths[i] : (16 - widths[i]) / 2;
                g.DrawLine(pen, x0, y, x0 + widths[i], y);
            }
        }

        private static void DrawColorPalette(Graphics g)
        {
            using var r = new SolidBrush(C.Red); g.FillEllipse(r, 1, 1, 6, 6);
            using var gr = new SolidBrush(C.Green); g.FillEllipse(gr, 8, 1, 6, 6);
            using var b = new SolidBrush(C.Blue); g.FillEllipse(b, 1, 8, 6, 6);
            using var y = new SolidBrush(C.Yellow); g.FillEllipse(y, 8, 8, 6, 6);
        }

        // ========== Status ==========

        private static void DrawStar(Graphics g)
        {
            using var brush = new SolidBrush(C.Gold);
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double angle = Math.PI / 2 + i * Math.PI / 5;
                float r = i % 2 == 0 ? 7f : 3f;
                pts[i] = new PointF(8f + r * (float)Math.Cos(angle), 8f - r * (float)Math.Sin(angle));
            }
            g.FillPolygon(brush, pts);
        }

        private static void DrawFlag(Graphics g)
        {
            using var pole = new Pen(C.Gray, 1.5f);
            g.DrawLine(pole, 3, 1, 3, 14);
            using var flag = new SolidBrush(C.Red);
            g.FillPolygon(flag, new Point[] { new(4, 1), new(13, 4), new(4, 7) });
        }

        private static void DrawLock(Graphics g, bool open = false)
        {
            using var pen = new Pen(C.Orange, 1.5f);
            // Shackle
            if (open)
                g.DrawArc(pen, 4, 0, 8, 8, 0, -180);
            else
                g.DrawArc(pen, 4, 1, 8, 8, 180, 180);
            // Body
            using var body = new SolidBrush(C.Orange);
            g.FillRectangle(body, 3, 7, 10, 8);
            // Keyhole
            using var kh = new SolidBrush(Color.FromArgb(60, 40, 0));
            g.FillEllipse(kh, 6, 9, 4, 3);
        }

        private static void DrawPin(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.5f);
            g.FillEllipse(new SolidBrush(C.Red), 4, 1, 8, 8);
            g.DrawLine(pen, 8, 9, 8, 15);
        }

        private static void DrawEye(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawArc(pen, 1, 3, 14, 10, 200, 140);
            g.DrawArc(pen, 1, 3, 14, 10, 20, -140);
            g.DrawEllipse(pen, 5, 5, 6, 6);
            using var pupil = new SolidBrush(C.DarkBlue);
            g.FillEllipse(pupil, 6, 6, 4, 4);
        }

        // ========== Objects ==========

        private static void DrawImageIcon(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.3f);
            g.DrawRectangle(pen, 1, 2, 13, 11);
            // Mountain
            using var mount = new SolidBrush(C.Green);
            g.FillPolygon(mount, new Point[] { new(2, 12), new(6, 6), new(10, 10), new(13, 7), new(13, 12) });
            // Sun
            using var sun = new SolidBrush(C.Yellow);
            g.FillEllipse(sun, 3, 3, 4, 4);
        }

        private static void DrawDatabase(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.3f);
            using var fill = new SolidBrush(Color.FromArgb(220, 210, 240));
            g.FillEllipse(fill, 2, 1, 12, 5);
            g.DrawEllipse(pen, 2, 1, 12, 5);
            g.DrawLine(pen, 2, 3, 2, 12);
            g.DrawLine(pen, 14, 3, 14, 12);
            g.DrawArc(pen, 2, 10, 12, 5, 0, 180);
            g.DrawArc(pen, 2, 6, 12, 5, 0, 180);
        }

        private static void DrawTableGrid(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            g.DrawLine(pen, 1, 5, 14, 5);
            g.DrawLine(pen, 1, 9, 14, 9);
            g.DrawLine(pen, 6, 1, 6, 14);
            g.DrawLine(pen, 10, 1, 10, 14);
        }

        private static void DrawBarChart(Graphics g)
        {
            using var b1 = new SolidBrush(C.Blue);
            using var b2 = new SolidBrush(C.Green);
            using var b3 = new SolidBrush(C.Orange);
            g.FillRectangle(b1, 2, 8, 3, 6);
            g.FillRectangle(b2, 6, 4, 3, 10);
            g.FillRectangle(b3, 10, 2, 3, 12);
            using var pen = new Pen(C.Gray, 1f);
            g.DrawLine(pen, 1, 14, 14, 14);
        }

        private static void DrawClock(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawEllipse(pen, 1, 1, 13, 13);
            g.DrawLine(pen, 8, 3, 8, 8);
            g.DrawLine(pen, 8, 8, 11, 10);
        }

        private static void DrawCalendarIcon(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.FillRectangle(new SolidBrush(C.Blue), 1, 1, 14, 4);
            g.DrawRectangle(pen, 1, 1, 14, 13);
            g.DrawLine(pen, 1, 5, 15, 5);
            using var font = new Font("Segoe UI", 6f, FontStyle.Bold);
            using var brush = new SolidBrush(C.Blue);
            g.DrawString("31", font, brush, 3, 5);
        }

        private static void DrawEnvelope(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawRectangle(pen, 1, 3, 14, 10);
            g.DrawLine(pen, 1, 3, 8, 9);
            g.DrawLine(pen, 15, 3, 8, 9);
        }

        private static void DrawUser(Graphics g, int xoff = 0)
        {
            int x = 5 + xoff;
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawEllipse(pen, x - 2, 1, 5, 5);
            g.DrawArc(pen, x - 4, 7, 9, 8, 200, 140);
        }

        private static void DrawKey(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.5f);
            g.DrawEllipse(pen, 1, 1, 7, 7);
            g.DrawLine(pen, 7, 5, 14, 5);
            g.DrawLine(pen, 14, 5, 14, 8);
            g.DrawLine(pen, 11, 5, 11, 7);
        }

        private static void DrawLinkChain(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawArc(pen, 1, 4, 8, 8, 90, 180);
            g.DrawLine(pen, 5, 4, 8, 4);
            g.DrawLine(pen, 5, 12, 8, 12);
            g.DrawArc(pen, 7, 4, 8, 8, 270, 180);
            g.DrawLine(pen, 8, 4, 11, 4);
            g.DrawLine(pen, 8, 12, 11, 12);
        }

        private static void DrawPaperclip(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.5f);
            g.DrawArc(pen, 5, 1, 6, 6, 0, -180);
            g.DrawLine(pen, 5, 4, 5, 12);
            g.DrawArc(pen, 3, 9, 6, 6, 180, -180);
            g.DrawLine(pen, 11, 4, 11, 11);
            g.DrawArc(pen, 9, 9, 4, 4, 0, 180);
        }

        // ========== Control type icons ==========

        private static void DrawCtrlBox(Graphics g, string text, Color c)
        {
            using var pen = new Pen(c, 1.2f);
            g.DrawRectangle(pen, 1, 3, 13, 10);
            using var font = new Font("Segoe UI", 6f);
            using var brush = new SolidBrush(c);
            g.DrawString(text, font, brush, 2, 4);
        }

        private static void DrawCtrlRoundBox(Graphics g, string text, Color c)
        {
            using var pen = new Pen(c, 1.2f);
            using var path = RoundRect(1, 3, 14, 10, 3);
            g.DrawPath(pen, path);
            using var font = new Font("Segoe UI", 5.5f);
            using var brush = new SolidBrush(c);
            g.DrawString(text, font, brush, 2, 4);
        }

        private static void DrawCtrlCheckbox(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawRectangle(pen, 2, 3, 10, 10);
            using var check = new Pen(C.Green, 2f);
            g.DrawLine(check, 4, 8, 6, 11);
            g.DrawLine(check, 6, 11, 10, 5);
        }

        private static void DrawCtrlRadio(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawEllipse(pen, 2, 2, 12, 12);
            using var fill = new SolidBrush(C.Blue);
            g.FillEllipse(fill, 5, 5, 6, 6);
        }

        private static void DrawCtrlDashedBox(Graphics g, Color c)
        {
            using var pen = new Pen(c, 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, 1, 1, 13, 13);
        }

        private static void DrawCtrlListLines(Graphics g, Color c)
        {
            using var pen = new Pen(c, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            for (int y = 4; y <= 12; y += 3)
            {
                g.DrawLine(pen, 3, y, 12, y);
            }
        }

        private static void DrawCtrlCombobox(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 4, 13, 8);
            g.DrawLine(pen, 10, 4, 10, 12);
            // Down arrow
            g.DrawLine(pen, 11, 7, 13, 9);
            g.DrawLine(pen, 13, 9, 15, 7);
        }

        private static void DrawCtrlTextLines(Graphics g, Color c, bool styled = false)
        {
            using var pen = new Pen(c, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var line = new Pen(styled ? C.Blue : c, 1f);
            g.DrawLine(line, 3, 4, 12, 4);
            using var line2 = new Pen(styled ? C.Red : c, 1f);
            g.DrawLine(line2, 3, 7, 10, 7);
            g.DrawLine(pen, 3, 10, 12, 10);
        }

        private static void DrawCtrlTitledBox(Graphics g, string title, Color c)
        {
            using var pen = new Pen(c, 1.2f);
            g.DrawRectangle(pen, 1, 4, 13, 11);
            using var font = new Font("Segoe UI", 5.5f, FontStyle.Bold);
            using var brush = new SolidBrush(c);
            using var bg = new SolidBrush(Color.FromArgb(240, 240, 240));
            g.FillRectangle(bg, 3, 1, 8, 8);
            g.DrawString(title, font, brush, 4, 0);
        }

        private static void DrawCtrlProgress(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 5, 13, 6);
            using var fill = new SolidBrush(C.Green);
            g.FillRectangle(fill, 2, 6, 8, 4);
        }

        private static void DrawCtrlToggle(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            using var path = RoundRect(1, 4, 14, 8, 4);
            using var fill = new SolidBrush(C.Blue);
            g.FillPath(fill, path);
            using var thumb = new SolidBrush(C.White);
            g.FillEllipse(thumb, 9, 5, 5, 5);
        }

        private static void DrawCtrlTrackbar(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.2f);
            g.DrawLine(pen, 1, 8, 14, 8);
            using var thumb = new SolidBrush(C.Blue);
            g.FillRectangle(thumb, 7, 4, 3, 8);
            // Ticks
            for (int x = 1; x <= 14; x += 3)
                g.DrawLine(pen, x, 12, x, 14);
        }

        private static void DrawCtrlTree(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.2f);
            g.DrawLine(pen, 3, 2, 3, 13);
            g.DrawLine(pen, 3, 4, 7, 4);
            g.DrawLine(pen, 7, 4, 7, 9);
            g.DrawLine(pen, 7, 6, 11, 6);
            g.DrawLine(pen, 7, 9, 11, 9);
            g.DrawLine(pen, 3, 12, 7, 12);
        }

        private static void DrawCtrlCheckedList(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1f);
            using var check = new Pen(C.Green, 1.2f);
            for (int y = 2; y <= 12; y += 4)
            {
                g.DrawRectangle(pen, 2, y, 4, 3);
                g.DrawLine(check, 3, y + 2, 4, y + 3);
                g.DrawLine(check, 4, y + 3, 6, y);
                g.DrawLine(pen, 8, y + 1, 13, y + 1);
            }
        }

        private static void DrawCtrlTabs(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 5, 13, 10);
            // Tab headers
            g.DrawRectangle(pen, 1, 1, 5, 4);
            g.DrawRectangle(pen, 7, 1, 5, 4);
        }

        private static void DrawCtrlSplitter(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var split = new Pen(C.Gray, 2f);
            g.DrawLine(split, 7, 1, 7, 14);
        }

        private static void DrawCtrlListViewGrid(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            // Header
            using var header = new SolidBrush(Color.FromArgb(200, 220, 240));
            g.FillRectangle(header, 2, 2, 12, 3);
            g.DrawLine(pen, 1, 5, 14, 5);
            g.DrawLine(pen, 6, 1, 6, 14);
            g.DrawLine(pen, 1, 9, 14, 9);
        }

        private static void DrawCtrlMenuStrip(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            using var bg = new SolidBrush(Color.FromArgb(200, 220, 240));
            g.FillRectangle(bg, 1, 1, 14, 4);
            g.DrawRectangle(pen, 1, 1, 14, 4);
            g.DrawLine(pen, 5, 1, 5, 5);
            g.DrawLine(pen, 10, 1, 10, 5);
            // Dropdown
            g.DrawRectangle(pen, 1, 5, 6, 9);
            g.DrawLine(pen, 3, 8, 5, 8);
            g.DrawLine(pen, 3, 10, 5, 10);
            g.DrawLine(pen, 3, 12, 5, 12);
        }

        private static void DrawCtrlToolbar(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            using var bg = new SolidBrush(Color.FromArgb(220, 230, 245));
            g.FillRectangle(bg, 1, 3, 14, 10);
            g.DrawRectangle(pen, 1, 3, 14, 10);
            // Buttons
            g.DrawRectangle(pen, 3, 5, 3, 6);
            g.DrawRectangle(pen, 7, 5, 3, 6);
            g.DrawRectangle(pen, 11, 5, 3, 6);
        }

        private static void DrawCtrlStatusbar(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            using var bg = new SolidBrush(Color.FromArgb(220, 230, 245));
            g.FillRectangle(bg, 0, 10, 16, 5);
            g.DrawRectangle(pen, 0, 10, 15, 4);
            g.DrawLine(pen, 8, 10, 8, 14);
            // Form outline above
            using var form = new Pen(C.LightGray, 1f);
            g.DrawRectangle(form, 0, 0, 15, 14);
        }

        private static void DrawCtrlScrollbar(Graphics g, bool horizontal)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            if (horizontal)
            {
                g.DrawRectangle(pen, 1, 5, 14, 6);
                g.DrawLine(pen, 3, 8, 5, 6); g.DrawLine(pen, 3, 8, 5, 10);
                g.DrawLine(pen, 13, 8, 11, 6); g.DrawLine(pen, 13, 8, 11, 10);
                using var thumb = new SolidBrush(C.Blue);
                g.FillRectangle(thumb, 6, 6, 4, 4);
            }
            else
            {
                g.DrawRectangle(pen, 5, 1, 6, 14);
                g.DrawLine(pen, 8, 3, 6, 5); g.DrawLine(pen, 8, 3, 10, 5);
                g.DrawLine(pen, 8, 13, 6, 11); g.DrawLine(pen, 8, 13, 10, 11);
                using var thumb = new SolidBrush(C.Blue);
                g.FillRectangle(thumb, 6, 6, 4, 4);
            }
        }

        private static void DrawCtrlFlowArrows(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.3f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            // Flow arrows
            g.DrawLine(pen, 3, 5, 7, 5);
            g.DrawLine(pen, 5, 3, 7, 5); g.DrawLine(pen, 5, 7, 7, 5);
            g.DrawLine(pen, 9, 10, 13, 10);
            g.DrawLine(pen, 11, 8, 13, 10); g.DrawLine(pen, 11, 12, 13, 10);
        }

        private static void DrawCtrlPropertyGrid(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.2f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            g.DrawLine(pen, 7, 1, 7, 14);
            for (int y = 4; y <= 12; y += 3)
                g.DrawLine(pen, 1, y, 14, y);
            // Labels
            using var font = new Font("Segoe UI", 4f);
            using var brush = new SolidBrush(C.Purple);
            g.DrawString("A", font, brush, 2, 1);
            g.DrawString("B", font, brush, 2, 4);
        }

        private static void DrawCtrlGlobe(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawEllipse(pen, 1, 1, 13, 13);
            g.DrawArc(pen, 4, 1, 7, 13, 90, 180);
            g.DrawArc(pen, 5, 1, 7, 13, 270, 180);
            g.DrawLine(pen, 1, 8, 14, 8);
            g.DrawLine(pen, 2, 5, 13, 5);
            g.DrawLine(pen, 2, 11, 13, 11);
        }

        private static void DrawCtrlContextMenu(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(pen, 2, 1, 11, 14);
            g.DrawLine(pen, 4, 4, 10, 4);
            g.DrawLine(pen, 4, 7, 10, 7);
            g.DrawLine(pen, 4, 10, 10, 10);
            // Highlight bar
            using var hl = new SolidBrush(Color.FromArgb(100, 60, 140, 220));
            g.FillRectangle(hl, 3, 6, 9, 3);
        }

        // ========== Utility ==========

        // ========== Alignment icons ==========

        private static void DrawAlignGuide(Graphics g, int mode)
        {
            // 0=left 1=right 2=top 3=bottom 4=hcenter 5=vcenter
            using var line = new Pen(C.Blue, 2f);
            using var box = new Pen(C.Orange, 1.2f);
            switch (mode)
            {
                case 0: g.DrawLine(line, 2, 1, 2, 14); g.DrawRectangle(box, 2, 3, 8, 4); g.DrawRectangle(box, 2, 9, 5, 4); break;
                case 1: g.DrawLine(line, 13, 1, 13, 14); g.DrawRectangle(box, 5, 3, 8, 4); g.DrawRectangle(box, 8, 9, 5, 4); break;
                case 2: g.DrawLine(line, 1, 2, 14, 2); g.DrawRectangle(box, 2, 2, 4, 8); g.DrawRectangle(box, 8, 2, 4, 5); break;
                case 3: g.DrawLine(line, 1, 13, 14, 13); g.DrawRectangle(box, 2, 5, 4, 8); g.DrawRectangle(box, 8, 8, 4, 5); break;
                case 4: g.DrawLine(line, 8, 1, 8, 14); g.DrawRectangle(box, 4, 3, 8, 4); g.DrawRectangle(box, 5, 9, 6, 4); break;
                case 5: g.DrawLine(line, 1, 8, 14, 8); g.DrawRectangle(box, 2, 4, 4, 8); g.DrawRectangle(box, 8, 5, 4, 6); break;
            }
        }

        private static void DrawDistHorizontal(Graphics g)
        {
            using var box = new Pen(C.Orange, 1.2f);
            using var arr = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(box, 1, 4, 3, 8); g.DrawRectangle(box, 6, 4, 3, 8); g.DrawRectangle(box, 11, 4, 3, 8);
            g.DrawLine(arr, 4, 8, 6, 8); g.DrawLine(arr, 9, 8, 11, 8);
        }

        private static void DrawDistVertical(Graphics g)
        {
            using var box = new Pen(C.Orange, 1.2f);
            using var arr = new Pen(C.Blue, 1.2f);
            g.DrawRectangle(box, 4, 1, 8, 3); g.DrawRectangle(box, 4, 6, 8, 3); g.DrawRectangle(box, 4, 11, 8, 3);
            g.DrawLine(arr, 8, 4, 8, 6); g.DrawLine(arr, 8, 9, 8, 11);
        }

        private static void DrawSameSize(Graphics g, bool w, bool h)
        {
            using var box = new Pen(C.Orange, 1.2f);
            using var arr = new Pen(C.Blue, 1.5f);
            if (w && h) { g.DrawRectangle(box, 2, 2, 11, 11); g.DrawLine(arr, 4, 8, 12, 8); g.DrawLine(arr, 8, 4, 8, 12); }
            else if (w) { g.DrawRectangle(box, 2, 4, 11, 8); g.DrawLine(arr, 4, 8, 12, 8); }
            else { g.DrawRectangle(box, 4, 2, 8, 11); g.DrawLine(arr, 8, 4, 8, 12); }
        }

        private static void DrawSnapGrid(Graphics g)
        {
            using var dot = new SolidBrush(C.Blue);
            for (int x = 2; x <= 14; x += 4)
                for (int y = 2; y <= 14; y += 4)
                    g.FillRectangle(dot, x, y, 2, 2);
        }

        private static void DrawSnapLines(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1f) { DashStyle = DashStyle.Dot };
            g.DrawLine(pen, 4, 1, 4, 14); g.DrawLine(pen, 11, 1, 11, 14);
            g.DrawLine(pen, 1, 5, 14, 5); g.DrawLine(pen, 1, 11, 14, 11);
            using var box = new Pen(C.Orange, 1.2f);
            g.DrawRectangle(box, 4, 5, 7, 6);
        }

        // ========== Layout & Grouping ==========

        private static void DrawGroup(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var inner = new Pen(C.Orange, 1f);
            g.DrawRectangle(inner, 3, 3, 4, 4); g.DrawRectangle(inner, 8, 8, 4, 4);
        }

        private static void DrawUngroup(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var inner = new Pen(C.Orange, 1f);
            g.DrawRectangle(inner, 2, 2, 4, 4); g.DrawRectangle(inner, 9, 9, 4, 4);
            DrawXSmall(g, 8, 8);
        }

        private static void DrawZOrder(Graphics g, bool front)
        {
            using var back = new SolidBrush(front ? Color.FromArgb(180, 180, 180) : C.Blue);
            using var frnt = new SolidBrush(front ? C.Blue : Color.FromArgb(180, 180, 180));
            g.FillRectangle(back, 1, 1, 8, 8);
            g.FillRectangle(frnt, 6, 6, 8, 8);
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 0.8f);
            g.DrawRectangle(pen, 1, 1, 8, 8); g.DrawRectangle(pen, 6, 6, 8, 8);
        }

        private static void DrawDockRegion(Graphics g, int side)
        {
            using var pen = new Pen(C.Gray, 1f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var fill = new SolidBrush(C.Blue);
            switch (side)
            {
                case 0: g.FillRectangle(fill, 2, 2, 4, 11); break;
                case 1: g.FillRectangle(fill, 9, 2, 4, 11); break;
                case 2: g.FillRectangle(fill, 2, 2, 11, 4); break;
                case 3: g.FillRectangle(fill, 2, 9, 11, 4); break;
                case 4: g.FillRectangle(fill, 2, 2, 11, 11); break;
            }
        }

        private static void DrawTile(Graphics g, bool horiz)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            if (horiz) { g.DrawRectangle(pen, 1, 1, 6, 13); g.DrawRectangle(pen, 8, 1, 6, 13); }
            else { g.DrawRectangle(pen, 1, 1, 13, 6); g.DrawRectangle(pen, 1, 8, 13, 6); }
        }

        // ========== Arrows ==========

        private static void DrawSolidArrow(Graphics g, int dir)
        {
            using var brush = new SolidBrush(C.Blue);
            switch (dir)
            {
                case 0: g.FillPolygon(brush, new Point[] { new(8, 1), new(14, 10), new(2, 10) }); break;
                case 1: g.FillPolygon(brush, new Point[] { new(14, 8), new(5, 2), new(5, 14) }); break;
                case 2: g.FillPolygon(brush, new Point[] { new(8, 14), new(2, 5), new(14, 5) }); break;
                case 3: g.FillPolygon(brush, new Point[] { new(1, 8), new(10, 2), new(10, 14) }); break;
            }
        }

        private static void DrawDiagArrow(Graphics g, int corner)
        {
            using var pen = new Pen(C.Blue, 2f);
            switch (corner)
            {
                case 0: g.DrawLine(pen, 12, 12, 3, 3); g.DrawLine(pen, 3, 3, 3, 8); g.DrawLine(pen, 3, 3, 8, 3); break;
                case 1: g.DrawLine(pen, 3, 12, 12, 3); g.DrawLine(pen, 12, 3, 12, 8); g.DrawLine(pen, 12, 3, 7, 3); break;
                case 2: g.DrawLine(pen, 12, 3, 3, 12); g.DrawLine(pen, 3, 12, 3, 7); g.DrawLine(pen, 3, 12, 8, 12); break;
                case 3: g.DrawLine(pen, 3, 3, 12, 12); g.DrawLine(pen, 12, 12, 12, 7); g.DrawLine(pen, 12, 12, 7, 12); break;
            }
        }

        private static void DrawExpand(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 1, 1, 5, 1); g.DrawLine(pen, 1, 1, 1, 5);
            g.DrawLine(pen, 14, 1, 10, 1); g.DrawLine(pen, 14, 1, 14, 5);
            g.DrawLine(pen, 1, 14, 5, 14); g.DrawLine(pen, 1, 14, 1, 10);
            g.DrawLine(pen, 14, 14, 10, 14); g.DrawLine(pen, 14, 14, 14, 10);
        }

        private static void DrawCollapse(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 5, 5, 1, 5); g.DrawLine(pen, 5, 5, 5, 1);
            g.DrawLine(pen, 10, 5, 14, 5); g.DrawLine(pen, 10, 5, 10, 1);
            g.DrawLine(pen, 5, 10, 1, 10); g.DrawLine(pen, 5, 10, 5, 14);
            g.DrawLine(pen, 10, 10, 14, 10); g.DrawLine(pen, 10, 10, 10, 14);
        }

        private static void DrawSortArrow(Graphics g, bool asc)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 3, 4, 3, 12);
            if (asc) { g.DrawLine(pen, 1, 6, 3, 4); g.DrawLine(pen, 5, 6, 3, 4); }
            else { g.DrawLine(pen, 1, 10, 3, 12); g.DrawLine(pen, 5, 10, 3, 12); }
            g.DrawLine(pen, 7, 4, 13, 4); g.DrawLine(pen, 7, 8, 11, 8); g.DrawLine(pen, 7, 12, 9, 12);
        }

        private static void DrawSwap(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 2, 5, 13, 5); g.DrawLine(pen, 10, 3, 13, 5); g.DrawLine(pen, 10, 7, 13, 5);
            g.DrawLine(pen, 13, 11, 2, 11); g.DrawLine(pen, 5, 9, 2, 11); g.DrawLine(pen, 5, 13, 2, 11);
        }

        private static void DrawMoveIcon(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 8, 1, 8, 14); g.DrawLine(pen, 1, 8, 14, 8);
            g.DrawLine(pen, 6, 3, 8, 1); g.DrawLine(pen, 10, 3, 8, 1);
            g.DrawLine(pen, 6, 13, 8, 15); g.DrawLine(pen, 10, 13, 8, 15);
            g.DrawLine(pen, 3, 6, 1, 8); g.DrawLine(pen, 3, 10, 1, 8);
            g.DrawLine(pen, 13, 6, 15, 8); g.DrawLine(pen, 13, 10, 15, 8);
        }

        // ========== Media ==========

        private static void DrawRewind(Graphics g)
        {
            using var brush = new SolidBrush(C.Blue);
            g.FillPolygon(brush, new Point[] { new(8, 8), new(15, 3), new(15, 13) });
            g.FillPolygon(brush, new Point[] { new(1, 8), new(8, 3), new(8, 13) });
        }

        private static void DrawFastForward(Graphics g)
        {
            using var brush = new SolidBrush(C.Blue);
            g.FillPolygon(brush, new Point[] { new(1, 3), new(8, 8), new(1, 13) });
            g.FillPolygon(brush, new Point[] { new(8, 3), new(15, 8), new(8, 13) });
        }

        private static void DrawSkip(Graphics g, bool prev)
        {
            using var brush = new SolidBrush(C.Blue);
            using var pen = new Pen(C.Blue, 2f);
            if (prev) { g.FillPolygon(brush, new Point[] { new(12, 3), new(5, 8), new(12, 13) }); g.DrawLine(pen, 3, 3, 3, 13); }
            else { g.FillPolygon(brush, new Point[] { new(4, 3), new(11, 8), new(4, 13) }); g.DrawLine(pen, 13, 3, 13, 13); }
        }

        private static void DrawVolume(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.FillPolygon(new SolidBrush(C.Blue), new Point[] { new(2, 5), new(5, 5), new(9, 2), new(9, 14), new(5, 11), new(2, 11) });
            g.DrawArc(pen, 10, 4, 4, 8, -60, 120);
        }

        private static void DrawMute(Graphics g)
        {
            DrawVolume(g);
            DrawStrikethrough(g);
        }

        // ========== Development ==========

        private static void DrawCodeBrackets(Graphics g)
        {
            using var pen = new Pen(C.Teal, 2f);
            g.DrawLine(pen, 5, 2, 2, 8); g.DrawLine(pen, 2, 8, 5, 14);
            g.DrawLine(pen, 11, 2, 14, 8); g.DrawLine(pen, 14, 8, 11, 14);
        }

        private static void DrawTerminal(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.3f);
            g.DrawRectangle(pen, 1, 2, 13, 12);
            g.DrawLine(pen, 3, 6, 6, 9); g.DrawLine(pen, 6, 9, 3, 12);
            g.DrawLine(pen, 8, 11, 12, 11);
        }

        private static void DrawBug(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.3f);
            g.DrawEllipse(pen, 4, 5, 8, 9);
            g.DrawEllipse(pen, 5, 2, 6, 4);
            g.DrawLine(pen, 3, 4, 1, 2); g.DrawLine(pen, 13, 4, 15, 2);
            g.DrawLine(pen, 3, 8, 1, 8); g.DrawLine(pen, 13, 8, 15, 8);
        }

        private static void DrawHammer(Graphics g)
        {
            using var pen = new Pen(C.Gray, 2f);
            g.DrawLine(pen, 4, 12, 10, 4);
            using var head = new SolidBrush(C.Orange);
            g.FillRectangle(head, 7, 1, 7, 5);
        }

        private static void DrawPackage(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.3f);
            g.DrawRectangle(pen, 2, 3, 12, 11);
            g.DrawLine(pen, 2, 7, 14, 7);
            g.DrawLine(pen, 6, 3, 6, 7); g.DrawLine(pen, 10, 3, 10, 7);
        }

        private static void DrawPluginIcon(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.5f);
            g.DrawRectangle(pen, 3, 6, 10, 8);
            g.DrawLine(pen, 5, 6, 5, 2); g.DrawLine(pen, 11, 6, 11, 2);
        }

        private static void DrawApiIcon(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.3f);
            g.DrawEllipse(pen, 1, 5, 5, 5); g.DrawEllipse(pen, 10, 5, 5, 5);
            g.DrawLine(pen, 6, 8, 10, 8);
            g.DrawEllipse(pen, 5, 1, 5, 5);
            g.DrawLine(pen, 8, 6, 8, 5);
        }

        private static void DrawBranch(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.5f);
            g.DrawLine(pen, 4, 3, 4, 13);
            g.DrawLine(pen, 4, 6, 12, 6);
            g.FillEllipse(new SolidBrush(C.Purple), 2, 1, 4, 4);
            g.FillEllipse(new SolidBrush(C.Purple), 2, 11, 4, 4);
            g.FillEllipse(new SolidBrush(C.Purple), 10, 4, 4, 4);
        }

        private static void DrawMerge(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.5f);
            g.DrawLine(pen, 4, 3, 4, 13);
            g.DrawLine(pen, 12, 6, 4, 10);
            g.FillEllipse(new SolidBrush(C.Purple), 2, 1, 4, 4);
            g.FillEllipse(new SolidBrush(C.Purple), 2, 11, 4, 4);
            g.FillEllipse(new SolidBrush(C.Purple), 10, 4, 4, 4);
        }

        private static void DrawCommit(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.5f);
            g.DrawLine(pen, 8, 1, 8, 5); g.DrawLine(pen, 8, 11, 8, 15);
            g.DrawEllipse(pen, 4, 4, 8, 8);
            g.FillEllipse(new SolidBrush(C.Purple), 6, 6, 4, 4);
        }

        private static void DrawTagIcon(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.3f);
            g.DrawPolygon(pen, new Point[] { new(1, 1), new(8, 1), new(14, 7), new(7, 14), new(1, 8) });
            g.FillEllipse(new SolidBrush(C.Orange), 3, 3, 3, 3);
        }

        // ========== Communication ==========

        private static void DrawChatBubble(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            using var path = RoundRect(1, 1, 13, 10, 3);
            g.DrawPath(pen, path);
            g.DrawPolygon(pen, new Point[] { new(4, 11), new(4, 15), new(8, 11) });
        }

        private static void DrawPhone(Graphics g)
        {
            using var pen = new Pen(C.Green, 1.5f);
            g.DrawArc(pen, 1, 1, 6, 6, 180, 90);
            g.DrawLine(pen, 1, 4, 1, 8);
            g.DrawLine(pen, 1, 8, 4, 11);
            g.DrawLine(pen, 4, 11, 8, 14);
            g.DrawLine(pen, 8, 14, 11, 14);
            g.DrawArc(pen, 8, 8, 6, 6, 0, 90);
        }

        private static void DrawBell(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.5f);
            g.DrawArc(pen, 3, 2, 10, 10, 180, 180);
            g.DrawLine(pen, 3, 7, 2, 12);
            g.DrawLine(pen, 13, 7, 14, 12);
            g.DrawLine(pen, 2, 12, 14, 12);
            g.FillEllipse(new SolidBrush(C.Orange), 6, 13, 4, 3);
        }

        private static void DrawShareIcon(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.FillEllipse(new SolidBrush(C.Blue), 10, 1, 4, 4);
            g.FillEllipse(new SolidBrush(C.Blue), 10, 11, 4, 4);
            g.FillEllipse(new SolidBrush(C.Blue), 1, 6, 4, 4);
            g.DrawLine(pen, 5, 8, 10, 3); g.DrawLine(pen, 5, 8, 10, 13);
        }

        private static void DrawVertArrowBox(Graphics g, bool down)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawRectangle(pen, 1, 1, 13, 13);
            if (down) { g.DrawLine(pen, 8, 4, 8, 11); g.DrawLine(pen, 5, 9, 8, 12); g.DrawLine(pen, 11, 9, 8, 12); }
            else { g.DrawLine(pen, 8, 11, 8, 4); g.DrawLine(pen, 5, 6, 8, 3); g.DrawLine(pen, 11, 6, 8, 3); }
        }

        private static void DrawCloud(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawArc(pen, 2, 4, 8, 8, 120, 180);
            g.DrawArc(pen, 5, 2, 8, 8, -30, 180);
            g.DrawLine(pen, 2, 11, 14, 11);
        }

        private static void DrawRss(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.5f);
            g.FillEllipse(new SolidBrush(C.Orange), 2, 11, 3, 3);
            g.DrawArc(pen, 1, 5, 10, 10, 240, 60);
            g.DrawArc(pen, -1, 1, 14, 14, 260, 50);
        }

        // ========== Shapes & Symbols ==========

        private static void DrawHeart(Graphics g)
        {
            using var brush = new SolidBrush(C.Red);
            using var path = new GraphicsPath();
            path.AddArc(1, 2, 7, 6, 180, 180);
            path.AddArc(8, 2, 7, 6, 180, 180);
            path.AddLine(15, 5, 8, 14);
            path.AddLine(8, 14, 1, 5);
            g.FillPath(brush, path);
        }

        private static void DrawLightning(Graphics g)
        {
            using var brush = new SolidBrush(C.Yellow);
            using var pen = new Pen(C.Orange, 1f);
            var pts = new Point[] { new(9, 0), new(5, 7), new(9, 7), new(6, 15), new(11, 7), new(7, 7) };
            g.FillPolygon(brush, pts); g.DrawPolygon(pen, pts);
        }

        private static void DrawSun(Graphics g)
        {
            using var brush = new SolidBrush(C.Yellow);
            g.FillEllipse(brush, 4, 4, 8, 8);
            using var pen = new Pen(C.Orange, 1.2f);
            for (int a = 0; a < 360; a += 45)
            {
                double r1 = a * Math.PI / 180.0;
                g.DrawLine(pen, 8 + (int)(5 * Math.Cos(r1)), 8 + (int)(5 * Math.Sin(r1)),
                                8 + (int)(7 * Math.Cos(r1)), 8 + (int)(7 * Math.Sin(r1)));
            }
        }

        private static void DrawMoon(Graphics g)
        {
            using var brush = new SolidBrush(C.Yellow);
            g.FillEllipse(brush, 2, 2, 12, 12);
            using var bg = new SolidBrush(Color.FromArgb(240, 240, 240));
            g.FillEllipse(bg, 5, 1, 12, 12);
        }

        private static void DrawTarget(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.3f);
            g.DrawEllipse(pen, 1, 1, 13, 13);
            g.DrawEllipse(pen, 4, 4, 7, 7);
            g.FillEllipse(new SolidBrush(C.Red), 6, 6, 3, 3);
        }

        private static void DrawShield(Graphics g)
        {
            using var brush = new SolidBrush(C.Blue);
            var pts = new Point[] { new(8, 1), new(14, 4), new(13, 10), new(8, 15), new(3, 10), new(2, 4) };
            g.FillPolygon(brush, pts);
            using var check = new Pen(C.White, 2f);
            g.DrawLine(check, 5, 8, 7, 11); g.DrawLine(check, 7, 11, 11, 5);
        }

        private static void DrawTrophy(Graphics g)
        {
            using var pen = new Pen(C.Gold, 1.5f);
            using var brush = new SolidBrush(C.Gold);
            g.FillRectangle(brush, 5, 2, 6, 7);
            g.DrawArc(pen, 2, 2, 5, 6, 90, 180);
            g.DrawArc(pen, 9, 2, 5, 6, 270, 180);
            g.DrawLine(pen, 8, 9, 8, 12);
            g.DrawLine(pen, 5, 12, 11, 12);
        }

        private static void DrawGift(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.3f);
            g.DrawRectangle(pen, 2, 5, 12, 9);
            g.DrawLine(pen, 8, 5, 8, 14);
            g.DrawLine(pen, 2, 8, 14, 8);
            g.DrawArc(pen, 3, 1, 5, 5, 0, -180);
            g.DrawArc(pen, 8, 1, 5, 5, 180, 180);
        }

        private static void DrawBookmark(Graphics g)
        {
            using var brush = new SolidBrush(C.Blue);
            g.FillPolygon(brush, new Point[] { new(3, 1), new(13, 1), new(13, 14), new(8, 10), new(3, 14) });
        }

        private static void DrawTagLabel(Graphics g)
        {
            using var pen = new Pen(C.Teal, 1.3f);
            g.DrawPolygon(pen, new Point[] { new(1, 5), new(5, 1), new(14, 1), new(14, 10), new(10, 14), new(1, 14) });
            g.DrawLine(pen, 5, 1, 5, 14);
        }

        private static void DrawPower(Graphics g)
        {
            using var pen = new Pen(C.Red, 2f);
            g.DrawLine(pen, 8, 1, 8, 7);
            g.DrawArc(pen, 2, 4, 12, 11, -30, -120);
        }

        // ========== Misc ==========

        private static void DrawFunnel(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawLine(pen, 1, 2, 15, 2);
            g.DrawLine(pen, 1, 2, 6, 9);
            g.DrawLine(pen, 15, 2, 10, 9);
            g.DrawLine(pen, 6, 9, 6, 14);
            g.DrawLine(pen, 10, 9, 10, 14);
        }

        private static void DrawLayers(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.2f);
            g.DrawPolygon(pen, new Point[] { new(8, 1), new(15, 5), new(8, 9), new(1, 5) });
            g.DrawLine(pen, 1, 8, 8, 12); g.DrawLine(pen, 15, 8, 8, 12);
        }

        private static void DrawRuler(Graphics g)
        {
            using var pen = new Pen(C.Orange, 1.2f);
            g.DrawRectangle(pen, 1, 4, 14, 8);
            for (int x = 3; x <= 13; x += 2)
                g.DrawLine(pen, x, 4, x, x % 4 == 0 ? 8 : 6);
        }

        private static void DrawWrench(Graphics g)
        {
            using var pen = new Pen(C.Gray, 1.8f);
            g.DrawLine(pen, 3, 13, 10, 4);
            g.DrawArc(pen, 7, 1, 7, 7, 180, 210);
        }

        private static void DrawMagicWand(Graphics g)
        {
            using var pen = new Pen(C.Purple, 1.5f);
            g.DrawLine(pen, 1, 14, 10, 4);
            using var star = new SolidBrush(C.Yellow);
            g.FillPolygon(star, new PointF[] { new(12, 1), new(13, 4), new(15, 2), new(13, 5), new(15, 6), new(12, 5), new(11, 8), new(11, 5), new(8, 5), new(11, 4) });
        }

        private static void DrawRecycle(Graphics g)
        {
            using var pen = new Pen(C.Green, 1.5f);
            g.DrawArc(pen, 1, 1, 14, 14, 0, 120);
            g.DrawArc(pen, 1, 1, 14, 14, 120, 120);
            g.DrawArc(pen, 1, 1, 14, 14, 240, 120);
        }

        private static void DrawFullscreen(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 1, 1, 5, 1); g.DrawLine(pen, 1, 1, 1, 5);
            g.DrawLine(pen, 14, 1, 10, 1); g.DrawLine(pen, 14, 1, 14, 5);
            g.DrawLine(pen, 1, 14, 5, 14); g.DrawLine(pen, 1, 14, 1, 10);
            g.DrawLine(pen, 14, 14, 10, 14); g.DrawLine(pen, 14, 14, 14, 10);
            g.DrawLine(pen, 1, 1, 6, 6); g.DrawLine(pen, 14, 14, 9, 9);
        }

        private static void DrawExitFullscreen(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.5f);
            g.DrawLine(pen, 6, 6, 2, 6); g.DrawLine(pen, 6, 6, 6, 2);
            g.DrawLine(pen, 10, 6, 14, 6); g.DrawLine(pen, 10, 6, 10, 2);
            g.DrawLine(pen, 6, 10, 2, 10); g.DrawLine(pen, 6, 10, 6, 14);
            g.DrawLine(pen, 10, 10, 14, 10); g.DrawLine(pen, 10, 10, 10, 14);
        }

        private static void DrawRestore(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawRectangle(pen, 4, 1, 10, 10);
            g.DrawRectangle(pen, 1, 4, 10, 10);
        }

        private static void DrawThumbsUp(Graphics g)
        {
            using var pen = new Pen(C.Blue, 1.3f);
            g.DrawLine(pen, 5, 14, 5, 7);
            g.DrawLine(pen, 5, 7, 8, 2);
            g.DrawArc(pen, 5, 6, 9, 8, -90, 180);
            g.DrawLine(pen, 5, 14, 14, 14);
            g.DrawLine(pen, 2, 7, 2, 14);
            g.DrawLine(pen, 2, 14, 5, 14);
        }

        private static void DrawThumbsDown(Graphics g)
        {
            using var pen = new Pen(C.Red, 1.3f);
            g.DrawLine(pen, 5, 2, 5, 9);
            g.DrawLine(pen, 5, 9, 8, 14);
            g.DrawArc(pen, 5, 2, 9, 8, 90, 180);
            g.DrawLine(pen, 5, 2, 14, 2);
            g.DrawLine(pen, 2, 2, 2, 9);
            g.DrawLine(pen, 2, 2, 5, 2);
        }

        // ========== Utility ==========

        private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            if (d > w) d = w;
            if (d > h) d = h;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
