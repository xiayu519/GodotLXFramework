#!/usr/bin/env python3
"""Exercise knowledge-store rejection and read-only behavior in isolated fixtures."""

from datetime import date
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from check_knowledge import CATEGORIES, check


ENTRY = """---
title: A durable decision
kind: decision
status: active
scope: Framework design
verified: 2026-09-09
sources:
  - AGENTS.md
---

Keep the reason for a tradeoff, not generated runtime facts.
"""


class KnowledgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lx-knowledge-test-")
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.memory = self.repo / ".codex" / "memory"
        for category in CATEGORIES:
            (self.memory / category).mkdir(parents=True)
        (self.repo / "AGENTS.md").write_text("Current instructions", encoding="utf-8")
        self.entry = self.memory / "decisions" / "2026-09-09-reason.md"
        self.entry.write_text(ENTRY, encoding="utf-8")
        self.index = self.memory / "INDEX.md"
        self.link = "- [Reason](decisions/2026-09-09-reason.md) — Framework; 2026-09-09.\n"
        self.index.write_text(self.link, encoding="utf-8")

    def result(self):
        return check(self.repo, today=date(2026, 9, 9))

    def codes(self):
        return {item["code"] for item in self.result()["errors"]}

    def test_valid_store_is_read_only(self):
        before = {p.relative_to(self.repo): p.read_bytes() for p in self.repo.rglob("*") if p.is_file()}
        result = self.result()
        self.assertTrue(result["pass"], result)
        self.assertEqual(1, result["entryCount"])
        self.assertEqual(before, {p.relative_to(self.repo): p.read_bytes() for p in self.repo.rglob("*") if p.is_file()})

    def test_old_states_rejected_without_migration_or_deletion(self):
        for status in ("superseded", "resolved", "unknown"):
            with self.subTest(status=status):
                content = ENTRY.replace("status: active", f"status: {status}")
                self.entry.write_text(content, encoding="utf-8")
                self.assertIn("inactive_entry", self.codes())
                self.assertEqual(content, self.entry.read_text(encoding="utf-8"))

    def test_old_missing_scope_rejected_without_adapter(self):
        self.entry.write_text(ENTRY.replace("scope: Framework design\n", ""), encoding="utf-8")
        self.assertIn("invalid_entry", self.codes())

    def test_broken_link_is_rejected(self):
        self.index.write_text(self.link.replace("reason.md", "missing.md"), encoding="utf-8")
        self.assertIn("broken_index_link", self.codes())

    def test_unindexed_entry_is_rejected(self):
        self.index.write_text("No entries", encoding="utf-8")
        self.assertIn("index_coverage", self.codes())

    def test_duplicate_index_link_is_rejected(self):
        self.index.write_text(self.link * 2, encoding="utf-8")
        self.assertIn("index_coverage", self.codes())

    def test_index_verification_date_must_match(self):
        self.index.write_text(self.link.replace("; 2026-09-09", "; 2026-09-08"), encoding="utf-8")
        self.assertIn("index_date_mismatch", self.codes())

    def test_wrong_category_is_rejected(self):
        self.entry.write_text(ENTRY.replace("kind: decision", "kind: feedback"), encoding="utf-8")
        self.assertIn("kind_mismatch", self.codes())

    def test_future_date_is_rejected(self):
        self.entry.write_text(ENTRY.replace("verified: 2026-09-09", "verified: 2026-09-10"), encoding="utf-8")
        self.assertIn("future_verified_date", self.codes())

    def test_missing_or_invalid_source_is_rejected(self):
        for source, code in [("missing.md", "missing_source"), ("../outside.md", "invalid_source"),
                             ("C:/Users/example/.codex/memories/MEMORY.md", "invalid_source")]:
            with self.subTest(source=source):
                self.entry.write_text(ENTRY.replace("  - AGENTS.md", "  - " + source), encoding="utf-8")
                self.assertIn(code, self.codes())

    def test_external_source_is_not_fetched(self):
        self.entry.write_text(ENTRY.replace("  - AGENTS.md", "  - https://example.invalid/reference"), encoding="utf-8")
        result = self.result()
        self.assertTrue(result["pass"], result)
        self.assertEqual(1, result["externalSourceUrlsNotFetched"])

    def test_index_cannot_escape_store(self):
        self.index.write_text("[Outside](../../AGENTS.md)", encoding="utf-8")
        self.assertIn("invalid_index_link", self.codes())

    def test_invalid_utf8_and_empty_body_are_rejected(self):
        self.entry.write_bytes(b"\xff")
        self.assertIn("invalid_entry", self.codes())
        self.entry.write_text(ENTRY.split("\n\n")[0], encoding="utf-8")
        self.assertIn("invalid_entry", self.codes())

    def test_duplicate_metadata_is_rejected(self):
        self.entry.write_text(ENTRY.replace("status: active", "status: active\nstatus: active"), encoding="utf-8")
        self.assertIn("invalid_entry", self.codes())

    def test_duplicate_body_warns_without_auto_removal(self):
        second = self.memory / "decisions" / "2026-09-09-second.md"
        second.write_text(ENTRY.replace("title: A durable decision", "title: Another decision"), encoding="utf-8")
        self.index.write_text(self.link + self.link.replace("reason.md", "second.md"), encoding="utf-8")
        result = self.result()
        self.assertTrue(result["pass"], result)
        self.assertEqual("duplicate_body_candidate", result["warnings"][0]["code"])
        self.assertTrue(second.exists())

    def test_archive_folders_are_not_treated_as_live_entries(self):
        (self.memory / "archive").mkdir()
        self.assertIn("unexpected_store_item", self.codes())

    def test_empty_live_store_is_valid(self):
        # This file was created by this fixture and has no external owner.
        self.entry.unlink()
        self.index.write_text("No relevant knowledge yet.", encoding="utf-8")
        self.assertTrue(self.result()["pass"])

    def test_cli_reports_failures_with_nonzero_exit(self):
        self.entry.write_text(ENTRY.replace("status: active", "status: superseded"), encoding="utf-8")
        script = Path(__file__).with_name("check_knowledge.py")
        result = subprocess.run([sys.executable, "-B", str(script), "--repo-root", str(self.repo)], capture_output=True, encoding="utf-8")
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertFalse(json.loads(result.stdout)["pass"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
