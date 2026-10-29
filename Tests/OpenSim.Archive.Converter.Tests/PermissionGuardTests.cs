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

using System.IO.Compression;
using System.Text;
using OpenSim.Archive.Eligibility.Tests;
using OpenSim.Archive.Inspection;
using OpenSim.ArchiveInspector;
using Xunit;

namespace OpenSim.Archive.Converter.Tests;

/// <summary>
/// oar-tools never loosens permissions. Every subcommand that writes an archive is run over archives whose parts and contents
/// carry restricted masks, and the output is compared with the input mask by mask: no base, owner, group, everyone or next-owner
/// mask may gain a bit, and the export bit may not appear where it was not. An object the output holds that the input did not
/// (the merge manifest notecard) must not carry the export bit. The subcommands that do not write an archive are run too, and the
/// archive they were given must be byte for byte as before. Each test writes its own archives; the class joins the console
/// collection because the commands print to the console.
/// </summary>
[Collection("Console output")]
public class PermissionGuardTests
{
    private const uint Export = 0x10000;
    private const uint Transfer = 0x2000, Modify = 0x4000, Copy = 0x8000, Move = 0x80000;

    private sealed record Masks(uint? Base, uint? Owner, uint? Group, uint? Everyone, uint? NextOwner)
    {
        public static Masks Of(PermissionMasks m) => new Masks(m.Base, m.Owner, m.Group, m.Everyone, m.NextOwner);
        public IEnumerable<(string name, uint? value)> All() =>
            new[] { ("base", Base), ("owner", Owner), ("group", Group), ("everyone", Everyone), ("next owner", NextOwner) };
        public override string ToString() => string.Join(" ", All().Select(a => a.name + "=" + (a.value.HasValue ? "0x" + a.value.Value.ToString("x") : "-")));
    }

