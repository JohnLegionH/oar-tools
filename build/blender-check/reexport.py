# Copyright (c) 2026 Legion Builds
#
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Imports the .glb files of the identity fixtures (objects whose parts share names) into Blender, notes what Blender calls each object, and
# exports the scene again three ways: with the exporter's default settings, with custom properties included, and with custom properties and
# attributes included. Run headless inside Blender:
#
#   blender --background --python reexport.py -- <identity folder> <output folder> <results.json> [<export folder name>]
#
# <identity folder> holds expected.json and the export folder (named "export", or by the fourth argument) written by the BlenderFixtureTests test. The output folder receives
# default/<object>.glb, extras/<object>.glb and extras-attributes/<object>.glb, which are the input of restore. The results file lists, for each
# object, the names Blender gave the objects and what each re-export kept of the exporter's extras and attributes.

import json
import os
import struct
import sys

import bpy


def read_glb_document(path):
    with open(path, "rb") as f:
        data = f.read()
    off = 12
    while off < len(data):
        length, kind = struct.unpack_from("<II", data, off)
        off += 8
        if kind == 0x4E4F534A:
            return json.loads(data[off:off + length].decode("utf-8"))
        off += length
    return None


def describe(path):
    """What a .glb holds of the exporter's identity data: the nodes' extras, the part ids and link numbers, the mesh and material extras, the attributes."""
    document = read_glb_document(path)
    nodes = document.get("nodes", [])
    attributes = sorted({a for m in document.get("meshes", []) for p in m["primitives"] for a in p["attributes"]})
    return {
        "nodes": len(nodes),
        "nodesWithExtras": sum(1 for n in nodes if n.get("extras")),
        "nodesWithPartUuid": sum(1 for n in nodes if (n.get("extras") or {}).get("partUuid")),
        "nodesWithLinkNumber": sum(1 for n in nodes if "linkNumber" in (n.get("extras") or {})),
        "objectNodeExtras": any((n.get("extras") or {}).get("kind") == "object" for n in nodes),
        "meshesWithExtras": sum(1 for m in document.get("meshes", []) if m.get("extras")),
        "materialsWithExtras": sum(1 for m in document.get("materials", []) if m.get("extras")),
        "attributes": attributes,
        "nodeNames": [n.get("name") for n in nodes],
    }


def find_glb(export, name):
    for entry in sorted(os.listdir(export)):
        manifest = os.path.join(export, entry, "manifest.json")
        if os.path.isfile(manifest):
            with open(manifest, encoding="utf-8") as f:
                m = json.load(f)
            if m.get("name") == name:
                return os.path.join(export, entry, m["file"])
    return None


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    folder, out, results = args[0], args[1], args[2]
    export_name = args[3] if len(args) > 3 else "export"
    with open(os.path.join(folder, "expected.json"), encoding="utf-8") as f:
        expected = json.load(f)
    report = {"blender": bpy.app.version_string, "objects": []}
    for e in expected:
        name = e["object"]
        glb = find_glb(os.path.join(folder, export_name), name)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=glb)
        objects = [{"name": o.name, "type": o.type, "partUuid": o.get("partUuid"), "linkNumber": o.get("linkNumber")} for o in bpy.data.objects if o.get("partUuid") is not None]
        entry = {"object": name, "original": describe(glb), "blenderObjects": objects, "exports": {}}
        for label, options in (("default", {}), ("extras", {"export_extras": True}), ("extras-attributes", {"export_extras": True, "export_attributes": True})):
            target = os.path.join(out, label)
            os.makedirs(target, exist_ok=True)
            path = os.path.join(target, name + ".glb")
            bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", **options)
            entry["exports"][label] = describe(path)
        report["objects"].append(entry)
        print("OBJECT %s: Blender named the objects %s" % (name, [o["name"] for o in objects]))
    with open(results, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    print("RESULT Blender %s: %d objects re-exported" % (bpy.app.version_string, len(report["objects"])))


main()
