// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// Thread-safe handle table for live <see cref="FormDescriptor"/>
 /// instances. The plugin allocates a handle for each open form,
 /// hands it back to the BTM caller, and uses it to look the form
 /// up on subsequent calls. <c>@FORMCLOSE</c> frees the handle.
 /// </summary>
 /// <remarks>
 /// The local-scope implementation (<see cref="LocalFormRegistry"/>)
 /// is the in-process registry. The cross-process Global registry
 /// implements the same interface but talks to
 /// <c>FormCast.Host.exe</c> via named pipes.
 /// </remarks>
    public interface IFormRegistry
    {
 /// <summary>
 /// Add <paramref name="descriptor"/> to the registry and return
 /// the newly-allocated handle. The handle is unique within the
 /// registry's lifetime and is never reused (so a freed handle
 /// passed back to <see cref="Lookup"/> always returns
 /// <see langword="null"/>).
 /// </summary>
        int Allocate(FormDescriptor descriptor);

 /// <summary>
 /// Look up a descriptor by handle. Returns <see langword="null"/>
 /// if the handle is unknown or has been freed.
 /// </summary>
        FormDescriptor? Lookup(int handle);

 /// <summary>
 /// Free the handle. Returns <see langword="true"/> if the handle
 /// existed and was removed; <see langword="false"/> if it was
 /// already gone or never allocated.
 /// </summary>
        bool Free(int handle);

 // Note: this method was named Get(int) in an earlier draft.
 // Renamed to Lookup to avoid conflict with VB's "Get" keyword
 // (CA1716). The semantic is the same.

 /// <summary>
 /// Snapshot of all currently-allocated handles, in unspecified
 /// order. The snapshot is taken under the registry lock so it
 /// is consistent at one point in time, but the registry may
 /// change immediately after the call returns.
 /// </summary>
        IReadOnlyCollection<int> AllHandles();

 /// <summary>
 /// Find a handle by descriptor name. Returns -1 if no descriptor
 /// in the registry matches. Used by <c>@FORMFIND</c>. Name
 /// matching is case-insensitive.
 /// </summary>
        int FindByName(string name);

 /// <summary>
 /// Total number of currently-allocated handles. O(1).
 /// </summary>
        int Count { get; }
    }
}
