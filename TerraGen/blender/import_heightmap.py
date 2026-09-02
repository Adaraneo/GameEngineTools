"""Import a TerraGen --export tile (.f32 + .json) into Blender as a mesh.

Reads the exact float32 elevation data directly (no PIL/numpy dependency,
no PNG decoding, no quantization loss) and builds a grid mesh via bmesh,
one vertex per sample, Z = elevation in meters.

Usage from Blender's Scripting tab:
    import import_heightmap
    import_heightmap.import_tile("C:/path/to/tile_..._..._....json")

Usage from the command line:
    blender --background --python import_heightmap.py -- "C:/path/to/tile.json" [z_scale] [xy_scale]

z_scale / xy_scale (both default 1.0) let you compress/exaggerate the mesh —
e.g. z_scale=3 to make modest real-world relief actually readable at a glance,
or xy_scale=0.001 to bring a many-kilometers-wide tile down to a sane Blender
unit scale.
"""

import json
import os
import struct

import bmesh
import bpy


def import_tile(json_path, z_scale=1.0, xy_scale=1.0):
    with open(json_path, "r", encoding="utf-8") as f:
        meta = json.load(f)

    base = os.path.splitext(json_path)[0]
    raw_path = base + ".f32"

    width = meta["width"]
    height = meta["height"]
    cell_size = meta["cellSizeMeters"]

    with open(raw_path, "rb") as f:
        raw_bytes = f.read()
    count = width * height
    expected_bytes = count * 4
    if len(raw_bytes) != expected_bytes:
        raise ValueError(
            f"{raw_path}: expected {expected_bytes} bytes ({count} float32 samples), got {len(raw_bytes)}"
        )
    values = struct.unpack(f"<{count}f", raw_bytes)  # little-endian float32, row-major, row 0 first

    mesh = bpy.data.meshes.new(meta["tileId"])
    bm = bmesh.new()

    verts = [[None] * width for _ in range(height)]
    for row in range(height):
        y = row * cell_size * xy_scale
        row_offset = row * width
        for col in range(width):
            x = col * cell_size * xy_scale
            z = values[row_offset + col] * z_scale
            verts[row][col] = bm.verts.new((x, y, z))

    bm.verts.ensure_lookup_table()
    for row in range(height - 1):
        for col in range(width - 1):
            bm.faces.new((
                verts[row][col],
                verts[row][col + 1],
                verts[row + 1][col + 1],
                verts[row + 1][col],
            ))

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(meta["tileId"], mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = (meta["originXMeters"] * xy_scale, meta["originYMeters"] * xy_scale, 0.0)

    print(
        f"Imported {meta['tileId']}: {width}x{height} verts, "
        f"elevation [{meta['elevationMinMeters']:.1f}, {meta['elevationMaxMeters']:.1f}] m"
    )
    return obj


def import_all_in_dir(dir_path, z_scale=1.0, xy_scale=1.0):
    """Imports every .json tile found directly in dir_path — convenience wrapper for a whole
    --export run's worth of tiles at once."""
    imported = []
    for name in sorted(os.listdir(dir_path)):
        if name.endswith(".json"):
            imported.append(import_tile(os.path.join(dir_path, name), z_scale, xy_scale))
    return imported


if __name__ == "__main__":
    import sys

    argv = sys.argv
    if "--" in argv:
        args = argv[argv.index("--") + 1:]
    else:
        args = []

    if not args:
        print("Usage: blender --background --python import_heightmap.py -- <tile.json> [z_scale] [xy_scale]")
    else:
        json_arg = args[0]
        z_arg = float(args[1]) if len(args) > 1 else 1.0
        xy_arg = float(args[2]) if len(args) > 2 else 1.0
        import_tile(json_arg, z_arg, xy_arg)
