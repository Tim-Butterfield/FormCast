// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// PluginContract.cs
// =================
//
// Independent re-declaration of the .NET plugin contract published by
// JP Software in the TakeCommand.Plugin SDK header file
// (`DotNetPluginSDK.cs`, shipped with the TCC v36 SDK and copyright
// 2005-2026 Rex C. Conn).
//
// We declare these types ourselves rather than vendoring JP Software's
// header so that this repository can be cloned and built without
// downloading any third-party SDK files. The shape of the contract
// (namespace, type names, method signatures) MUST match JP's published
// SDK exactly, because TC-DotNetPluginHost64.dll loads plugins by
// resolving `TakeCommand.Plugin.ITCCPlugin` from the loaded assembly
// via reflection. Any divergence from JP's contract will break loading.
//
// If JP Software ever publishes the TakeCommand.Plugin SDK as a NuGet
// package, this file should be removed and replaced with a PackageReference.

using System;

namespace TakeCommand.Plugin
{
 /// <summary>
 /// Metadata that a TCC plugin returns from <see cref="ITCCPlugin.GetPluginInfo"/>.
 /// </summary>
 /// <remarks>
 /// TCC reads this immediately after loading the assembly to discover what
 /// commands, internal variables, and variable functions the plugin
 /// provides. The <see cref="Functions"/> string is the entry point for
 /// dispatch. Every name listed there must have a corresponding method
 /// on the plugin class.
 /// </remarks>
    public sealed class TCCPluginInfo
    {
 /// <summary>Short name of the plugin (typically the DLL name).</summary>
        public string? Name { get; set; }

 /// <summary>Author's name.</summary>
        public string? Author { get; set; }

 /// <summary>Author's email address.</summary>
        public string? Email { get; set; }

 /// <summary>Author's web page.</summary>
        public string? WWW { get; set; }

 /// <summary>Brief, one-line description of the plugin.</summary>
        public string? Description { get; set; }

 /// <summary>
 /// Comma-delimited list of commands, internal variables, and variable
 /// functions the plugin provides. Naming convention:
 /// <list type="bullet">
 /// <item><description>
 /// <c>MYCOMMAND</c> declares an internal command. Method:
 /// <c>public int MYCOMMAND(StringBuilder args)</c>.
 /// </description></item>
 /// <item><description>
 /// <c>_MYVAR</c> declares an internal variable. Method:
 /// <c>public int _MYVAR(StringBuilder args)</c>.
 /// </description></item>
 /// <item><description>
 /// <c>@MYFUNC</c> declares a variable function. The method
 /// name takes the <c>f_</c> prefix because <c>@</c> is not a
 /// valid C# identifier character:
 /// <c>public int f_MYFUNC(StringBuilder args)</c>.
 /// </description></item>
 /// <item><description>
 /// <c>*MYKEYS</c> would be a keystroke handler, but these are
 /// NOT supported in .NET plugins (only native plugins can
 /// register them).
 /// </description></item>
 /// </list>
 /// All dispatch methods receive a <see cref="System.Text.StringBuilder"/>
 /// containing the call arguments and return an <see cref="int"/>:
 /// <c>0</c> for success, <c>0xFEDCBA98</c> (unchecked) to signal "this
 /// plugin did not handle the call, keep searching for a match", or
 /// any other value to indicate an error.
 /// </summary>
        public string? Functions { get; set; }

 /// <summary>Major version number.</summary>
        public int Major { get; set; }

 /// <summary>Minor version number.</summary>
        public int Minor { get; set; }

 /// <summary>Build number.</summary>
        public int Build { get; set; }
    }

 /// <summary>
 /// The interface every .NET TCC plugin must implement. The host
 /// (<c>TC-DotNetPluginHost64.dll</c>) discovers the implementing type
 /// by reflection and instantiates exactly one instance per loaded
 /// plugin assembly.
 /// </summary>
    public interface ITCCPlugin
    {
 /// <summary>
 /// Return the plugin's metadata. Called once, immediately after the
 /// assembly is loaded and before <see cref="Initialize"/>.
 /// </summary>
 /// <returns>
 /// A populated <see cref="TCCPluginInfo"/>. May not be <c>null</c>.
 /// </returns>
        TCCPluginInfo GetPluginInfo();

 /// <summary>
 /// Called by TCC after the plugin assembly is loaded and after
 /// <see cref="GetPluginInfo"/> has been queried. Use this hook to
 /// allocate resources, register handlers, and verify any required
 /// preconditions. The plugin's commands and functions will not be
 /// dispatched until this method returns <c>true</c>.
 /// </summary>
 /// <returns>
 /// <c>true</c> if the plugin loaded successfully and is ready to
 /// receive calls; <c>false</c> to abort the load.
 /// </returns>
        bool Initialize();

 /// <summary>
 /// Called by TCC when the plugin is being unloaded. The plugin must
 /// release all resources it allocated in <see cref="Initialize"/>.
 /// After this method returns, no further calls will be made to any
 /// method on this instance.
 /// </summary>
 /// <param name="endProcess">
 /// <c>true</c> when the entire TCC command processor is shutting
 /// down; <c>false</c> when only this plugin is being unloaded
 /// (e.g. via <c>PLUGIN /U</c>) and TCC will continue to run.
 /// </param>
 /// <returns>
 /// <c>true</c> on successful shutdown; <c>false</c> on error
 /// (logged but not blocking).
 /// </returns>
        bool Shutdown(bool endProcess);
    }
}
