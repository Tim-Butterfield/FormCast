// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Disable xUnit test parallelization across the FormCast.Tests
// assembly. Several test classes (FormShowTests, FormSaveImageTests,
// ForcedShutdownTests, GuiHostThreadTests, etc.) spin up real
// WinForms message loops and create real HWNDs in the test process.
// Running them in parallel allows windows from one class to bleed
// into the HWND enumeration of another, which makes the // forced-shutdown test (which counts windows before/after) flaky.
//
// The cost is a slower test run; the gain is determinism.

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
