/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Text.Json;
using Xunit;

namespace OpenSim.Archive.Compare.Tests;

/// <summary>
/// Checks a real archive and the copies the rewriter made of it. The folder that holds identity.oar, renamed.oar and the
/// rewriter's proof.json (it names the source archive, the renamed part and its new name) is given by the environment
/// variable OAR_TOOLS_PROOF_ARCHIVES. The archives are not part of this repository: when the variable is not set the test is skipped,
/// and its skip reason says so.
/// </summary>
public class RealOutputTests
{
    [FactIfEnvironment("OAR_TOOLS_PROOF_ARCHIVES", "the folder that holds identity.oar, renamed.oar and proof.json")]
    public void RealArchive_IdentityCopyPasses_RenamePassesOnlyWithItsPlan()
    {
        string dir = Environment.GetEnvironmentVariable("OAR_TOOLS_PROOF_ARCHIVES");

        using JsonDocument proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "proof.json")));
        string source = proof.RootElement.GetProperty("source").GetString();
        string partId = proof.RootElement.GetProperty("renamedPartId").GetString();
        string newName = proof.RootElement.GetProperty("renameNewName").GetString();

        ArchiveComparer comparer = new ArchiveComparer();
        ArchiveSnapshot original = ArchiveSnapshot.Read(source);

        CompareResult identity = comparer.Compare(original, ArchiveSnapshot.Read(Path.Combine(dir, "identity.oar")));
        File.WriteAllText(Path.Combine(dir, "compare-identity.txt"), identity.ToText());
        File.WriteAllText(Path.Combine(dir, "compare-identity.json"), identity.ToJson());
        Assert.True(identity.Pass, identity.ToText());
        Assert.Equal(original.Parts.Count, identity.PartsMatched);

        ArchiveSnapshot renamed = ArchiveSnapshot.Read(Path.Combine(dir, "renamed.oar"));
        CompareResult noPlan = comparer.Compare(original, renamed);
        File.WriteAllText(Path.Combine(dir, "compare-rename-noplan.txt"), noPlan.ToText());
        Assert.False(noPlan.Pass);
        Assert.Single(noPlan.Unplanned);
        Assert.Equal("name", noPlan.Unplanned[0].Field);

        string planJson = JsonSerializer.Serialize(new
        {
            version = 1,
            partChanges = new[] { new { part = partId, field = "name", to = newName } }
        });
        File.WriteAllText(Path.Combine(dir, "rename-plan.json"), planJson);
        CompareResult withPlan = comparer.Compare(original, renamed, ComparePlan.Parse(planJson));
        File.WriteAllText(Path.Combine(dir, "compare-rename.txt"), withPlan.ToText());
        File.WriteAllText(Path.Combine(dir, "compare-rename.json"), withPlan.ToJson());
        Assert.True(withPlan.Pass, withPlan.ToText());
        Assert.Single(withPlan.Planned);
    }
}
