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
using System.Xml.Linq;
using OpenSim.Framework;
using Xunit;

namespace OpenSim.Archive.Inspection.Tests;

/// <summary>
/// PrimFaceCount is OpenSim's rule for the number of faces of a prim (SceneObjectPart.GetNumberOfSides), carried without the
/// scene. Fixtures/face-counts.csv holds the answers the original gave for 4000 shapes: ordinary prims over the profile, hollow,
/// cut, path and twist fields, and 200 sculpt and mesh shapes. Each test builds its own shapes, so the class runs in parallel.
/// </summary>
public class PrimFaceCountTests
{
    private static IEnumerable<string[]> Rows() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "face-counts.csv")).Skip(1).Select(l => l.Split(','));

    [Fact]
    public void TheCarriedRuleGivesEveryAnswerTheOriginalGave()
    {
        int rows = 0;
        List<string> wrong = new List<string>();
        foreach (string[] c in Rows())
        {
            PrimitiveBaseShape s = new PrimitiveBaseShape
            {
                ProfileCurve = byte.Parse(c[0], CultureInfo.InvariantCulture),
                PathCurve = byte.Parse(c[1], CultureInfo.InvariantCulture),
                ProfileBegin = ushort.Parse(c[2], CultureInfo.InvariantCulture),
                ProfileEnd = ushort.Parse(c[3], CultureInfo.InvariantCulture),
                ProfileHollow = ushort.Parse(c[4], CultureInfo.InvariantCulture),
                PathBegin = ushort.Parse(c[5], CultureInfo.InvariantCulture),
                PathEnd = ushort.Parse(c[6], CultureInfo.InvariantCulture),
                PathSkew = sbyte.Parse(c[7], CultureInfo.InvariantCulture),
                PathTaperX = sbyte.Parse(c[8], CultureInfo.InvariantCulture),
                PathTaperY = sbyte.Parse(c[9], CultureInfo.InvariantCulture),
                PathRevolutions = byte.Parse(c[10], CultureInfo.InvariantCulture),
                PathRadiusOffset = sbyte.Parse(c[11], CultureInfo.InvariantCulture),
                PathTwist = sbyte.Parse(c[12], CultureInfo.InvariantCulture),
                PathTwistBegin = sbyte.Parse(c[13], CultureInfo.InvariantCulture),
                SculptEntry = c[14] == "1",
                SculptType = byte.Parse(c[15], CultureInfo.InvariantCulture)
            };
            int want = int.Parse(c[16], CultureInfo.InvariantCulture);
            int got = PrimFaceCount.GetNumberOfSides(s);
            rows++;
            if (got != want)
                wrong.Add(string.Join(",", c.Take(16)) + " want " + want + " got " + got);
        }
        Assert.Equal(4000, rows);
        Assert.True(wrong.Count == 0, wrong.Count + " shapes differ, first: " + wrong.FirstOrDefault());
    }

    [Theory]
    [InlineData("box", 6)]
    [InlineData("cylinder", 3)]
    [InlineData("sphere", 1)]
    public void APlainPrimOfTheMainKindsHasTheKnownNumberOfFaces(string kind, int faces)
    {
        PrimitiveBaseShape s = kind switch
        {
            "box" => new PrimitiveBaseShape { ProfileCurve = 1, PathCurve = 16 },
            "cylinder" => new PrimitiveBaseShape { ProfileCurve = 0, PathCurve = 16 },
            _ => new PrimitiveBaseShape { ProfileCurve = 5, PathCurve = 32 }
        };
        Assert.Equal(faces, PrimFaceCount.GetNumberOfSides(s));
    }

    [Fact]
    public void ABoxWithAProfileCutAndAHollowCountsTheCutFacesAndTheHollowFace()
    {
        // 6 faces, one side removed by the cut (12500 of 50000), two cut faces added, one hollow face
        XElement shape = XElement.Parse("<Shape><ProfileCurve>1</ProfileCurve><PathCurve>16</PathCurve><ProfileBegin>12500</ProfileBegin><ProfileHollow>20000</ProfileHollow></Shape>");
        Assert.Equal(6 - 1 + 2 + 1, PrimFaceCount.GetNumberOfSides(PrimFaceCount.ToServerShape(PrimFaceCount.ShapeFromXml(shape))));
    }

    [Fact]
    public void ReadingAShapeElementGivesTheStoredFields()
    {
        XElement shape = XElement.Parse("<Shape><PCode>9</PCode><ProfileCurve>33</ProfileCurve><PathCurve>32</PathCurve><PathBegin>10</PathBegin><PathEnd>20</PathEnd>" +
            "<PathTwist>-5</PathTwist><ProfileHollow>1000</ProfileHollow><SculptEntry>true</SculptEntry><SculptType>5</SculptType></Shape>");
        ShapeData d = PrimFaceCount.ShapeFromXml(shape);
        Assert.Equal(9, d.PCode);
        Assert.Equal(33, d.ProfileCurve);
        Assert.Equal(32, d.PathCurve);
        Assert.Equal(10, d.PathBegin);
        Assert.Equal(20, d.PathEnd);
        Assert.Equal(-5, d.PathTwist);
        Assert.Equal(1000, d.ProfileHollow);
        Assert.True(d.SculptEntry);
        Assert.Equal(5, d.SculptType);
    }

    [Fact]
    public void AMissingShapeElementIsAnEmptyShape()
    {
        ShapeData d = PrimFaceCount.ShapeFromXml(null);
        Assert.Equal(0, d.ProfileCurve);
        Assert.False(d.SculptEntry);
    }
}
