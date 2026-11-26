"""Negative checks for evidence drift and unsafe plan admission."""
import importlib.util
import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("reference_verifier", ROOT / "tools/verify_delivery_reference_catalog.py")
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class ReferenceSafetyTests(unittest.TestCase):
    def test_reference_cannot_escape_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            for path in ("../outside.cs", "C:/outside.cs", "folder\\file.cs", ""):
                with self.subTest(path=path), self.assertRaises(ValueError):
                    VERIFIER.safe_path(Path(directory), path)

    def test_catalog_cannot_use_branch_as_exact_source(self):
        self.assertTrue(VERIFIER.verify_catalog(ROOT, {"source_commit": "main", "references": []}))

    def test_catalog_cannot_be_empty(self):
        self.assertIn("catalogue is empty", VERIFIER.verify_catalog(ROOT, {"source_commit": "a" * 40, "references": []}))

    def test_source_catalog_cannot_claim_runtime_acceptance(self):
        result = VERIFIER.verify_catalog(ROOT, {"source_commit": "a" * 40, "references": [
            {"id": "R", "runtime_acceptance": "PASS", "sources": [], "tests": [], "limitations": ["none"]}]})
        self.assertTrue(any("runtime acceptance" in error for error in result))

    def test_duplicate_reference_ids_are_rejected(self):
        item = {"id": "R", "runtime_acceptance": "NOT_RUN", "sources": [], "tests": [], "limitations": ["none"]}
        result = VERIFIER.verify_catalog(ROOT, {"source_commit": "a" * 40, "references": [item, item]})
        self.assertTrue(any("duplicate" in error for error in result))

    def test_modified_source_cannot_keep_historical_ci_label(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "example.cs").write_text("class Changed {}", encoding="utf-8")
            item = {"path": "example.cs", "line": 1, "anchor": "class Original",
                    "sha256": hashlib.sha256(b"class Original {}").hexdigest(), "git_blob": "old-blob"}
            catalog = {"source_commit": "a" * 40, "historical_ci": {"source_commit": "b" * 40},
                       "references": [{"id": "R", "runtime_acceptance": "NOT_RUN", "sources": [item],
                                       "tests": [item], "limitations": ["source only"],
                                       "historical_evidence": "HISTORICAL_CI_TREE_MATCH"}]}
            with patch.object(VERIFIER, "git", side_effect=lambda _, *args: "new-blob" if args[-1].startswith("b") else "old-blob"):
                errors = VERIFIER.verify_catalog(root, catalog)
            for finding in ("working source drift", "missing symbol/test anchor", "historical CI source differs"):
                with self.subTest(finding=finding):
                    self.assertTrue(any(finding in error for error in errors))


if __name__ == "__main__":
    unittest.main()
