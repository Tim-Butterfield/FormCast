// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LocalFormRegistry"/>. Covers allocation,
    /// lookup, free, name lookup, snapshot semantics, and basic
    /// concurrency. No WinForms involved.
    /// </summary>
    public class LocalFormRegistryTests
    {
        [Fact]
        public void New_registry_is_empty()
        {
            var r = new LocalFormRegistry();
            Assert.Equal(0, r.Count);
            Assert.Empty(r.AllHandles());
        }

        [Fact]
        public void Allocate_returns_increasing_handles()
        {
            var r = new LocalFormRegistry();
            int h1 = r.Allocate(new FormDescriptor { Name = "a" });
            int h2 = r.Allocate(new FormDescriptor { Name = "b" });
            int h3 = r.Allocate(new FormDescriptor { Name = "c" });

            Assert.True(h2 > h1);
            Assert.True(h3 > h2);
            Assert.Equal(3, r.Count);
        }

        [Fact]
        public void Lookup_returns_the_allocated_descriptor()
        {
            var r = new LocalFormRegistry();
            var d = new FormDescriptor { Name = "settings", Width = 400 };
            int h = r.Allocate(d);

            FormDescriptor? lookup = r.Lookup(h);
            Assert.NotNull(lookup);
            Assert.Same(d, lookup);
            Assert.Equal("settings", lookup!.Name);
            Assert.Equal(400, lookup.Width);
        }

        [Fact]
        public void Lookup_unknown_handle_returns_null()
        {
            var r = new LocalFormRegistry();
            Assert.Null(r.Lookup(42));
            Assert.Null(r.Lookup(-1));
            Assert.Null(r.Lookup(0));
        }

        [Fact]
        public void Free_removes_the_handle()
        {
            var r = new LocalFormRegistry();
            int h = r.Allocate(new FormDescriptor { Name = "x" });
            Assert.True(r.Free(h));
            Assert.Null(r.Lookup(h));
            Assert.Equal(0, r.Count);
        }

        [Fact]
        public void Free_unknown_handle_returns_false()
        {
            var r = new LocalFormRegistry();
            Assert.False(r.Free(99));
        }

        [Fact]
        public void Freed_handle_is_never_reused()
        {
            // Stale-handle safety: a BTM that holds a handle past its
            // @FORMCLOSE must never accidentally collide with a fresh
            // form opened later in the same session.
            var r = new LocalFormRegistry();
            int h1 = r.Allocate(new FormDescriptor { Name = "a" });
            r.Free(h1);
            int h2 = r.Allocate(new FormDescriptor { Name = "b" });
            Assert.NotEqual(h1, h2);
            Assert.True(h2 > h1);
            Assert.Null(r.Lookup(h1));
        }

        [Fact]
        public void FindByName_returns_handle_for_match()
        {
            var r = new LocalFormRegistry();
            int h = r.Allocate(new FormDescriptor { Name = "settings" });
            r.Allocate(new FormDescriptor { Name = "logs" });
            r.Allocate(new FormDescriptor { Name = "main" });

            Assert.Equal(h, r.FindByName("settings"));
        }

        [Fact]
        public void FindByName_is_case_insensitive()
        {
            var r = new LocalFormRegistry();
            int h = r.Allocate(new FormDescriptor { Name = "Settings" });
            Assert.Equal(h, r.FindByName("settings"));
            Assert.Equal(h, r.FindByName("SETTINGS"));
            Assert.Equal(h, r.FindByName("SeTtInGs"));
        }

        [Fact]
        public void FindByName_returns_minus_one_for_unknown()
        {
            var r = new LocalFormRegistry();
            r.Allocate(new FormDescriptor { Name = "a" });
            Assert.Equal(-1, r.FindByName("not-there"));
            Assert.Equal(-1, r.FindByName(string.Empty));
        }

        [Fact]
        public void AllHandles_returns_a_snapshot()
        {
            var r = new LocalFormRegistry();
            int h1 = r.Allocate(new FormDescriptor { Name = "a" });
            int h2 = r.Allocate(new FormDescriptor { Name = "b" });

            IReadOnlyCollection<int> snapshot = r.AllHandles();
            Assert.Equal(2, snapshot.Count);
            Assert.Contains(h1, snapshot);
            Assert.Contains(h2, snapshot);

            // Mutating the registry after the snapshot does not affect it.
            r.Free(h1);
            Assert.Equal(2, snapshot.Count);
            Assert.Equal(1, r.Count);
        }

        [Fact]
        public void Allocate_throws_on_null()
        {
            var r = new LocalFormRegistry();
            Assert.Throws<System.ArgumentNullException>(() => r.Allocate(null!));
        }

        [Fact]
        public async Task Concurrent_allocate_produces_unique_handles()
        {
            // Single-lock implementation but worth proving the lock
            // covers the increment so two threads cannot collide on
            // _nextHandle.
            var r = new LocalFormRegistry();
            const int threadCount = 8;
            const int perThread = 250;
            var allHandles = new List<int>();
            var lockObj = new object();

            await Task.WhenAll(Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                var local = new List<int>(perThread);
                for (int i = 0; i < perThread; i++)
                {
                    local.Add(r.Allocate(new FormDescriptor()));
                }
                lock (lockObj) { allHandles.AddRange(local); }
            })));

            Assert.Equal(threadCount * perThread, allHandles.Count);
            Assert.Equal(threadCount * perThread, new HashSet<int>(allHandles).Count);
            Assert.Equal(threadCount * perThread, r.Count);
        }
    }
}
