/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenSim.Archive.GltfExport;
using Xunit;

namespace OarTools.ViewerVolume.Tests;

/// <summary>The face mappings glTF's texture transform cannot say: when they need a second coordinate set, and what is in it.</summary>
public class ViewerTextureMappingTests
{
    [Theory]
    [InlineData(false, 1f, 1f, 0f, false)]      // plain
    [InlineData(false, 4f, 2f, 0f, false)]      // unequal repeats, no rotation: the extension says it exactly
    [InlineData(false, 2f, 2f, 0.6f, false)]    // rotation, equal repeats: exact
    [InlineData(false, 4f, 2f, 0.6f, true)]     // rotation with unequal repeats
    [InlineData(true, 1f, 1f, 0f, true)]        // planar
    [InlineData(true, 4f, 2f, 0.6f, true)]      // planar and rotated
    public void OnlyAPlanarFaceOrARotatedFaceWithUnequalRepeatsNeedsTheSecondSet(bool planar, float repeatU, float repeatV, float rotation, bool needs)
    {
        Assert.Equal(needs, ViewerTextureMapping.NeedsSecondSet(planar, repeatU, repeatV, rotation));
        Assert.Equal(needs, ViewerTextureMapping.SecondSetReason(planar, repeatU, repeatV, rotation) != null);
    }

    [Fact]
    public void TheReasonNamesTheMapping()
    {
        Assert.Equal("planar mapping", ViewerTextureMapping.SecondSetReason(true, 1f, 1f, 0f));
        Assert.Equal("rotation with unequal repeats", ViewerTextureMapping.SecondSetReason(false, 4f, 2f, 0.6f));
    }

    // a unit box's top face is four corners at x and y of plus or minus a half, normal +z
    private static readonly float[] TopPositions = { -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f };
    private static readonly float[] TopNormals = { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 };
    private static readonly float[] TopUvs = { 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f };

    [Fact]
    public void APlanarTopFaceRepeatsOnceEveryHalfMetreAlongXAndYAndIgnoresTheDefaultCoordinates()
    {
        // size 2 x 4 x 6: a corner at x = 0.5 is 1 m from the middle, at y = 0.5 it is 2 m. On a top face u = 1 + (2x - 0.5) and v (up) = 2y + 0.5.
        float[] got = ViewerTextureMapping.SecondSet(TopPositions, TopNormals, new float[8], true, 2, 4, 6, 1, 1, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            float x = TopPositions[i * 3] * 2, y = TopPositions[i * 3 + 1] * 4;
            Assert.Equal(1f + (2f * x - 0.5f), got[i * 2], 4);
            Assert.Equal(1f - (2f * y + 0.5f), got[i * 2 + 1], 4);
        }
    }

    [Fact]
    public void ATransformOnAPlanarFaceTurnsAndRepeatsTheProjectedCoordinatesAboutTheMiddle()
    {
        float[] plain = ViewerTextureMapping.SecondSet(TopPositions, TopNormals, new float[8], true, 1, 1, 1, 1, 1, 0, 0, 0);
        float[] moved = ViewerTextureMapping.SecondSet(TopPositions, TopNormals, new float[8], true, 1, 1, 1, 3, 1, 0.25f, 0f, 0f);
        for (int i = 0; i < 4; i++)
        {
            // repeat 3 along u about 0.5, then offset 0.25; v unchanged
            Assert.Equal((plain[i * 2] - 0.5f) * 3f + 0.25f + 0.5f, moved[i * 2], 4);
            Assert.Equal(plain[i * 2 + 1], moved[i * 2 + 1], 4);
        }
    }

    [Fact]
    public void ARotatedFaceWithUnequalRepeatsIsTheViewersTurnThenRepeatThenOffsetOfItsDefaultCoordinates()
    {
        float rot = 0.6f, repU = 4f, repV = 2f, offU = 0.1f, offV = -0.2f;
        float[] got = ViewerTextureMapping.SecondSet(TopPositions, TopNormals, TopUvs, false, 1, 1, 1, repU, repV, offU, offV, rot);
        for (int i = 0; i < 4; i++)
        {
            float s = TopUvs[i * 2] - 0.5f, t = (1f - TopUvs[i * 2 + 1]) - 0.5f;
            float turnedS = s * MathF.Cos(rot) + t * MathF.Sin(rot), turnedT = -s * MathF.Sin(rot) + t * MathF.Cos(rot);
            Assert.Equal(turnedS * repU + offU + 0.5f, got[i * 2], 4);
            Assert.Equal(1f - (turnedT * repV + offV + 0.5f), got[i * 2 + 1], 4);
        }
    }
}
