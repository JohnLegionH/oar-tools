/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenSim.Archive.Inspection;

namespace OpenSim.ArchiveInspector;

/// <summary>
/// The oar-tools program. The first argument names the subcommand; the program has no other mode.
/// </summary>
public static class Program
{
    /// <summary>The environment variable the oar-inspect, oar-export, oar-restore and oar-convert wrapper scripts set to their own name.</summary>
    public const string LegacyNameVariable = "OAR_TOOLS_LEGACY_NAME";

    private const string Usage =
@"Usage: oar-tools <subcommand> [arguments]

Subcommands:
  inspect      lists every part in link order and every permission mask of an OAR or IAR
  export       writes each build of an OAR as glTF
  restore      writes a new OAR from the original and its .glb files after editing in Blender
  convert      writes a new OAR with the convertible parts turned into mesh parts; --merge merges the parts of each linkset,
               --relink-from restores a link order
  report       who is credited for what in an OAR or IAR
  compare      checks a rewritten OAR against its original
  scan         finds parts whose scripts could break if they became mesh
  eligibility  decides which parts become mesh, which stay as they are, and why

oar-tools <subcommand> --help describes a subcommand. oar-tools --version prints the release.
oar-tools only reads the archives it is given and writes new files. It never changes who created or owns anything, never
adds a permission bit, and never contacts a grid.";

    public static int Main(string[] args)
    {
        string legacy = Environment.GetEnvironmentVariable(LegacyNameVariable);
        if (string.IsNullOrEmpty(legacy))
            legacy = null;

        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 64;
        }

        string sub = args[0];
        string[] rest = args.Skip(1).ToArray();

        // the old program names are still accepted as the first argument, for scripts that call 'oar-tools oar-convert ...'
        if (sub.StartsWith("oar-", StringComparison.Ordinal))
        {
            legacy ??= sub;
            sub = sub.Substring(4);
        }

        if (sub == "--version")
        {
            Console.Out.WriteLine("oar-tools " + ToolRelease.Version);
            return 0;
        }

        int code;
        switch (sub)
        {
            case "inspect": code = InspectCommand.Run(rest); break;
            case "export": code = ExportCommand.Run(rest); break;
            case "restore": code = RestoreCommand.Run(rest); break;
            case "convert": code = ConvertCommand.Run(rest); break;
            case "report": code = ReportCommand.Run(rest); break;
            case "compare": code = CompareCommand.Run(rest); break;
            case "scan": code = ScanCommand.Run(rest); break;
            case "eligibility": code = EligibilityCommand.Run(rest); break;
            case "-h":
            case "--help":
            case "help":
                Console.Out.WriteLine(Usage);
                return 0;
            default:
                Console.Error.WriteLine("unknown subcommand '" + Sanitize.Text(args[0]) + "'");
                Console.Error.WriteLine(Usage);
                return 64;
        }

        if (legacy != null && (rest.Contains("--help") || rest.Contains("-h")))
            Console.Out.WriteLine(LegacyNotice(legacy, sub));
        return code;
    }

    /// <summary>The line a legacy wrapper adds to --help: the old name works in this release and is going away.</summary>
    public static string LegacyNotice(string legacyName, string subcommand) =>
        "Note: the name " + legacyName + " is going away. It still works in this release; use 'oar-tools " + subcommand + "' instead.";
}
