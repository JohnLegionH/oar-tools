# Copyright (c) 2026 Legion Builds
#
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Imports the .glb files of an oar-tools export with Blender's own glTF importer and checks what comes out against the glTF file and
# against the values the fixtures were made from. Run headless inside Blender:
#
#   blender --background --python check_import.py -- <fixtures folder> <default|clean> <results.json>
#
# <fixtures folder> holds expected.json and the export folders <default> and <clean>, as written by the BlenderFixtureTests test with the
# OAR_TOOLS_BLENDER_FIXTURES variable set. The script prints one line per check that failed and a summary, writes every check to
# <results.json>, and exits with status 1 if any check failed.

import json
import math
import os
import struct
import sys

import bpy

TOLERANCE = 1e-5
failures = 0
results = []


def check(fixture, name, ok, detail=""):
    global failures
    results.append({"fixture": fixture, "check": name, "ok": bool(ok), "detail": detail})
    if not ok:
        failures += 1
        print("FAIL %s: %s %s" % (fixture, name, detail))


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    off = 12
    document, binary = None, b""
    while off < len(data):
        length, kind = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off:off + length]
        off += length
        if kind == 0x4E4F534A:
            document = json.loads(chunk.decode("utf-8"))
        elif kind == 0x004E4942:
            binary = chunk
    return document, binary


