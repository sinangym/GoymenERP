from __future__ import annotations

import copy
import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
EVALUATOR_PATH = REPOSITORY_ROOT / "tools/evaluate_ai_eval_set.py"
MODULE_NAME = "goymen_ai_eval_set_evaluator"
SPEC = importlib.util.spec_from_file_location(MODULE_NAME, EVALUATOR_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {EVALUATOR_PATH}")
EVALUATOR = importlib.util.module_from_spec(SPEC)
sys.modules[MODULE_NAME] = EVALUATOR
SPEC.loader.exec_module(EVALUATOR)

COPIED = (
    EVALUATOR.EVAL_SET_PATH,
    EVALUATOR.EVAL_SCHEMA_PATH,
    EVALUATOR.TOOL_CATALOG_PATH,
    "contracts/ai/decision-index-v1.json",
)


class CommittedSetTests(unittest.TestCase):
    def test_committed_set_passes_without_a_model(self) -> None:
        report = EVALUATOR.evaluate()
        self.assertEqual([], report["errors"])
        self.assertTrue(report["ok"])
        self.assertEqual("modelsiz", report["mode"])
        self.assertEqual(0, report["model_runs"])

    def test_committed_set_has_the_planned_size(self) -> None:
        counts = EVALUATOR.evaluate()["category_counts"]
        self.assertGreaterEqual(sum(counts[c] for c in EVALUATOR.DECISION_CATEGORIES), 150)
        self.assertEqual(20, counts["islem_sorgusu"])
        for category in EVALUATOR.DECISION_CATEGORIES:
            self.assertGreater(counts[category], 0)

    def test_model_mode_is_refused(self) -> None:
        self.assertEqual(2, EVALUATOR.main(["--model", "any"]))


class MutationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        for relative in COPIED:
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPOSITORY_ROOT / relative, target)
        self.path = self.root / EVALUATOR.EVAL_SET_PATH
        self.document = json.loads(self.path.read_text(encoding="utf-8"))

    def tearDown(self) -> None:
        self.directory.cleanup()

    def evaluate_with(self, document: dict) -> dict:
        self.path.write_text(json.dumps(document, ensure_ascii=False), encoding="utf-8")
        return EVALUATOR.evaluate(self.root)

    def first(self, document: dict, category: str) -> dict:
        return next(item for item in document["items"] if item["category"] == category)

    def test_unknown_source_is_an_error(self) -> None:
        document = copy.deepcopy(self.document)
        self.first(document, "bilgi_cikarma")["expected_source_ids"] = ["ZZZ-D999999"]
        self.assertTrue(any("ZZZ-D999999" in error for error in self.evaluate_with(document)["errors"]))

    def test_personal_data_is_an_error(self) -> None:
        document = copy.deepcopy(self.document)
        self.first(document, "yetki_disi")["question"] = "Bilgiyi ornek@example.com adresine gönder."
        self.assertTrue(any("kişisel veri" in error for error in self.evaluate_with(document)["errors"]))

    def test_duplicate_identity_is_an_error(self) -> None:
        document = copy.deepcopy(self.document)
        document["items"][1]["id"] = document["items"][0]["id"]
        self.assertTrue(any("tekrar" in error for error in self.evaluate_with(document)["errors"]))

    def test_shipment_count_must_follow_the_fixture(self) -> None:
        document = copy.deepcopy(self.document)
        scenario = next(item for item in document["items"] if item["id"] == "EV-S-002")
        scenario["expected_tool_result"]["pendingCount"] = 6
        self.assertTrue(any("pendingCount" in error for error in self.evaluate_with(document)["errors"]))

    def test_unknown_tool_is_an_error(self) -> None:
        document = copy.deepcopy(self.document)
        self.first(document, "bilgi_cikarma")["expected_tool_call"]["toolId"] = "unknown.tool.v1"
        self.assertTrue(any("katalogda yok" in error for error in self.evaluate_with(document)["errors"]))

    def test_unmarked_item_fails_schema(self) -> None:
        document = copy.deepcopy(self.document)
        document["items"][0]["data_marker"] = "GERCEK"
        self.assertFalse(self.evaluate_with(document)["ok"])

    def test_missing_shipment_scenarios_are_an_error(self) -> None:
        document = copy.deepcopy(self.document)
        document["items"] = [item for item in document["items"] if item["id"] != "EV-S-020"]
        self.assertTrue(any("senaryosu sayısı" in error for error in self.evaluate_with(document)["errors"]))


if __name__ == "__main__":
    unittest.main()
