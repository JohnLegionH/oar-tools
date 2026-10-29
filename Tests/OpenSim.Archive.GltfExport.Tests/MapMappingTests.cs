/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Text.Json;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using SkiaSharp;
using Xunit;
using static OpenSim.Archive.GltfExport.Tests.TestKit;

namespace OpenSim.Archive.GltfExport.Tests;

/// <summary>
/// Every texture map of a face reads TEXCOORD_0 through KHR_texture_transform when its mapping can be said that way, and
/// otherwise reads an extra coordinate set holding its exact mapping. Maps with identical mappings share a set.
/// </summary>
public class MapMappingTests
{
    private static readonly UUID Diffuse = UUID.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly UUID Normal = UUID.Parse("d0000000-0000-4000-8000-0000000000d1");
    private static readonly UUID Spec = UUID.Parse("e0000000-0000-4000-8000-0000000000e1");
    private static readonly UUID PbrAsset = UUID.Parse("f0000000-0000-4000-8000-0000000000f1");
    private static readonly UUID PbrNormal = UUID.Parse("d0000000-0000-4000-8000-0000000000d2");
    private static readonly UUID PbrEmissive = UUID.Parse("d0000000-0000-4000-8000-0000000000d3");

    private sealed class Map
    {
        public float RepeatU = 1, RepeatV = 1, OffsetU, OffsetV, Rotation;
    }