def accessor(document, binary, index):
    a = document["accessors"][index]
    view = document["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
    code = {5126: "f", 5125: "I", 5123: "H"}[a["componentType"]]
    count = a["count"] * width
    values = struct.unpack_from("<%d%s" % (count, code), binary, view.get("byteOffset", 0) + a.get("byteOffset", 0))
    return [values[i:i + width] for i in range(0, count, width)] if width > 1 else list(values)


def corners(document, binary, primitive, attribute):
    """The values of an attribute at each triangle corner of a primitive, in triangle order (indexed or not)."""
    attributes = primitive["attributes"]
    if attribute not in attributes:
        return None
    values = accessor(document, binary, attributes[attribute])
    if "indices" in primitive:
        return [values[i] for i in accessor(document, binary, primitive["indices"])]
    return values


def prop(obj, name):
    """A custom property of the object, or of its mesh data (the importer puts the extras of a node on the object and of a mesh on the mesh)."""
    if name in obj.keys():
        return obj[name]
    data = getattr(obj, "data", None)
    if data is not None and name in data.keys():
        return data[name]
    return None


def image_of(material):
    if material is None or not material.use_nodes:
        return None
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            return node.image
    return None


def uv_map_nodes(material):
    if material is None or not material.use_nodes:
        return []
    return [n.uv_map for n in material.node_tree.nodes if n.type == "UVMAP"]


def layer_read(material, texture):
    """The UV map the image node showing <texture> reads: its layer name, None for the first map (no link), or a word for a node not found."""
    if material is None or not material.use_nodes:
        return "no nodes"

    def trace(node):
        links = node.inputs["Vector"].links if "Vector" in node.inputs else []
        if not links:
            return None
        source = links[0].from_node
        if source.type == "UVMAP":
            return source.uv_map
        if source.type == "MAPPING":
            return trace(source)
        return "unexpected node " + source.type

    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None and node.image.name.startswith(texture):
            return trace(node)
    return "no image node"


def build_folder(export, object_name):
    for entry in sorted(os.listdir(export)):
        manifest = os.path.join(export, entry, "manifest.json")
        if os.path.isfile(manifest):
            with open(manifest, encoding="utf-8") as f:
                m = json.load(f)
            if m.get("name") == object_name:
                return os.path.join(export, entry, m["file"]), m
    return None, None


def check_object(fixture, export, expected):
    name = expected["object"]
    glb, manifest = build_folder(export, name)
    check(name, "the build has a .glb", glb is not None)
    if glb is None:
        return
    document, binary = read_glb(glb)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.gltf(filepath=glb)
    except RuntimeError as e:
        check(name, "Blender imports the .glb", False, str(e).splitlines()[-1] if str(e) else "import failed")
        return

    parts = expected["parts"]
    exported = [p for p in parts if p["status"] == "exported"]
    skipped = [p for p in parts if p["status"] == "skipped"]

    nodes = {n.get("name"): n for n in document["nodes"]}
    for p in exported:
        obj = bpy.data.objects.get(p["name"])
        check(name, "object named '%s' exists" % p["name"], obj is not None)
        if obj is None:
            continue
        check(name, "'%s' link number" % p["name"], prop(obj, "linkNumber") == p["linkNumber"], "got %r, want %r" % (prop(obj, "linkNumber"), p["linkNumber"]))
        check(name, "'%s' creator id" % p["name"], prop(obj, "creatorId") == p["creatorId"], "got %r, want %r" % (prop(obj, "creatorId"), p["creatorId"]))
        check(name, "'%s' creator name" % p["name"], (prop(obj, "creatorName") or "") == p["creatorName"], "got %r, want %r" % (prop(obj, "creatorName"), p["creatorName"]))
        check(name, "'%s' description" % p["name"], (prop(obj, "description") or "") == p["description"], "got %r" % (prop(obj, "description"),))
        check(name, "'%s' part uuid" % p["name"], prop(obj, "partUuid") == p["partUuid"], "got %r" % (prop(obj, "partUuid"),))
        if obj.type == "MESH":
            check_mesh(name, obj, p, nodes[p["name"]], document, binary)
    for p in skipped:
        check(name, "skipped part '%s' is not imported" % p["name"], bpy.data.objects.get(p["name"]) is None)
        check(name, "skipped part '%s' is nowhere in the file" % p["name"], p["name"] not in json.dumps(document) and p["partUuid"] not in json.dumps(document))

    # the object node: the root name and the link order of the parts
    holder = [o for o in bpy.data.objects if prop(o, "kind") == "object"]
    check(name, "the object node is imported", len(holder) == 1)
    if holder:
        check(name, "root part name on the object node", prop(holder[0], "rootPartName") == name, "got %r" % (prop(holder[0], "rootPartName"),))
        order = prop(holder[0], "linkOrder")
        want = [p["linkNumber"] for p in sorted(exported, key=lambda p: p["linkNumber"])]
        got = [o["linkNumber"] for o in order] if order else None
        check(name, "link order on the object node", got == want, "got %r" % (got,))


def check_mesh(fixture, obj, part, node, document, binary):
    mesh = obj.data
    gltf_mesh = document["meshes"][node["mesh"]]
    primitives = gltf_mesh["primitives"]

    vertices = sum(document["accessors"][p["attributes"]["POSITION"]]["count"] for p in primitives)
    triangles = sum((document["accessors"][p["indices"]]["count"] if "indices" in p else document["accessors"][p["attributes"]["POSITION"]]["count"]) // 3 for p in primitives)
    check(fixture, "'%s' vertex count equals the glTF's" % part["name"], len(mesh.vertices) == vertices, "Blender %d, glTF %d" % (len(mesh.vertices), vertices))
    check(fixture, "'%s' face count equals the glTF's" % part["name"], len(mesh.polygons) == triangles, "Blender %d, glTF %d" % (len(mesh.polygons), triangles))
    if len(mesh.polygons) != triangles or len(mesh.loops) != triangles * 3:
        return

    check(fixture, "'%s' has a UV map" % part["name"], len(mesh.uv_layers) >= 1)
    if not mesh.uv_layers:
        return

    # polygon -> the primitive (face) it came from, in the file's order
    owner = []
    for k, p in enumerate(primitives):
        count = (document["accessors"][p["indices"]]["count"] if "indices" in p else document["accessors"][p["attributes"]["POSITION"]]["count"]) // 3
        owner.extend([k] * count)

    uv0 = []
    pos = []
    for p in primitives:
        uv0.extend(corners(document, binary, p, "TEXCOORD_0"))
        pos.extend(corners(document, binary, p, "POSITION"))
    layer = mesh.uv_layers[0].data
    worst = 0.0
    for i, (u, v) in enumerate(uv0):
        got = layer[i].uv
        worst = max(worst, abs(got[0] - u), abs((1.0 - got[1]) - v))
    check(fixture, "'%s' first UV map equals TEXCOORD_0" % part["name"], worst <= TOLERANCE, "largest difference %g" % worst)

    # positions: Blender's axes are the glTF's turned back (x, y, z) = (gx, -gz, gy)
    worst = 0.0
    for i, loop in enumerate(mesh.loops):
        vx = mesh.vertices[loop.vertex_index].co
        gx, gy, gz = pos[i]
        worst = max(worst, abs(vx.x - gx), abs(vx.y + gz), abs(vx.z - gy))
    check(fixture, "'%s' positions equal the glTF's" % part["name"], worst <= TOLERANCE, "largest difference %g" % worst)

    # the extra coordinate sets: the faces that have each one, and its values in the matching UV map
    set_faces = {int(n): set(faces) for n, faces in part.get("setFaces", {"1": part["secondSetFaces"]}).items()}
    for n in sorted(set_faces):
        attribute = "TEXCOORD_%d" % n
        has = [k for k, p in enumerate(primitives) if attribute in p["attributes"]]
        got_faces = {primitives[k]["extras"]["primFace"] for k in has}
        check(fixture, "'%s' faces with %s" % (part["name"], attribute), got_faces == set_faces[n], "got %r, want %r" % (sorted(got_faces), sorted(set_faces[n])))
        if not has:
            continue
        check(fixture, "'%s' has UV map number %d" % (part["name"], n + 1), len(mesh.uv_layers) > n)
        if len(mesh.uv_layers) > n:
            values_at = []
            for p in primitives:
                values = corners(document, binary, p, attribute)
                values_at.extend(values if values is not None else [None] * len(corners(document, binary, p, "POSITION")))
            layer_n = mesh.uv_layers[n].data
            worst = 0.0
            for i, value in enumerate(values_at):
                if value is None:
                    continue
                worst = max(worst, abs(layer_n[i].uv[0] - value[0]), abs((1.0 - layer_n[i].uv[1]) - value[1]))
            check(fixture, "'%s' UV map number %d equals %s" % (part["name"], n + 1, attribute), worst <= TOLERANCE, "largest difference %g" % worst)
    faces_second = set_faces.get(1, set())

    # each face shows its texture through the right material slot
    for polygon in mesh.polygons:
        k = owner[polygon.index]
        face = primitives[k]["extras"]["primFace"]
        texture = part["faceTextures"].get(str(face), part["defaultTexture"])
        if polygon.index and owner[polygon.index - 1] == k and polygon.material_index == mesh.polygons[polygon.index - 1].material_index:
            continue                       # one check per face is enough: the slot is the same for the whole primitive
        slots = obj.material_slots
        material = slots[polygon.material_index].material if polygon.material_index < len(slots) else None
        label = "'%s' face %d" % (part["name"], face)
        check(fixture, label + " has a material", material is not None)
        if material is None:
            continue
        check(fixture, label + " material carries the texture uuid", material.get("textureId") == texture, "got %r, want %s" % (material.get("textureId"), texture))
        image = image_of(material)
        if face in part["textureMissing"]:
            check(fixture, label + " missing texture is flagged", material.get("placeholder") is True and material.get("textureStatus") == "missing-from-archive", "placeholder %r, status %r" % (material.get("placeholder"), material.get("textureStatus")))
            check(fixture, label + " missing texture has no image", image is None)
        else:
            check(fixture, label + " shows the texture's image", image is not None and image.name.startswith(texture), "image %r, want %s" % (image.name if image else None, texture))
        # every map of the face reads the UV map its mapping needs
        roles = part.get("mapSets", {}).get(str(face))
        textures = part.get("mapTextures", {}).get(str(face))
        if roles is None:
            roles = {"diffuse": 1 if face in faces_second else 0}
            textures = {"diffuse": texture}
        for role, want in sorted(roles.items()):
            if role == "diffuse" and face in part["textureMissing"]:
                continue
            read = layer_read(material, textures[role])
            wanted = mesh.uv_layers[want].name if want < len(mesh.uv_layers) else "<no UV map number %d>" % (want + 1)
            ok = (read is None or read == mesh.uv_layers[0].name) if want == 0 else read == wanted
            check(fixture, label + " %s map reads UV map number %d" % (role, want + 1), ok, "reads %r, want %r" % (read, wanted))


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    fixtures, layout, out = args[0], args[1], args[2]
    with open(os.path.join(fixtures, "expected.json"), encoding="utf-8") as f:
        expected = json.load(f)
    print("Blender %s, layout %s, %d objects" % (bpy.app.version_string, layout, len(expected)))
    for e in expected:
        check_object(e["object"], os.path.join(fixtures, layout), e)
    summary = {"blender": bpy.app.version_string, "layout": layout, "checks": len(results), "failed": failures, "results": results}
    with open(out, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=1)
    print("RESULT Blender %s %s: %d checks, %d failed" % (bpy.app.version_string, layout, len(results), failures))
    sys.exit(1 if failures else 0)


main()
