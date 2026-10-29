/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenMetaverse;
using OpenSim.ArchiveInspector;
using Xunit;
using static OpenSim.Archive.GltfExport.Tests.TestKit;

namespace OpenSim.Archive.GltfExport.Tests;

/// <summary>The oar-export command line: the geometry is written as built unless --clean is given (--no-clean is accepted and does nothing), and --version names the version. Redirects the console, so it shares a collection with the other classes that do.</summary>
[Collection("console-output")]
public class CleanExportCommandTests
{
    private static (int exit, string output) Run(params string[] args)
    {
        TextWriter saved = Console.Out;
        StringWriter capture = new StringWriter();
        Console.SetOut(capture);
        try { return (ExportCommand.Run(args), capture.ToString()); }
        finally { Console.SetOut(saved); }
    }

    /// <summary>
    /// The command holds the managed heap to the memory cap, and the runtime's limit is process wide. A second run in the same
    /// process used to work its cap out from the limit the first run had set (half of it), and lower the limit again, so a host
    /// that ran the command several times ended with a heap too small for anything else it was doing.
    /// </summary>
    [Fact]
    public void RunningTheCommandAgainDoesNotLowerTheProcessMemoryLimitAgain()
    {
        static long Limit()
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        ExportWith();
        long afterFirst = Limit();
        // the limit is in force after the first run, and is never above the default cap
        Assert.True(afterFirst <= ArchiveExporter.DefaultMaxMemoryLimitMegabytes * 1024L * 1024L, "limit after the first run: " + afterFirst);
        ExportWith();
        ExportWith();
        Assert.Equal(afterFirst, Limit());
    }

    private static Glb ExportWith(params string[] extra)
    {
        string dir = NewFolder();
        string archive = Path.Combine(dir, "sphere.oar");
        File.WriteAllBytes(archive, Oar(("sphere", ObjectXml(new PartSpec { Name = "Sphere", Shape = Box(profileCurve: 5, pathCurve: 32) }))));
        string output = Path.Combine(dir, "out");
        // the command's own messages go into the failure text, so a failing run says why (for example a missing native image library)
        TextWriter savedOut = Console.Out, savedError = Console.Error;
        StringWriter said = new StringWriter(), complained = new StringWriter();
        Console.SetOut(said);
        Console.SetError(complained);
        int exit;
        try { exit = ExportCommand.Run(new[] { archive, "--out", output }.Concat(extra).ToArray()); }
        finally { Console.SetOut(savedOut); Console.SetError(savedError); }
        Assert.True(exit == 0, "exit code " + exit + "; output: " + said + "; errors: " + complained);
        return ReadGlb(File.ReadAllBytes(Directory.GetFiles(output, "*.glb", SearchOption.AllDirectories).Single()));
    }

    [Fact]
    public void WithoutAnyOptionTheFileIsWrittenAsBuiltWithoutAStamp()
    {
        Glb glb = ExportWith();
        Assert.False(glb.Root.GetProperty("asset").TryGetProperty("extras", out _));
        Assert.False(glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0].TryGetProperty("indices", out _));
    }

    [Fact]
    public void CleanWritesTheFileCleanedAndStamped()
    {
        Glb glb = ExportWith("--clean");
        Assert.True(glb.Root.GetProperty("asset").GetProperty("extras").GetProperty("oarExport").GetProperty("clean").GetBoolean());
        Assert.True(glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0].TryGetProperty("indices", out _));
    }

    [Fact]
    public void NoCleanIsStillAcceptedAndChangesNothing()
    {
        Glb withFlag = ExportWith("--no-clean");
        Glb without = ExportWith();
        Assert.Equal(without.Json.RootElement.GetRawText(), withFlag.Json.RootElement.GetRawText());
        Assert.Equal(without.Bin, withFlag.Bin);
    }

    [Fact]
    public void TheHelpSaysCleanIsOptInAndNoCleanDoesNothing()
    {
        (int exit, string output) = Run("--help");
        Assert.Equal(0, exit);
        Assert.Contains("[--clean]", output);
        Assert.Contains("--no-clean           accepted and ignored", output);
        Assert.Contains("--clean              remove duplicate and loose vertices", output);
    }

    [Fact]
    public void VersionPrintsTheProgramAndItsVersionAndExitsWithoutExporting()
    {
        (int exit, string output) = Run("--version");
        Assert.Equal(0, exit);
        Assert.Contains("oar-tools " + ToolRelease.Version, output);
    }
}
