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

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using Xunit;

namespace OpenSim.Archive.ScriptScan.Tests;

/// <summary>
/// The LSL and OSSL constants (LSL_Constants.cs, carried from OpenSim) against the table the original script base class held:
/// Fixtures/lsl-constants.txt lists every public static field with its type and value, one per line (a string value is written
/// as a JSON string). Each test reads its own copy of the file, so the class runs in parallel.
/// </summary>
public class LslConstantsTableTests
{
    private static string[] Expected() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lsl-constants.txt")).Where(l => l.Length > 0).ToArray();

    private static Dictionary<string, string> Actual()
    {
        Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FieldInfo f in typeof(ScriptBaseClass).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            object v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
            if (v == null)
                continue;
            string type = f.FieldType.Name;
            string text = v is double dbl ? dbl.ToString("R", CultureInfo.InvariantCulture) : v.ToString();
            if (type == "Vector3" || type == "Quaternion")
                text = "(value)";
            else if (v is string || type == "LSLString")
                text = JsonSerializer.Serialize(text);
            d[f.Name] = type + ":" + text;
        }
        return d;
    }

    [Fact]
    public void EveryConstantOfTheOriginalIsThereWithItsTypeAndValue()
    {
        Dictionary<string, string> actual = Actual();
        List<string> problems = new List<string>();
        foreach (string line in Expected())
        {
            int eq = line.IndexOf('=');
            string name = line.Substring(0, eq), want = line.Substring(eq + 1);
            if (!actual.TryGetValue(name, out string got))
                problems.Add(name + " is missing");
            else if (got != want)
                problems.Add(name + " is " + got + ", the original has " + want);
        }
        Assert.True(problems.Count == 0, problems.Count + " differences, first: " + problems.FirstOrDefault());
    }

    [Fact]
    public void TheTableHoldsExactlyTheOriginalsConstants()
    {
        Assert.Equal(964, Expected().Length);
        Assert.Equal(964, Actual().Count);
    }

    [Fact]
    public void TheScannerSeesTheLinkConstants()
    {
        Assert.True(LslConstants.TryGet("LINK_SET", out LslValue set));
        Assert.True(set.IsInt);
        Assert.Equal(-1, set.Int);
        Assert.True(LslConstants.TryGet("PRIM_LINK_TARGET", out LslValue target));
        Assert.Equal(34, target.Int);
        Assert.True(LslConstants.TryGet("TRUE", out LslValue t));
        Assert.Equal(1, t.Int);
        Assert.True(LslConstants.Count >= 900);
    }
}
