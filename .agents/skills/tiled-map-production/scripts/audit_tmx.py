#!/usr/bin/env python3
"""Read-only TMX resource audit. This does not validate gameplay or rendering."""

import argparse
import base64
from bisect import bisect_right
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import struct
import sys
import xml.etree.ElementTree as ET
import zlib


GID_MASK = 0x0FFFFFFF
IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".svg"}
MAX_CELLS = 16_000_000


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_xml(path, expected):
    payload = path.read_bytes()
    if b"<!DOCTYPE" in payload.upper() or b"<!ENTITY" in payload.upper():
        raise ValueError("DTD/entity declarations are not supported")
    root = ET.fromstring(payload)
    if root.tag != expected:
        raise ValueError(f"Expected <{expected}>, found <{root.tag}>")
    return root


def decode_data(element, encoding, compression, expected):
    if expected < 0 or expected > MAX_CELLS:
        raise ValueError("Layer cell count exceeds the supported bound")
    raw = "".join(element.itertext()).strip()
    if not encoding:
        if compression:
            raise ValueError("Compression requires base64 encoding")
        values = [int(tile.get("gid", "0")) for tile in element.findall("tile")]
    elif encoding == "csv":
        if compression:
            raise ValueError("Compression requires base64 encoding")
        tokens = raw.rstrip(",").split(",") if raw else []
        values = [int(token.strip()) for token in tokens]
    elif encoding == "base64":
        binary = base64.b64decode("".join(raw.split()), validate=True)
        if compression in ("zlib", "gzip"):
            decoder = zlib.decompressobj(15 if compression == "zlib" else 31)
            binary = decoder.decompress(binary, expected * 4 + 1)
            if not decoder.eof or decoder.unconsumed_tail or decoder.unused_data:
                raise ValueError("Compressed payload is truncated, oversized, or has trailing data")
        elif compression:
            raise ValueError(f"Unsupported compression: {compression}")
        if len(binary) != expected * 4:
            raise ValueError(f"Expected {expected * 4} data bytes, found {len(binary)}")
        values = list(struct.unpack(f"<{expected}I", binary))
    else:
        raise ValueError(f"Unsupported encoding: {encoding}")
    if len(values) != expected:
        raise ValueError(f"Expected {expected} cells, found {len(values)}")
    if any(value < 0 or value > 0xFFFFFFFF for value in values):
        raise ValueError("GIDs must be unsigned 32-bit integers")
    return values


