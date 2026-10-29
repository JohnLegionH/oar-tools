# Releasing oar-tools

## Build

`build/release.sh <output folder>` publishes the program for `win-x64` and `linux-x64`, and writes the Windows zip, the Linux
tar.gz and `SHA256SUMS.txt`. Run it from a clean checkout of the commit to be released, with the version set in
`Directory.Build.props` and `Source/OpenSim.ArchiveInspector/ToolRelease.cs` (a test checks that the two agree). Give it an
output folder that does not exist yet: besides the packages it keeps the exact files each package is made from as
`stage/<package name>/`, and writes `STAGED-FILES.txt` (SHA-256, size and path of every staged file) beside `SHA256SUMS.txt`.
`python -I build/package_test.py` tests that step.

## Checks before a release

1. `dotnet build OarTools.sln -c Release` and `dotnet test OarTools.sln -c Release` pass on Windows and on Linux, from a fresh
   clone of the commit. The CI workflow runs both.
2. The package starts on a Linux system without OpenSSL: unpack the tar.gz in a container that has no libssl or libcrypto and
   run `--version`, `inspect`, `convert`, `export` and `restore` on a small archive.
3. **Region round trip.** The unit tests write their archives in code; this check shows that a real region server accepts what
   the tool writes. It is run with the packaged program from the zip (not a development build) against a disposable OpenSim
   region that holds nothing you want to keep. The program that is run is the staged one,
   `stage/<windows package name>/oar-tools.exe`: the same files that are zipped, so there is no unpacking step.
   * Start the region with no content. Save an OAR from it and count its objects with `oar-tools inspect`; the count must be
     zero. (Do not rely on a console listing for this.)
   * Make small synthetic archives that cover each way the tool writes an archive: `convert` turning prims into mesh,
     `convert --merge`, `convert --relink-from` over a linkset whose prims hold link-number scripts, and `restore` from an
     `.glb` edited after `export`.
   * Load each output into the region with `load oar --merge`, then `save oar` the region to a new file.
   * Check the saved file against the tool's own output with `oar-tools inspect` (and `compare --match position`): the same
     objects, link order, names, permission masks and contents. For the relink case the scripts must compile and run in the
     region.
   * Run `export` on a region-saved OAR and validate the `.glb` files with a glTF validator.
   * Stop the region cleanly.
4. The permission guard (`PermissionGuardTests`) is green: no subcommand adds a permission bit.
5. **The packages are what was tested.** Without extracting anything, read each package's entry list and the SHA-256 of each
   entry (for example with Python's `zipfile` and `tarfile`, reading only) and compare them with `STAGED-FILES.txt`: every
   file matches, and nothing is extra or missing. For the Linux package, also mount its stage read-only in a Linux container
   and run `oar-tools --version` and `oar-tools inspect` on one output of the checks above.
6. **Blender import check.** The unit tests read the `.glb` files back as JSON; this check shows that Blender's own importer reads
   what the export writes. It runs in a throwaway container, so nothing is installed on the machine that runs it, and covers two
   Blender versions: the Ubuntu package and the current long-term-support build from blender.org (check
   https://www.blender.org/download/lts/ and update `BLENDER_LTS_URL` in `build/blender-check/run.sh` when the version has moved).
   * Write the fixtures: run `BlenderFixtureTests` with `OAR_TOOLS_BLENDER_FIXTURES` set to a new, empty folder. It writes an archive
     with every prim type, a sculpt, a mesh part, a part with six differently textured faces, a face rotated with unequal repeats,
     planar faces, a face whose texture is not in the archive, a fifty-part linkset whose parts have numeric link prefixes, and an
     object with a part that may not be exported; the export of it in both layouts (`default` and `--clean`); and `expected.json`,
     the values each part and face must come back as.
   * Run `build/blender-check/run.sh` in an `ubuntu:24.04` container with the repository mounted at `/src`, that folder at
     `/fixtures` and an empty folder at `/results` (the command is in the script's header). For both Blender versions and both
     layouts it imports every `.glb` headless and checks: object names equal part names; vertex and face counts equal the glTF's;
     the first UV map equals `TEXCOORD_0` (and the second equals `TEXCOORD_1` for the faces that have one); positions equal the
     glTF's; the texture UUID, link number, creator and description of every part and material can be read from the custom
     properties; every face shows its texture through the right material slot; a part that may not be exported is not in the file.
   * Every check must pass in both versions. Record the two Blender versions and the counts in the release notes.

## Publish

Create the release as a draft on the repository, attach both packages and `SHA256SUMS.txt`, and publish it by hand after
reading the notes. The tag is created when the draft is published.
