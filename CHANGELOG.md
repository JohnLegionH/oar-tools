# Changelog

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
