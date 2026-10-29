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

`inspect`, `report`, `compare`, `scan` and `eligibility` only read: they never write an archive and never change the archives
they are given; they print a report, and write report files only where `--out` or `--json` names a place for them. `export`
writes `.glb` files and textures; `restore` and `convert` write a new OAR (or IAR, for `convert --relink-from`) and leave the
input as it was.

`oar-tools <subcommand> --help` prints the full usage and exit codes of a subcommand.

## What export keeps

`export` writes each build the way the Second Life viewer builds it, and then changes nothing.

* **The geometry is not altered.** The vertices, normals, triangles and face numbers are written exactly as the viewer's volume code
  builds them (see `docs/GEOMETRY.md`): nothing is welded, no loose vertex or zero-area triangle is removed, nothing is reordered,
  and the faces are not indexed. `--clean` is the one opt-in change: it removes duplicate and loose vertices and zero-area triangles
  (texture coordinates, faces and face numbers never change) and stamps the file. `--no-clean` is still accepted and does nothing.
* **The original UV map is the first UV map.** `TEXCOORD_0` of every primitive is the face's own map, never transformed: the
  viewer's coordinates for a prim or a sculpt, and the mesh asset's own coordinates for a mesh part (glTF puts v = 0 at the top of
  the image, so v is turned over; Blender's importer turns it back). A face's texture repeats, offset and rotation are written as
  the material's `KHR_texture_transform` where that says them exactly. Where it cannot (a planar face, or a face rotated with
  unequal repeats: the viewer turns the coordinates and then repeats them, glTF repeats and then turns), the exact mapping is in
  `TEXCOORD_1` (Blender's second UV map) and only that face's material reads it. The material's extras say which set it uses and
  why (`uvSet`, `uvSetReason`) and keep the face's texture, repeats, offsets, rotation and mapping either way.
* **Textures and parts keep their identity.** A face's texture UUID is the material's `textureId` in its extras, the material's
  name and the name of the image; a texture that is not in the archive still has its UUID there, and the material says
  `missing-from-archive` and `placeholder`. Each part is a node named exactly as the part, whose extras hold its description, link
  number, part UUID, creator UUID and creator name (when the archive has one), owner and permission masks. The object's node holds
  the object's name, the root part's name and the parts in link order. Blender shows all of these as custom properties.
* **Only parts whose permissions allow it are exported** (the rule below).
* **What glTF itself requires.** Axes are Y up (a viewer vector (x, y, z) is written (x, z, -y)), which Blender's importer turns
  back to Z up; a face is a list of triangles; images are PNG, with the original asset copied beside them. A name that two parts
  share is kept as it is (Blender makes object names unique itself; the link number in the extras tells the parts apart).

The Blender importer is the check: `build/blender-check` imports an export of every prim type, a sculpt, a mesh part, a
multi-face part, a rotated and a planar face, a missing texture, a fifty-part linkset and a part that may not be exported, and
compares what Blender holds (object names, vertex and face counts, the first UV map, the texture UUIDs, link numbers and creators,
and the texture of every face's material slot) with the file and with the values the archive was made from (`docs/RELEASE.md`).

### The export permission rule

A part is exported unless its creator refused it. In plain words:

* A part held by its creator, a library part, and a part with no known creator or owner are exported.
* A part held by someone other than its creator is refused only when its base permission mask has the export right and neither its
  owner mask nor its everyone mask has it (after a transfer, the creator's choice stays in the everyone mask). A part whose base
  mask has no export right at all (an archive saved before the right existed) is exported.
* A refused part is skipped: it has no node, no mesh, no material and no inventory in the `.glb`, and nothing of it is exported.
  The rest of the object is exported. The part is listed in the build's `manifest.json` (status `skipped`, with the reason, its link
  number, name, ids and masks), counted in `export-summary.json` and `export-summary.txt` (skipped, and by reason) and, when every
  part of a build is refused, the build has no `.glb` and its manifest says so.
* A part exports normally as soon as its permissions allow it: the owner mask or the everyone mask has the export right.
* `convert` applies the same test: a part that fails it is not converted but kept as a prim, with the reason code `EXPORT_DENIED`.
  `inspect` reports the same parts in its export finding.

## What oar-tools never does

* It never adds a permission bit. Every subcommand that writes an archive keeps the base, owner, group, everyone and
  next-owner masks of every part and every item it carried over exactly as they were, and sets no export bit. A test runs
  each of those subcommands over archives with restricted masks and fails if any mask gains a bit
  (`PermissionGuardTests`). The manifest notecard that `convert --merge` adds is a new item; it gets the masks of the
  parts it describes (the parts of one merged part have identical masks; if they ever differed, it would get the bits they
  all have), so it is never more permissive than they are.
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

Before 0.5.0 the tools were separate programs, or modes of one program named by its first argument. Each has a subcommand
now:

| Before 0.5.0 | Now |
|---|---|
| `oar-inspect <archive>` | `oar-tools inspect <archive>` |
| `OpenSim.ArchiveInspector <archive>` (the bare-archive mode, with `--json`, `--all`, `--stats`) | `oar-tools report <archive>` |
| `oar-export` | `oar-tools export` |
| `oar-restore` | `oar-tools restore` |
| `oar-convert` | `oar-tools convert` |
| `oar-convert --merge`, `oar-convert --relink-from` | `oar-tools convert --merge`, `oar-tools convert --relink-from` |
| `oar-compare` | `oar-tools compare` |
| `oar-scan` | `oar-tools scan` |
| `oar-eligibility` | `oar-tools eligibility` |
| `oar-convert --fix-perms`, `--creator` | removed (changing permissions and importing new content are out of scope for this tool) |
| `oar-import`, `oar-texfetch` | removed |

The options of each subcommand are those of the program it replaces. For this release the package holds a wrapper script for
`oar-inspect`, `oar-export`, `oar-restore` and `oar-convert`, in `.cmd` form on Windows and as a shell script on Linux. A
wrapper calls the program in the same folder with the subcommand name and passes its arguments through unchanged, so
`oar-convert in.oar --out out.oar` does what `oar-tools convert in.oar --out out.oar` does, and the exit code is the
program's. `--help` through a wrapper ends with a line saying that the old name is going away. The wrappers will be removed
in a later release; use `oar-tools <subcommand>`. There is no wrapper for `oar-compare`, `oar-scan`, `oar-eligibility` or the
bare-archive mode: use the subcommand.

`oar-tools` also accepts an old name as its first argument (`oar-tools oar-convert ...`, `oar-tools oar-scan ...`) and treats
it the same way.

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
* The geometry in the glTF export is the Second Life viewer's own tessellation at its highest level of detail, with its face
  numbering (`ViewerFaceRule`); see `docs/GEOMETRY.md` for how it was checked and where it stops. The code that does it
  re-implements the viewer's volume code and lives in its own project, `Source/OarTools.ViewerVolume` (see Licences).

Every file carried from OpenSim keeps OpenSim's notice, and a file we changed says "Adapted 2026 by Legion Builds" below it.

## Licences

This repository is BSD-3-Clause (`LICENSE`), except for one project: `Source/OarTools.ViewerVolume`, which is licensed
LGPL-2.1 only (`LICENSES/LGPL-2.1.txt`). It re-implements, in C#, the Second Life viewer's volume generation
(`indra/llmath/llvolume.cpp`), so its files keep the viewer's notice (Copyright (C) 2010, Linden Research, Inc., LGPL version 2.1
only) with "Adapted 2026 by Legion Builds" below it. The full source of the program is in this repository, so anyone can change
that library and rebuild the program; the package carries it as its own assembly, `OarTools.ViewerVolume.dll`. The viewer
reference data under `Tests/OpenSim.Archive.GltfExport.Tests/Fixtures/ViewerReference` is output generated with the viewer's
volume code and used as test data.

The licence texts, and the libraries that are bundled, are in `THIRD-PARTY-NOTICES.txt`.
