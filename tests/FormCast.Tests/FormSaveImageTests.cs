// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Drawing;
using System.IO;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Tests for <c>@FORMSAVEIMAGE</c> dispatch and the
    /// <see cref="FormRealizer.SaveImage"/> /
    /// <see cref="FormRealizer.SnapshotToBitmap"/> helpers. Self-
    /// consistency only: rendering the same hidden form twice with
    /// the same descriptor must produce identical bitmaps. Stored
    /// golden bitmaps are deferred until the Phase 12 documentation
    /// auto-generation pipeline can publish them with the right DPI
    /// and font fallback assumptions baked in.
    /// </summary>
    public class FormSaveImageTests : IDisposable
    {
        private readonly global::FormCast.Plugin _plugin;
        private readonly string _tempDir;

        public FormSaveImageTests()
        {
            Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
            global::FormCast.HeadlessMode.Refresh();
            _plugin = new global::FormCast.Plugin();
            _plugin.Initialize();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "FormCast.SaveImageTests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _plugin.Shutdown(endProcess: false);
            Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
            global::FormCast.HeadlessMode.Refresh();
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; the OS will reap %TEMP% eventually.
            }
        }

        private static StringBuilder Buf(string s = "") => new StringBuilder(s);

        private string OpenForm(string name = "img", int w = 200, int h = 100)
        {
            var args = Buf($"form,{name},10,20,{w},{h}");
            _plugin.f_FORMOPEN(args);
            return args.ToString();
        }

        private string TempPng(string label) =>
            Path.Combine(_tempDir, label + ".png");

        // ---- Validation ----

        [Fact]
        public void FORMSAVEIMAGE_empty_args_returns_bad_args()
        {
            var args = Buf(string.Empty);
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMSAVEIMAGE_one_arg_returns_bad_args()
        {
            var args = Buf("L:0:1");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMSAVEIMAGE_three_args_returns_bad_args()
        {
            var args = Buf("L:0:1,foo.png,extra");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMSAVEIMAGE_unparseable_handle_returns_invalid_handle()
        {
            var args = Buf("notahandle," + TempPng("bad-handle"));
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20100", args.ToString());
        }

        [Fact]
        public void FORMSAVEIMAGE_unknown_handle_returns_invalid_handle()
        {
            var args = Buf("L:0:99999," + TempPng("missing"));
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20100", args.ToString());
        }

        [Fact]
        public void FORMSAVEIMAGE_empty_path_returns_bad_args()
        {
            string h = OpenForm("p");
            var args = Buf($"{h},");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("20101", args.ToString());
        }

        // ---- Happy path ----

        [Fact]
        public void FORMSAVEIMAGE_writes_a_real_png_file()
        {
            string h = OpenForm("png", 220, 140);
            string path = TempPng("png-output");

            var args = Buf($"{h},{path}");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("0", args.ToString());

            Assert.True(File.Exists(path), $"Expected PNG at {path}");
            var info = new FileInfo(path);
            Assert.True(info.Length > 100, $"PNG suspiciously small: {info.Length} bytes");

            // Sanity: it should round-trip through Bitmap with sensible
            // dimensions. Form.Width includes the chrome, so it's >= 220.
            using var bmp = new Bitmap(path);
            Assert.True(bmp.Width >= 220);
            Assert.True(bmp.Height >= 140);
        }

        [Fact]
        public void FORMSAVEIMAGE_two_renders_of_same_form_are_pixel_identical()
        {
            // Self-consistency check via SnapshotToBitmap, which is the
            // mechanism Phase 11 designer round-trip tests will use.
            string h = OpenForm("dup", 240, 160);
            // Add a couple of controls so the render is non-trivial.
            _plugin.f_FORMADD(Buf($"{h},lbl,LABEL,8,8,200,20,Caption"));
            _plugin.f_FORMADD(Buf($"{h},btn,BUTTON,8,40,80,24,OK"));

            // Realize once via FORMSHOW so both snapshots hit the
            // same Form instance.
            _plugin.f_FORMSHOW(Buf(h));

            // Reach into the realized form by re-rendering through
            // the dispatch path. Two snapshots via SnapshotToBitmap
            // would require the realized Form reference, which is not
            // exposed. Instead we go through @FORMSAVEIMAGE twice and
            // diff the resulting files.
            string path1 = TempPng("dup-1");
            string path2 = TempPng("dup-2");

            var args = Buf($"{h},{path1}");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("0", args.ToString());

            args = Buf($"{h},{path2}");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("0", args.ToString());

            using var bmp1 = new Bitmap(path1);
            using var bmp2 = new Bitmap(path2);
            var result = BitmapDiff.Compare(bmp1, bmp2);
            Assert.False(result.SizeMismatch);
            Assert.True(result.IsIdentical,
                $"Expected pixel-identical re-renders; differing={result.DifferingPixels}/{result.TotalPixels}");
        }

        [Fact]
        public void FORMSAVEIMAGE_lazily_realizes_when_FORMSHOW_was_skipped()
        {
            // Skip @FORMSHOW; @FORMSAVEIMAGE should realize on its own.
            string h = OpenForm("lazy", 200, 100);
            int seq = int.Parse(h.Split(':')[2]);
            Assert.False(_plugin.IsRealized(seq));

            string path = TempPng("lazy");
            var args = Buf($"{h},{path}");
            _plugin.f_FORMSAVEIMAGE(args);
            Assert.Equal("0", args.ToString());

            Assert.True(_plugin.IsRealized(seq));
            Assert.True(File.Exists(path));
        }
    }
}
