/*
 * Copyright (c) 2026 Legion Builds
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Legion Builds nor the names of its contributors
 *       may be used to endorse or promote products derived from this software
 *       without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenSim.ArchiveInspector;
using Xunit;

namespace OpenSim.Archive.Converter.Tests;

/// <summary>
/// The oar-tools command line: the subcommand names, the old oar-inspect, oar-export, oar-restore and oar-convert names that keep
/// working through wrapper scripts for this release, and the notice their --help carries. Each test uses its own folder and the
/// console is swapped inside <see cref="Capture"/>, so the class joins the console collection.
/// </summary>
[Collection("Console output")]
public class WrapperAndDispatchTests
{
    private static readonly string[] Wrapped = { "inspect", "export", "restore", "convert" };

    private static (int code, string output, string error) Capture(Func<int> run)
    {
        TextWriter oldOut = Console.Out, oldErr = Console.Error;
        StringWriter o = new StringWriter(), e = new StringWriter();
        Console.SetOut(o);
        Console.SetError(e);
        try { return (run(), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OarTools.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("OarTools.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void VersionIsThePackageRelease()
    {
        var r = Capture(() => Program.Main(new[] { "--version" }));
        Assert.Equal(0, r.code);
        Assert.Equal("oar-tools 0.6.0", r.output.Trim());
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("export")]
    [InlineData("restore")]
    [InlineData("convert")]
    public void EachSubcommandPrintsItsOwnHelp(string sub)
    {
        var r = Capture(() => Program.Main(new[] { sub, "--help" }));
        Assert.Equal(0, r.code);
        Assert.Contains("oar-tools " + sub, r.output);
        Assert.DoesNotContain("is going away", r.output);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("export")]
    [InlineData("restore")]
    [InlineData("convert")]
    public void TheOldNameSaysItIsGoingAwayInItsHelp(string sub)
    {
        string old = Environment.GetEnvironmentVariable(Program.LegacyNameVariable);
        Environment.SetEnvironmentVariable(Program.LegacyNameVariable, "oar-" + sub);
        try
        {
            var r = Capture(() => Program.Main(new[] { sub, "--help" }));
            Assert.Equal(0, r.code);
            Assert.Contains("oar-" + sub + " is going away", r.output);
            Assert.Contains("oar-tools " + sub, r.output);
        }
        finally { Environment.SetEnvironmentVariable(Program.LegacyNameVariable, old); }
    }

    [Fact]
    public void AnOldNameGivenAsTheFirstArgumentStillWorksAndSaysItIsGoingAway()
    {
        var r = Capture(() => Program.Main(new[] { "oar-convert", "--help" }));
        Assert.Equal(0, r.code);
        Assert.Contains("oar-convert is going away", r.output);
    }

    [Fact]
    public void AnUnknownSubcommandIsRefusedWithTheListOfSubcommands()
    {
        var r = Capture(() => Program.Main(new[] { "frobnicate" }));
        Assert.Equal(64, r.code);
        Assert.Contains("unknown subcommand 'frobnicate'", r.error);
        Assert.Contains("inspect", r.error);
        Assert.Contains("convert", r.error);
    }

    [Fact]
    public void NoArgumentsPrintsTheUsageAndExits64()
    {
        var r = Capture(() => Program.Main(Array.Empty<string>()));
        Assert.Equal(64, r.code);
        Assert.Contains("Usage: oar-tools <subcommand>", r.error);
    }

    [Fact]
    public void TheMainHelpStatesWhatTheProgramNeverDoes()
    {
        var r = Capture(() => Program.Main(new[] { "--help" }));
        Assert.Equal(0, r.code);
        Assert.Contains("never adds a permission bit", System.Text.RegularExpressions.Regex.Replace(r.output, @"\s+", " "));
        Assert.Contains("never contacts a grid", System.Text.RegularExpressions.Regex.Replace(r.output, @"\s+", " "));
    }

    private static string WrapperName(string sub) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "oar-" + sub + ".cmd" : "oar-" + sub;

    /// <summary>A folder holding the wrapper for <paramref name="sub"/> beside a stand-in oar-tools that prints what it was given.</summary>
    private static string WrapperFolder(string sub)
    {
        string dir = Path.Combine(Path.GetTempPath(), "wrapper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(RepoRoot(), "wrappers", WrapperName(sub));
        Assert.True(File.Exists(source), "the wrapper " + source + " does not exist");
        File.Copy(source, Path.Combine(dir, WrapperName(sub)));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.WriteAllText(Path.Combine(dir, "oar-tools.cmd"),
                "@echo off\r\necho ARGS[%*]\r\necho LEGACY[%OAR_TOOLS_LEGACY_NAME%]\r\nexit /b 7\r\n");
        }
        else
        {
            string stub = Path.Combine(dir, "oar-tools");
            File.WriteAllText(stub, "#!/bin/sh\necho \"ARGS[$*]\"\necho \"LEGACY[$OAR_TOOLS_LEGACY_NAME]\"\nexit 7\n");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(Path.Combine(dir, WrapperName(sub)), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return dir;
    }

    private static (int code, string output) RunWrapper(string dir, string sub, params string[] args)
    {
        ProcessStartInfo psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", Path.Combine(dir, WrapperName(sub)) } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { Path.Combine(dir, WrapperName(sub)) } };
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        psi.RedirectStandardOutput = true;
        psi.UseShellExecute = false;
        psi.Environment.Remove(Program.LegacyNameVariable);
        using Process p = Process.Start(psi);
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("export")]
    [InlineData("restore")]
    [InlineData("convert")]
    public void AWrapperPassesItsArgumentsToTheSubcommandNamesItselfAndReturnsTheExitCode(string sub)
    {
        string dir = WrapperFolder(sub);
        var r = RunWrapper(dir, sub, "first.oar", "--out", "second.oar");
        Assert.Equal(7, r.code);
        Assert.Contains("ARGS[" + sub + " first.oar --out second.oar]", r.output);
        Assert.Contains("LEGACY[oar-" + sub + "]", r.output);
    }

    [Fact]
    public void EveryWrapperIsPresentForBothPlatforms()
    {
        foreach (string sub in Wrapped)
        {
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "wrappers", "oar-" + sub + ".cmd")), "oar-" + sub + ".cmd");
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "wrappers", "oar-" + sub)), "oar-" + sub);
        }
    }

    [Theory]
    [InlineData(typeof(OpenSim.Archive.Converter.Merge.ArchiveMergePlanner))]
    [InlineData(typeof(OpenSim.Archive.Eligibility.ArchiveEligibility))]
    public void TheVersionAReportCarriesIsTheRelease(Type fromTheAssembly)
    {
        string stamped = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(fromTheAssembly.Assembly)?.InformationalVersion;
        Assert.Equal(ToolRelease.Version, stamped);
    }
}
