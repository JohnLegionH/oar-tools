# The geometry the export writes

`oar-tools export` writes each prim and sculpt as the mesh the Second Life viewer draws, not as the physics shape a region
builds for it. `convert` uses the same geometry for the mesh parts it writes, so a converted part looks like the prim it
replaces. This page says what that means, how it was checked against the viewer, how close the match is, and where it stops.

## What the export produces

For a prim or a sculpt the export writes, face by face:

* **The viewer's faces, in the viewer's order.** A prim's faces are the ones its profile gives, in this order: the cap at the far
  end of the path (when the path is open), the outer sides (one for a circle or half circle, one per side of a square or
  triangle that the cut leaves), the inner wall of a hollow, the cap at the near end (when the path is open), and the two flat faces
  of a profile cut. Face *i* here is face *i* of a texture entry and the face number a script gives. A face with no area (the cut
  faces of a plain sphere lie on its axis) is left out of the file and its number stays unused.
* **The viewer's vertices and triangles.** Points are placed on the profile and the path exactly where the viewer places them at
  the level of detail used (below): the same number of sides on a circle, the extra points along the edges of a square or
  triangle, the same rows of points along a straight path (more of them with a twist), the same steps round a circular path
  (more of them with revolutions and twist). The triangles join the same points in the same direction.
* **The viewer's normals.** A smooth side shares normals between its triangles and wraps them where the surface closes on
  itself; a flat side (a square or triangle profile, and the flat inner wall of a hollow) does not share them across an edge; a
  cap has one normal. The poles of a sphere share one.
* **The viewer's texture coordinates**, written with v running down the image as glTF has it (the viewer's v is one minus this).
  Sides take u from the profile and v from the path; caps take both from the profile point; the bottom cap is mirrored.
* **A sculpt** takes each vertex from one pixel of its map, in a grid whose size follows the map's shape and the level of
  detail, stitched and mirrored or inverted as its type says, with a placeholder when the map's surface is too small or too large
  to be a sculpt.

### The level of detail

The viewer has four levels of detail for a prim or sculpt, with detail scales 1, 1.5, 2.5 and 4 (the last is what it draws when
the object is close). The export writes the **highest**, scale 4: a circle has 24 sides, a sculpt has up to 32 by 32 quads.
The code that builds faces takes the scale as a parameter, and the tests check all four.

`convert` writes the mesh asset with that geometry as its high level. Its three lower levels are not the viewer's lower
tessellations of the prim: the converter makes them by simplifying the high mesh (`LodGenerator`), as it does for any mesh.

## What export keeps

The README's "What export keeps" lists everything an export carries; for the geometry:

* **Nothing is changed after the volume is built.** The vertices, normals, triangles and face numbers are those of the viewer's
  volume code at its highest level of detail, written as they came out: not welded, not reduced, not reordered, not indexed.
  `--clean` is the only option that changes them, and it is opt-in.
* **The first UV map is the original.** `TEXCOORD_0` of each primitive is the volume's own coordinates for a prim or a sculpt and the
  mesh asset's own coordinates for a mesh part, with v turned over for glTF. Tests compare it with the generated or decoded values
  bit for bit, for every face of the shapes in `ExportFidelityTests`, whatever the face's texture mapping.
* **A face's texture mapping** is `KHR_texture_transform` when that is exact, and otherwise `TEXCOORD_1`:
  a planar face (the viewer projects each vertex onto the plane its normal points away from, one repeat per half metre of the
  part's size, and then applies the face's transform), and a face with a rotation and unequal repeats (the viewer turns, then
  repeats; glTF repeats, then turns). The code is `ViewerTextureMapping` in `Source/OarTools.ViewerVolume`; the extras of the
  material list `uvSet`, `uvSetReason` and every Second Life value of the face.
* **What glTF requires of the file:** Y up (Blender turns it back to Z up), triangles, v = 0 at the top of the image.

## How it was checked

The viewer's own volume code (the profile, path and face building of `indra/llmath/llvolume.cpp`) was built as a small command-line
test program in a container and run on a set of shapes at all four levels of detail. For each it printed every face's vertices,
normals, texture coordinates and triangles in the viewer's face order. Only those numbers are kept in the repository
(`Tests/OpenSim.Archive.GltfExport.Tests/Fixtures/ViewerReference`); the program and the viewer's code are not part of the
repository and are not distributed.

