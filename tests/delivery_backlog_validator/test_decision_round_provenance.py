from __future__ import annotations

import ast
import contextlib
import csv
import importlib.util
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any, Callable


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_delivery_backlog.py"
RECOMPUTE_PATH = Path(__file__).with_name("recompute_delivery_backlog_provenance.py")


def _load_module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load module from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


VALIDATOR = _load_module("goymen_delivery_backlog_decision_round", VALIDATOR_PATH)
RECOMPUTE = _load_module("goymen_delivery_backlog_independent_recompute", RECOMPUTE_PATH)

REQUIRED_FILES = (
    "docs/backlog/DELIVERY_BACKLOG_v1.json",
    "docs/backlog/task_manifest.json",
    "docs/decisions/KARAR_SICILI.md",
    "docs/decisions/KARAR_SICILI.csv",
    "docs/decisions/ACIK_KARARLAR.md",
    "docs/decisions/SINAN_KARARLARI_2026-10-21.md",
    "docs/decisions/SINAN_KARARLARI_2026-10-22.md",
    "docs/acceptance/ACCEPTANCE_70.md",
    "docs/ui/CANONICAL_SCREEN_STATUS.csv",
)


class IndependentProvenanceRecomputeTests(unittest.TestCase):
    def test_live_and_reserved_open_id_sets_are_explicit_and_disjoint(self) -> None:
        required_open_ids = frozenset(
            [
                *[f"OPEN-{number:03d}" for number in range(1, 22)],
                "OPEN-030",
            ]
        )
        reserved_open_ids = frozenset(
            f"OPEN-{number:03d}" for number in range(22, 30)
        )

        self.assertEqual(required_open_ids, RECOMPUTE.REQUIRED_OPEN_IDS)
        self.assertEqual(reserved_open_ids, RECOMPUTE.RESERVED_OPEN_IDS)
        self.assertTrue(
            RECOMPUTE.REQUIRED_OPEN_IDS.isdisjoint(RECOMPUTE.RESERVED_OPEN_IDS)
        )

    def test_parent_provenance_is_recomputed_without_validator_constants(self) -> None:
        source = RECOMPUTE_PATH.read_text(encoding="utf-8")
        self.assertNotIn("validate_delivery_backlog", source)
        self.assertNotIn("EXPECTED_", source)
        imports = []
        for node in ast.walk(ast.parse(source)):
            if isinstance(node, ast.Import):
                imports.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom):
                imports.append(node.module or "")
        self.assertFalse(
            any("validate_delivery_backlog" in imported for imported in imports), imports
        )

        result = RECOMPUTE.recompute(REPOSITORY_ROOT)

        self.assertEqual(213, result["decisionRegistry"]["count"])
        self.assertEqual(117, result["legacyDecisionRegistry"]["count"])
        self.assertTrue(result["legacyDecisionRegistry"]["isReviewed"])
        self.assertEqual(96, result["delegatedSourceAdmission"]["count"])
        self.assertTrue(result["delegatedSourceAdmission"]["isExact"])
        self.assertEqual(
            {"GECICI_VARSAYIM": 99, "KILITLI": 114},
            result["decisionRegistry"]["statusHistogram"],
        )
        self.assertEqual(
            "90742638160e681618ed4008d93f5ebbf4495e761078d54f66fe16818da2259c",
            result["decisionRegistry"]["canonicalRowsSha256"],
        )
        self.assertEqual(
            "6681913f63c339f913a6882a23d4d91440f67e3284ead64123f988ea6473e3cd",
            result["decisionRegistry"]["orderedIdsSha256"],
        )
        self.assertTrue(result["decisionRegistryParity"]["isExact"])
        self.assertEqual([], result["decisionRoundParity"]["missingInMarkdown"])
        self.assertEqual([], result["decisionRoundParity"]["missingInCsv"])
        self.assertEqual([], result["decisionRoundParity"]["semanticMismatches"])
        self.assertEqual(
            "2dba55e20f351209fe65b1b3eab8f16cc72004ce4ba094d6a19c2c16a24ac3d8",
            result["openDecisionCatalog"]["canonicalRowsSha256"],
        )
        active_open_ids = [
            "OPEN-001",
            "OPEN-004",
            "OPEN-005",
            *[f"OPEN-{number:03d}" for number in range(7, 22)],
            "OPEN-030",
        ]
        self.assertEqual(22, result["openDecisionCatalog"]["count"])
        self.assertEqual(
            {"KAPANDI": 3, "KISMEN": 19},
            result["openDecisionCatalog"]["statusHistogram"],
        )
        self.assertEqual(active_open_ids, result["openDecisionCatalog"]["activeIds"])
        self.assertEqual(
            ["OPEN-002", "OPEN-003", "OPEN-006"],
            result["openDecisionCatalog"]["resolvedIds"],
        )
        self.assertTrue(result["openDecisionCatalog"]["isExpectedExact"])
        self.assertEqual([], result["openDecisionCatalog"]["missingExpectedIds"])
        self.assertEqual([], result["openDecisionCatalog"]["extraExpectedIds"])
        self.assertEqual(
            "4454ee3f8131cda338722d06a9c5170598e09b8608a764ac598051a8aacd6cd0",
            result["deliveryBacklog"]["canonicalMappingSha256"],
        )
        self.assertEqual([], result["deliveryBacklog"]["missingDecisionIds"])
        self.assertEqual([], result["deliveryBacklog"]["extraDecisionIds"])
        self.assertEqual(
            active_open_ids,
            result["deliveryBacklog"]["usedActiveOpenIds"],
        )
        self.assertEqual(
            ["OPEN-002", "OPEN-003", "OPEN-006"],
            result["deliveryBacklog"]["usedResolvedOpenHistoryIds"],
        )
        self.assertEqual([], result["deliveryBacklog"]["missingActiveOpenIds"])
        self.assertEqual([], result["deliveryBacklog"]["extraActiveOpenIds"])
        self.assertEqual(
            [], result["deliveryBacklog"]["missingResolvedOpenHistoryIds"]
        )
        self.assertEqual(
            [], result["deliveryBacklog"]["extraResolvedOpenHistoryIds"]
        )


class DecisionRoundSemanticInvariantTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        with (
            REPOSITORY_ROOT / "docs/decisions/KARAR_SICILI.csv"
        ).open(encoding="utf-8-sig", newline="") as stream:
            cls.decisions = {
                row["DecisionId"]: dict(row) for row in csv.DictReader(stream)
            }
        open_rows, _ = RECOMPUTE._read_active_markdown_table(
            REPOSITORY_ROOT / "docs/decisions/ACIK_KARARLAR.md",
            header=RECOMPUTE.OPEN_MARKDOWN_HEADER,
            separator=RECOMPUTE.OPEN_MARKDOWN_SEPARATOR,
            pattern=RECOMPUTE.OPEN_PATTERN,
            expected_columns=6,
        )
        cls.open_decisions = {
            cells[0]: {
                "status": cells[1].strip("*"),
                "accepted": cells[2],
                "remaining": cells[3],
                "evidence": cells[4],
                "delivery": cells[5],
            }
            for cells in open_rows
        }
        cls.backlog = json.loads(
            (
                REPOSITORY_ROOT / "docs/backlog/DELIVERY_BACKLOG_v1.json"
            ).read_text(encoding="utf-8")
        )

    def test_q1_dual_source_and_vkn_mutability_remain_explicit(self) -> None:
        decision = self.decisions["DEC-ORG-005"]
        source = decision["Kaynak"]
        self.assertIn("5496849206", source)
        self.assertIn("5506986884", source)

        locked_text = f"{decision['Karar']} {decision['KabulKaniti']}".casefold()
        self.assertIn("grup çapında benzersiz", locked_text)
        self.assertIn("aktif ve pasif", locked_text)
        self.assertIn("where vkn is not null", locked_text)
        self.assertIn("1111111111", locked_text)
        self.assertIn("kaldır", locked_text)
        self.assertTrue(
            "fatura/e-belgeye kapalı" in locked_text
            or "resmî belge üretemez" in locked_text,
            locked_text,
        )
        self.assertIn("open-030", locked_text)

        open_030 = self.open_decisions["OPEN-030"]
        self.assertEqual("KISMEN", open_030["status"])
        self.assertIn(
            "vkn ilk kayıttan sonra değiştirilebilir",
            open_030["accepted"].casefold(),
        )
        remaining = open_030["remaining"].casefold()
        self.assertIn("null", remaining)
        self.assertIn("geçici", remaining)
        self.assertTrue(
            "yetki" in remaining or "yetkili" in remaining,
            open_030["remaining"],
        )
        self.assertTrue(
            "audit" in remaining or "denetim" in remaining,
            open_030["remaining"],
        )
        self.assertIn("çakış", remaining)
        self.assertIn("5506986884", open_030["evidence"])

        active_locations: list[str] = []
        history_locations: list[str] = []
        for wave in self.backlog["waves"]:
            for item in wave["slices"]:
                if "OPEN-030" in item.get("open", []):
                    active_locations.append(item["id"])
                if "OPEN-030" in item.get("resolved_open_history", []):
                    history_locations.append(item["id"])
        self.assertEqual(["W1-ORG"], active_locations)
        self.assertEqual([], history_locations)

        historical_record = (
            REPOSITORY_ROOT / "docs/decisions/SINAN_KARARLARI_2026-08-31.md"
        ).read_text(encoding="utf-8")
        historical_open_snapshot = self._markdown_section(
            historical_record,
            "## 7. TARİHSEL / SUPERSEDED",
            "## 8. 2026-09-01",
        )
        self.assertNotIn("OPEN-030", historical_open_snapshot)

    def test_q3_is_narrow_and_document_decisions_remain_in_force(self) -> None:
        integration = self.decisions["DEC-INT-001"]
        text = f"{integration['Karar']} {integration['KabulKaniti']}".casefold()
        for required in ("e-fatura/e-arşiv", "mikro", "banka"):
            self.assertIn(required, text)
        self.assertIn("dec-doc-006/007", text)
        self.assertTrue("kapsamaz" in text or "kapsam dışı" in text, text)
        for overbroad in ("tüm dış entegrasyon", "bütün dış entegrasyon"):
            self.assertNotIn(overbroad, text)

        for relative_path in (
            "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
            "docs/delivery/DECISION_PACK.md",
        ):
            content = (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8")
            section = self._normalized(
                self._markdown_section(content, "### Q3", "### Q4")
            )
            for required in ("e-fatura/e-arşiv", "mikro", "banka"):
                self.assertIn(required, section, relative_path)
            self.assertIn("dec-doc-006/007", section, relative_path)
            for overbroad in ("tüm dış entegrasyon", "bütün dış entegrasyon"):
                self.assertNotIn(overbroad, section, relative_path)

        self.assertEqual("KILITLI", self.decisions["DEC-DOC-006"]["Durum"])
        self.assertIn("e-posta", self.decisions["DEC-DOC-006"]["Karar"].casefold())
        self.assertEqual("KILITLI", self.decisions["DEC-DOC-007"]["Durum"])
        self.assertIn("paylaş", self.decisions["DEC-DOC-007"]["Karar"].casefold())

    def test_q4_is_only_a_temporary_assumption_on_every_decision_surface(self) -> None:
        recovery = self.decisions["DEC-TEC-007"]
        self.assertEqual("GECICI_VARSAYIM", recovery["Durum"])
        recovery_text = f"{recovery['Karar']} {recovery['KabulKaniti']}".casefold()
        self.assertIn("kesin değeri bilmediğini", recovery_text)
        self.assertIn("şirket", recovery_text)
        self.assertIn("veri yoğunlu", recovery_text)
        self.assertIn("kabul edilmiş production hedefi değildir", recovery_text)

        open_017 = self.open_decisions["OPEN-017"]
        self.assertEqual("KISMEN", open_017["status"])
        self.assertIn("gecici_varsayim", open_017["accepted"].casefold())

        for relative_path in (
            "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
            "docs/delivery/DECISION_PACK.md",
        ):
            content = (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8")
            section = self._normalized(
                self._markdown_section(content, "### Q4", "### Q5")
            )
            self.assertIn("gecici_varsayim", section, relative_path)
            self.assertIn("kabul edilmiş production hedefi değildir", section, relative_path)
            self.assertNotIn("q4 — rpo/rto: `a`", section, relative_path)

    def test_q5_locks_outcomes_but_leaves_technical_method_open(self) -> None:
        period = self.decisions["DEC-DATA-001"]
        self.assertEqual("KILITLI", period["Durum"])
        locked_text = period["Karar"].casefold()
        self.assertIn("yıllık", locked_text)
        self.assertIn("açık", locked_text)
        self.assertTrue(
            "çok-yıllı" in locked_text or "birden fazla" in locked_text,
            locked_text,
        )
        self.assertTrue(
            "en yüksek" in locked_text or "en yetkili" in locked_text,
            locked_text,
        )
        for method in (
            "carry-forward",
            "snapshot",
            "türetilmiş",
            "legal-hold",
            "ikinci onay",
            "audit",
            "purge",
        ):
            self.assertNotIn(method, locked_text)
        self.assertIn("open-018/021", period["KabulKaniti"].casefold())

        open_method = " ".join(
            self.open_decisions[decision_id]["remaining"]
            for decision_id in ("OPEN-018", "OPEN-021")
        )
        open_method = self._normalized(open_method)
        self.assertTrue(
            "carry-forward" in open_method or "snapshot" in open_method,
            open_method,
        )
        self.assertTrue(
            "türetilmiş" in open_method
            or "read model" in open_method
            or "çok-yıllı okuma mimarisi" in open_method,
            open_method,
        )
        for method in ("legal-hold", "ikinci onay", "audit", "purge"):
            self.assertIn(method, open_method)

        for relative_path in (
            "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
            "docs/delivery/DECISION_PACK.md",
        ):
            content = (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8")
            section = self._normalized(
                self._markdown_section(content, "### Q5", "### Q6")
            )
            self.assertTrue(
                "kilitlenmeyen yöntem" in section
                or "teknik yöntem kilitli değildir" in section,
                relative_path,
            )
            self.assertIn("gerçekleştirebil", section, relative_path)
            self.assertNotIn("başlatabil", section, relative_path)
            for method in ("carry-forward", "snapshot", "legal-hold", "ikinci onay"):
                self.assertIn(method, section, relative_path)

        sinan = (
            REPOSITORY_ROOT / "docs/decisions/SINAN_KARARLARI_2026-08-31.md"
        ).read_text(encoding="utf-8")
        pack = (REPOSITORY_ROOT / "docs/delivery/DECISION_PACK.md").read_text(
            encoding="utf-8"
        )
        self.assertIn("TARİHSEL / SUPERSEDED", sinan)
        self.assertIn("TARİHSEL / SUPERSEDED", pack)

    def test_dec_set_001_has_one_dedicated_settings_owner_slice(self) -> None:
        locations: list[str] = []
        for wave in self.backlog["waves"]:
            for item in wave["slices"]:
                if "DEC-SET-001" in item.get("decisions", []):
                    locations.append(item["id"])
        self.assertNotIn("DEC-SET-001", self.backlog.get("global_decisions", []))
        self.assertEqual(["W1-SETTINGS"], locations)

        settings = next(
            item
            for wave in self.backlog["waves"]
            for item in wave["slices"]
            if item["id"] == "W1-SETTINGS"
        )
        scope = settings["scope"].casefold()
        self.assertTrue("ayar" in scope or "settings" in scope, scope)
        self.assertTrue("politika" in scope or "policy" in scope, scope)

    @staticmethod
    def _markdown_section(content: str, start: str, end: str) -> str:
        start_index = content.index(start)
        end_index = content.index(end, start_index)
        return content[start_index:end_index]

    @staticmethod
    def _normalized(value: str) -> str:
        return " ".join(value.casefold().split())


class DecisionRoundMutationTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        for relative_path in REQUIRED_FILES:
            source = REPOSITORY_ROOT / relative_path
            target = self.root / relative_path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        self.decision_md = self.root / "docs/decisions/KARAR_SICILI.md"
        self.decision_csv = self.root / "docs/decisions/KARAR_SICILI.csv"
        self.open_md = self.root / "docs/decisions/ACIK_KARARLAR.md"
        self.backlog = self.root / "docs/backlog/DELIVERY_BACKLOG_v1.json"

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_delegated_source_admission_is_separate_from_delivery_coverage(self) -> None:
        errors, statistics = VALIDATOR.validate_repository_with_statistics(self.root)
        self.assertEqual([], errors)
        assert statistics is not None
        self.assertEqual((117, 213, 96), (
            statistics.decision_count, statistics.registry_record_count,
            statistics.delegated_record_count,
        ))
        result = RECOMPUTE.recompute(self.root)
        self.assertEqual(117, len(result["deliveryBacklog"]["usedDecisionIds"]))
        self.assertEqual([], result["deliveryBacklog"]["missingDecisionIds"])
        self.assertTrue(result["delegatedSourceAdmission"]["isExact"])

    def test_delegated_literal_comparison_and_no133_are_lossless(self) -> None:
        errors = []
        source = VALIDATOR._read_delegated_source(
            self.root / VALIDATOR.DELEGATED_SOURCE_PATH, errors,
        )
        self.assertEqual([], errors)
        by_id = {row["id"]: row for row in source}
        self.assertIn("fark < blok", by_id["DELEGE-20261021-62"]["decision"])
        text = (self.root / VALIDATOR.DELEGATED_SOURCE_PATH).read_text(encoding="utf-8")
        combined = next(line for line in text.splitlines() if line.startswith("| 133 |"))
        self.assertEqual(
            combined.split("|")[2].strip(),
            by_id["DELEGE-20261021-133"]["area"] + ": " + by_id["DELEGE-20261021-133"]["decision"],
        )
        self.assertTrue(RECOMPUTE.recompute(self.root)["delegatedSourceAdmission"]["isExact"])

    def test_delegated_fields_cannot_drift_even_when_markdown_and_csv_agree(self) -> None:
        md = self.decision_md.read_text(encoding="utf-8")
        csv_text = self.decision_csv.read_text(encoding="utf-8")
        # Each replacement affects one complete admitted row on both surfaces.
        cases = (
            ("DecisionId", "DELEGE-20261022-4"),
            ("DecisionId", "DELEGE-20261021-999"),
            ("Alan", "Changed area"),
            ("Karar", "Changed policy"),
            ("Durum", "KILITLI"),
            ("Durum", "UNSUPPORTED"),
            ("Oncelik", "K0"),
            ("Kaynak", "[unknown source](unknown.md)"),
            ("Sahip", "Different owner"),
            ("KabulKaniti", "Runtime accepted"),
        )
        for field, value in cases:
            with self.subTest(field=field, value=value):
                self.decision_md.write_text(md, encoding="utf-8")
                self.decision_csv.write_text(csv_text, encoding="utf-8")
                rows = self._read_csv_rows()
                row = next(row for row in rows if row["DecisionId"] == "DELEGE-20261021-4")
                row[field] = value
                self._write_csv_rows(rows)
                column = VALIDATOR.DECISION_CSV_HEADERS.index(field)
                def replace_cell(line: str) -> str:
                    cells = [cell.strip() for cell in line.split("|")[1:-1]]
                    cells[column] = value
                    return "| " + " | ".join(cells) + " |"
                self._mutate_markdown_decision("DELEGE-20261021-4", replace_cell)
                self.assert_error_contains("Delegated registry source/identity/order/status/field drift")
                self.assertFalse(RECOMPUTE.recompute(self.root)["delegatedSourceAdmission"]["isExact"])

    def test_missing_duplicate_and_extra_delegations_are_rejected(self) -> None:
        originals = (self.decision_md.read_text(encoding="utf-8"), self.decision_csv.read_text(encoding="utf-8"))
        for kind in ("missing", "duplicate", "extra"):
            with self.subTest(kind=kind):
                self.decision_md.write_text(originals[0], encoding="utf-8")
                self.decision_csv.write_text(originals[1], encoding="utf-8")
                rows = self._read_csv_rows()
                record = dict(next(row for row in rows if row["DecisionId"] == "DELEGE-20261021-4"))
                line = self._markdown_decision_line(record["DecisionId"])
                if kind == "missing":
                    self._remove_markdown_decision(record["DecisionId"])
                    rows = [row for row in rows if row["DecisionId"] != record["DecisionId"]]
                else:
                    if kind == "extra":
                        line = line.replace(record["DecisionId"], "DELEGE-20261021-999", 1)
                        record["DecisionId"] = "DELEGE-20261021-999"
                    self.decision_md.write_text(self.decision_md.read_text(encoding="utf-8") + line + "\n", encoding="utf-8")
                    rows.append(record)
                self._write_csv_rows(rows)
                self.assert_error_contains("Delegated registry source/identity/order/status/field drift")
                self.assertFalse(RECOMPUTE.recompute(self.root)["delegatedSourceAdmission"]["isExact"])

    def test_delegations_cannot_be_promoted_to_backlog_authority(self) -> None:
        backlog = json.loads(self.backlog.read_text(encoding="utf-8"))
        backlog["global_decisions"].append("DELEGE-20261021-4")
        self.backlog.write_text(json.dumps(backlog, ensure_ascii=False), encoding="utf-8")
        self.assert_error_contains("unknown decision ids: DELEGE-20261021-4")
        self.assertEqual(["DELEGE-20261021-4"], RECOMPUTE.recompute(self.root)["deliveryBacklog"]["extraDecisionIds"])

    def test_same_set_delegated_order_swap_is_rejected_on_both_surfaces(self) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        indices = [index for index, line in enumerate(lines) if line.startswith("| DELEGE-")]
        first, second = indices[:2]
        lines[first], lines[second] = lines[second], lines[first]
        self.decision_md.write_text("\n".join(lines) + "\n", encoding="utf-8")
        rows = self._read_csv_rows()
        positions = [index for index, row in enumerate(rows) if row["DecisionId"].startswith("DELEGE-")]
        first, second = positions[:2]
        rows[first], rows[second] = rows[second], rows[first]
        self._write_csv_rows(rows)
        self.assert_error_contains("Delegated registry source/identity/order/status/field drift")
        self.assertFalse(RECOMPUTE.recompute(self.root)["delegatedSourceAdmission"]["isExact"])

    def test_delegated_one_surface_semantic_drift_is_rejected(self) -> None:
        self._mutate_markdown_decision(
            "DELEGE-20261021-4", lambda row: row.replace("ölçek 3", "ölçek 9", 1),
        )
        self.assert_error_contains("Markdown/CSV semantic drift for 'DELEGE-20261021-4'")
        self.assertFalse(RECOMPUTE.recompute(self.root)["decisionRegistryParity"]["isExact"])

    def test_authorized_no73_note_cannot_be_removed_from_the_core(self) -> None:
        note = "; üçüncü sonuç — DELEGE-20261021-73"
        for path in (self.decision_md, self.decision_csv):
            text = path.read_text(encoding="utf-8")
            self.assertIn(note, text)
            path.write_text(text.replace(note, "", 1), encoding="utf-8")
        self.assert_error_contains("Decision registry row baseline hash drift")
        self.assertFalse(RECOMPUTE.recompute(self.root)["legacyDecisionRegistry"]["isReviewed"])

    def test_changed_authority_cannot_launder_changed_registry_text(self) -> None:
        authority = self.root / VALIDATOR.DELEGATED_SOURCE_PATH
        authority.write_text(authority.read_text(encoding="utf-8").replace("ölçek 3", "ölçek 9", 1), encoding="utf-8")
        for path in (self.decision_md, self.decision_csv):
            path.write_text(path.read_text(encoding="utf-8").replace("ölçek 3", "ölçek 9", 1), encoding="utf-8")
        self.assert_error_contains("Delegated decision source baseline hash drift")
        with self.assertRaisesRegex(ValueError, "authority source digest changed"):
            RECOMPUTE.recompute(self.root)

    def test_missing_authority_and_delegated_markup_fail_closed(self) -> None:
        authority = self.root / VALIDATOR.DELEGATED_SOURCE_PATH
        authority.unlink()
        self.assert_error_contains("Delegated decision source unavailable")
        with self.assertRaises(OSError):
            RECOMPUTE.recompute(self.root)

    def test_literal_admission_does_not_allow_html_inside_or_outside_inline_code(self) -> None:
        for markup in (
            "<script>", "`<script>`", "<!-- hidden -->", "<div>",
            "``DELEGE-20261021-<No>`", "`DELEGE-20261021-<No>``",
        ):
            with self.subTest(markup=markup):
                self.assertTrue(VALIDATOR._has_raw_markup(markup))
                self.assertTrue(RECOMPUTE._contains_markup(markup))

    def test_add_decision_in_both_sources_is_rejected(self) -> None:
        self.decision_md.write_text(
            self.decision_md.read_text(encoding="utf-8")
            + "| DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |\n",
            encoding="utf-8",
        )
        rows = self._read_csv_rows()
        rows.append(
            {
                "DecisionId": "DEC-TST-999",
                "Alan": "Test",
                "Karar": "Added",
                "Durum": "KILITLI",
                "Oncelik": "K1",
                "Kaynak": "Test",
                "Sahip": "Test",
                "KabulKaniti": "Test",
            }
        )
        self._write_csv_rows(rows)

        self.assert_error_contains("Decision registry baseline drift: expected 117, got 118")

    def test_remove_decision_from_both_sources_is_rejected(self) -> None:
        self._remove_markdown_decision("DEC-AI-009")
        self._write_csv_rows(
            [row for row in self._read_csv_rows() if row["DecisionId"] != "DEC-AI-009"]
        )

        self.assert_error_contains("Decision registry baseline drift: expected 117, got 116")

    def test_status_swap_with_same_histogram_is_rejected(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001", lambda row: row.replace("| KILITLI |", "| GECICI_VARSAYIM |")
        )
        self._mutate_markdown_decision(
            "DEC-PERF-001", lambda row: row.replace("| GECICI_VARSAYIM |", "| KILITLI |")
        )
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Durum"] = "GECICI_VARSAYIM"
            elif row["DecisionId"] == "DEC-PERF-001":
                row["Durum"] = "KILITLI"
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("locked baseline drift" in error for error in errors), errors)
        self.assertFalse(any("temporary baseline drift" in error for error in errors), errors)

    def test_decision_header_and_active_table_context_are_required(self) -> None:
        original = self.decision_md.read_text(encoding="utf-8")
        header = VALIDATOR.DECISION_MARKDOWN_HEADER
        cases = (
            (
                "header_swap",
                original.replace(
                    header,
                    "| ID | Karar | Alan | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |",
                    1,
                ),
                "must contain exactly one canonical Markdown header; found 0",
            ),
            (
                "fenced",
                original.replace(header, "```markdown\n" + header, 1) + "```\n",
                "inactive fence/comment/raw-HTML context",
            ),
        )
        for case, content, expected in cases:
            with self.subTest(case=case):
                self.decision_md.write_text(content, encoding="utf-8")
                self.assert_error_contains(expected)
                self.decision_md.write_text(original, encoding="utf-8")

    def test_open_header_and_active_table_context_are_required(self) -> None:
        original = self.open_md.read_text(encoding="utf-8")
        header = VALIDATOR.OPEN_MARKDOWN_HEADER
        cases = (
            (
                "header_swap",
                original.replace(
                    header,
                    header.replace("| ID | Durum |", "| Durum | ID |", 1),
                    1,
                ),
                "must contain exactly one canonical Markdown header; found 0",
            ),
            (
                "fenced",
                original.replace(header, "~~~markdown\n" + header, 1) + "~~~\n",
                "inactive fence/comment/raw-HTML context",
            ),
        )
        for case, content, expected in cases:
            with self.subTest(case=case):
                self.open_md.write_text(content, encoding="utf-8")
                self.assert_error_contains(expected)
                self.open_md.write_text(original, encoding="utf-8")

    def test_lazy_blockquote_before_canonical_header_is_rejected(self) -> None:
        catalogs = (
            (self.decision_md, VALIDATOR.DECISION_MARKDOWN_HEADER, "decision registry"),
            (self.open_md, VALIDATOR.OPEN_MARKDOWN_HEADER, "open decision catalog"),
        )

        for path, header, label in catalogs:
            with self.subTest(path=path.name):
                original = path.read_text(encoding="utf-8")
                mutated = original.replace(
                    header,
                    "> inert quote paragraph\n" + header,
                    1,
                )
                self.assertNotEqual(original, mutated)
                path.write_text(mutated, encoding="utf-8")

                errors = self._errors()
                self.assertTrue(
                    any(
                        label in error.casefold()
                        and (
                            "blockquote" in error.casefold()
                            or "inactive" in error.casefold()
                        )
                        for error in errors
                    ),
                    errors,
                )
                with self.assertRaises(ValueError) as raised:
                    RECOMPUTE.recompute(self.root)
                self.assertRegex(
                    str(raised.exception).casefold(),
                    r"blockquote|inactive",
                )

                path.write_text(original, encoding="utf-8")

    def test_noncanonical_optional_pipe_rows_cannot_bypass_registry_pins(self) -> None:
        cases = (
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                " | DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "**DEC-TST-999** | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC-<!-- hidden -->TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC\\-TST\\-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (self.decision_md, "| DEC-AI-009 |", "DEC-TST-999"),
            (
                self.open_md,
                "| OPEN-021 |",
                "OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (
                self.open_md,
                "| OPEN-021 |",
                " | OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (
                self.open_md,
                "| OPEN-021 |",
                "> OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (self.open_md, "| OPEN-021 |", "OPEN-999"),
        )
        originals = {
            self.decision_md: self.decision_md.read_text(encoding="utf-8"),
            self.open_md: self.open_md.read_text(encoding="utf-8"),
        }
        for path, after_prefix, added_row in cases:
            with self.subTest(path=path.name, row=added_row):
                lines = originals[path].splitlines()
                insertion = next(
                    index + 1
                    for index, line in enumerate(lines)
                    if line.startswith(after_prefix)
                )
                lines.insert(insertion, added_row)
                path.write_text("\n".join(lines) + "\n", encoding="utf-8")
                expected = (
                    "raw HTML/comment markup is not allowed"
                    if "<" in added_row
                    else "contains a non-canonical row"
                )
                self.assert_error_contains(expected)
                with self.assertRaisesRegex(
                    ValueError,
                    "raw HTML/comment markup is not allowed"
                    if "<" in added_row
                    else "non-canonical table row",
                ):
                    RECOMPUTE.recompute(self.root)
                path.write_text(originals[path], encoding="utf-8")

    def test_complex_fence_comment_and_raw_html_contexts_are_rejected(self) -> None:
        catalogs = (
            (self.decision_md, VALIDATOR.DECISION_MARKDOWN_HEADER),
            (self.open_md, VALIDATOR.OPEN_MARKDOWN_HEADER),
        )
        wrappers = (
            ("long_fence_short_pseudo_close", "````\n```\n", "\n````"),
            ("long_fence_mixed_pseudo_close", "````\n````~\n", "\n````"),
            ("comment_close_reopen", "<!--\n--> <!--\n", "\n-->"),
            ("pre", "<pre>\n", "\n</pre>"),
            ("processing_instruction", "<?test\n", "\n?>"),
            ("cdata", "<![CDATA[\n", "\n]]>"),
            ("block_tag", "<table>\n", "\n</table>"),
        )
        for path, header in catalogs:
            original = path.read_text(encoding="utf-8")
            for case, prefix, suffix in wrappers:
                with self.subTest(path=path.name, case=case):
                    mutated = original.replace(header, prefix + header, 1) + suffix + "\n"
                    path.write_text(mutated, encoding="utf-8")
                    self.assert_error_contains("inactive fence/comment/raw-HTML context")
                    with self.assertRaisesRegex(
                        ValueError,
                        "raw HTML/comment markup is not allowed"
                        if "<" in prefix
                        else "inactive fence/comment/raw-HTML context",
                    ):
                        RECOMPUTE.recompute(self.root)
                    path.write_text(original, encoding="utf-8")

    def test_semantic_column_change_in_both_sources_is_rejected(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001",
            lambda row: row.replace(
                "Göymen ERP bağımsız yeni üründür",
                "Göymen ERP değiştirilmiş üründür",
                1,
            ),
        )
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Karar"] = row["Karar"].replace(
                    "Göymen ERP bağımsız yeni üründür",
                    "Göymen ERP değiştirilmiş üründür",
                    1,
                )
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("Markdown/CSV semantic drift" in error for error in errors), errors)

    def test_open_decision_content_change_is_rejected(self) -> None:
        content = self.open_md.read_text(encoding="utf-8")
        content = content.replace("| OPEN-001 | **KISMEN** |", "| OPEN-001 | **AÇIK** |", 1)
        self.open_md.write_text(content, encoding="utf-8")

        self.assert_error_contains("Open decision catalog row baseline hash drift")

    def test_open_status_cell_is_exact_and_case_sensitive(self) -> None:
        original = self.open_md.read_text(encoding="utf-8")
        cases = (
            ("plain", "KISMEN"),
            ("case_drift", "**kismen**"),
            ("unsupported", "**BEKLIYOR**"),
        )

        for case, status_cell in cases:
            with self.subTest(case=case):
                mutated = original.replace(
                    "| OPEN-001 | **KISMEN** |",
                    f"| OPEN-001 | {status_cell} |",
                    1,
                )
                self.assertNotEqual(original, mutated)
                self.open_md.write_text(mutated, encoding="utf-8")

                errors = self._errors()
                self.assertTrue(
                    any(
                        "Open decision catalog 'OPEN-001' has unsupported status"
                        in error
                        for error in errors
                    ),
                    errors,
                )
                with self.assertRaisesRegex(ValueError, "has invalid status"):
                    RECOMPUTE.recompute(self.root)

                self.open_md.write_text(original, encoding="utf-8")

    def test_reserved_open_ids_cannot_be_reassigned_to_vkn_lifecycle(self) -> None:
        original = self.open_md.read_text(encoding="utf-8")
        self.assertIn("| OPEN-030 |", original)

        for reserved_id in sorted(RECOMPUTE.RESERVED_OPEN_IDS):
            with self.subTest(reserved_id=reserved_id):
                mutated = original.replace(
                    "| OPEN-030 |",
                    f"| {reserved_id} |",
                    1,
                )
                self.assertNotEqual(original, mutated)
                self.open_md.write_text(mutated, encoding="utf-8")

                errors = self._errors()
                self.assertTrue(
                    any(
                        error.startswith(
                            "Open decision catalog stable id "
                            f"reserved/cannot be reassigned: {reserved_id}"
                        )
                        for error in errors
                    ),
                    errors,
                )

                result = RECOMPUTE.recompute(self.root)
                self.assertEqual(
                    ["OPEN-030"],
                    result["openDecisionCatalog"]["missingExpectedIds"],
                )
                self.assertEqual(
                    [reserved_id],
                    result["openDecisionCatalog"]["extraExpectedIds"],
                )
                self.assertFalse(result["openDecisionCatalog"]["isExpectedExact"])

                self.open_md.write_text(original, encoding="utf-8")

    def test_zero_open_rows_fail_closed_in_validator_and_recomputer(self) -> None:
        lines = self.open_md.read_text(encoding="utf-8").splitlines()
        self.open_md.write_text(
            "\n".join(line for line in lines if not line.startswith("| OPEN-")) + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical Markdown table has no data rows")
        result = RECOMPUTE.recompute(self.root)
        self.assertEqual(0, result["openDecisionCatalog"]["count"])
        self.assertEqual(
            [
                *[f"OPEN-{number:03d}" for number in range(1, 22)],
                "OPEN-030",
            ],
            result["openDecisionCatalog"]["missingExpectedIds"],
        )
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, RECOMPUTE.main(["--root", str(self.root)]))

    def test_removing_new_decision_from_backlog_is_rejected(self) -> None:
        backlog = json.loads(self.backlog.read_text(encoding="utf-8"))
        removed = 0
        for wave in backlog["waves"]:
            for item in wave["slices"]:
                decisions = item.get("decisions", [])
                if "DEC-AI-009" in decisions:
                    decisions.remove("DEC-AI-009")
                    removed += 1
        self.assertEqual(1, removed)
        self.backlog.write_text(
            json.dumps(backlog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

        self.assert_error_contains("missing decision ids: DEC-AI-009")

    def test_duplicate_decision_row_is_rejected(self) -> None:
        line = self._markdown_decision_line("DEC-AI-009")
        self.decision_md.write_text(
            self.decision_md.read_text(encoding="utf-8") + line + "\n", encoding="utf-8"
        )
        rows = self._read_csv_rows()
        rows.append(dict(next(row for row in rows if row["DecisionId"] == "DEC-AI-009")))
        self._write_csv_rows(rows)

        self.assert_error_contains("Duplicate decision registry id: 'DEC-AI-009'")
        self.assert_error_contains("Duplicate decision registry CSV id: 'DEC-AI-009'")

    def test_reorder_only_in_both_sources_is_rejected_by_order_pin(self) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        first = next(i for i, line in enumerate(lines) if line.startswith("| DEC-DOC-010 |"))
        second = next(i for i, line in enumerate(lines) if line.startswith("| DEC-AI-009 |"))
        lines[first], lines[second] = lines[second], lines[first]
        self.decision_md.write_text("\n".join(lines) + "\n", encoding="utf-8")
        rows = self._read_csv_rows()
        first = next(i for i, row in enumerate(rows) if row["DecisionId"] == "DEC-DOC-010")
        second = next(i for i, row in enumerate(rows) if row["DecisionId"] == "DEC-AI-009")
        rows[first], rows[second] = rows[second], rows[first]
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("order baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("Markdown/CSV row order drift" in error for error in errors), errors)

    def test_markdown_only_drift_is_rejected_by_parity(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001", lambda row: row.replace("| Yönetişim |", "| MD-only |", 1)
        )

        self.assert_error_contains(
            "Decision registry Markdown/CSV semantic drift for 'DEC-GOV-001': area"
        )

    def test_csv_only_drift_is_rejected_by_parity(self) -> None:
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Alan"] = "CSV-only"
        self._write_csv_rows(rows)

        self.assert_error_contains(
            "Decision registry Markdown/CSV semantic drift for 'DEC-GOV-001': area"
        )

    def test_csv_bom_and_crlf_do_not_create_false_parity_drift(self) -> None:
        csv_text = self.decision_csv.read_text(encoding="utf-8-sig")
        csv_payload = csv_text.replace("\r\n", "\n").replace("\n", "\r\n")
        self.decision_csv.write_bytes(b"\xef\xbb\xbf" + csv_payload.encode("utf-8"))
        md_text = self.decision_md.read_text(encoding="utf-8")
        self.decision_md.write_bytes(
            md_text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")
        )

        errors = self._errors()
        self.assertEqual([], errors)

    def assert_error_contains(self, expected: str) -> None:
        errors = self._errors()
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected {expected!r} in {errors!r}",
        )

    def _errors(self) -> list[str]:
        errors, _ = VALIDATOR.validate_repository_with_statistics(self.root)
        return errors

    def _read_csv_rows(self) -> list[dict[str, str]]:
        with self.decision_csv.open(encoding="utf-8-sig", newline="") as stream:
            return [dict(row) for row in csv.DictReader(stream)]

    def _write_csv_rows(self, rows: list[dict[str, str]]) -> None:
        fieldnames = list(VALIDATOR.DECISION_CSV_HEADERS)
        with self.decision_csv.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=fieldnames, lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)

    def _markdown_decision_line(self, decision_id: str) -> str:
        return next(
            line
            for line in self.decision_md.read_text(encoding="utf-8").splitlines()
            if line.startswith(f"| {decision_id} |")
        )

    def _mutate_markdown_decision(
        self, decision_id: str, transform: Callable[[str], str]
    ) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if line.startswith(f"| {decision_id} |"):
                lines[index] = transform(line)
                break
        else:
            self.fail(f"Missing Markdown decision {decision_id}")
        self.decision_md.write_text("\n".join(lines) + "\n", encoding="utf-8")

    def _remove_markdown_decision(self, decision_id: str) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        filtered = [line for line in lines if not line.startswith(f"| {decision_id} |")]
        self.assertEqual(len(lines) - 1, len(filtered))
        self.decision_md.write_text("\n".join(filtered) + "\n", encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
