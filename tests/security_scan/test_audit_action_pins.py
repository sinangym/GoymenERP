"""Tests for the GitHub Action pinning audit (DALGA_PLANI_v2 1.18, WP16)."""
from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
AUDIT_PATH = REPOSITORY_ROOT / "tools/security/audit_action_pins.py"
SPEC = importlib.util.spec_from_file_location("goymen_audit_action_pins", AUDIT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {AUDIT_PATH}")
AUDIT = importlib.util.module_from_spec(SPEC)
sys.modules["goymen_audit_action_pins"] = AUDIT
SPEC.loader.exec_module(AUDIT)

SHA = "0123456789abcdef0123456789abcdef01234567"


class Workspace:
    def __init__(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        (self.root / AUDIT.WORKFLOW_DIRECTORY).mkdir(parents=True)
        self.baseline(["actions/checkout@v7"])

    def baseline(self, refs: list[str], **extra: object) -> None:
        document = {"format": AUDIT.BASELINE_FORMAT, "note": "test", "mutable_refs": refs, **extra}
        target = self.root / AUDIT.BASELINE_PATH
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(document), encoding="utf-8")

    def workflow(self, name: str, *uses: str) -> None:
        steps = "".join(f"      - uses: {value}\n" for value in uses)
        text = f"name: t\non: push\njobs:\n  a:\n    runs-on: ubuntu-latest\n    steps:\n{steps}"
        (self.root / AUDIT.WORKFLOW_DIRECTORY / name).write_text(text, encoding="utf-8")

    def close(self) -> None:
        self.directory.cleanup()


class ActionPinAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self.workspace = Workspace()
        self.addCleanup(self.workspace.close)

    def test_sha_pins_local_actions_and_listed_tags_pass(self) -> None:
        self.workspace.workflow(
            "ci.yml", "actions/checkout@v7", f"actions/setup-node@{SHA} # v7.1.0", "./.github/actions/x"
        )
        report = AUDIT.audit(self.workspace.root)
        self.assertTrue(report["ok"], report)
        self.assertEqual(report["counts"], {"local": 1, "sha": 1, "tag": 1})

    def test_new_mutable_refs_fail(self) -> None:
        for value in ("actions/setup-node@v7", "actions/checkout@v6", "actions/checkout@main",
                      "someone/action@v1", f"actions/checkout@{SHA[:12]}"):
            with self.subTest(uses=value):
                self.workspace.workflow("ci.yml", "actions/checkout@v7", value)
                report = AUDIT.audit(self.workspace.root)
                self.assertFalse(report["ok"])
                self.assertEqual([item["uses"] for item in report["violations"]], [value])

    def test_docker_and_malformed_references_fail(self) -> None:
        for value in ("docker://alpine:3", "not-an-action"):
            with self.subTest(uses=value):
                self.workspace.workflow("ci.yml", "actions/checkout@v7", value)
                self.assertFalse(AUDIT.audit(self.workspace.root)["ok"])

    def test_quoted_values_and_trailing_comments_are_read(self) -> None:
        self.workspace.workflow("ci.yml", "'actions/checkout@v7'", "\"actions/checkout@v7\"  # comment")
        report = AUDIT.audit(self.workspace.root)
        self.assertTrue(report["ok"], report)
        self.assertEqual(report["references"], 2)

    def test_disabled_workflows_are_not_active_and_are_skipped(self) -> None:
        self.workspace.workflow("ci.yml", "actions/checkout@v7")
        self.workspace.workflow("old.yml.disabled", "someone/action@main")
        self.assertTrue(AUDIT.audit(self.workspace.root)["ok"])

    def test_unused_baseline_entry_fails_so_the_baseline_only_shrinks(self) -> None:
        self.workspace.workflow("ci.yml", f"actions/checkout@{SHA}")
        report = AUDIT.audit(self.workspace.root)
        self.assertFalse(report["ok"])
        self.assertEqual(report["stale_baseline_entries"], ["actions/checkout@v7"])

    def test_malformed_baseline_fails_closed(self) -> None:
        for refs, extra in (
            (["actions/checkout@main"], {}),
            (["b/b@v1", "a/a@v1"], {}),
            (["a/a@v1", "a/a@v1"], {}),
            ([f"a/a@{SHA}"], {}),
            (["a/a@v1"], {"extra": True}),
        ):
            with self.subTest(refs=refs, extra=extra):
                self.workspace.baseline(refs, **extra)
                self.workspace.workflow("ci.yml", "actions/checkout@v7")
                with self.assertRaises(AUDIT.AuditError):
                    AUDIT.audit(self.workspace.root)


class RepositoryActionPinTests(unittest.TestCase):
    def test_active_workflows_of_this_repository_pass_the_audit(self) -> None:
        report = AUDIT.audit(REPOSITORY_ROOT)
        self.assertEqual(report["violations"], [], "pin new actions to a full commit SHA")
        self.assertEqual(report["stale_baseline_entries"], [], "delete baseline entries that moved to a SHA")
        self.assertGreater(report["references"], 0)

    def test_baseline_holds_only_official_actions(self) -> None:
        document = json.loads((REPOSITORY_ROOT / AUDIT.BASELINE_PATH).read_text(encoding="utf-8"))
        self.assertTrue(all(entry.startswith("actions/") for entry in document["mutable_refs"]))


if __name__ == "__main__":
    unittest.main()
