# Changelog

## Unreleased

### Changed

* **`export` changes nothing after the geometry is built.** Cleaning is no longer the default: the faces are written exactly as
  the viewer's volume code builds them, with no welding, no removal of loose vertices or zero-area triangles, no reordering and no
  indexing. `--clean` turns cleaning on (it needs to be asked for); `--no-clean` is still accepted and now does nothing.
  `ExportOptions.Clean` is false unless set. A file written with `--clean` carries the same stamp as before.
* **The original UV map is always `TEXCOORD_0`.** The export no longer writes the viewer's texture transform into a face's
  coordinates. A face rotated with unequal repeats, and a planar face (whose projection was not applied before), carry their exact
  mapping in `TEXCOORD_1`, read only by that face's material (`texCoord` 1, no `KHR_texture_transform`); the other faces are as before.
  The material extras gain `uvSet` and `uvSetReason`; `uvTransformBaked` is gone. A face that has a second coordinate set is left
  as built when `--clean` is given.
* **The object node lists the root part's name and the parts in link order** (`rootPartName`, `linkOrder`).
* **The viewer's volume code is its own project, licensed as the viewer's is.** The code that re-implements the Second Life
  viewer's volume generation (`ViewerVolume`, `ViewerFaceRule`, the sculpt geometry and the point counts of a path and a profile)
  moved into `Source/OarTools.ViewerVolume`, an assembly of its own licensed LGPL-2.1 only, with the viewer's copyright notice
  kept in each file. The rest of the repository is unchanged (BSD-3-Clause). The LGPL text is in `LICENSES/LGPL-2.1.txt`, in
  `THIRD-PARTY-NOTICES.txt` and in both packages.

* **The exported geometry is what the viewer draws.** Prims and sculpts are tessellated as the Second Life viewer does at its
  highest level of detail: its points on the profile and path, its faces in its order, its triangles, normals and texture
  coordinates. The export used to build far fewer vertices (two triangles on a box side where the viewer has eighteen), and built the
  isometric and right triangle profiles as squares. The result was checked against the viewer's own volume code for 290 prim
  shapes and 116 sculpts at all four levels of detail; see `docs/GEOMETRY.md`. Files written by earlier versions have fewer
  vertices; the face numbers and the texture coordinate convention are the same.
* **`convert` keeps a prim as a prim** when its path type is not a line or a circle (the flexible path type, the viewer's test
  path) or its profile type is above the half circle: reason code `SHAPE_UNSUPPORTED`.

### Added

* `docs/GEOMETRY.md` and the README say what export keeps and state the export permission rule.
* `build/blender-check`: imports an export with Blender's own glTF importer, in the distribution's Blender and in the current
  long-term-support build, and checks names, counts, the first UV map, texture UUIDs, link numbers, creators and the texture of
  each face's material slot. It is a release check (`docs/RELEASE.md`).
* `ViewerTextureMapping` in `OarTools.ViewerVolume` (LGPL-2.1, with the viewer's notice): the planar projection and the face
  transform the second coordinate set is made from.

## 0.5.0

The tools move into their own repository and become one program.

### Changed

* **One program.** `oar-tools` has eight subcommands: `inspect`, `export`, `restore`, `convert`, `report`, `compare`, `scan`
  and `eligibility`. `inspect`, `report`, `compare`, `scan` and `eligibility` only read; `export` writes `.glb` files, and
  `restore` and `convert` write a new archive. The package holds one program instead of five near-identical ones.
* **Old names, and where they went.** `oar-inspect` is `inspect`; the bare-archive mode of `OpenSim.ArchiveInspector` is
  `report`; `oar-export`, `oar-restore`, `oar-convert` (with `--merge` and `--relink-from`), `oar-compare`, `oar-scan` and
  `oar-eligibility` are the subcommands of the same names. `oar-convert --fix-perms` and `--creator`, `oar-import` and
  `oar-texfetch` are gone. See "The old program names" in the README.
* **The old names are wrappers.** `oar-inspect`, `oar-export`, `oar-restore` and `oar-convert` are small scripts (`.cmd` on
  Windows, shell on Linux) that call `oar-tools` with the subcommand and pass the arguments through. Their `--help` says the
  old name is going away. They are removed in a later release.
* **No server stack.** The program no longer builds against the region server. The face-count rule of
  `SceneObjectPart.GetNumberOfSides` and OpenSim's LSL constants are carried as plain code (`PrimFaceCount`, `LSL_Constants`),
  checked against 4000 shapes and 964 constants taken from the original. `OpenSim.Framework` is carried whole.
* **The merge manifest notecard has the masks of the parts it describes.** In 0.4.x its base and current masks were a full
  mask, export bit included, whatever the parts allowed. It now carries the base, owner, group, everyone and next-owner
  masks of the parts merged into the part that holds it (identical by construction; where they differ, the bits all of them
  have). No other mask in any archive written by oar-tools changes.
* **The derived next-owner label** of `inspect` (introduced in oar tools 0.4.4, recorded here because this changelog starts at
  0.5.0): the next-owner column of an object's effective permissions is this tool's own combination, not the region's answer.
  The text report reads `next owner (derived)`, the CSV column is `effective_next_derived` (it was `effective_next` in 0.4.3)
  and the JSON entry is `nextOwnerDerived` (it was `nextOwner`). A script that reads the CSV or JSON of 0.4.3 must use the new
  names. The letters in the column are unchanged.
* The proof tests that read their data from environment variables now use the prefix `OAR_TOOLS_TEST_`
  (`OAR_TOOLS_PROOF_ARCHIVES` for `RealOutputTests`, which is skipped, and says so, when it is not set).

### Fixed

* `export` set the runtime's process-wide heap limit on every run from a cap worked out from the limit the run before had set,
  so a process that ran it several times (a test host) halved its limit each time. It now sets the limit once per process.
  This made the Ubuntu test job fail intermittently with `OutOfMemoryException` in tests that did not export at all.

### Removed

Changing permissions and importing new content are out of scope for this tool, so `--fix-perms` and `oar-import` are not part of it.

* **`oar-convert --fix-perms` and `--creator`**, and the code behind them. oar-tools never adds a permission bit.
* **`oar-import`** (the importer that wrote full-permission objects from `.glb` files).
* **`oar-texfetch`** (the texture fetch through a grid login) and `region-texture-check.py`.
* The materials and rigged-mesh work that was on separate branches of the original repository.
* The tests that load archives into a region server's scene or compare with the server's own object serializer. They need
  the server and stay with it; the equivalents here run the tools over archives written by hand.

### Added

* `PermissionGuardTests`: every subcommand that writes an archive is run over archives with restricted masks and the output
  masks are compared with the input.
* `NeverContactsAGridTests`.
* A CI workflow that builds and tests on Ubuntu and Windows, and a release script.