    private static string Folder() => Path.Combine(Path.GetTempPath(), "permission-guard-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Every part and every content item of every object file, by id, with its masks.</summary>
    private static Dictionary<string, Masks> Snapshot(string archive)
    {
        Dictionary<string, Masks> all = new Dictionary<string, Masks>();
        using FileStream fs = File.OpenRead(archive);
        TarStreamReader tar = new TarStreamReader(new GZipStream(fs, CompressionMode.Decompress));
        TarEntryHeader h;
        while ((h = tar.ReadNext()) != null)
        {
            byte[] body = tar.ReadBody();
            if (!h.Name.StartsWith("objects/", StringComparison.Ordinal) || !h.Name.EndsWith(".xml", StringComparison.Ordinal))
                continue;
            foreach (ParsedGroup g in ObjectXmlParser.Parse(body).Groups)
                foreach (ParsedPart p in g.Parts)
                {
                    all["part " + p.Part.PartId + " (" + p.Part.Name + ")"] = Masks.Of(p.Part.Masks);
                    foreach (ItemRecord it in p.Items)
                        all["item " + it.ItemId + " (" + it.Name + ") in part " + p.Part.PartId] = Masks.Of(it.Masks);
                }
        }
        return all;
    }

    /// <summary>Fails when an output mask has a bit its input did not, or an object the input did not hold has the export bit.</summary>
    private static void AssertNoLoosening(string source, string output, int expectParts)
    {
        Dictionary<string, Masks> before = Snapshot(source), after = Snapshot(output);
        List<string> problems = new List<string>();
        int compared = 0;
        foreach (KeyValuePair<string, Masks> a in after)
        {
            // the key holds the id first; a name may differ after a rename, so match on the id part
            string id = a.Key.Substring(0, a.Key.IndexOf(" (", StringComparison.Ordinal));
            string tail = a.Key.Contains(" in part ") ? a.Key.Substring(a.Key.IndexOf(" in part ", StringComparison.Ordinal)) : "";
            KeyValuePair<string, Masks> was = before.FirstOrDefault(b => b.Key.StartsWith(id + " (", StringComparison.Ordinal) && b.Key.EndsWith(tail, StringComparison.Ordinal));
            if (was.Key == null)
            {
                foreach ((string name, uint? value) in a.Value.All())
                    if (value.HasValue && (value.Value & Export) != 0)
                        problems.Add(a.Key + " is new and its " + name + " mask carries the export bit: " + a.Value);
                continue;
            }
            compared++;
            foreach (((string name, uint? now), (_, uint? old)) in a.Value.All().Zip(was.Value.All()))
            {
                uint gained = (now ?? 0) & ~(old ?? 0);
                if (gained != 0)
                    problems.Add(a.Key + ": " + name + " mask gained 0x" + gained.ToString("x") + " (was " + was.Value + ", now " + a.Value + ")");
            }
        }
        Assert.True(compared >= expectParts, "only " + compared + " objects were compared, expected at least " + expectParts);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A linkset of five boxes with restricted masks, a stand-alone box with a different set, and contents with their own masks:
    /// a script on one box and a notecard-type item on the stand-alone. Nothing carries the export bit, so any appearance of it is a gain.
    /// </summary>
    private static (string archive, List<KitPart> parts) RestrictedFixture(bool withExport = false)
    {
        uint exp = withExport ? Export : 0;
        List<KitPart> linkset = Enumerable.Range(1, 5).Select(i => new KitPart("Box " + i, i)
        {
            Offset = (i * 2) + "," + (i % 2) + ",0.5",
            Scale = (1 + i * 0.25f).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",1,0.5",
            BaseMask = Move | Transfer | exp, OwnerMask = Move | Transfer, GroupMask = Transfer, EveryoneMask = 0, NextOwnerMask = Move
        }).ToList();
        linkset[0].Offset = "0,0,0";
        Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>();
        (KitItem item, string entry, byte[] data) script = EligibilityKit.Script("lamp", "default { state_entry() { llSay(0, \"hi\"); } }");
        script.item.BasePermissions = Move | Transfer | Modify | exp;
        script.item.CurrentPermissions = Move | Transfer;
        script.item.GroupPermissions = Transfer;
        script.item.EveryonePermissions = 0;
        script.item.NextPermissions = Move;
        linkset[2].Items.Add(script.item);
        assets[script.entry] = script.data;

        KitPart alone = new KitPart("Alone", 0)
        {
            Scale = "2,3,4", BaseMask = Move | Copy | Transfer | Modify | exp, OwnerMask = Move | Copy, GroupMask = Copy, EveryoneMask = Copy, NextOwnerMask = Transfer
        };
        KitItem note = new KitItem
        {
            Name = "note", Asset = Guid.NewGuid(), Type = 7,
            BasePermissions = Move | Copy, CurrentPermissions = Move, EveryonePermissions = 0, GroupPermissions = 0, NextPermissions = 0
        };
        alone.Items.Add(note);
        string path = EligibilityKit.WriteArchive(new List<List<KitPart>> { linkset, new List<KitPart> { alone } }, assets);
        return (path, linkset.Concat(new[] { alone }).ToList());
    }


    /// <summary>The link number of every part, by part id.</summary>
    private static Dictionary<Guid, int> LinkNumbers(string archive)
    {
        Dictionary<Guid, int> all = new Dictionary<Guid, int>();
        using FileStream fs = File.OpenRead(archive);
        TarStreamReader tar = new TarStreamReader(new GZipStream(fs, CompressionMode.Decompress));
        TarEntryHeader h;
        while ((h = tar.ReadNext()) != null)
        {
            byte[] body = tar.ReadBody();
            if (h.Name.StartsWith("objects/", StringComparison.Ordinal) && h.Name.EndsWith(".xml", StringComparison.Ordinal))
                foreach (ParsedGroup g in ObjectXmlParser.Parse(body).Groups)
                    foreach (ParsedPart p in g.Parts)
                        all[p.Part.PartId.Guid] = p.Part.LinkNum;
        }
        return all;
    }

    /// <summary>The same boxes with the link order of the children reversed, so relinking has parts to move.</summary>
    private static string ReversedReference(List<KitPart> parts)
    {
        List<KitPart> linkset = parts.Take(5).Select((p, i) => new KitPart(p.Name, i == 0 ? 1 : 7 - (i + 1))
        {
            Offset = p.Offset, Scale = p.Scale, BaseMask = p.BaseMask, OwnerMask = p.OwnerMask, GroupMask = p.GroupMask,
            EveryoneMask = p.EveryoneMask, NextOwnerMask = p.NextOwnerMask
        }).ToList();
        KitPart alone = new KitPart(parts[5].Name, 0) { Scale = parts[5].Scale, BaseMask = parts[5].BaseMask, OwnerMask = parts[5].OwnerMask };
        return EligibilityKit.WriteArchive(new List<List<KitPart>> { linkset, new List<KitPart> { alone } });
    }

    private static string NewOutput() => Folder() + ".oar";

    private static int Run(params string[] args) => Program.Main(args);

    [Fact]
    public void ConvertOnePartForOnePartLoosensNothing()
    {
        (string src, _) = RestrictedFixture();
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", src, "--out", output));
        AssertNoLoosening(src, output, expectParts: 6);
    }

    [Fact]
    public void ConvertMergeLoosensNothingAndTheManifestNotecardHasNoExportBit()
    {
        (string src, _) = RestrictedFixture();
        string output = NewOutput();
        string plans = Folder();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", src, "--merge", "--out", output, "--merge-plan-dir", plans));
        Dictionary<string, Masks> after = Snapshot(output);
        string noteKey = Assert.Single(after.Keys, k => k.StartsWith("item ", StringComparison.Ordinal) && k.Contains("(" + MergeNotecardName() + ")"));
        AssertNoLoosening(src, output, expectParts: 2);
        // the notecard carries the masks of the part that holds it, which are the masks of every part merged into it
        string partId = noteKey.Substring(noteKey.IndexOf(" in part ", StringComparison.Ordinal) + " in part ".Length);
        Masks held = after.Single(k => k.Key.StartsWith("part " + partId + " (", StringComparison.Ordinal)).Value;
        Assert.Equal(held, after[noteKey]);
        Assert.Equal(Move | Transfer, held.Base);
    }

    [Fact]
    public void TheManifestNotecardTakesTheMostRestrictiveMasksOfTheMergedParts()
    {
        PermissionMasks a = new PermissionMasks { Base = Move | Transfer | Copy, Owner = Move | Transfer, Group = Transfer, Everyone = 0, NextOwner = Move | Copy };
        PermissionMasks b = new PermissionMasks { Base = Move | Copy | Modify, Owner = Move | Copy, Group = Copy, Everyone = Copy, NextOwner = Move };
        PermissionMasks m = OpenSim.Archive.Converter.Merge.MergeArchiveWriter.MostRestrictive(new[] { a, b });
        Assert.NotNull(m);
        Assert.Equal(Move | Copy, m.Base);
        Assert.Equal(Move, m.Owner);
        Assert.Equal(0u, m.Group);
        Assert.Equal(0u, m.Everyone);
        Assert.Equal(Move, m.NextOwner);
        Assert.Equal(a.Owner, OpenSim.Archive.Converter.Merge.MergeArchiveWriter.MostRestrictive(new[] { a }).Owner);
        // a mask the archive does not give stays absent only when no part gives it
        PermissionMasks none = new PermissionMasks();
        Assert.Equal(Move, OpenSim.Archive.Converter.Merge.MergeArchiveWriter.MostRestrictive(new[] { none, b }).NextOwner);
    }

    [Fact]
    public void ConvertMergeKeepsTheMasksOfTheCarrierPartAndNeverMergesPartsWithDifferentMasks()
    {
        (string src, List<KitPart> parts) = RestrictedFixture();
        // one more box in the linkset with a looser owner mask: it must not merge with the others, and nothing else may change
        parts[3].OwnerMask = Move | Transfer | Copy;
        string altered = EligibilityKit.WriteArchive(new List<List<KitPart>> { parts.Take(5).ToList() }, null);
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", altered, "--merge", "--out", output, "--merge-plan-dir", Folder()));
        AssertNoLoosening(altered, output, expectParts: 2);
    }

    [Fact]
    public void ConvertRelinkLoosensNothing()
    {
        (string src, List<KitPart> parts) = RestrictedFixture();
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", src, "--relink-from", ReversedReference(parts), "--out", output));
        Assert.NotEqual(LinkNumbers(src).OrderBy(x => x.Key).ToList(), LinkNumbers(output).OrderBy(x => x.Key).ToList());
        AssertNoLoosening(src, output, expectParts: 6);
    }

    [Fact]
    public void ConvertRelinkOfAnArchiveWithTheExportBitKeepsExactlyThatBit()
    {
        (string src, List<KitPart> parts) = RestrictedFixture(withExport: true);
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", src, "--relink-from", ReversedReference(parts), "--out", output));
        Assert.NotEqual(LinkNumbers(src).OrderBy(x => x.Key).ToList(), LinkNumbers(output).OrderBy(x => x.Key).ToList());
        AssertNoLoosening(src, output, expectParts: 6);
        Dictionary<string, Masks> before = Snapshot(src), after = Snapshot(output);
        Assert.Equal(before.Count, after.Count);
        foreach (KeyValuePair<string, Masks> b in before)
            Assert.Equal(b.Value, after[b.Key]);
    }

    [Fact]
    public void ExportAndInspectLeaveTheArchiveTheyWereGivenUnchangedAndWriteNoArchive()
    {
        (string src, _) = RestrictedFixture();
        byte[] before = File.ReadAllBytes(src);
        string outDir = Folder();
        Assert.Equal(0, Run("export", src, "--out", outDir));
        string report = Path.Combine(Folder(), "inspect.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        int inspected = Run("inspect", src, "--out", report);
        Assert.True(inspected is 0 or 1 or 3, "inspect exit code " + inspected);
        Assert.Equal(before, File.ReadAllBytes(src));
        Assert.Empty(Directory.EnumerateFiles(outDir, "*.oar", SearchOption.AllDirectories));
    }

    [Fact]
    public void RestoreOfAnUnchangedExportLoosensNothing()
    {
        (string src, _) = RestrictedFixture();
        string glb = Folder();
        Assert.Equal(0, Run("export", src, "--out", glb));
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("restore", src, glb, "--out", output));
        Assert.True(File.Exists(output), "restore wrote no archive");
        AssertNoLoosening(src, output, expectParts: 6);
    }

    [Fact]
    public void AnArchiveWithTheExportBitOnItsPartsComesOutWithTheSameMasks()
    {
        (string src, _) = RestrictedFixture(withExport: true);
        string output = NewOutput();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Assert.Equal(0, Run("convert", src, "--out", output));
        AssertNoLoosening(src, output, expectParts: 6);
    }

    private static string MergeNotecardName() => OpenSim.Archive.Converter.Merge.MergeArchiveWriter.ManifestName;
}
