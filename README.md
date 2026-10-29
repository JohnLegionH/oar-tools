# oar-tools

Command-line tools for OpenSimulator region archives (OAR) and inventory archives (IAR). One program, `oar-tools`, with
subcommands. It reads archives, reports on them, and writes new ones; it does not need a running grid.

| Subcommand | What it does |
|---|---|
| `inspect` | Lists every part of an OAR or IAR in link order with every permission mask of every part and of every item inside it, the effective permissions of each object, and the creators and owners. Can compare two archives. Text, CSV or JSON. |
| `export` | Writes each build (linkset) of an OAR as a glTF binary (`.glb`) with a manifest, and the textures and mesh assets beside it, so the build can be edited in Blender. |
| `restore` | Takes the original OAR and the folder of `.glb` files (as exported, or edited in Blender) and writes a new OAR. A part whose geometry did not change is copied byte for byte; a changed part becomes a mesh part; moved, turned and resized parts are updated; deleted and added nodes are handled as the usage text describes (`oar-tools restore --help`). |
| `convert` | Turns the prims and sculpts of an OAR that are safe to convert into mesh parts and writes a new OAR, then checks the new archive against the old one. `--merge` merges the parts of each linkset that can merge into fewer mesh parts and writes a plan that says which parts merged and why the others stayed. `--relink-from <reference>` writes a copy of an OAR or IAR in which the parts have the link order of the reference. |
| `report` | Who is credited for what in an OAR or IAR. |
| `compare` | Checks a rewritten OAR against its original, part by part. |
| `scan` | Reads the scripts and says which parts could break if they became mesh. |
| `eligibility` | Decides which parts become mesh and which stay as they are, with the reason for each. |

`oar-tools <subcommand> --help` prints the full usage and exit codes of a subcommand.

## What oar-tools never does

* It never adds a permission bit. Every subcommand that writes an archive keeps the base, owner, group, everyone and
  next-owner masks of every part and every item it carried over exactly as they were, and sets no export bit. A test runs
  each of those subcommands over archives with restricted masks and fails if any mask gains a bit
  (`PermissionGuardTests`). The manifest notecard that `convert --merge` adds is a new item; it gets every permission bit
  of a full mask except export.
* It never changes who created or owns something that is in the archive. `restore` gives a part or texture it adds
  (a node with no part id in the `.glb`) a creator: the owner of the root part, or the one named with `--creator`; the
  report lists each of them.
* It never contacts a grid. No project other than the carried OpenSim.Framework uses a network type, and
  `NeverContactsAGridTests` scans the source for them.

## Install

Download the package for your system from the releases page, check it against `SHA256SUMS.txt`, and unpack it into a
folder. The package holds the program, the two native libraries it loads (the image library and the mesh optimiser), the
wrapper scripts, and the licence files. Keep the files together.

**Windows** (`oar-tools-0.5.0-windows-x64.zip`): unpack, then run `oar-tools.exe` from that folder, or add the folder to
`PATH`.

```
oar-tools.exe inspect region.oar
oar-tools.exe convert region.oar --out region-mesh.oar
```

**Linux** (`oar-tools-0.5.0-linux-x64.tar.gz`): unpack, then run `oar-tools` from that folder, or link it into a folder on
`PATH` (a link to the program or to a wrapper is followed, so the files beside it are found).

```
tar -xzf oar-tools-0.5.0-linux-x64.tar.gz
./oar-tools-0.5.0-linux-x64/oar-tools inspect region.oar
```

The Linux program starts on a system without OpenSSL: the bundled OpenMetaverse libraries are rewritten when the package is
built so that their hash objects are made in managed code (`OpenSim.Archive.PackagePatch`, `OpenSim.Archive.ManagedHash`).
It needs no .NET installation; the runtime is part of the program.

## The old program names

Before 0.5.0 the tools were separate programs: `oar-inspect`, `oar-export`, `oar-restore`, `oar-convert` (and, in the
repository they came from, `oar-import` and `oar-texfetch`, which are not part of oar-tools). For this release the package
holds a wrapper script for each of the first four, in `.cmd` form on Windows and as a shell script on Linux. A wrapper
calls the program in the same folder with the subcommand name and passes its arguments through unchanged, so
`oar-convert in.oar --out out.oar` does what `oar-tools convert in.oar --out out.oar` does, and the exit code is the
program's. `--help` through a wrapper ends with a line saying that the old name is going away. The wrappers will be removed
in a later release; use `oar-tools <subcommand>`.

`oar-tools` also accepts the old name as its first argument (`oar-tools oar-convert ...`) and treats it the same way.

## Permissions and the export bit

`inspect` reads and reports masks; it never writes. `convert` copies parts it does not convert exactly. A converted part
keeps its masks, and a merged part is the lowest-numbered of the parts it replaced, which all have the same creator and the
same masks (parts with a different creator or different masks never merge: `ClassKey` in `MergePlanner`). Archives written by
`restore` and `convert --relink-from` copy the masks of the parts they carry over.

## Build from source

```
dotnet build OarTools.sln -c Release
dotnet test OarTools.sln -c Release
```

The solution needs the .NET SDK named in `global.json` (10.0.x; a newer feature band is accepted) and NuGet access. The
tests build their archives in code. `OpenSim.Archive.Compare.Tests.RealOutputTests` needs archives that this repository does
not hold; it is skipped, and says so, unless `OAR_TOOLS_PROOF_ARCHIVES` names the folder that holds them.

`build/release.sh <output folder>` publishes the program for `win-x64` and `linux-x64` and writes the Windows zip, the
Linux tar.gz and `SHA256SUMS.txt`. `build/gen-notices.py` writes `THIRD-PARTY-NOTICES.txt` from the published program's
dependency lists.

## Where the code came from

oar-tools started from OpenSim-Tranquillity (https://github.com/OpenSim-NGC/OpenSim-Tranquillity): the `oar-inspect`,
`oar-export`, `oar-restore` and `oar-convert` tools of the fork's `feature/oar-import` branch at commit `5e91a003d4`
(release tag `oar-tools-v0.4.4`), which is based on OpenSimulator. This repository has its own history; the first commit is
that code with the changes listed in `CHANGELOG.md`.

* `Source/OpenSim.Framework` is OpenSim.Framework carried whole, with its notices. Its `VersionInfo.cs` is adapted (the
  version constants are plain text).
* `Source/OpenSim.Framework.Serialization` holds only OpenSim's archive constants, tar reader and tar writer, and the OSPA
  name constants.
* `Source/OpenSim.Region.PhysicsModules.Meshing` and `.SharedBase` hold OpenSim's prim and sculpt mesher and the mesh
  interfaces, without the region module wrapper.
* `Source/OpenSim.Archive.Inspection/PrimFaceCount.cs` is OpenSim's `SceneObjectPart.GetNumberOfSides` and `GetPrimType`
  as functions of a prim shape.
* `Source/OpenSim.Archive.ScriptScan/Lsl/LSL_Constants.cs` is OpenSim's LSL constants file, copied without a code change.
* The mesher in the glTF export follows the Second Life viewer's face numbering; see `ViewerFaceRule`.

Every file carried from OpenSim keeps OpenSim's notice, and a file we changed says "Adapted 2026 by Legion Builds" below it.
The licences are described in `LICENSE`; the libraries that are bundled are in `THIRD-PARTY-NOTICES.txt`.
