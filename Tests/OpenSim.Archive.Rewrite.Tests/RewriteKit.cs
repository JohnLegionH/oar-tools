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
using Xunit;

namespace OpenSim.Archive.Rewrite.Tests;

internal static class RewriteKit
{
    public static readonly UUID Owner = UUID.Parse("a11ce000-0000-4000-8000-000000000001");
    public static readonly UUID DefaultTexture = UUID.Parse("89556747-24cb-43ed-920b-47caed15465f");

    public static List<(string name, byte[] data)> ReadEntries(byte[] archive)
    {
        using MemoryStream ms = new MemoryStream(archive);
        return ReadEntries(ms);
    }

    public static List<(string name, byte[] data)> ReadEntries(Stream archive)
    {
        List<(string, byte[])> list = new List<(string, byte[])>();
        // The archive saver's output may be a plain tar or gzip; the rewriter's output is always gzip.
        BufferedStream buffered = new BufferedStream(archive);
        bool gzip = buffered.ReadByte() == 0x1f && buffered.ReadByte() == 0x8b;
        archive.Seek(0, SeekOrigin.Begin);
        Stream body = gzip ? new GZipStream(archive, CompressionMode.Decompress) : archive;
        TarStreamReader tar = new TarStreamReader(body);
        TarEntryHeader h;
        while ((h = tar.ReadNext()) != null)
            list.Add((h.Name, tar.ReadBody()));
        return list;
    }

    /// <summary>A path in the test run's scratch folder (TEMP as the session sets it).</summary>
    public static string TempFile(string suffix)
    {
        string dir = Path.Combine(Path.GetTempPath(), "archive-rewrite-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + suffix);
    }
}
