/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Text.Json.Nodes;
using OpenSim.Archive.Eligibility.Tests;
using OpenSim.Archive.GltfExport;
using Xunit;

namespace OpenSim.Archive.Restore.Tests;

/// <summary>
/// restore finds the part behind each object of a file by the part UUID, or the link number, that the exporter wrote into the
/// object's extras. It never uses a name: a modelling tool renames duplicates, and objects may be named like another part.
/// </summary>
public class RestoreIdentityTests
{
    /// <summary>A linkset of five parts that are all named "Object" (link numbers 1 to 5), exported.</summary>
    private sealed class FiveObjects
    {
        public string Dir = Path.Combine(Path.GetTempPath(), "archive-restore-tests", Guid.NewGuid().ToString("N"));
        public KitPart[] Parts;
        public string Archive, GlbDir, Glb;

        public FiveObjects()
        {
            Directory.CreateDirectory(Dir);
            Parts = Enumerable.Range(0, 5).Select(i => new KitPart("Object", i + 1) { Scale = "1,2,3", TextureEntry = RestoreTests.Fixture.ValidTextureEntry() }).ToArray();
            Archive = EligibilityKit.WriteArchive(new List<List<KitPart>> { Parts.ToList() }, new Dictionary<string, byte[]>());
            GlbDir = Path.Combine(Dir, "glb");
            Assert.Equal(0, ArchiveExporter.Export(Archive, new ExportOptions { OutputDirectory = GlbDir, ImageCheck = () => null }).ExitCode);
            Glb = Directory.GetFiles(GlbDir, "*.glb", SearchOption.AllDirectories).Single();
        }

        public void Edit(Action<JsonNode, byte[]> edit) => File.WriteAllBytes(Glb, GlbPatch.Edit(File.ReadAllBytes(Glb), edit));

        public RestoreResult Restore() =>
            new ArchiveRestorer(new RestoreOptions { WorkFolder = Path.Combine(Dir, "work") }).Restore(Archive, GlbDir, Path.Combine(Dir, "out.oar"));

        public string Out => Path.Combine(Dir, "out.oar");
    }

    private static IEnumerable<JsonNode> PartNodes(JsonNode json) =>
        json["nodes"].AsArray().Where(n => n["extras"]?["partUuid"] != null);

    private static PartReport Of(RestoreResult r, KitPart p) => r.Parts.Single(x => x.PartId == p.Id);

    [Fact]
    public void FivePartsNamedTheSameAreToldApartByPartIdWhateverTheToolCalledThem()
    {
        FiveObjects f = new FiveObjects();
        f.Edit((json, bin) =>
        {
            // a modelling tool renames duplicates ("Object", "Object.001", ...) and here it handed the names out in another order
            string[] names = { "Object.004", "Object.002", "Object", "Object.003", "Object.001" };
            int i = 0;
            foreach (JsonNode n in PartNodes(json))
                n["name"] = names[i++];
            GlbPatch.MoveFace(json, bin, f.Parts[2].Id.ToString(), 0, 0.5f);
        });
        RestoreResult r = f.Restore();

        Assert.Equal(PartOutcome.Rebuilt, Of(r, f.Parts[2]).Outcome);
        Assert.Equal(3, Of(r, f.Parts[2]).LinkNum);
        foreach (int other in new[] { 0, 1, 3, 4 })
            Assert.Equal(PartOutcome.Unchanged, Of(r, f.Parts[other]).Outcome);
        Assert.Equal(0, r.Count(PartOutcome.Added));
        Assert.Equal(0, r.Count(PartOutcome.Deleted));
    }

    [Fact]
    public void ANodeNamedLikeAnotherPartIsStillThePartItsExtrasSay()
    {
        RestoreTests.Fixture f = new RestoreTests.Fixture();
        f.Edit(f.Root, (json, bin) =>
        {
            JsonNode root = GlbPatch.PartNode(json, RestoreTests.PartUuid(f.Root)), child = GlbPatch.PartNode(json, RestoreTests.PartUuid(f.Child));
            (root["name"], child["name"]) = ((string)child["name"], (string)root["name"]);
            GlbPatch.MoveFace(json, bin, RestoreTests.PartUuid(f.Child), 0, 0.5f);
        });
        RestoreResult r = f.Restore();

        Assert.Equal(PartOutcome.Rebuilt, RestoreTests.Of(r, f.Child).Outcome);
        Assert.Equal("Child", RestoreTests.Of(r, f.Child).Name);
        Assert.Equal(PartOutcome.Unchanged, RestoreTests.Of(r, f.Root).Outcome);
    }

