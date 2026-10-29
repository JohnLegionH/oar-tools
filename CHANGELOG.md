# Changelog

## 0.5.0

The tools move into their own repository and become one program.

### Changed

* **One program.** `inspect`, `export`, `restore` and `convert` (and `report`, `compare`, `scan`, `eligibility`) are
  subcommands of `oar-tools`. The package holds one program instead of five near-identical ones.
* **The old names are wrappers.** `oar-inspect`, `oar-export`, `oar-restore` and `oar-convert` are small scripts (`.cmd` on
  Windows, shell on Linux) that call `oar-tools` with the subcommand and pass the arguments through. Their `--help` says the
  old name is going away. They are removed in a later release.
* **No server stack.** The program no longer builds against the region server. The face-count rule of
  `SceneObjectPart.GetNumberOfSides` and OpenSim's LSL constants are carried as plain code (`PrimFaceCount`, `LSL_Constants`),
  checked against 4000 shapes and 964 constants taken from the original. `OpenSim.Framework` is carried whole.
* **The merge manifest notecard no longer has the export bit.** Its base and current masks were a full mask (export included);
  they are now a full mask without export. No other mask in any archive written by oar-tools changes.
* **The derived next-owner label** of `inspect` (introduced in oar tools 0.4.4, recorded here because this changelog starts at
  0.5.0): the next-owner column of an object's effective permissions is this tool's own combination, not the region's answer.
  The text report reads `next owner (derived)`, the CSV column is `effective_next_derived` (it was `effective_next` in 0.4.3)
  and the JSON entry is `nextOwnerDerived` (it was `nextOwner`). A script that reads the CSV or JSON of 0.4.3 must use the new
  names. The letters in the column are unchanged.
* The proof tests that read their data from environment variables now use the prefix `OAR_TOOLS_TEST_`
  (`OAR_TOOLS_PROOF_ARCHIVES` for `RealOutputTests`, which is skipped, and says so, when it is not set).

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
