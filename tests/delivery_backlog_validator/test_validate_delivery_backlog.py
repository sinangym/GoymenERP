from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_delivery_backlog.py"
MODULE_NAME = "goymen_delivery_backlog_validator"
MODULE_SPEC = importlib.util.spec_from_file_location(MODULE_NAME, VALIDATOR_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load delivery backlog validator from {VALIDATOR_PATH}")
VALIDATOR_MODULE = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_NAME] = VALIDATOR_MODULE
MODULE_SPEC.loader.exec_module(VALIDATOR_MODULE)

validate_repository_with_statistics = (
    VALIDATOR_MODULE.validate_repository_with_statistics
)

FIXTURE_SCREEN_HASH = hashlib.sha256(b"C-001").hexdigest()
FIXTURE_DECISION_ROW = (
    "| DEC-TST-001 | Fixture | Fixture | KILITLI | K0 | Fixture source | "
    "Fixture owner | Fixture acceptance |"
)
FIXTURE_DECISION_ROWS_HASH = hashlib.sha256(
    FIXTURE_DECISION_ROW.encode("utf-8")
).hexdigest()
FIXTURE_DECISION_ORDER_HASH = hashlib.sha256(b"DEC-TST-001").hexdigest()
FIXTURE_OPEN_ROW = (
    "| OPEN-001 | **KISMEN** | Fixture closed | Fixture open | Fixture evidence | "
    "Fixture code evidence |"
)
FIXTURE_OPEN_ROWS_HASH = hashlib.sha256(FIXTURE_OPEN_ROW.encode("utf-8")).hexdigest()
FIXTURE_ACCEPTANCE_ROW = "- **AT-001:** Fixture acceptance."
FIXTURE_ACCEPTANCE_ROWS_HASH = hashlib.sha256(
    FIXTURE_ACCEPTANCE_ROW.encode("utf-8")
).hexdigest()
FIXTURE_COVERAGE_MAPPING = {
    "source": "Fixture source",
    "notes": ["Fixture note"],
    "lanes": {
        "codex": "Fixture backend lane",
        "claude": "Fixture frontend lane",
        "chatgpt": "Fixture specification lane",
    },
    "global_decisions": ["DEC-TST-001"],
    "global_acceptance": [],
    "wave_order": ["W0"],
    "waves": {
        "W0": {
            "title": "Fixture wave",
            "faz": "Fixture phase",
        }
    },
    "slice_order": {"W0": ["SLICE-001"]},
    "slices": {
        "SLICE-001": {
            "wave_id": "W0",
            "task": "ERP-001",
            "lane": "codex",
            "scope": "Minimal delivery fixture",
            "depends_on": [],
            "decisions": [],
            "open": ["OPEN-001"],
            "acceptance": ["AT-001"],
            "acceptance_notes": [],
            "screens": ["C-001"],
            "issues": [],
            "critical_analysis": None,
            "manifest_task": "ERP-001",
            "manifest_status": "ready",
            "manifest_depends_on": [],
            "manifest_blocked_by": [],
            "execution_state": None,
            "protocol_gate": None,
            "production_gate": {
                "key": "Fixture:Enabled",
                "default": False,
                "scope": "Fixture only",
            },
            "decision_only": False,
        }
    },
}
FIXTURE_COVERAGE_HASH = hashlib.sha256(
    json.dumps(
        FIXTURE_COVERAGE_MAPPING,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
).hexdigest()
ERP_F_PROTOCOL_GATE = "CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md \u00a711"


class DeliveryBacklogValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        self.backlog_path = self.root / "docs/backlog/DELIVERY_BACKLOG_v1.json"
        self.manifest_path = self.root / "docs/backlog/task_manifest.json"
        self.decision_registry_path = self.root / "docs/decisions/KARAR_SICILI.md"
        self.decision_registry_csv_path = self.root / "docs/decisions/KARAR_SICILI.csv"
        self.open_decisions_path = self.root / "docs/decisions/ACIK_KARARLAR.md"
        self.acceptance_path = self.root / "docs/acceptance/ACCEPTANCE_70.md"
        self.screen_catalog_path = self.root / "docs/ui/CANONICAL_SCREEN_STATUS.csv"
        self._build_valid_fixture()

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_current_repository_delivery_backlog_passes(self) -> None:
        errors, statistics = validate_repository_with_statistics(REPOSITORY_ROOT)

        self.assertEqual([], errors)
        self.assertIsNotNone(statistics)

    def test_minimal_valid_fixture_passes(self) -> None:
        errors, statistics = self._validate_fixture_with_statistics()

        self.assertEqual([], errors)
        self.assertIsNotNone(statistics)
        assert statistics is not None
        self.assertEqual(1, statistics.wave_count)
        self.assertEqual(1, statistics.slice_count)
        self.assertEqual(1, statistics.manifest_task_count)

    def test_manifest_status_dependency_and_blocker_drift_are_rejected(self) -> None:
        cases = (
            ("manifest_status", "blocked", "manifest_status drift"),
            ("manifest_depends_on", ["ERP-404"], "manifest_depends_on drift"),
            ("manifest_blocked_by", ["OPEN-999"], "manifest_blocked_by drift"),
        )

        for field, stale_value, expected in cases:
            with self.subTest(field=field):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                backlog["waves"][0]["slices"][0][field] = stale_value
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(expected)

    def test_manifest_task_marker_removal_is_rejected(self) -> None:
        backlog = self._read_json(self.backlog_path)
        backlog["waves"][0]["slices"][0].pop("manifest_task")
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "Delivery backlog is missing active manifest task mappings: ERP-001"
        )

    def test_partial_manifest_snapshot_is_rejected(self) -> None:
        for field in (
            "manifest_status",
            "manifest_depends_on",
            "manifest_blocked_by",
        ):
            with self.subTest(field=field):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                backlog["waves"][0]["slices"][0].pop(field)
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(
                    f"incomplete manifest snapshot; missing {field}"
                )

    def test_active_manifest_task_must_be_mapped_exactly_once(self) -> None:
        backlog = self._read_json(self.backlog_path)
        duplicate_mapping = copy.deepcopy(backlog["waves"][0]["slices"][0])
        duplicate_mapping.update(
            {
                "id": "SLICE-002",
                "scope": "Duplicate manifest mapping fixture",
                "open": [],
                "acceptance": [],
                "screens": [],
            }
        )
        duplicate_mapping.pop("production_gate")
        backlog["waves"][0]["slices"].append(duplicate_mapping)
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "Duplicate delivery backlog manifest mapping id: 'ERP-001'"
        )

    def test_unknown_slice_dependency_is_rejected(self) -> None:
        backlog = self._read_json(self.backlog_path)
        backlog["waves"][0]["slices"][0]["depends_on"] = ["SLICE-404"]
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains("depends on unknown slice 'SLICE-404'")

    def test_dependency_cycle_is_rejected_with_canonical_path(self) -> None:
        backlog = self._read_json(self.backlog_path)
        first = backlog["waves"][0]["slices"][0]
        first["depends_on"] = ["SLICE-002"]
        backlog["waves"][0]["slices"].append(self._secondary_slice())
        backlog["waves"][0]["slices"][1]["depends_on"] = ["SLICE-001"]
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "Delivery backlog cycle detected: SLICE-001 -> SLICE-002 -> SLICE-001"
        )

    def test_duplicate_slice_id_is_rejected(self) -> None:
        backlog = self._read_json(self.backlog_path)
        duplicate = copy.deepcopy(backlog["waves"][0]["slices"][0])
        backlog["waves"][0]["slices"].append(duplicate)
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains("Duplicate slice id: 'SLICE-001'")

    def test_unknown_decision_open_acceptance_and_screen_ids_are_rejected(self) -> None:
        cases = (
            ("decisions", "DEC-TST-999", "unknown decisions id 'DEC-TST-999'"),
            ("open", "OPEN-999", "unknown open id 'OPEN-999'"),
            ("acceptance", "AT-999", "unknown acceptance id 'AT-999'"),
            ("screens", "C-999", "unknown screens id 'C-999'"),
        )

        for field, unknown_id, expected in cases:
            with self.subTest(field=field):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                backlog["waves"][0]["slices"][0][field].append(unknown_id)
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(expected)

    def test_missing_canonical_coverage_is_rejected(self) -> None:
        cases = (
            (
                self.decision_registry_path,
                "| DEC-TST-002 | Missing | Fixture | KILITLI | K1 | Fixture source | "
                "Fixture owner | Fixture acceptance |\n",
                "missing decision ids: DEC-TST-002",
            ),
            (
                self.open_decisions_path,
                "| OPEN-002 | **KISMEN** | Missing closed | Missing open | "
                "Missing evidence | Missing code evidence |\n",
                "missing open decision ids: OPEN-002",
            ),
            (
                self.acceptance_path,
                "- **AT-002:** Missing fixture acceptance.\n",
                "missing acceptance ids: AT-002",
            ),
            (
                self.screen_catalog_path,
                "C-002,Missing coverage fixture\n",
                "missing screen ids: C-002",
            ),
        )

        for path, addition, expected in cases:
            with self.subTest(path=path.name):
                self._build_valid_fixture()
                path.write_text(
                    path.read_text(encoding="utf-8") + addition,
                    encoding="utf-8",
                )

                self.assert_fixture_error_contains(expected)

    def test_catalog_and_backlog_cannot_shrink_together(self) -> None:
        self.acceptance_path.write_text("# Acceptance\n", encoding="utf-8")
        backlog = self._read_json(self.backlog_path)
        backlog["waves"][0]["slices"][0]["acceptance"] = []
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "Acceptance baseline drift: expected AT-001..AT-070, got"
        )

    def test_lane_scope_wave_id_and_backlog_status_are_fail_closed(self) -> None:
        cases = (
            ("lane", "field 'lane' must be one of"),
            ("scope", "field 'scope' must be a non-empty trimmed string"),
            ("wave_id", "field 'id' must be a non-empty trimmed string"),
            ("status", "field 'status' must equal 'ACTIVE_GATED'"),
        )

        for case, expected in cases:
            with self.subTest(case=case):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                if case == "lane":
                    backlog["waves"][0]["slices"][0]["lane"] = "codez"
                elif case == "scope":
                    backlog["waves"][0]["slices"][0]["scope"] = " "
                elif case == "wave_id":
                    backlog["waves"][0]["id"] = " W0 "
                else:
                    backlog["status"] = "DRAFT"
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(expected)

    def test_mapping_hash_rejects_coverage_move_with_same_global_sets(self) -> None:
        backlog = self._read_json(self.backlog_path)
        backlog["waves"][0]["slices"].append(self._secondary_slice())
        expected_hash = self._coverage_hash(backlog)
        self._write_json(self.backlog_path, backlog)
        self.assertEqual([], self._validate_fixture(expected_hash))

        backlog["waves"][0]["slices"][0]["acceptance"] = []
        backlog["waves"][0]["slices"][1]["acceptance"] = ["AT-001"]
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "Delivery backlog coverage mapping hash drift",
            coverage_hash=expected_hash,
        )

    def test_semantic_mapping_hash_rejects_valid_but_unreviewed_drift(self) -> None:
        cases = (
            "lanes",
            "lane",
            "scope",
            "dependency",
            "production_gate",
            "execution_gate",
        )

        for case in cases:
            with self.subTest(case=case):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                item = backlog["waves"][0]["slices"][0]
                if case == "lanes":
                    backlog["lanes"]["codex"] = "Unreviewed expanded ownership"
                elif case == "lane":
                    item["lane"] = "claude"
                elif case == "scope":
                    item["scope"] = "Changed but still schema-valid scope"
                elif case == "dependency":
                    item["depends_on"] = ["SLICE-001"]
                elif case == "production_gate":
                    item["production_gate"]["key"] = "Fixture:DifferentGate"
                else:
                    item["execution_state"] = "UNREVIEWED"
                    item["protocol_gate"] = "unreviewed-protocol"
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(
                    "Delivery backlog coverage mapping hash drift"
                )

    def test_slice_priority_order_is_part_of_semantic_hash(self) -> None:
        backlog = self._read_json(self.backlog_path)
        backlog["waves"][0]["slices"].append(self._secondary_slice())
        expected_hash = self._coverage_hash(backlog)
        self._write_json(self.backlog_path, backlog)
        self.assertEqual([], self._validate_fixture(expected_hash))

        backlog["waves"][0]["slices"].reverse()
        self._write_json(self.backlog_path, backlog)
        self.assert_fixture_error_contains(
            "Delivery backlog coverage mapping hash drift",
            coverage_hash=expected_hash,
        )

    def test_unknown_backlog_wave_and_slice_fields_fail_closed(self) -> None:
        cases = (
            ("backlog", "execution_override", "delivery backlog has unknown fields"),
            ("wave", "priority_override", "wave at index 0 has unknown fields"),
            ("slice", "execution_override", "wave 'W0' slice 0 has unknown fields"),
        )

        for target, field, expected in cases:
            with self.subTest(target=target):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                if target == "backlog":
                    backlog[field] = "unsafe"
                elif target == "wave":
                    backlog["waves"][0][field] = "unsafe"
                else:
                    backlog["waves"][0]["slices"][0][field] = "IMPLEMENTATION_ALLOWED"
                self._write_json(self.backlog_path, backlog)
                self.assert_fixture_error_contains(expected)

    def test_decision_status_swap_with_same_counts_fails_row_hash(self) -> None:
        backlog = self._read_json(self.backlog_path)
        backlog["global_decisions"].append("DEC-TST-002")
        coverage_hash = self._coverage_hash(backlog)
        self._write_json(self.backlog_path, backlog)

        first = FIXTURE_DECISION_ROW
        second = (
            "| DEC-TST-002 | Fixture two | Fixture | GECICI_VARSAYIM | K1 | "
            "Fixture source | Fixture owner | Fixture acceptance |"
        )
        original_rows_hash = hashlib.sha256(
            "\n".join(sorted((first, second))).encode("utf-8")
        ).hexdigest()
        swapped_first = first.replace("KILITLI", "GECICI_VARSAYIM")
        swapped_second = second.replace("GECICI_VARSAYIM", "KILITLI")
        self._write_text(
            self.decision_registry_path,
            "# Decisions\n\n"
            "| ID | Alan | Karar | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |\n"
            "|---|---|---|---|---|---|---|---|\n"
            f"{swapped_first}\n{swapped_second}\n",
        )

        with mock.patch.multiple(
            VALIDATOR_MODULE,
            EXPECTED_DECISION_COUNT=2,
            EXPECTED_LOCKED_DECISION_COUNT=1,
            EXPECTED_TEMPORARY_DECISION_COUNT=1,
            EXPECTED_DECISION_ROWS_SHA256=original_rows_hash,
            EXPECTED_DECISION_ORDER_SHA256=hashlib.sha256(
                b"DEC-TST-001\nDEC-TST-002"
            ).hexdigest(),
            EXPECTED_OPEN_IDS={"OPEN-001"},
            EXPECTED_ACCEPTANCE_IDS={"AT-001"},
            EXPECTED_OPEN_ROWS_SHA256=FIXTURE_OPEN_ROWS_HASH,
            EXPECTED_ACCEPTANCE_ROWS_SHA256=FIXTURE_ACCEPTANCE_ROWS_HASH,
            EXPECTED_SCREEN_COUNT=1,
            EXPECTED_COVERAGE_MAPPING_SHA256=coverage_hash,
            EXPECTED_CANONICAL_SCREEN_IDS_SHA256=FIXTURE_SCREEN_HASH,
        ):
            errors, statistics = validate_repository_with_statistics(self.root)

        self.assertIsNone(statistics)
        self.assertTrue(
            any("Decision registry row baseline hash drift" in error for error in errors),
            errors,
        )
        self.assertFalse(any("locked baseline drift" in error for error in errors), errors)
        self.assertFalse(any("temporary baseline drift" in error for error in errors), errors)

    def test_open_and_acceptance_content_drift_with_same_ids_fails_hash(self) -> None:
        cases = (
            (
                "open",
                self.open_decisions_path,
                "Fixture open",
                "Weakened production gate",
                "Open decision catalog row baseline hash drift",
            ),
            (
                "acceptance",
                self.acceptance_path,
                "Fixture acceptance.",
                "Weakened acceptance.",
                "Acceptance catalog row baseline hash drift",
            ),
        )

        for case, path, old, new, expected in cases:
            with self.subTest(case=case):
                self._build_valid_fixture()
                content = path.read_text(encoding="utf-8").replace(old, new)
                path.write_text(content, encoding="utf-8")
                self.assert_fixture_error_contains(expected)

    def test_decision_only_cannot_bypass_an_open_production_gate(self) -> None:
        backlog = self._read_json(self.backlog_path)
        item = backlog["waves"][0]["slices"][0]
        item["lane"] = "chatgpt"
        item["decision_only"] = True
        item.pop("production_gate")
        self._write_json(self.backlog_path, backlog)

        self.assert_fixture_error_contains(
            "decision_only is reserved for the non-executable W5-DECISIONPACK"
        )

    def test_placeholder_hash_sentinels_fail_closed(self) -> None:
        cases = (
            (
                "coverage",
                "TO_BE_REPLACED",
                FIXTURE_SCREEN_HASH,
                FIXTURE_DECISION_ROWS_HASH,
                "Delivery backlog coverage mapping hash drift",
            ),
            (
                "screen",
                FIXTURE_COVERAGE_HASH,
                "TO_BE_REPLACED",
                FIXTURE_DECISION_ROWS_HASH,
                "Canonical screen id baseline hash drift",
            ),
            (
                "decision",
                FIXTURE_COVERAGE_HASH,
                FIXTURE_SCREEN_HASH,
                "TO_BE_REPLACED",
                "Decision registry row baseline hash drift",
            ),
        )

        for case, coverage_hash, screen_hash, decision_rows_hash, expected in cases:
            with self.subTest(case=case):
                errors, statistics = self._validate_fixture_with_statistics(
                    coverage_hash,
                    screen_hash,
                    decision_rows_hash,
                )
                self.assertIsNone(statistics)
                self.assertTrue(
                    any(expected in error for error in errors),
                    f"Expected {expected!r} in {errors!r}",
                )

    def test_legacy_flag_and_unsafe_or_missing_production_gate_are_rejected(self) -> None:
        cases = (
            ("legacy", "uses legacy ambiguous 'flag'; use production_gate"),
            ("default_true", "production_gate default must be false"),
            (
                "missing",
                "has OPEN-* dependencies but no default-false production_gate",
            ),
        )

        for case, expected in cases:
            with self.subTest(case=case):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                item = backlog["waves"][0]["slices"][0]
                if case == "legacy":
                    item["flag"] = "legacy-fixture"
                elif case == "default_true":
                    item["production_gate"]["default"] = True
                else:
                    item.pop("production_gate")
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(expected)

    def test_erp_f_protocol_and_execution_gates_are_preserved(self) -> None:
        cases = (
            (
                "protocol_gate",
                "unreviewed-protocol",
                "must preserve the ERP-000F protocol gate",
            ),
            (
                "execution_state",
                "IMPLEMENTATION_ALLOWED",
                "must remain execution_state 'SPEC_ONLY'",
            ),
        )

        for field, value, expected in cases:
            with self.subTest(field=field):
                self._build_valid_fixture()
                backlog = self._read_json(self.backlog_path)
                manifest = self._read_json(self.manifest_path)
                manifest["tasks"][0]["id"] = "ERP-000F"
                item = backlog["waves"][0]["slices"][0]
                item.update(
                    {
                        "id": "W0-OUTBOX",
                        "task": "ERP-000F",
                        "manifest_task": "ERP-000F",
                        "execution_state": "SPEC_ONLY",
                        "protocol_gate": ERP_F_PROTOCOL_GATE,
                    }
                )
                expected_hash = self._coverage_hash(backlog)
                item[field] = value
                self._write_json(self.manifest_path, manifest)
                self._write_json(self.backlog_path, backlog)

                self.assert_fixture_error_contains(
                    expected,
                    coverage_hash=expected_hash,
                )

    def test_duplicate_json_object_key_is_rejected(self) -> None:
        self.backlog_path.write_text(
            '{"global_decisions":[],"global_decisions":[],"waves":[]}\n',
            encoding="utf-8",
        )

        self.assert_fixture_error_contains(
            "Duplicate JSON object key: 'global_decisions'"
        )

    def test_non_finite_json_numbers_are_rejected(self) -> None:
        for constant in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(constant=constant):
                self._build_valid_fixture()
                content = self.backlog_path.read_text(encoding="utf-8")
                content = content.replace(
                    '"global_decisions"',
                    f'"nonfinite": {constant},\n  "global_decisions"',
                    1,
                )
                self.backlog_path.write_text(content, encoding="utf-8")

                self.assert_fixture_error_contains(
                    f"Non-standard JSON numeric constant: {constant}"
                )

        self._build_valid_fixture()
        content = self.backlog_path.read_text(encoding="utf-8")
        content = content.replace(
            '"global_decisions"',
            '"overflow": 1e999,\n  "global_decisions"',
            1,
        )
        self.backlog_path.write_text(content, encoding="utf-8")
        self.assert_fixture_error_contains("Non-finite JSON number: 1e999")

    def test_utf8_bom_in_screen_catalog_header_is_accepted(self) -> None:
        self.screen_catalog_path.write_text(
            "canonical_id,name\nC-001,Fixture screen\n",
            encoding="utf-8-sig",
        )

        self.assertEqual([], self._validate_fixture())

    def assert_fixture_error_contains(
        self,
        expected: str,
        *,
        coverage_hash: str = FIXTURE_COVERAGE_HASH,
    ) -> None:
        errors = self._validate_fixture(coverage_hash)
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected {expected!r} in {errors!r}",
        )

    def _validate_fixture(
        self,
        coverage_hash: str = FIXTURE_COVERAGE_HASH,
    ) -> list[str]:
        errors, _ = self._validate_fixture_with_statistics(coverage_hash)
        return errors

    def _validate_fixture_with_statistics(
        self,
        coverage_hash: str = FIXTURE_COVERAGE_HASH,
        screen_hash: str = FIXTURE_SCREEN_HASH,
        decision_rows_hash: str = FIXTURE_DECISION_ROWS_HASH,
    ) -> tuple[list[str], Any]:
        with mock.patch.multiple(
            VALIDATOR_MODULE,
            EXPECTED_DECISION_COUNT=1,
            EXPECTED_LOCKED_DECISION_COUNT=1,
            EXPECTED_TEMPORARY_DECISION_COUNT=0,
            EXPECTED_DECISION_ROWS_SHA256=decision_rows_hash,
            EXPECTED_DECISION_ORDER_SHA256=FIXTURE_DECISION_ORDER_HASH,
            EXPECTED_OPEN_IDS={"OPEN-001"},
            EXPECTED_ACCEPTANCE_IDS={"AT-001"},
            EXPECTED_OPEN_ROWS_SHA256=FIXTURE_OPEN_ROWS_HASH,
            EXPECTED_ACCEPTANCE_ROWS_SHA256=FIXTURE_ACCEPTANCE_ROWS_HASH,
            EXPECTED_SCREEN_COUNT=1,
            EXPECTED_COVERAGE_MAPPING_SHA256=coverage_hash,
            EXPECTED_CANONICAL_SCREEN_IDS_SHA256=screen_hash,
        ):
            return validate_repository_with_statistics(self.root)

    def _build_valid_fixture(self) -> None:
        self._write_json(
            self.manifest_path,
            {
                "version": "1.0",
                "tasks": [
                    {
                        "id": "ERP-001",
                        "status": "ready",
                        "depends_on": [],
                        "blocked_by": [],
                    }
                ],
            },
        )
        self._write_json(
            self.backlog_path,
            {
                "version": "1.1",
                "status": "ACTIVE_GATED",
                "owner": "CHATGPT-BUSINESS",
                "source": "Fixture source",
                "protocol": (
                    "docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md"
                ),
                "notes": ["Fixture note"],
                "lanes": {
                    "codex": "Fixture backend lane",
                    "claude": "Fixture frontend lane",
                    "chatgpt": "Fixture specification lane",
                },
                "global_decisions": ["DEC-TST-001"],
                "global_acceptance": [],
                "waves": [
                    {
                        "id": "W0",
                        "title": "Fixture wave",
                        "faz": "Fixture phase",
                        "slices": [
                            {
                                "id": "SLICE-001",
                                "task": "ERP-001",
                                "manifest_task": "ERP-001",
                                "manifest_status": "ready",
                                "manifest_depends_on": [],
                                "manifest_blocked_by": [],
                                "lane": "codex",
                                "scope": "Minimal delivery fixture",
                                "depends_on": [],
                                "decisions": [],
                                "open": ["OPEN-001"],
                                "acceptance": ["AT-001"],
                                "screens": ["C-001"],
                                "production_gate": {
                                    "key": "Fixture:Enabled",
                                    "default": False,
                                    "scope": "Fixture only",
                                },
                            }
                        ],
                    }
                ],
            },
        )
        self._write_text(
            self.decision_registry_path,
            "# Decisions\n\n"
            "| ID | Alan | Karar | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |\n"
            "|---|---|---|---|---|---|---|---|\n"
            f"{FIXTURE_DECISION_ROW}\n",
        )
        self._write_text(
            self.decision_registry_csv_path,
            "DecisionId,Alan,Karar,Durum,Oncelik,Kaynak,Sahip,KabulKaniti\n"
            "DEC-TST-001,Fixture,Fixture,KILITLI,K0,Fixture source,Fixture owner,"
            "Fixture acceptance\n",
        )
        self._write_text(
            self.open_decisions_path,
            "# Open decisions\n\n"
            "| ID | Durum | Kullanıcı tarafından kapanan / kabul edilen bölüm | "
            "Gerçekten açık kalan bölüm | Karar kanıtı | Kod / seed kanıtı |\n"
            "|---|---|---|---|---|---|\n"
            f"{FIXTURE_OPEN_ROW}\n",
        )
        self._write_text(
            self.acceptance_path,
            f"# Acceptance\n\n{FIXTURE_ACCEPTANCE_ROW}\n",
        )
        self._write_text(
            self.screen_catalog_path,
            "canonical_id,name\nC-001,Fixture screen\n",
        )

    @staticmethod
    def _secondary_slice() -> dict[str, Any]:
        return {
            "id": "SLICE-002",
            "task": "Secondary fixture behavior",
            "lane": "claude",
            "scope": "Secondary mapping fixture",
            "depends_on": [],
            "decisions": [],
            "open": [],
            "acceptance": [],
            "screens": [],
        }

    @staticmethod
    def _coverage_hash(backlog: dict[str, Any]) -> str:
        mapping = {
            "source": backlog.get("source"),
            "notes": backlog.get("notes", []),
            "lanes": backlog.get("lanes"),
            "global_decisions": sorted(backlog["global_decisions"]),
            "global_acceptance": sorted(backlog["global_acceptance"]),
            "wave_order": [],
            "waves": {},
            "slice_order": {},
            "slices": {},
        }
        for wave in backlog["waves"]:
            mapping["wave_order"].append(wave["id"])
            mapping["slice_order"][wave["id"]] = []
            mapping["waves"][wave["id"]] = {
                "title": wave.get("title"),
                "faz": wave.get("faz"),
            }
            for item in wave["slices"]:
                mapping["slice_order"][wave["id"]].append(item["id"])
                mapping["slices"][item["id"]] = {
                    "wave_id": wave["id"],
                    "task": item.get("task"),
                    "lane": item.get("lane"),
                    "scope": item.get("scope"),
                    "depends_on": sorted(item.get("depends_on", [])),
                    "decisions": sorted(item.get("decisions", [])),
                    "open": sorted(item.get("open", [])),
                    "acceptance": sorted(item.get("acceptance", [])),
                    "acceptance_notes": sorted(item.get("acceptance_notes", [])),
                    "screens": sorted(item.get("screens", [])),
                    "issues": sorted(item.get("issues", [])),
                    "critical_analysis": item.get("critical_analysis"),
                    "manifest_task": item.get("manifest_task"),
                    "manifest_status": item.get("manifest_status"),
                    "manifest_depends_on": item.get("manifest_depends_on"),
                    "manifest_blocked_by": item.get("manifest_blocked_by"),
                    "execution_state": item.get("execution_state"),
                    "protocol_gate": item.get("protocol_gate"),
                    "production_gate": item.get("production_gate"),
                    "decision_only": item.get("decision_only", False),
                }
        return hashlib.sha256(
            json.dumps(
                mapping,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            ).encode("utf-8")
        ).hexdigest()

    @staticmethod
    def _read_json(path: Path) -> Any:
        return json.loads(path.read_text(encoding="utf-8"))

    @staticmethod
    def _write_json(path: Path, value: Any) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(value, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    @staticmethod
    def _write_text(path: Path, value: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
