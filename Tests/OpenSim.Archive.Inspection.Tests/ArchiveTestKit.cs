/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.IO.Compression;
using System.Text;
using OpenMetaverse;
using OpenSim.Archive.Inspection;
using OpenSim.Framework;
using OpenSim.Framework.Serialization;

namespace OpenSim.Archive.Inspection.Tests;

/// <summary>Plain tar files, archive control files and inventory item XML built by hand, and the report helpers.</summary>
internal static class ArchiveTestKit
{
    public static readonly UUID Alice = UUID.Parse("a11ce000-0000-4000-8000-000000000001");
    public static readonly UUID Bob = UUID.Parse("b0b00000-0000-4000-8000-000000000002");
    public static readonly UUID Carol = UUID.Parse("ca701000-0000-4000-8000-000000000003");
    public static readonly UUID Dave = UUID.Parse("da7e0000-0000-4000-8000-000000000004");
    public static readonly UUID Group1 = UUID.Parse("9a009000-0000-4000-8000-000000000005");

    public static ArchiveReport Inspect(byte[] archive, InspectorOptions options = null)
    {
        using MemoryStream ms = new MemoryStream(archive);
        return new ArchiveInspector(options).Inspect(ms);
    }

    public static string Dump(ArchiveReport r) =>
        "status=" + r.Status + " stop=" + r.StopReason + " at=" + r.StoppedAt + " entries=" + r.Archive.EntryCount +
        Environment.NewLine + "problems: " + string.Join(" | ", r.Problems.Select(p => p.Entry + ": " + p.Message)) +
        Environment.NewLine + "assets: " + string.Join(" | ", r.Assets.Select(a => a.Entry + " type=" + a.TypeName + " read=" + a.BodyRead));

    // ------------------------------------------------------------------ hand-made tar archives

    public static byte[] Tar(IEnumerable<(string name, byte[] data)> entries, bool gzip = true)
    {
        using MemoryStream raw = new MemoryStream();
        TarArchiveWriter w = new TarArchiveWriter(raw);
        foreach ((string name, byte[] data) in entries)
            w.WriteFile(name, data);
        w.Close();
        byte[] tar = raw.ToArray();
        if (!gzip)
            return tar;

        using MemoryStream gz = new MemoryStream();
        using (GZipStream z = new GZipStream(gz, CompressionLevel.Fastest, true))
            z.Write(tar, 0, tar.Length);
        return gz.ToArray();
    }

    public static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    public static byte[] OarControl(string version = "0.8") =>
        Utf8("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n<archive major_version=\"" + version.Split('.')[0] + "\" minor_version=\"" + version.Split('.')[1] + "\">" +
             "<creation_info><datetime>1700000000</datetime><id>" + UUID.Random() + "</id></creation_info><assets_included>True</assets_included>" +
             "<region_info><size_in_meters>256,256</size_in_meters></region_info></archive>");

    public static byte[] IarControl(string version = "0.3") =>
        Utf8("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n<archive major_version=\"" + version.Split('.')[0] + "\" minor_version=\"" + version.Split('.')[1] + "\"><assets_included>True</assets_included></archive>");

    public static string IarItemXml(string name, UUID id, UUID creator, UUID owner, UUID asset, int assetType, int date = 1700000000, string creatorUuidText = null, string creatorData = null, string creatorIdText = null)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n<InventoryItem>");
        sb.Append("<Name>").Append(System.Security.SecurityElement.Escape(name)).Append("</Name>");
        sb.Append("<ID>").Append(id).Append("</ID><InvType>").Append(assetType == 6 ? 6 : 7).Append("</InvType>");
        sb.Append("<CreatorUUID>").Append(System.Security.SecurityElement.Escape(creatorUuidText ?? creator.ToString())).Append("</CreatorUUID>");
        sb.Append("<CreationDate>").Append(date).Append("</CreationDate><Owner>").Append(owner).Append("</Owner><Description></Description>");
        sb.Append("<AssetType>").Append(assetType).Append("</AssetType><AssetID>").Append(asset).Append("</AssetID>");
        sb.Append("<SaleType>0</SaleType><SalePrice>0</SalePrice><BasePermissions>2147483647</BasePermissions><CurrentPermissions>2147483647</CurrentPermissions>");
        sb.Append("<EveryOnePermissions>0</EveryOnePermissions><NextPermissions>581632</NextPermissions><Flags>0</Flags>");
        sb.Append("<GroupID>").Append(UUID.Zero).Append("</GroupID><GroupOwned>False</GroupOwned>");
        if (creatorData != null)
            sb.Append("<CreatorData>").Append(System.Security.SecurityElement.Escape(creatorData)).Append("</CreatorData>");
        if (creatorIdText != null)
            sb.Append("<CreatorID>").Append(creatorIdText).Append("</CreatorID>");
        sb.Append("</InventoryItem>");
        return sb.ToString();
    }
}
