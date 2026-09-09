#!/usr/bin/env python3
"""Isolated behavioral tests for the read-only map and alpha audit tools."""

import base64
import gzip
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
import xml.etree.ElementTree as ET

from audit_tmx import audit, decode_data
from audit_alpha import inspect_alpha

try:
    from PIL import Image
except ImportError:
    Image = None


def png_bytes(width, height):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rows = (b"\x00" + bytes([80, 120, 60, 255]) * width) * height
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


class MapAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="tiled-map-audit-")
        self.root = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.map = self.root / "map.tmx"
        self.atlas = ('<tileset name="ground" tilewidth="2" tileheight="2" columns="2" tilecount="2">'
                      '<image source="atlas.png" width="4" height="2"/></tileset>')
        (self.root / "atlas.png").write_bytes(png_bytes(4, 2))
        self.write("ground.tsx", self.atlas)

    def write(self, name, text):
        target = self.root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
        return target

    def make_map(self, content=None, tileset=None, orientation="orthogonal", infinite=False):
        tileset = tileset or '<tileset firstgid="1" source="ground.tsx"/>'
        content = content if content is not None else '<layer id="1" width="2" height="1"><data encoding="csv">1,2</data></layer>'
        self.write("map.tmx", f'<map orientation="{orientation}" tilewidth="2" tileheight="2" width="2" height="1" infinite="{int(infinite)}">{tileset}{content}</map>')
        return audit(self.map)

    def codes(self, result, severity="errors"):
        return {item["code"] for item in result[severity]}

    def test_atlas_reuse_and_input_unchanged(self):
        result = self.make_map()
        before = {file.name: file.read_bytes() for file in self.root.iterdir()}
        self.assertTrue(result["pass"], result)
        self.assertEqual(2, result["tilesets"][0]["directlyUsed"])
        self.assertEqual([], result["warnings"])
        audit(self.map, self.root)
        self.assertEqual(before, {file.name: file.read_bytes() for file in self.root.iterdir()})

    def test_isometric_and_flip_bits(self):
        for flag in (0x80000000, 0x40000000, 0x20000000, 0x10000000, 0xF0000000):
            with self.subTest(flag=flag):
                result = self.make_map(f'<objectgroup><object id="1" gid="{flag | 1}"/></objectgroup>', orientation="isometric")
                self.assertTrue(result["pass"], result)
                self.assertEqual({0: 1}, result["tilesets"][0]["instancesByLocalId"])

    def test_nested_group_and_infinite_chunks(self):
        result = self.make_map('<group><group><layer id="4" width="0" height="0"><data encoding="csv"><chunk x="-2" y="0" width="2" height="1">1,2</chunk><chunk x="0" y="0" width="1" height="1">1</chunk></data></layer></group></group>', infinite=True)
        self.assertTrue(result["pass"], result)
        self.assertEqual(3, result["tilesets"][0]["placedInstances"])

    def test_encodings(self):
        payload = struct.pack("<3I", 1, 0, 2)
        for compression in (None, "zlib", "gzip"):
            binary = zlib.compress(payload) if compression == "zlib" else gzip.compress(payload) if compression == "gzip" else payload
            data = ET.fromstring(f'<data>{base64.b64encode(binary).decode()}</data>')
            with self.subTest(compression=compression):
                self.assertEqual([1, 0, 2], decode_data(data, "base64", compression, 3))
        self.assertEqual([1, 0], decode_data(ET.fromstring('<data><tile gid="1"/><tile gid="0"/></data>'), None, None, 2))

    def test_bad_payloads_fail(self):
        for data, encoding, compression, size in [
            ('<data>1</data>', 'csv', None, 2),
            ('<data>1,,2</data>', 'csv', None, 3),
            ('<data>-1</data>', 'csv', None, 1),
            ('<data>4294967296</data>', 'csv', None, 1),
            ('<data>!!!!</data>', 'base64', None, 1),
            ('<data>AAAAAA==</data>', 'base64', 'zstd', 1),
            ('<data>1</data>', 'csv', 'gzip', 1),
        ]:
            with self.subTest(data=data, compression=compression), self.assertRaises(ValueError):
                decode_data(ET.fromstring(data), encoding, compression, size)

    def test_oversized_compressed_payload_fails(self):
        data = ET.fromstring(f'<data>{base64.b64encode(zlib.compress(bytes(400))).decode()}</data>')
        with self.assertRaises(ValueError):
            decode_data(data, "base64", "zlib", 1)

    def test_missing_file_and_invalid_gid(self):
        self.assertIn("invalid_gid", self.codes(self.make_map('<objectgroup><object gid="3"/></objectgroup>')))
        self.write("ground.tsx", self.atlas.replace("atlas.png", "missing.png"))
        self.assertIn("missing_resource", self.codes(self.make_map()))

    def test_sparse_collection_ids(self):
        self.write("ground.tsx", '<tileset name="sparse" tilecount="2" columns="0"><tile id="0"><image source="atlas.png"/></tile><tile id="5"><image source="atlas.png"/></tile></tileset>')
        result = self.make_map('<objectgroup><object gid="1"/><object gid="6"/></objectgroup>')
        self.assertTrue(result["pass"], result)
        self.assertIn("invalid_gid", self.codes(self.make_map('<objectgroup><object gid="2"/></objectgroup>')))

    def test_animation_frames_are_indirectly_used(self):
        animated = self.atlas.replace('</tileset>', '<tile id="0"><animation><frame tileid="1" duration="100"/></animation></tile></tileset>')
        self.write("ground.tsx", animated)
        result = self.make_map('<objectgroup><object gid="1"/></objectgroup>')
        self.assertTrue(result["pass"], result)
        self.assertEqual(2, result["tilesets"][0]["usedIncludingAnimation"])
        self.assertEqual([], result["tilesets"][0]["unusedLocalIds"])
        self.write("ground.tsx", animated.replace('tileid="1"', 'tileid="7"'))
        self.assertIn("invalid_animation_frame", self.codes(self.make_map()))

    def test_relative_tsx_and_file_property(self):
        self.write("sets/ground.tsx", self.atlas.replace("atlas.png", "../atlas.png"))
        self.write("extra.txt", "metadata")
        result = self.make_map('<properties><property name="file" type="file" value="extra.txt"/></properties>', '<tileset firstgid="1" source="sets/ground.tsx"/>')
        self.assertTrue(result["pass"], result)
        self.assertEqual(4, result["referencedFileCount"])

    def test_inline_collision_objects_are_not_placed_instances(self):
        inline = self.atlas.replace('<tileset ', '<tileset firstgid="1" ', 1).replace('</tileset>', '<tile id="0"><objectgroup><object id="7"/></objectgroup></tile></tileset>')
        result = self.make_map(tileset=inline)
        self.assertTrue(result["pass"], result)
        self.assertEqual(0, result["objectCount"])

    def test_identical_and_unreferenced_images_are_only_warnings(self):
        (self.root / "copy.png").write_bytes((self.root / "atlas.png").read_bytes())
        (self.root / "old.png").write_bytes(png_bytes(1, 1))
        self.make_map('<imagelayer><image source="copy.png"/></imagelayer>')
        result = audit(self.map, self.root)
        self.assertTrue(result["pass"], result)
        self.assertIn("byte_identical_images", self.codes(result, "warnings"))
        self.assertIn("unreferenced_image_candidate", self.codes(result, "warnings"))
        self.assertTrue((self.root / "old.png").exists())

    def test_png_dimensions_and_atlas_capacity(self):
        self.write("ground.tsx", self.atlas.replace('width="4"', 'width="8"'))
        self.assertIn("image_size_mismatch", self.codes(self.make_map()))
        self.write("ground.tsx", self.atlas.replace('tilecount="2"', 'tilecount="3"'))
        self.assertIn("atlas_capacity", self.codes(self.make_map()))

    def test_template_and_xml_entity_fail_closed(self):
        self.assertIn("unsupported_template", self.codes(self.make_map('<objectgroup><object template="object.tx"/></objectgroup>')))
        self.write("map.tmx", '<!DOCTYPE map [<!ENTITY a "test">]><map/>')
        self.assertFalse(audit(self.map)["pass"])

    def test_firstgid_overlap_fails(self):
        result = self.make_map(tileset='<tileset firstgid="1" source="ground.tsx"/><tileset firstgid="2" source="ground.tsx"/>')
        self.assertIn("overlapping_firstgid", self.codes(result))

    def test_cli_exit_and_json(self):
        self.make_map()
        script = Path(__file__).with_name("audit_tmx.py")
        result = subprocess.run([sys.executable, "-B", str(script), str(self.map)], capture_output=True, encoding="utf-8")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(json.loads(result.stdout)["pass"])
        result = subprocess.run([sys.executable, "-B", str(script), str(self.root / "missing.tmx")], capture_output=True, encoding="utf-8")
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertFalse(json.loads(result.stdout)["pass"])