    [Fact]
    public void AFileThatLostItsCustomPropertiesIsRefusedEvenWhenEveryNameIsStillTheOriginal()
    {
        FiveObjects f = new FiveObjects();
        // what Blender writes with its default export settings: the same objects and names, no custom properties anywhere
        f.Edit((json, bin) =>
        {
            foreach (JsonNode n in json["nodes"].AsArray())
                n.AsObject().Remove("extras");
            foreach (string list in new[] { "meshes", "materials" })
                if (json[list] is JsonArray items)
                    foreach (JsonNode m in items)
                        m.AsObject().Remove("extras");
        });
        RestoreException e = Assert.Throws<RestoreException>(() => f.Restore());
        Assert.Contains("Custom Properties", e.Message);
        Assert.Contains("export", e.Message);
        Assert.False(File.Exists(f.Out), "nothing is written when the parts cannot be identified");
    }

    [Fact]
    public void WithOnlyTheLinkNumbersLeftTheLinkNumberFindsThePart()
    {
        FiveObjects f = new FiveObjects();
        string id = f.Parts[3].Id.ToString();
        f.Edit((json, bin) =>
        {
            GlbPatch.MoveFace(json, bin, id, 0, 0.5f);
            int k = 0;
            foreach (JsonNode n in PartNodes(json))
            {
                n["extras"].AsObject().Remove("partUuid");
                n["name"] = k++ == 0 ? "Object" : "Object.00" + (k - 1);
            }
        });
        RestoreResult r = f.Restore();

        Assert.Equal(PartOutcome.Rebuilt, Of(r, f.Parts[3]).Outcome);
        Assert.Equal(4, Of(r, f.Parts[3]).LinkNum);
        foreach (int other in new[] { 0, 1, 2, 4 })
            Assert.Equal(PartOutcome.Unchanged, Of(r, f.Parts[other]).Outcome);
        Assert.Equal(0, r.Count(PartOutcome.Added));
    }

    [Fact]
    public void TwoNodesWithTheSameLinkNumberAndNoPartIdAreRefusedNotGuessedByName()
    {
        FiveObjects f = new FiveObjects();
        f.Edit((json, bin) =>
        {
            foreach (JsonNode n in PartNodes(json))
                n["extras"].AsObject().Remove("partUuid");
            PartNodes(json).ToList(); // all five now carry only a link number
            JsonNode[] nodes = json["nodes"].AsArray().Where(n => n["extras"]?["linkNumber"] != null).ToArray();
            nodes[4]["extras"]["linkNumber"] = 1;
        });
        RestoreException e = Assert.Throws<RestoreException>(() => f.Restore());
        Assert.Contains("link number", e.Message);
        Assert.False(File.Exists(f.Out));
    }

    [Fact]
    public void TwoObjectsCarryingOnePartIdAreRefusedWhenNoNameSaysWhichIsTheOriginal()
    {
        FiveObjects f = new FiveObjects();
        f.Edit((json, bin) =>
        {
            JsonNode[] nodes = PartNodes(json).ToArray();
            // the tool renamed all five alike and the user copied the third object: two objects now carry its part id
            for (int i = 0; i < nodes.Length; i++)
                nodes[i]["name"] = "Object.00" + (i + 1);
            JsonNode copy = JsonNode.Parse(nodes[2].ToJsonString());
            copy["name"] = "Object.006";
            json["nodes"].AsArray().Add(copy);
            json["nodes"].AsArray().First(n => (string)n["extras"]?["kind"] == "object")["children"].AsArray().Add(json["nodes"].AsArray().Count - 1);
        });
        RestoreException e = Assert.Throws<RestoreException>(() => f.Restore());
        Assert.Contains(f.Parts[2].Id.ToString(), e.Message);
        Assert.False(File.Exists(f.Out));
    }

    // ------------------------------------------------------------------ edited extra coordinate sets

    /// <summary>One part with a planar face (face 1), so the file holds a second coordinate set for it.</summary>
    private sealed class PlanarPart
    {
        public string Dir = Path.Combine(Path.GetTempPath(), "archive-restore-tests", Guid.NewGuid().ToString("N"));
        public KitPart Part = new KitPart("Planar", 1) { Scale = "1,2,3" };
        public string Archive, GlbDir, Glb;

