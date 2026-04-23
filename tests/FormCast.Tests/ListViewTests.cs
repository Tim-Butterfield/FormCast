// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using FormCast.Forms;
using FormCast.Forms.Controls;
using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the LISTVIEW control: descriptor-side prop bag
 /// management (addcolumn / additem / clear / multiselect / sort),
 /// type-aware sort comparer (text / number / date / size / icon
 /// fallback), and realizer-side ListView construction with
 /// columns, items, and an installed
 /// <see cref="ListViewItemSorter"/>.
 /// </summary>
 public class ListViewTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly GuiHostThread _host;

 public ListViewTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 _host = new GuiHostThread();
 _host.Start();
 }

 public void Dispose()
 {
 _host.Stop();
 _host.Dispose();
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "lvtest", int w = 600, int h = 400)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 private void AddListView(string handle, string id = "lv",
 int x = 5, int y = 5, int w = 580, int h = 380)
 {
 var args = Buf($"{handle},{id},LISTVIEW,{x},{y},{w},{h},");
 _plugin.f_FORMADD(args);
 }

 private string Set(string handle, string ctrl, string prop, string value)
 {
 var args = Buf($"{handle},{ctrl},{prop},{value}");
 _plugin.f_FORMSET(args);
 return args.ToString();
 }

 private string Get(string handle, string ctrl, string prop)
 {
 var args = Buf($"{handle},{ctrl},{prop}");
 _plugin.f_FORMGET(args);
 return args.ToString();
 }

 // -----------------------------------------------------------------
 // Recognition: LISTVIEW is a known type
 // -----------------------------------------------------------------

 [Fact]
 public void LISTVIEW_is_a_recognized_control_type()
 {
 Assert.True(ControlBuilders.IsRecognizedType("LISTVIEW"));
 Assert.True(ControlBuilders.IsRecognizedType("listview"));
 }

 [Fact]
 public void FORMADD_LISTVIEW_succeeds()
 {
 string h = OpenForm();
 var args = Buf($"{h},lv,LISTVIEW,5,5,400,200,");
 _plugin.f_FORMADD(args);
 Assert.Equal("0", args.ToString());
 }

 // -----------------------------------------------------------------
 // FORMSET addcolumn / additem auto-number into the prop bag
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSET_addcolumn_appends_into_prop_bag_with_dense_numbering()
 {
 string h = OpenForm();
 AddListView(h);

 Assert.Equal("0", Set(h, "lv", "addcolumn", "Name|320|text"));
 Assert.Equal("0", Set(h, "lv", "addcolumn", "Size|100|number"));
 Assert.Equal("0", Set(h, "lv", "addcolumn", "Modified|220|date"));

 // Read the descriptor's prop bag through the test peeker.
 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var lv = form.Controls.First(c => c.Id == "lv");
 Assert.Equal("Name|320|text", lv.Properties["_lv.col.0"]);
 Assert.Equal("Size|100|number", lv.Properties["_lv.col.1"]);
 Assert.Equal("Modified|220|date", lv.Properties["_lv.col.2"]);
 }

 [Fact]
 public void FORMSET_additem_appends_into_prop_bag_with_dense_numbering()
 {
 string h = OpenForm();
 AddListView(h);

 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "addcolumn", "Size|100|number");

 Set(h, "lv", "additem", "alpha.txt|1024");
 Set(h, "lv", "additem", "beta.txt|2048");
 Set(h, "lv", "additem", "gamma.txt|512");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var lv = form.Controls.First(c => c.Id == "lv");
 Assert.Equal("alpha.txt|1024", lv.Properties["_lv.item.0"]);
 Assert.Equal("beta.txt|2048", lv.Properties["_lv.item.1"]);
 Assert.Equal("gamma.txt|512", lv.Properties["_lv.item.2"]);
 }

 [Fact]
 public void FORMSET_clear_removes_columns_and_items()
 {
 string h = OpenForm();
 AddListView(h);
 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "additem", "row1");
 Set(h, "lv", "additem", "row2");

 Assert.Equal("0", Set(h, "lv", "clear", string.Empty));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var lv = form.Controls.First(c => c.Id == "lv");
 Assert.False(lv.Properties.ContainsKey("_lv.col.0"));
 Assert.False(lv.Properties.ContainsKey("_lv.item.0"));
 Assert.False(lv.Properties.ContainsKey("_lv.item.1"));
 }

 [Fact]
 public void FORMSET_multiselect_and_sort_land_in_prop_bag()
 {
 string h = OpenForm();
 AddListView(h);
 Set(h, "lv", "multiselect", "1");
 Set(h, "lv", "sort", "Name|asc");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var lv = form.Controls.First(c => c.Id == "lv");
 Assert.Equal("1", lv.Properties["_lv.multiselect"]);
 Assert.Equal("Name|asc", lv.Properties["_lv.sort"]);
 }

 // -----------------------------------------------------------------
 // ListViewItemSorter: type-aware comparison
 // -----------------------------------------------------------------

 [Fact]
 public void Sorter_text_compares_lexically_case_insensitive()
 {
 Assert.True(ListViewItemSorter.CompareByType("alpha", "BETA", "text") < 0);
 Assert.Equal(0, ListViewItemSorter.CompareByType("foo", "FOO", "text"));
 }

 [Fact]
 public void Sorter_number_compares_numerically()
 {
 // 9 < 10 numerically (lexical would put "10" before "9")
 Assert.True(ListViewItemSorter.CompareByType("9", "10", "number") < 0);
 Assert.True(ListViewItemSorter.CompareByType("3.14", "2.71", "number") > 0);
 }

 [Fact]
 public void Sorter_date_compares_chronologically()
 {
 Assert.True(ListViewItemSorter.CompareByType(
 "2026-01-01", "2026-12-31", "date") < 0);
 Assert.True(ListViewItemSorter.CompareByType(
 "2025-12-31 23:59", "2026-01-01 00:00", "date") < 0);
 }

 [Fact]
 public void Sorter_size_compares_by_parsed_bytes()
 {
 // 1.5 KB > 1 KB > 999 B
 Assert.True(ListViewItemSorter.CompareByType("1.5 KB", "1 KB", "size") > 0);
 Assert.True(ListViewItemSorter.CompareByType("1 KB", "999 B", "size") > 0);
 // 1 MB > 999 KB
 Assert.True(ListViewItemSorter.CompareByType("1 MB", "999 KB", "size") > 0);
 }

 [Fact]
 public void ParseSizeBytes_recognizes_common_suffixes()
 {
 Assert.Equal(1024L, ListViewItemSorter.ParseSizeBytes("1 KB"));
 Assert.Equal(1024L, ListViewItemSorter.ParseSizeBytes("1KB"));
 Assert.Equal(1536L, ListViewItemSorter.ParseSizeBytes("1.5KB"));
 Assert.Equal(1024L * 1024L, ListViewItemSorter.ParseSizeBytes("1 MB"));
 Assert.Equal(1024L * 1024L * 1024L, ListViewItemSorter.ParseSizeBytes("1 GB"));
 Assert.Equal(120L, ListViewItemSorter.ParseSizeBytes("120"));
 Assert.Equal(120L, ListViewItemSorter.ParseSizeBytes("120 B"));
 Assert.Equal(-1L, ListViewItemSorter.ParseSizeBytes("garbage"));
 }

 [Fact]
 public void Sorter_icon_falls_back_to_lexical_in_M9_1()
 {
 // defers icon-index sorting; the comparer falls back
 // to OrdinalIgnoreCase lexical compare.
 Assert.True(ListViewItemSorter.CompareByType("ext:.txt", "ext:.zip", "icon") < 0);
 }

 // -----------------------------------------------------------------
 // ListViewSortState + Compare end-to-end via the IComparer
 // -----------------------------------------------------------------

 [Fact]
 public void Sorter_respects_ascending_descending_flag()
 {
 var state = new ListViewSortState
 {
 ColumnTypes = new[] { "number" },
 SortColumn = 0,
 Ascending = true,
 };
 var sorter = new ListViewItemSorter(state);
 var a = new ListViewItem("9");
 var b = new ListViewItem("10");

 Assert.True(sorter.Compare(a, b) < 0);
 state.Ascending = false;
 Assert.True(sorter.Compare(a, b) > 0);
 }

 // -----------------------------------------------------------------
 // Realizer: BuildListView produces a populated ListView
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_listview_creates_columns_and_items_on_GUI_thread()
 {
 string h = OpenForm("realtest");
 AddListView(h);
 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "addcolumn", "Size|100|number");
 Set(h, "lv", "additem", "alpha|9");
 Set(h, "lv", "additem", "beta|10");
 Set(h, "lv", "additem", "gamma|2");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var lv = (ListView)realized.Controls[0];
 Assert.Equal(View.Details, lv.View);
 Assert.Equal(2, lv.Columns.Count);
 Assert.Equal("Name", lv.Columns[0].Text);
 Assert.Equal("Size", lv.Columns[1].Text);
 Assert.Equal(3, lv.Items.Count);
 Assert.Equal("alpha", lv.Items[0].Text);
 Assert.Equal("9", lv.Items[0].SubItems[1].Text);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 [Fact]
 public void Realize_listview_initial_sort_orders_items_by_number_column()
 {
 string h = OpenForm("sorted");
 AddListView(h);
 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "addcolumn", "Size|100|number");
 Set(h, "lv", "additem", "ten|10");
 Set(h, "lv", "additem", "two|2");
 Set(h, "lv", "additem", "nine|9");
 Set(h, "lv", "sort", "1|asc");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var lv = (ListView)realized.Controls[0];
 // Sorted ascending by Size column: 2, 9, 10
 Assert.Equal("two", lv.Items[0].Text);
 Assert.Equal("nine", lv.Items[1].Text);
 Assert.Equal("ten", lv.Items[2].Text);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 [Fact]
 public void Realize_listview_multiselect_flag_is_honored()
 {
 string h = OpenForm("ms");
 AddListView(h);
 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "multiselect", "1");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var lv = (ListView)realized.Controls[0];
 Assert.True(lv.MultiSelect);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // FormSerializer round-trip: a LISTVIEW with columns and items
 // survives serialize -> deserialize unchanged.
 // -----------------------------------------------------------------

 [Fact]
 public void LISTVIEW_descriptor_round_trips_through_FormSerializer()
 {
 string h = OpenForm("rt");
 AddListView(h);
 Set(h, "lv", "addcolumn", "Name|200|text");
 Set(h, "lv", "addcolumn", "Size|100|number");
 Set(h, "lv", "additem", "alpha|9");
 Set(h, "lv", "additem", "beta|10");
 Set(h, "lv", "sort", "1|desc");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 string json = FormSerializer.Serialize(form);
 FormDescriptor reloaded = FormSerializer.Deserialize(json);

 var lv = reloaded.Controls.First(c => c.Id == "lv");
 Assert.Equal("Name|200|text", lv.Properties["_lv.col.0"]);
 Assert.Equal("Size|100|number", lv.Properties["_lv.col.1"]);
 Assert.Equal("alpha|9", lv.Properties["_lv.item.0"]);
 Assert.Equal("beta|10", lv.Properties["_lv.item.1"]);
 Assert.Equal("1|desc", lv.Properties["_lv.sort"]);
 }
 }
}