class Audit:
    def __init__(self, map_path):
        self.path = Path(map_path).resolve()
        self.errors = []
        self.warnings = []
        self.resources = {self.path} if self.path.is_file() else set()
        self.images = {}
        self.sets = []
        self.uses = Counter()
        self.cells = 0
        self.objects = 0
        self.tile_objects = 0
        self.orientation = None

    def label(self, path):
        try:
            return Path(path).relative_to(self.path.parent).as_posix()
        except ValueError:
            return Path(path).as_posix()

    def issue(self, code, where, detail, warning=False):
        target = self.warnings if warning else self.errors
        target.append({"code": code, "where": str(where), "detail": str(detail)})

    def reference(self, source, base, where):
        if not source or "://" in source:
            self.issue("invalid_reference", where, "Expected a local non-empty file path")
            return None
        relative = Path(source)
        if relative.is_absolute():
            self.issue("absolute_reference", where, source, warning=True)
        target = (base / relative).resolve()
        if not target.is_file():
            self.issue("missing_resource", where, target)
            return None
        self.resources.add(target)
        return target

    def inspect_document(self, root, base, where):
        for prop in root.iter("property"):
            if prop.get("type") == "file":
                source = prop.get("value", prop.text or "")
                if source:
                    self.reference(source, base, f"{where}:property:{prop.get('name')}")
        for image in root.iter("image"):
            target = self.reference(image.get("source"), base, where)
            if target is None:
                continue
            info = self.images.get(target)
            if info is None:
                info = {"path": self.label(target), "bytes": target.stat().st_size,
                        "sha256": sha256(target)}
                if target.suffix.lower() == ".png":
                    with target.open("rb") as stream:
                        header = stream.read(24)
                    if (len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n"
                            or header[12:16] != b"IHDR"):
                        self.issue("invalid_png_header", where, target)
                    else:
                        info["width"], info["height"] = struct.unpack(">II", header[16:24])
                        if not info["width"] or not info["height"]:
                            self.issue("invalid_png_size", where, target)
                self.images[target] = info
            for axis in ("width", "height"):
                if axis in info and image.get(axis) is not None:
                    if int(image.get(axis)) != info[axis]:
                        self.issue("image_size_mismatch", where, f"{target.name}: {axis}")

    def tileset(self, entry):
        first = int(entry.get("firstgid", "0"))
        if first <= 0 or first > GID_MASK:
            raise ValueError("Invalid firstgid")
        base = self.path.parent
        if entry.get("source"):
            target = self.reference(entry.get("source"), base, "tileset")
            if target is None:
                return
            root = load_xml(target, "tileset")
            base = target.parent
            where = self.label(target)
        else:
            root, where = entry, f"inline:{first}"
        self.inspect_document(root, base, where)
        count = int(root.get("tilecount", "-1"))
        if count < 0 or count > MAX_CELLS:
            raise ValueError(f"Invalid or missing tilecount: {where}")
        tiles = root.findall("tile")
        ids = [int(tile.get("id", "-1")) for tile in tiles]
        if any(tile_id < 0 for tile_id in ids) or len(set(ids)) != len(ids):
            raise ValueError(f"Negative or duplicate local tile ID: {where}")
        atlas = root.find("image")
        if atlas is not None:
            valid = set(range(count))
            tw, th = int(root.get("tilewidth", "0")), int(root.get("tileheight", "0"))
            margin, spacing = int(root.get("margin", "0")), int(root.get("spacing", "0"))
            if min(tw, th) <= 0 or min(margin, spacing) < 0:
                raise ValueError(f"Invalid atlas geometry: {where}")
            target = (base / atlas.get("source", "")).resolve()
            info = self.images.get(target, {})
            width = info.get("width", int(atlas.get("width", "0")))
            height = info.get("height", int(atlas.get("height", "0")))
            columns = max(0, (width - 2 * margin + spacing) // (tw + spacing))
            rows = max(0, (height - 2 * margin + spacing) // (th + spacing))
            if columns != int(root.get("columns", "0")) or columns * rows < count:
                self.issue("atlas_capacity", where, "Declared columns/tilecount exceed or disagree with image geometry")
        else:
            valid = set(ids)
            if len(valid) != count:
                self.issue("collection_count", where, f"Declared {count}, found {len(valid)}")
        if not set(ids).issubset(valid):
            self.issue("invalid_tile_metadata_id", where, sorted(set(ids) - valid))
        animations = {}
        for tile in tiles:
            frames = tile.findall("animation/frame")
            if tile.find("animation") is not None and not frames:
                self.issue("empty_animation", where, tile.get("id"))
            if frames:
                targets = [int(frame.get("tileid", "-1")) for frame in frames]
                if any(value not in valid for value in targets):
                    self.issue("invalid_animation_frame", where, targets)
                if any(int(frame.get("duration", "0")) <= 0 for frame in frames):
                    self.issue("invalid_frame_duration", where, tile.get("id"))
                animations[int(tile.get("id"))] = targets
        for obj in root.iter("object"):
            if obj.get("template") or obj.get("gid"):
                self.issue("unsupported_tileset_object", where, "Resolve template/tile objects with native Tiled")
        self.sets.append({"first": first, "name": root.get("name", where), "source": where,
                          "valid": valid, "animations": animations, "counts": Counter()})

    def run(self, asset_root=None):
        root = load_xml(self.path, "map")
        self.orientation = root.get("orientation")
        if self.orientation not in {"orthogonal", "isometric", "staggered", "hexagonal"}:
            self.issue("invalid_orientation", self.path, self.orientation)
        if min(int(root.get("tilewidth", "0")), int(root.get("tileheight", "0"))) <= 0:
            self.issue("invalid_map_tile_size", self.path, "Tile dimensions must be positive")
        # Inline tileset paths are relative to the map; external TSX paths are not.
        for child in root:
            if child.tag != "tileset":
                self.inspect_document(child, self.path.parent, self.path.name)
        for entry in root.findall("tileset"):
            self.tileset(entry)
        self.sets.sort(key=lambda item: item["first"])
        for previous, current in zip(self.sets, self.sets[1:]):
            end = previous["first"] + max(previous["valid"], default=0)
            if current["first"] <= end:
                self.issue("overlapping_firstgid", current["source"], previous["source"])
        for layer in root.iter("layer"):
            data = layer.find("data")
            where = f"layer:{layer.get('id', layer.get('name', '?'))}"
            if data is None:
                self.issue("missing_layer_data", where, "No data element")
                continue
            chunks = data.findall("chunk")
            containers = chunks or [data]
            for container in containers:
                geometry = container if chunks else layer
                width, height = int(geometry.get("width", "0")), int(geometry.get("height", "0"))
                if min(width, height) < 0:
                    raise ValueError(f"Negative layer dimensions: {where}")
                values = decode_data(container, data.get("encoding"), data.get("compression"), width * height)
                self.cells += len(values)
                self.uses.update(value & GID_MASK for value in values if value & GID_MASK)
        map_objects = (obj for child in root if child.tag != "tileset" for obj in child.iter("object"))
        for obj in map_objects:
            self.objects += 1
            if obj.get("template"):
                self.issue("unsupported_template", f"object:{obj.get('id')}", "Resolve object templates with native Tiled first")
            if obj.get("gid") is not None:
                gid = int(obj.get("gid"))
                if gid < 0 or gid > 0xFFFFFFFF:
                    raise ValueError("Object GID must be unsigned 32-bit")
                if gid & GID_MASK:
                    self.tile_objects += 1
                    self.uses[gid & GID_MASK] += 1
        starts = [item["first"] for item in self.sets]
        for gid, uses in self.uses.items():
            index = bisect_right(starts, gid) - 1
            if index < 0 or gid - starts[index] not in self.sets[index]["valid"]:
                self.issue("invalid_gid", self.path.name, f"GID {gid}, occurrences {uses}")
            else:
                self.sets[index]["counts"][gid - starts[index]] += uses
        if asset_root is not None:
            folder = Path(asset_root).resolve()
            if not folder.is_dir():
                self.issue("invalid_asset_root", folder, "Expected an existing directory")
            else:
                for file in sorted(folder.rglob("*")):
                    if file.is_file() and file.suffix.lower() in IMAGE_EXTENSIONS and file.resolve() not in self.resources:
                        self.issue("unreferenced_image_candidate", self.label(file),
                                   "Not referenced by this map; check other maps and sources before removal", warning=True)

    def report(self):
        tilesets = []
        for item in self.sets:
            direct = set(item["counts"])
            closure, pending = set(direct), list(direct)
            while pending:
                for frame in item["animations"].get(pending.pop(), []):
                    if frame in item["valid"] and frame not in closure:
                        closure.add(frame)
                        pending.append(frame)
            unused = sorted(item["valid"] - closure)
            tilesets.append({"name": item["name"], "source": item["source"],
                             "prototypeCount": len(item["valid"]), "directlyUsed": len(direct),
                             "usedIncludingAnimation": len(closure), "unusedLocalIds": unused,
                             "placedInstances": sum(item["counts"].values()),
                             "instancesByLocalId": dict(sorted(item["counts"].items()))})
        warnings = list(self.warnings)
        for item in tilesets:
            if item["unusedLocalIds"]:
                warnings.append({"code": "unused_prototype_candidate", "where": item["source"],
                                 "detail": item["unusedLocalIds"]})
        hashes = defaultdict(list)
        for info in self.images.values():
            hashes[info["sha256"]].append(info["path"])
        duplicates = [sorted(paths) for paths in hashes.values() if len(paths) > 1]
        for paths in duplicates:
            warnings.append({"code": "byte_identical_images", "where": paths,
                             "detail": "Candidate duplicate files; visual near-duplicates are not detected"})
        return {"pass": not self.errors, "scope": "TMX resource structure only; no visual, collision, licensing, or runtime proof",
                "map": str(self.path), "mapSha256": sha256(self.path) if self.path.is_file() else None,
                "orientation": self.orientation, "cellsIncludingEmpty": self.cells,
                "objectCount": self.objects, "tileObjectCount": self.tile_objects,
                "referencedFileCount": len(self.resources), "uniqueImageFiles": len(self.images),
                "imageFileBytes": sum(info["bytes"] for info in self.images.values()),
                "tilesets": tilesets, "images": sorted(self.images.values(), key=lambda info: info["path"]),
                "errors": self.errors, "warnings": warnings}


def audit(map_path, asset_root=None):
    checker = Audit(map_path)
    try:
        checker.run(asset_root)
    except (OSError, ET.ParseError, ValueError, zlib.error, struct.error) as exc:
        checker.issue("unreadable_or_unsupported_input", checker.path, exc)
    return checker.report()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("map", type=Path)
    parser.add_argument("--asset-root", type=Path, help="List unreferenced image candidates; never deletes files")
    args = parser.parse_args()
    result = audit(args.map, args.asset_root)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