The shapes, each stored the way a region stores it (fields as integers), are:

* the seven basic shapes (box, cylinder, prism, sphere, torus, tube, ring) and the isometric and right triangle profiles, and the
  second circular path;
* each of them plain and with: a hollow of every hole type and of three sizes, a path cut, a profile cut (on and off the quarter
  grid, begin only, end only), a dimple, twist at one end and at both, taper in x and y, top shear, skew, revolutions,
  radius offset, a changed hole size, and combinations that change the number of faces: 29 variants of each shape,
  290 shapes in all;
* sculpts of every stitching type (sphere, torus, plane, cylinder), each plain, mirrored, inverted and both, from synthetic maps
  of 32 x 32, 64 x 64, 64 x 16, 16 x 64 and 128 x 32 pixels, and sculpts with a hollow (circle and square hole), a profile cut,
  a path cut and combinations from three of the maps: 116 sculpts in all.

Every shape is compared at all four levels of detail, face by face. `ViewerGeometryTests` fails unless, for every face, after
vertices that agree are merged:

| What | Tolerance |
|---|---|
| Face count and order | equal; a face has the viewer's number |
| Vertices and triangles | equal in number |
| Positions | within 1e-4 of the unit box, each way (every vertex of one has a vertex of the other that close) |
| Texture coordinates | within 1e-4 |
| Normals | within 2e-3 (difference of the unit normals) |
| Triangles | the same corners (matched by vertex) in the same winding |

The tolerances allow for single-precision arithmetic. Cleaning (the export's merge of vertices and removal of triangles
with no area) must not change any of this: the same comparison is run on the cleaned faces, and a cleaned face may differ
from an uncleaned one only in how its vertices are shared.

**Result:** all 290 shapes and all 116 sculpts match at all four levels of detail, with cleaning on and off. Before this
check, the export used the region's prim mesher in viewer mode: it matched the viewer's face list but built far fewer
vertices (two triangles on a box side where the viewer has eighteen), triangulated caps and hollows differently, and built
the isometric and right triangle profiles as squares.

## Where it stops

* **Prims and sculpts only.** A mesh part is exported from its mesh asset as it is.
* **Path and profile types.** The straight path and the two circular paths are built; the flexible path type and the viewer's
  test path are not, nor profile types above the half circle. `convert` keeps a prim with such a type as a prim
  (reason code `SHAPE_UNSUPPORTED`). A part with the flexible *setting* is kept as before (`FLEXI`).
* **Sculpts that are not a circle profile on a circular path** with a hollow, cut or open path are kept as prims
  (`SCULPT_SHAPE_UNVERIFIED`).
* **Triangulation at points on a straight line.** The strip between the outer edge and the hole of a hollow cap chooses between
  two triangles by the sign of an area; where three profile points lie on a straight line that sign is the last bit of a float,
  and a different maths library could choose the other triangle there. The region covered is the same; one triangle
  would be split at the collinear point or not. The tests pass with the library used to build them.
* **Quantities that are not geometry.** The mapping from a part's stored bytes to the fields above (a cut is the stored number
  times 0.00002 and so on) is the one the viewer uses for those fields; the stored bytes of a sculpt map are not decoded by
  the check (the maps are given as raw pixels).
* **The viewer at other distances.** The viewer picks one of its four levels of detail by how large the object looks on screen;
  the export always writes the highest and the full sculpt map.

## Refreshing the reference data

The reference files are text, one case after another: `CASE name faces N`, then for each face `FACE i verts V tris T`, one
`V x y z nx ny nz u v` line per vertex and one `T a b c` line per triangle. `cases.tsv` and `sculpt-cases.tsv` list the shapes by their
stored fields and the maps by file name; `maps` holds the raw pixels (red, green, blue, the first row at the bottom of the picture, the
order the viewer's image buffers have). A new case is added by writing its fields to the list, running the viewer's volume code on it
at the four detail scales and appending the output to the four `dump-lod` files.
