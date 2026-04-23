// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// In-process, thread-safe <see cref="IFormRegistry"/> for forms
 /// owned by the current TCC session. Handles are monotonically
 /// increasing integers starting at 1; freed handles are NEVER
 /// reused, so a stale handle held by user BTM after
 /// <c>@FORMCLOSE</c> is reliably distinguishable from a valid one.
 /// </summary>
 /// <remarks>
 /// Concurrency model: a single internal lock guards all mutating
 /// operations and snapshots. The lock is held only briefly --
 /// every operation is O(1) or O(N) over the registry contents and
 /// has no I/O -- so contention is not expected to matter even when
 /// the GuiHostThread, the callback worker thread, and the script
 /// thread are all touching the registry.
 /// </remarks>
    public sealed class LocalFormRegistry : IFormRegistry
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, FormDescriptor> _byHandle =
            new Dictionary<int, FormDescriptor>();
        private int _nextHandle = 1;

 /// <inheritdoc />
        public int Allocate(FormDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            lock (_lock)
            {
                int handle = _nextHandle++;
                _byHandle[handle] = descriptor;
                return handle;
            }
        }

 /// <inheritdoc />
        public FormDescriptor? Lookup(int handle)
        {
            lock (_lock)
            {
                return _byHandle.TryGetValue(handle, out var d) ? d : null;
            }
        }

 /// <inheritdoc />
        public bool Free(int handle)
        {
            lock (_lock)
            {
                return _byHandle.Remove(handle);
            }
        }

 /// <inheritdoc />
        public IReadOnlyCollection<int> AllHandles()
        {
            lock (_lock)
            {
 // Snapshot. The returned collection is detached from
 // the registry, so it stays valid even if a concurrent
 // call mutates _byHandle right after we return.
                return new List<int>(_byHandle.Keys);
            }
        }

 /// <inheritdoc />
        public int FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            lock (_lock)
            {
                foreach (var kvp in _byHandle)
                {
                    if (string.Equals(kvp.Value.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Key;
                    }
                }
                return -1;
            }
        }

 /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _byHandle.Count;
                }
            }
        }
    }
}
