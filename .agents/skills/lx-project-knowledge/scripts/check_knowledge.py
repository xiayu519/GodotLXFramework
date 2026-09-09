#!/usr/bin/env python3
"""Read-only structural audit of the live repository knowledge store."""

import argparse
from collections import Counter, defaultdict
from datetime import date
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit


CATEGORIES = {"problems": "problem", "decisions": "decision", "feedback": "feedback", "references": "reference"}
FIELDS = {"title", "kind", "status", "scope", "verified", "sources"}
LINK = re.compile(r"\[[^\]\n]+\]\(([^)\n]+)\)")


def scalar(text):
    text = text.strip()
    if text.startswith('"'):
        result = json.loads(text)
        if not isinstance(result, str):
            raise ValueError("Expected a scalar string")
        return result
    if text.startswith("'"):
        if not text.endswith("'") or len(text) < 2:
            raise ValueError("Unterminated scalar string")
        return text[1:-1].replace("''", "'")
    if not text or text[0] in "[{|>&*!":
        raise ValueError("Use a non-empty plain or quoted scalar")
    return text


def parse_entry(text):
    lines = text.splitlines()
    if not lines or lines[0] != "---":
        raise ValueError("Missing frontmatter")
    try:
        end = lines.index("---", 1)
    except ValueError as exc:
        raise ValueError("Unclosed frontmatter") from exc
    metadata = {}
    reading_sources = False
    for line in lines[1:end]:
        if not line.strip():
            continue
        item = re.fullmatch(r"  - (.+)", line)
        if item and reading_sources:
            metadata["sources"].append(scalar(item.group(1)))
            continue
        match = re.fullmatch(r"([a-z_]+):(?: (.*))?", line)
        if not match or match.group(1) not in FIELDS:
            raise ValueError("Unsupported metadata field or syntax")
        key, value = match.group(1), match.group(2) or ""
        if key in metadata:
            raise ValueError(f"Duplicate metadata field: {key}")
        reading_sources = key == "sources"
        if reading_sources:
            if value:
                raise ValueError("sources must be an indented list")
            metadata[key] = []
        else:
            metadata[key] = scalar(value)
    missing = FIELDS - metadata.keys()
    if missing:
        raise ValueError("Missing metadata: " + ", ".join(sorted(missing)))
    if not metadata["sources"]:
        raise ValueError("At least one source is required")
    body = "\n".join(lines[end + 1:]).strip()
    if not body:
        raise ValueError("Entry body is empty")
    return metadata, body


def local_target(base, source, boundary):
    # Source and index paths are intentionally repository-portable.
    path = Path(unquote(source))
    if not source or path.is_absolute() or "\\" in source or ":" in source:
        raise ValueError("Expected a relative forward-slash repository path")
    if ".." in path.parts:
        raise ValueError("Parent traversal is not allowed")
    resolved = (base / path).resolve()
    if not resolved.is_relative_to(boundary.resolve()):
        raise ValueError("Resolved path escapes its allowed root")
    return resolved


