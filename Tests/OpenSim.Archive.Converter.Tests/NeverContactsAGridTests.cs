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

using System.Text.RegularExpressions;
using Xunit;

namespace OpenSim.Archive.Converter.Tests;

/// <summary>
/// oar-tools reads and writes files and never contacts a grid. The code written for oar-tools (every project except the carried
/// OpenSim.Framework, which holds OpenSim's own web helpers that nothing here calls) is scanned for the framework's network types.
/// Each test reads source files only, so the class runs in parallel.
/// </summary>
public class NeverContactsAGridTests
{
    private static readonly Regex Network = new Regex(@"\b(HttpClient|HttpWebRequest|WebRequest|WebClient|TcpClient|TcpListener|UdpClient|Socket|HttpListener|System\.Net\.Http|System\.Net\.Sockets)\b", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OarTools.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("OarTools.sln not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> OwnSource() =>
        Directory.EnumerateDirectories(Path.Combine(RepoRoot(), "Source"))
            .Where(d => Path.GetFileName(d) != "OpenSim.Framework")
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    [Fact]
    public void NoSourceFileOutsideTheCarriedFrameworkUsesANetworkType()
    {
        List<string> files = OwnSource().ToList();
        Assert.True(files.Count > 100, "only " + files.Count + " source files were scanned");
        List<string> hits = files.Where(f => Network.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(RepoRoot(), f)).ToList();
        Assert.True(hits.Count == 0, "network types are used in: " + string.Join(", ", hits));
    }

    [Fact]
    public void TheScanFindsANetworkTypeWhereThereIsOne()
    {
        Assert.Matches(Network, "var c = new System.Net.Http.HttpClient();");
        Assert.DoesNotMatch(Network, "var path = Path.Combine(a, b);");
    }
}
