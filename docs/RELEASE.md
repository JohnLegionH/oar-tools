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

## Publish

Create the release as a draft on the repository, attach both packages and `SHA256SUMS.txt`, and publish it by hand after
reading the notes. The tag is created when the draft is published.