def check(repo_root, today=None):
    repo = Path(repo_root).resolve()
    memory = repo / ".codex" / "memory"
    today = today or date.today()
    errors, warnings, entries = [], [], {}
    total_bytes, external_sources = 0, 0

    def issue(code, path, detail):
        errors.append({"code": code, "path": str(path), "detail": str(detail)})

    def read(path):
        nonlocal total_bytes
        if not path.resolve().is_relative_to(memory.resolve()) or not path.resolve().is_relative_to(repo):
            raise ValueError("Memory file resolves outside the project store")
        payload = path.read_bytes()
        total_bytes += len(payload)
        return payload.decode("utf-8-sig")

    if not memory.is_dir() or not memory.resolve().is_relative_to(repo):
        issue("invalid_store", memory, "Expected the repository .codex/memory directory")
    else:
        # Deliberately avoid recursively traversing arbitrary folders or native memory stores.
        for item in memory.iterdir():
            if item.name not in {*CATEGORIES, "INDEX.md", ".gitkeep"}:
                issue("unexpected_store_item", item, "Only the index and live categories belong here")
        for category, kind in CATEGORIES.items():
            folder = memory / category
            if not folder.is_dir() or not folder.resolve().is_relative_to(memory.resolve()):
                issue("invalid_category", folder, "Missing or externally redirected category")
                continue
            for path in sorted(folder.iterdir()):
                if path.name == ".gitkeep" and path.is_file():
                    continue
                if not path.is_file() or not re.fullmatch(r"\d{4}-\d{2}-\d{2}-.+\.md", path.name):
                    issue("unexpected_entry", path, "Expected a dated Markdown entry, not an archive or report")
                    continue
                relative = path.relative_to(memory).as_posix()
                entries[relative] = None
                try:
                    metadata, body = parse_entry(read(path))
                    if metadata["kind"] != kind:
                        issue("kind_mismatch", path, f"Expected kind {kind}")
                    if metadata["status"] != "active":
                        issue("inactive_entry", path, "Remove obsolete entries; no legacy status compatibility")
                    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", metadata["verified"]):
                        raise ValueError("verified must use YYYY-MM-DD")
                    verified = date.fromisoformat(metadata["verified"])
                    if verified > today:
                        issue("future_verified_date", path, metadata["verified"])
                    for source in metadata["sources"]:
                        url = urlsplit(source)
                        if url.scheme in {"http", "https"} and url.netloc:
                            external_sources += 1
                            continue
                        try:
                            target = local_target(repo, source, repo)
                            if not target.is_file():
                                issue("missing_source", path, source)
                        except ValueError as exc:
                            issue("invalid_source", path, f"{source}: {exc}")
                    entries[relative] = {"path": relative, "title": metadata["title"], "scope": metadata["scope"],
                                         "verified": metadata["verified"],
                                         "body_hash": hashlib.sha256(re.sub(r"\s+", " ", body).encode("utf-8")).hexdigest()}
                except (OSError, ValueError) as exc:
                    issue("invalid_entry", path, exc)

        index_path = memory / "INDEX.md"
        indexed = Counter()
        try:
            index = read(index_path)
            for line in index.splitlines():
                for match in LINK.finditer(line):
                    source = match.group(1)
                    try:
                        target = local_target(memory, source, memory)
                        relative = target.relative_to(memory.resolve()).as_posix()
                        if relative not in entries:
                            issue("broken_index_link", index_path, source)
                            continue
                        indexed[relative] += 1
                        entry = entries[relative]
                        # A creation date inside the linked filename is not the review date.
                        if entry and entry["verified"] not in line[match.end():]:
                            issue("index_date_mismatch", index_path, relative)
                    except ValueError as exc:
                        issue("invalid_index_link", index_path, f"{source}: {exc}")
        except (OSError, ValueError) as exc:
            issue("invalid_index", index_path, exc)
        for relative in entries:
            if indexed[relative] != 1:
                issue("index_coverage", memory / relative, f"Expected one index link, found {indexed[relative]}")

    body_groups = defaultdict(list)
    for entry in entries.values():
        if entry:
            body_groups[entry["body_hash"]].append(entry["path"])
    for paths in body_groups.values():
        if len(paths) > 1:
            warnings.append({"code": "duplicate_body_candidate", "paths": paths})
    return {"pass": not errors, "scope": "Live project knowledge structure only; no semantic truth or native memory audit",
            "entryCount": len(entries), "markdownBytes": total_bytes, "externalSourceUrlsNotFetched": external_sources,
            "errors": errors, "warnings": warnings}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[4])
    args = parser.parse_args()
    try:
        result = check(args.repo_root)
    except (OSError, ValueError) as exc:
        result = {"pass": False, "errors": [{"code": "unreadable_store", "detail": str(exc)}]}
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