@unittest.skipIf(Image is None, "Pillow unavailable: alpha behavior was not tested")
class AlphaAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="tiled-alpha-audit-")
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "prop.png"

    def test_detached_low_alpha_candidate_and_read_only(self):
        image = Image.new("RGBA", (24, 24))
        for x in range(4, 20):
            for y in range(4, 20):
                image.putpixel((x, y), (60, 70, 80, 255))
        image.putpixel((0, 0), (30, 30, 30, 1))
        image.save(self.path)
        before = self.path.read_bytes()
        result = inspect_alpha(self.path)
        self.assertEqual(257, result["visiblePixels"])
        self.assertEqual(2, result["componentCount"])
        self.assertEqual(1, result["candidateCount"])
        self.assertTrue(result["pass"])
        self.assertEqual(before, self.path.read_bytes())
        self.assertEqual(hashlib.sha256(before).hexdigest(), result["sha256"])

    def test_diagonal_connectivity(self):
        image = Image.new("RGBA", (3, 3))
        image.putpixel((0, 0), (255, 255, 255, 255))
        image.putpixel((1, 1), (255, 255, 255, 255))
        image.save(self.path)
        self.assertEqual(1, inspect_alpha(self.path)["componentCount"])

    def test_legal_detached_parts_are_not_deleted_or_failed(self):
        image = Image.new("RGBA", (5, 5))
        image.putpixel((0, 0), (255, 255, 255, 255))
        image.putpixel((4, 4), (255, 255, 255, 255))
        image.save(self.path)
        result = inspect_alpha(self.path)
        self.assertTrue(result["pass"])
        self.assertEqual(2, result["componentCount"])
        self.assertEqual(0, result["candidateCount"])

    def test_empty_alpha_and_invalid_threshold(self):
        Image.new("RGBA", (2, 2)).save(self.path)
        self.assertEqual(0, inspect_alpha(self.path)["componentCount"])
        with self.assertRaises(ValueError):
            inspect_alpha(self.path, threshold=255)


if __name__ == "__main__":
    unittest.main(verbosity=2)
