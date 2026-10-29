/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenMetaverse;
using OpenSim.Archive.Inspection;
using Xunit;

namespace OpenSim.Archive.GltfExport.Tests;

/// <summary>
/// The export-permission rule, one test per case. It follows Firestorm's policy for grids without export support:
/// the part is judged as if its owner asked to export it.
/// </summary>
public class ExportPermissionRuleTests
{
    private const uint Export = (uint)PermissionMask.Export;
    private const uint Modify = (uint)PermissionMask.Modify;
    private const uint Copy = (uint)PermissionMask.Copy;
    private const uint Transfer = (uint)PermissionMask.Transfer;
    private const uint Full = Modify | Copy | Transfer;

    private static readonly UUID Alice = new UUID("a11ce000-0000-4000-8000-000000000001");
    private static readonly UUID Bob = new UUID("b0b00000-0000-4000-8000-000000000002");

    private static PartRecord Part(UUID creator, UUID owner, uint? baseMask, uint? ownerMask, uint? everyoneMask)
    {
        return new PartRecord
        {
            Name = "Part",
            Creator = new CreatorRef { Id = creator },
            OwnerId = owner,
            Masks = new PermissionMasks { Base = baseMask, Owner = ownerMask, Everyone = everyoneMask }
        };
    }

    private static bool Allowed(PartRecord p) => ExportPermission.IsAllowed(p, out _);

    [Fact]
    public void TheCreatorWhoOwnsThePartMayExportItWhateverTheMasksSay()
    {
        Assert.True(Allowed(Part(Alice, Alice, 0u, 0u, 0u)));
        Assert.True(Allowed(Part(Alice, Alice, Export | Full, Full, 0u)));
        Assert.True(Allowed(Part(Alice, Alice, null, null, null)));
    }

    [Fact]
    public void WithTheExportBitInBaseTheOwnerMaskGrantsIt()
        => Assert.True(Allowed(Part(Alice, Bob, Export | Full, Export, 0u)));

    [Fact]
    public void WithTheExportBitInBaseTheEveryoneMaskGrantsIt()
        => Assert.True(Allowed(Part(Alice, Bob, Export | Full, 0u, Export)));

    [Fact]
    public void WithTheExportBitInBaseAndNeitherMaskGrantingItThePartIsRefusedEvenWithFullPermissions()
    {
        PartRecord p = Part(Alice, Bob, Export | Full, Full, 0u);
        Assert.False(ExportPermission.IsAllowed(p, out string reason));
        Assert.Contains("creator did not allow export", reason);
    }

    [Fact]
    public void WithoutTheExportBitInBaseFullOwnerPermissionsAllowIt()
        => Assert.True(Allowed(Part(Alice, Bob, Full, Full, 0u)));

    [Theory]
    [InlineData(Copy | Transfer)]
    [InlineData(Modify | Transfer)]
    [InlineData(Modify | Copy)]
    [InlineData(0u)]
    public void WithoutTheExportBitInBaseAnOwnerMaskMissingModifyCopyOrTransferRefusesIt(uint ownerMask)
    {
        PartRecord p = Part(Alice, Bob, Full, ownerMask, 0u);
        Assert.False(ExportPermission.IsAllowed(p, out string reason));
        Assert.Contains("creator did not allow export", reason);
    }

    [Fact]
    public void WithoutTheExportBitInBaseTheEveryoneMaskDoesNotHelp()
        => Assert.False(Allowed(Part(Alice, Bob, Full, 0u, Full)));

    [Fact]
    public void AnOwnerMaskWithExportButNotTheBaseBitAndNoFullPermissionsIsRefused()
        => Assert.False(Allowed(Part(Alice, Bob, Full, Export | Copy, Export)));

    [Fact]
    public void AMissingBaseMaskCountsAsNoExportBit()
    {
        Assert.False(Allowed(Part(Alice, Bob, null, Copy | Transfer, 0u)));
        Assert.True(Allowed(Part(Alice, Bob, null, Full, 0u)));
    }

    [Fact]
    public void MissingMasksRefuseAPartHeldBySomeoneOtherThanItsCreator()
        => Assert.False(Allowed(Part(Alice, Bob, null, null, null)));

    [Fact]
    public void LibraryContentGoesThroughTheSameChecks()
    {
        UUID library = OpenSim.Framework.Constants.m_MrOpenSimID;
        Assert.False(Allowed(Part(library, Bob, Full, 0u, 0u)));
        Assert.False(Allowed(Part(library, Bob, Export | Full, Full, 0u)));
        Assert.True(Allowed(Part(library, Bob, Full, Full, 0u)));
        Assert.True(Allowed(Part(library, Bob, Export | Full, Export, 0u)));
    }

    [Fact]
    public void AnUnknownCreatorGoesThroughTheSameChecks()
    {
        Assert.False(Allowed(Part(UUID.Zero, Bob, Full, 0u, 0u)));
        Assert.False(Allowed(Part(UUID.Zero, Bob, Export | Full, Full, 0u)));
        Assert.True(Allowed(Part(UUID.Zero, Bob, Full, Full, 0u)));
    }

    [Fact]
    public void AnUnknownOwnerGoesThroughTheSameChecks()
    {
        Assert.False(Allowed(Part(Alice, UUID.Zero, Full, 0u, 0u)));
        Assert.False(Allowed(Part(Alice, UUID.Zero, Export | Full, Full, 0u)));
        Assert.True(Allowed(Part(Alice, UUID.Zero, Full, Full, 0u)));
    }

    [Fact]
    public void AnUnknownCreatorAndAnUnknownOwnerAreNotTheSamePerson()
    {
        Assert.False(Allowed(Part(UUID.Zero, UUID.Zero, 0u, 0u, 0u)));
        Assert.True(Allowed(Part(UUID.Zero, UUID.Zero, Full, Full, 0u)));
    }
}
