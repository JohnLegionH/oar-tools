/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenSim.Archive.GltfExport;
using Volume = OpenSim.Archive.GltfExport.ViewerVolume;
using Xunit;

namespace OarTools.ViewerVolume.Tests;

/// <summary>
/// The project holds code that re-implements the Second Life viewer's volume generation, so it is licensed as the viewer's code is
/// (LGPL-2.1 only) and its files keep the viewer's notice. These tests keep the project from drifting from that.
/// </summary>
public class LicenceTests
{
    // Files that re-express a function of the viewer's volume code; they carry the viewer's own notice.
    private static readonly string[] Derived = { "ViewerVolume.cs", "ViewerFaceRule.cs", "SculptGeometry.cs", "SculptVolume.cs", "ViewerPointCounts.cs", "ViewerTextureMapping.cs" };

    // The year each source file's notice gives (the "firstyear" of the viewer's $LicenseInfo line): llface.cpp 2001, the rest llvolume.cpp 2002.
    private static string FirstYear(string file) => file == "ViewerTextureMapping.cs" ? "2001" : "2002";

    private static string Root()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OarTools.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.True(dir != null, "OarTools.sln was not found above " + AppContext.BaseDirectory);
        return dir;
    }

    private static string[] SourceFiles() =>
        Directory.GetFiles(Path.Combine(Root(), "Source", "OarTools.ViewerVolume"), "*.cs", SearchOption.TopDirectoryOnly);

    private static string Head(string path) => string.Join("\n", File.ReadLines(path).Take(40));

    [Fact]
    public void EveryFileOfTheProjectCarriesTheLgplNoticeAndNoOtherLicence()
    {
        string[] files = SourceFiles();
        Assert.NotEmpty(files);
        foreach (string f in files)
        {
            string head = Head(f);
            Assert.True(head.Contains("version 2.1 of the License only"), Path.GetFileName(f) + " has no LGPL-2.1 notice");
            Assert.False(head.Contains("Mozilla Public License"), Path.GetFileName(f) + " carries the MPL header");
        }
    }

    [Fact]
    public void EveryDerivedFileKeepsTheViewersCopyrightNoticeAndSaysItWasAdapted()
    {
        foreach (string name in Derived)
        {
            string path = Path.Combine(Root(), "Source", "OarTools.ViewerVolume", name);
            Assert.True(File.Exists(path), name + " is missing");
            string head = Head(path);
            Assert.Contains("Copyright (C) 2010, Linden Research, Inc.", head);
            Assert.Contains("$LicenseInfo:firstyear=" + FirstYear(name) + "&license=viewerlgpl$", head);
            Assert.Contains("Linden Research, Inc., 945 Battery Street, San Francisco, CA  94111  USA", head);
            Assert.Contains("Adapted 2026 by Legion Builds", head);
        }
    }

    [Fact]
    public void TheDerivedFilesAreTheOnesThatCarryTheViewersNotice()
    {
        // a file added to the project without the notice must be listed above on purpose, or carry only our own line
        foreach (string f in SourceFiles())
        {
            bool hasViewerNotice = Head(f).Contains("Linden Research");
            bool listed = Derived.Contains(Path.GetFileName(f));
            Assert.True(hasViewerNotice == listed || !listed && !hasViewerNotice,
                Path.GetFileName(f) + (listed ? " is listed as derived but has no viewer notice" : " has the viewer notice but is not listed as derived"));
        }
    }

    [Fact]
    public void TheLgplTextIsInTheRepositoryAndTheNoticesFile()
    {
        string text = File.ReadAllText(Path.Combine(Root(), "LICENSES", "LGPL-2.1.txt"));
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", text);
        Assert.Contains("Version 2.1, February 1999", text);
        string notices = File.ReadAllText(Path.Combine(Root(), "THIRD-PARTY-NOTICES.txt"));
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", notices);
        Assert.Contains("Linden Research, Inc.", notices);
    }

    [Fact]
    public void ABoxIsBuiltWithSixFacesAtTheHighestDetail()
    {
        PrimParams p = new PrimParams
        {
            ProfileCurve = 1, PathCurve = 16, ProfileEnd = 1f, PathEnd = 1f, PathScaleX = 1f, PathScaleY = 1f
        };
        List<Volume.VolumeFace> faces = Volume.Generate(p, Volume.HighestDetail);
        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.NotEmpty(f.Indices));
    }
}
