#!/usr/bin/env python3
"""Inspect alpha islands without modifying or automatically classifying artwork."""

import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import sys


def inspect_alpha(path, threshold=0, max_candidate_pixels=512, max_candidate_ratio=0.02):
    from PIL import Image

    if not 0 <= threshold <= 254 or max_candidate_pixels < 1 or not 0 <= max_candidate_ratio <= 1:
        raise ValueError("Invalid alpha threshold or candidate bounds")
    path = Path(path).resolve()
    with Image.open(path) as source:
        if getattr(source, "n_frames", 1) != 1:
            raise ValueError("Animated images require an explicit frame-by-frame audit")
        image = source.convert("RGBA")
    width, height = image.size
    alpha = image.getchannel("A").tobytes()
    visited = bytearray(width * height)
    components = []
    for start, value in enumerate(alpha):
        if value <= threshold or visited[start]:
            continue
        visited[start] = 1
        pending = deque([start])
        count, low, high = 0, 255, 0
        left = right = start % width
        top = bottom = start // width
        while pending:
            index = pending.popleft()
            x, y = index % width, index // width
            count += 1
            left, right = min(left, x), max(right, x)
            top, bottom = min(top, y), max(bottom, y)
            low, high = min(low, alpha[index]), max(high, alpha[index])
            for ny in range(max(0, y - 1), min(height, y + 2)):
                for nx in range(max(0, x - 1), min(width, x + 2)):
                    neighbor = ny * width + nx
                    if not visited[neighbor] and alpha[neighbor] > threshold:
                        visited[neighbor] = 1
                        pending.append(neighbor)
        components.append({"pixels": count, "bbox": [left, top, right + 1, bottom + 1],
                           "minAlpha": low, "maxAlpha": high})
    components.sort(key=lambda item: (-item["pixels"], item["bbox"]))
    largest = components[0]["pixels"] if components else 0
    for index, component in enumerate(components):
        component["reviewCandidate"] = (index > 0 and component["pixels"] <= max_candidate_pixels
                                        and component["pixels"] <= largest * max_candidate_ratio)
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return {"pass": True, "scope": "Alpha component statistics only; candidates are not confirmed defects",
            "path": str(path), "sha256": digest.hexdigest(), "width": width, "height": height,
            "threshold": threshold, "visiblePixels": sum(value > threshold for value in alpha),
            "fullyTransparentPixels": alpha.count(0), "fullyOpaquePixels": alpha.count(255),
            "componentCount": len(components), "candidateCount": sum(item["reviewCandidate"] for item in components),
            "components": components,
            "note": "Detached foliage, shadows, and parts may be intentional. No pixels were changed."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--threshold", type=int, default=0)
    parser.add_argument("--max-candidate-pixels", type=int, default=512)
    parser.add_argument("--max-candidate-ratio", type=float, default=0.02)
    args = parser.parse_args()
    code = 0
    try:
        result = inspect_alpha(args.image, args.threshold, args.max_candidate_pixels, args.max_candidate_ratio)
    except ImportError:
        result = {"pass": False, "error": "Pillow is required; no dependency was installed automatically"}
        code = 2
    except (OSError, ValueError) as exc:
        result = {"pass": False, "error": str(exc)}
        code = 1
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return code


if __name__ == "__main__":
    sys.exit(main())