    private static byte[] Png(byte shade)
    {
        using SKBitmap bmp = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgb888x, SKAlphaType.Opaque));
        bmp.Erase(new SKColor(shade, shade, shade));
        Assert.True(SkiaImageUtils.TryEncodeToJ2KLossless(bmp, out byte[] j2k));
        return j2k;
    }

    /// <summary>One box whose face 1 has a diffuse texture with the given mapping, a legacy material with a normal map and a specular map, and optionally a PBR material.</summary>
    private static Glb Export(Map diffuse, bool planar, Map normal = null, Map specular = null, bool pbr = false)
    {
        Primitive.TextureEntry te = new Primitive.TextureEntry(Diffuse);
        Primitive.TextureEntryFace f = te.CreateFace(1);
        f.TextureID = Diffuse;
        f.RepeatU = diffuse.RepeatU; f.RepeatV = diffuse.RepeatV; f.OffsetU = diffuse.OffsetU; f.OffsetV = diffuse.OffsetV; f.Rotation = diffuse.Rotation;
        if (planar)
            f.TexMapType = MappingType.Planar;

        List<(UUID, sbyte, byte[])> assets = new List<(UUID, sbyte, byte[])> { (Diffuse, (sbyte)AssetType.Texture, Png(10)) };
        if (normal != null || specular != null)
        {
            FaceMaterial fm = new FaceMaterial();
            if (normal != null)
            {
                fm.NormalMapID = Normal;
                fm.NormalRepeatX = normal.RepeatU; fm.NormalRepeatY = normal.RepeatV;
                fm.NormalOffsetX = normal.OffsetU; fm.NormalOffsetY = normal.OffsetV; fm.NormalRotation = normal.Rotation;
                assets.Add((Normal, (sbyte)AssetType.Texture, Png(128)));
            }
            if (specular != null)
            {
                fm.SpecularMapID = Spec;
                fm.SpecularRepeatX = specular.RepeatU; fm.SpecularRepeatY = specular.RepeatV;
                fm.SpecularOffsetX = specular.OffsetU; fm.SpecularOffsetY = specular.OffsetV; fm.SpecularRotation = specular.Rotation;
                assets.Add((Spec, (sbyte)AssetType.Texture, Png(200)));
            }
            fm.genID();
            f.MaterialID = fm.ID;
            assets.Add((fm.ID, (sbyte)AssetType.OSMaterial, fm.toLLSDxml()));
        }
        PartSpec root = new PartSpec { Name = "Maps", Shape = Box(), TextureEntryBase64 = Convert.ToBase64String(te.GetBytes()) };
        if (pbr)
        {
            PrimitiveBaseShape shape = new PrimitiveBaseShape();
            shape.RenderMaterials = new Primitive.RenderMaterials
            {
                entries = new[] { new Primitive.RenderMaterials.RenderMaterialEntry { te_index = 1, id = PbrAsset } }
            };
            root.ExtraParamsBase64 = Convert.ToBase64String(shape.ExtraParams);
            string json = "{\"asset\":{\"version\":\"2.0\"},\"images\":[{\"uri\":\"" + PbrNormal + "\"},{\"uri\":\"" + PbrEmissive + "\"}],"
                + "\"textures\":[{\"source\":0},{\"source\":1}],\"materials\":[{\"normalTexture\":{\"index\":0},\"emissiveTexture\":{\"index\":1}}]}";
            assets.Add((PbrAsset, (sbyte)AssetType.Material, System.Text.Encoding.UTF8.GetBytes(json)));
            assets.Add((PbrNormal, (sbyte)AssetType.Texture, Png(128)));
            assets.Add((PbrEmissive, (sbyte)AssetType.Texture, Png(250)));
        }
        string dir = NewFolder();
        string file = Path.Combine(dir, "maps.oar");
        File.WriteAllBytes(file, OarWithAssets(new[] { ("maps", ObjectXml(root)) }, assets.ToArray()));
        ExportSummary s = ArchiveExporter.Export(file, new ExportOptions { SelfContained = true, OutputDirectory = Path.Combine(dir, "out") });
        SummaryBuild b = Assert.Single(s.BuildList);
        return ReadGlb(File.ReadAllBytes(Path.Combine(dir, "out", b.Folder, b.File)));
    }

    private static JsonElement PrimitiveOfFace(Glb glb, int face)
    {
        foreach (JsonElement mesh in glb.Root.GetProperty("meshes").EnumerateArray())
            foreach (JsonElement prim in mesh.GetProperty("primitives").EnumerateArray())
                if (prim.GetProperty("extras").GetProperty("primFace").GetInt32() == face)
                    return prim;
        throw new InvalidOperationException("no face " + face);
    }

    private static JsonElement MaterialOfFace(Glb glb, int face) =>
        glb.Root.GetProperty("materials")[PrimitiveOfFace(glb, face).GetProperty("material").GetInt32()];

    private static float[] Uvs(Glb glb, int face, string set) => glb.Floats(PrimitiveOfFace(glb, face).GetProperty("attributes").Req(set).GetInt32());

    private static int Sets(Glb glb, int face) =>
        PrimitiveOfFace(glb, face).GetProperty("attributes").EnumerateObject().Count(a => a.Name.StartsWith("TEXCOORD_"));

    private static int TexCoordOf(JsonElement textureInfo) => textureInfo.TryGetProperty("texCoord", out JsonElement t) ? t.GetInt32() : 0;

    private static JsonElement DiffuseInfo(JsonElement m) => m.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture");

    private static bool HasTransform(JsonElement info) => info.TryGetProperty("extensions", out JsonElement e) && e.TryGetProperty("KHR_texture_transform", out _);

    // The viewer's rule written out (indra/newview/llface.cpp xform), v up.
    private static (float u, float v) Viewer(float u, float vUp, Map m)
    {
        float s = u - 0.5f, t = vUp - 0.5f, temp = s;
        s = s * MathF.Cos(m.Rotation) + t * MathF.Sin(m.Rotation);
        t = -temp * MathF.Sin(m.Rotation) + t * MathF.Cos(m.Rotation);
        return (s * m.RepeatU + m.OffsetU + 0.5f, t * m.RepeatV + m.OffsetV + 0.5f);
    }

    // a legacy material stores its transform in ten-thousandths and a face in 16 bits, so the sets agree to a few thousandths
    private static void AssertSetIsViewerMapping(Glb glb, string set, Map m)
    {
        float[] raw = Uvs(glb, 1, "TEXCOORD_0"), got = Uvs(glb, 1, set);
        Assert.Equal(raw.Length, got.Length);
        for (int i = 0; i < raw.Length; i += 2)
        {
            (float eu, float ev) = Viewer(raw[i], 1f - raw[i + 1], m);
            Assert.Equal(eu, got[i], 2);
            Assert.Equal(1f - ev, got[i + 1], 2);
        }
    }

    private static readonly Map Plain = new Map();
    private static Map Rotated() => new Map { RepeatU = 3f, RepeatV = 1.5f, OffsetU = 0.1f, OffsetV = -0.2f, Rotation = 0.6f };
    private static Map Shifted() => new Map { RepeatU = 2f, RepeatV = 2f, OffsetU = 0.25f, OffsetV = 0.5f };

    [Fact]
    public void AnExactDiffuseAndAnExactNormalMapBothReadTexCoord0WithTheirOwnTransform()
    {
        Glb glb = Export(Shifted(), false, normal: new Map { RepeatU = 4f, RepeatV = 4f });
        JsonElement m = MaterialOfFace(glb, 1);
        Assert.Equal(1, Sets(glb, 1));
        Assert.Equal(0, TexCoordOf(DiffuseInfo(m)));
        Assert.Equal(0, TexCoordOf(m.GetProperty("normalTexture")));
        Assert.True(HasTransform(DiffuseInfo(m)));
        Assert.Equal(new[] { 4f, 4f }, m.GetProperty("normalTexture").GetProperty("extensions").GetProperty("KHR_texture_transform").GetProperty("scale").EnumerateArray().Select(e => e.GetSingle()).ToArray());
    }

    [Fact]
    public void AnInexactNormalMapReadsItsOwnSetWhileAnExactDiffuseKeepsTexCoord0()
    {
        Map normal = Rotated();
        Glb glb = Export(Shifted(), false, normal: normal);
        JsonElement m = MaterialOfFace(glb, 1);
        Assert.Equal(2, Sets(glb, 1));
        Assert.Equal(0, TexCoordOf(DiffuseInfo(m)));
        Assert.True(HasTransform(DiffuseInfo(m)));
        JsonElement n = m.GetProperty("normalTexture");
        Assert.Equal(1, TexCoordOf(n));
        Assert.False(HasTransform(n));
        AssertSetIsViewerMapping(glb, "TEXCOORD_1", normal);
    }

    [Fact]
    public void ADiffuseAndANormalMapWithTheSameInexactMappingShareOneSet()
    {
        Glb glb = Export(Rotated(), false, normal: Rotated());
        JsonElement m = MaterialOfFace(glb, 1);
        Assert.Equal(2, Sets(glb, 1));
        Assert.Equal(1, TexCoordOf(DiffuseInfo(m)));
        Assert.Equal(1, TexCoordOf(m.GetProperty("normalTexture")));
        AssertSetIsViewerMapping(glb, "TEXCOORD_1", Rotated());
    }

    [Fact]
    public void ADiffuseAndANormalMapWithDifferentInexactMappingsEachReadTheirOwnSet()
    {
        Map normal = new Map { RepeatU = 5f, RepeatV = 2f, OffsetU = -0.3f, Rotation = -1.1f };
        Glb glb = Export(Rotated(), false, normal: normal);
        JsonElement m = MaterialOfFace(glb, 1);
        Assert.Equal(3, Sets(glb, 1));
        Assert.Equal(1, TexCoordOf(DiffuseInfo(m)));
        Assert.Equal(2, TexCoordOf(m.GetProperty("normalTexture")));
        AssertSetIsViewerMapping(glb, "TEXCOORD_1", Rotated());
        AssertSetIsViewerMapping(glb, "TEXCOORD_2", normal);
    }

    [Fact]
    public void OnAPlanarFaceTheNormalMapIsProjectedToo()
    {
        Map normal = new Map { RepeatU = 4f, RepeatV = 4f };
        Glb planar = Export(Plain, true, normal: normal);
        JsonElement m = MaterialOfFace(planar, 1);
        Assert.Equal(3, Sets(planar, 1));
        Assert.Equal(1, TexCoordOf(DiffuseInfo(m)));
        Assert.Equal(2, TexCoordOf(m.GetProperty("normalTexture")));
        // the two sets differ by the normal map's repeats
        Assert.NotEqual(Uvs(planar, 1, "TEXCOORD_1"), Uvs(planar, 1, "TEXCOORD_2"));
        // TEXCOORD_0 stays the original map, never projected
        Glb control = Export(Plain, false);
        Assert.Equal(Uvs(control, 1, "TEXCOORD_0").Select(BitConverter.SingleToInt32Bits), Uvs(planar, 1, "TEXCOORD_0").Select(BitConverter.SingleToInt32Bits));
    }

    [Fact]
    public void APlanarDiffuseAndAPlanarNormalMapWithTheSameTransformShareOneSet()
    {
        Glb glb = Export(Plain, true, normal: new Map());
        JsonElement m = MaterialOfFace(glb, 1);
        Assert.Equal(2, Sets(glb, 1));
        Assert.Equal(1, TexCoordOf(DiffuseInfo(m)));
        Assert.Equal(1, TexCoordOf(m.GetProperty("normalTexture")));
    }

    [Fact]
    public void ASpecularMapIsInExtrasWithItsOwnMappingAndNeedsNoSet()
    {
        Map spec = Rotated();
        Glb glb = Export(Shifted(), false, specular: spec);
        Assert.Equal(1, Sets(glb, 1));
        JsonElement lm = MaterialOfFace(glb, 1).GetProperty("extras").GetProperty("legacyMaterial");
        Assert.Equal(3f, lm.GetProperty("specularRepeat")[0].GetSingle(), 3);
        Assert.Equal(0.6f, lm.GetProperty("specularRotation").GetSingle(), 3);
    }

    [Fact]
    public void EveryMapNamesItsSetInTheMaterialExtras()
    {
        Glb glb = Export(Rotated(), false, normal: new Map { RepeatU = 5f, RepeatV = 2f, Rotation = -1.1f });
        JsonElement maps = MaterialOfFace(glb, 1).GetProperty("extras").Req("mapUvSets");
        Assert.Equal("TEXCOORD_1", maps.Req("diffuse").GetString());
        Assert.Equal("TEXCOORD_2", maps.Req("normal").GetString());
    }

    [Fact]
    public void APbrMapOnAPlanarFaceReadsTheFaceProjectionAndOnAFlatFaceTexCoord0()
    {
        Glb planar = Export(Plain, true, pbr: true);
        JsonElement mp = MaterialOfFace(planar, 1);
        Assert.Equal(2, Sets(planar, 1));
        Assert.Equal(1, TexCoordOf(DiffuseInfo(mp)));
        Assert.Equal(1, TexCoordOf(mp.GetProperty("normalTexture")));
        Assert.Equal(1, TexCoordOf(mp.GetProperty("emissiveTexture")));

        Glb flat = Export(Plain, false, pbr: true);
        JsonElement mf = MaterialOfFace(flat, 1);
        Assert.Equal(1, Sets(flat, 1));
        Assert.Equal(0, TexCoordOf(mf.GetProperty("normalTexture")));
        Assert.Equal(0, TexCoordOf(mf.GetProperty("emissiveTexture")));
    }

    [Fact]
    public void ExtraSetsAreHashedInTheMeshExtrasByFaceNumber()
    {
        Glb glb = Export(Rotated(), false, normal: new Map { RepeatU = 5f, RepeatV = 2f, Rotation = -1.1f });
        // a modelling tool keeps a mesh's extras, not a primitive's
        JsonElement hashes = glb.Root.GetProperty("meshes")[0].GetProperty("extras").Req("uvSetHashes").Req("1");
        Assert.Equal(2, hashes.EnumerateObject().Count());
        Assert.Matches("^[0-9a-f]{64}$", hashes.Req("TEXCOORD_1").GetString());
        Assert.NotEqual(hashes.Req("TEXCOORD_1").GetString(), hashes.Req("TEXCOORD_2").GetString());
    }
}