        public PlanarPart()
        {
            Directory.CreateDirectory(Dir);
            OpenMetaverse.Primitive.TextureEntry te = new OpenMetaverse.Primitive.TextureEntry(new OpenMetaverse.UUID("89556747-24cb-43ed-920b-47caed15465f"));
            te.CreateFace(1).TexMapType = OpenMetaverse.MappingType.Planar;
            Part.TextureEntry = Convert.ToBase64String(te.GetBytes());
            Archive = EligibilityKit.WriteArchive(new List<List<KitPart>> { new List<KitPart> { Part } }, new Dictionary<string, byte[]>());
            GlbDir = Path.Combine(Dir, "glb");
            Assert.Equal(0, ArchiveExporter.Export(Archive, new ExportOptions { OutputDirectory = GlbDir, ImageCheck = () => null }).ExitCode);
            Glb = Directory.GetFiles(GlbDir, "*.glb", SearchOption.AllDirectories).Single();
        }

        public void Edit(Action<JsonNode, byte[]> edit) => File.WriteAllBytes(Glb, GlbPatch.Edit(File.ReadAllBytes(Glb), edit));

        public RestoreResult Restore() =>
            new ArchiveRestorer(new RestoreOptions { WorkFolder = Path.Combine(Dir, "work") }).Restore(Archive, GlbDir, Path.Combine(Dir, "out.oar"));
    }

    /// <summary>Adds <paramref name="delta"/> to every value of one coordinate set of one prim face, in the file's binary chunk.</summary>
    private static void ShiftSet(JsonNode json, byte[] bin, int face, string set, float delta)
    {
        foreach (JsonNode mesh in json["meshes"].AsArray())
            foreach (JsonNode prim in mesh["primitives"].AsArray())
            {
                if ((int)prim["extras"]["primFace"] != face)
                    continue;
                JsonNode a = json["accessors"][(int)prim["attributes"][set]], v = json["bufferViews"][(int)a["bufferView"]];
                int start = (int)(v["byteOffset"] ?? 0) + (int)(a["byteOffset"] ?? 0);
                for (int i = 0; i < (int)a["count"] * 2; i++)
                    BitConverter.TryWriteBytes(bin.AsSpan(start + i * 4), BitConverter.ToSingle(bin, start + i * 4) + delta);
            }
    }

    [Fact]
    public void AnUneditedExtraCoordinateSetGivesNoWarning()
    {
        PlanarPart p = new PlanarPart();
        RestoreResult r = p.Restore();
        PartReport report = Of(r, p.Part);
        Assert.Equal(PartOutcome.Unchanged, report.Outcome);
        Assert.DoesNotContain(report.Notes, n => n.Contains("TEXCOORD_1"));
    }

    [Fact]
    public void RoundingByAModellingToolDoesNotCountAsAnEdit()
    {
        PlanarPart p = new PlanarPart();
        p.Edit((json, bin) => ShiftSet(json, bin, 1, "TEXCOORD_1", 1e-6f));
        Assert.DoesNotContain(Of(p.Restore(), p.Part).Notes, n => n.Contains("TEXCOORD_1"));
    }

    [Fact]
    public void AnEditedExtraSetIsWarnedAboutForItsFaceAndNotCarriedBack()
    {
        PlanarPart p = new PlanarPart();
        p.Edit((json, bin) => ShiftSet(json, bin, 1, "TEXCOORD_1", 0.25f));
        RestoreResult r = p.Restore();
        PartReport report = Of(r, p.Part);

        // the part is judged by TEXCOORD_0, which was not edited, so nothing is rebuilt
        Assert.Equal(PartOutcome.Unchanged, report.Outcome);
        string note = Assert.Single(report.Notes, n => n.Contains("TEXCOORD_1"));
        Assert.Contains("face 1", note);
        Assert.Contains("not carried back", note);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void AMissingExtraSetIsWarnedAboutToo()
    {
        PlanarPart p = new PlanarPart();
        p.Edit((json, bin) =>
        {
            foreach (JsonNode mesh in json["meshes"].AsArray())
                foreach (JsonNode prim in mesh["primitives"].AsArray())
                    prim["attributes"].AsObject().Remove("TEXCOORD_1");
        });
        PartReport report = Of(p.Restore(), p.Part);
        Assert.Contains(report.Notes, n => n.Contains("face 1") && n.Contains("TEXCOORD_1"));
    }

    [Fact]
    public void AnEditOfTheFirstUvMapIsStillCarriedBack()
    {
        PlanarPart p = new PlanarPart();
        p.Edit((json, bin) => ShiftSet(json, bin, 0, "TEXCOORD_0", 0.25f));
        PartReport report = Of(p.Restore(), p.Part);
        Assert.Equal(PartOutcome.Rebuilt, report.Outcome);
        Assert.DoesNotContain(report.Notes, n => n.Contains("TEXCOORD_1"));
    }
}
