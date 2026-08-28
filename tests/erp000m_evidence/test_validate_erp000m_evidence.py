from __future__ import annotations

import json
import re
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any


MODULE_DIRECTORY = Path(__file__).resolve().parent
if str(MODULE_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(MODULE_DIRECTORY))

from validate_erp000m_evidence import (  # noqa: E402
    DOCS_RELATIVE_ROOT,
    EVIDENCE_TEMPLATE,
    ENVIRONMENT_TEMPLATE,
    README_TEMPLATE,
    WORKLOAD_TEMPLATE,
    validate_repository,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class Erp000mEvidenceTemplateValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        destination = self.root / DOCS_RELATIVE_ROOT
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(REPOSITORY_ROOT / DOCS_RELATIVE_ROOT, destination)

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_repository_templates_are_valid(self) -> None:
        self.assertEqual([], validate_repository(self.root))

    def test_duplicate_json_key_is_rejected(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        duplicate = (
            '"owner": "__REQUIRED_INPUT__:OWNER__",\n'
            '    "owner": "__REQUIRED_INPUT__:OWNER__",'
        )
        path.write_text(
            raw.replace('"owner": "__REQUIRED_INPUT__:OWNER__",', duplicate, 1),
            encoding="utf-8",
        )

        self.assert_error_contains("duplicate key 'owner'")

    def test_non_finite_json_numbers_are_rejected(self) -> None:
        for non_finite in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(non_finite=non_finite):
                self._restore_templates()
                path = self.root / WORKLOAD_TEMPLATE
                raw = path.read_text(encoding="utf-8")
                path.write_text(
                    raw.replace('"__REQUIRED_INPUT__:OWNER__"', non_finite, 1),
                    encoding="utf-8",
                )
                self.assert_error_contains("non-finite JSON number")

    def test_candidate_decision_fields_reject_concrete_defaults(self) -> None:
        cases = {
            "provider": "fixture-provider",
            "edition": "fixture-edition",
            "collation": "fixture-collation",
            "isolation_level": "fixture-isolation",
            "topology": "fixture-topology",
        }
        for field, value in cases.items():
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(WORKLOAD_TEMPLATE)
                payload["candidate_inputs"][field] = value
                self._write_json(WORKLOAD_TEMPLATE, payload)
                self.assert_error_contains(
                    f"candidate_inputs.{field}: forbidden concrete default"
                )

    def test_workload_numeric_values_are_rejected(self) -> None:
        cases: list[tuple[tuple[Any, ...], Any, str]] = [
            (("dataset", "volume_classes", 0, "record_count"), 100, "record_count"),
            (
                ("workload", "concurrency", "concurrent_actor_count"),
                10,
                "concurrent_actor_count",
            ),
            (
                ("workload", "operation_classes", 0, "mix_share"),
                "50%",
                "mix_share",
            ),
            (
                ("performance_acceptance_inputs", 0, "threshold"),
                "300 ms",
                "threshold",
            ),
        ]
        for path_parts, value, field in cases:
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(WORKLOAD_TEMPLATE)
                self._set_nested(payload, path_parts, value)
                self._write_json(WORKLOAD_TEMPLATE, payload)
                self.assert_error_contains("forbidden concrete default")

    def test_measurement_values_and_thresholds_have_no_defaults(self) -> None:
        for field, value in (("measured_value", 1), ("threshold", "1 second")):
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(EVIDENCE_TEMPLATE)
                payload["measurements"][0][field] = value
                self._write_json(EVIDENCE_TEMPLATE, payload)
                self.assert_error_contains(
                    f"measurements.[0].{field}: forbidden concrete default"
                )

    def test_required_metadata_cannot_be_removed(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        del payload["metadata"]["source_commit"]
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains("metadata: missing required key 'source_commit'")

    def test_tool_version_rows_cannot_be_empty(self) -> None:
        payload = self._read_json(EVIDENCE_TEMPLATE)
        payload["metadata"]["tool_versions"] = []
        self._write_json(EVIDENCE_TEMPLATE, payload)

        self.assert_error_contains(
            "metadata.tool_versions: template array must contain exactly 1 row"
        )

    def test_seed_hash_and_reproduction_fields_are_required(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        del payload["dataset"]["generator"]["seed"]
        del payload["dataset"]["generator"]["dataset_hash_sha256"]
        del payload["dataset"]["generator"]["reproduction_instructions_ref"]
        self._write_json(WORKLOAD_TEMPLATE, payload)

        errors = validate_repository(self.root)
        self.assertTrue(any("missing required key 'seed'" in error for error in errors))
        self.assertTrue(
            any("missing required key 'dataset_hash_sha256'" in error for error in errors)
        )
        self.assertTrue(
            any(
                "missing required key 'reproduction_instructions_ref'" in error
                for error in errors
            )
        )

    def test_unknown_sensitive_shaped_key_is_rejected(self) -> None:
        payload = self._read_json(EVIDENCE_TEMPLATE)
        sensitive_key = "pass" + "word"
        payload["metadata"][sensitive_key] = "__REQUIRED_INPUT__:FORBIDDEN__"
        self._write_json(EVIDENCE_TEMPLATE, payload)

        self.assert_error_contains("sensitive-looking key is forbidden")

    def test_sensitive_shaped_values_are_rejected(self) -> None:
        shaped_values = (
            "gh" + "p_" + ("A" * 32),
            "Server=fixture;" + "Pass" + "word=not-a-real-value;",
            "-----BEGIN " + "PRIVATE KEY-----",
        )
        for shaped_value in shaped_values:
            with self.subTest(value_prefix=shaped_value[:8]):
                self._restore_templates()
                payload = self._read_json(EVIDENCE_TEMPLATE)
                payload["metadata"]["owner"] = shaped_value
                self._write_json(EVIDENCE_TEMPLATE, payload)
                self.assert_error_contains("sensitive-looking value is forbidden")

    def test_invalid_placeholder_marker_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["metadata"]["owner"] = "<OWNER>"
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains("metadata.owner: expected exact placeholder")

    def test_provider_placeholder_swap_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["candidate_inputs"]["provider"] = "__REQUIRED_INPUT__:ADR-003:EDITION__"
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains(
            "candidate_inputs.provider: forbidden concrete default; value must remain "
            "an exact placeholder"
        )

    def test_threshold_placeholder_swap_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["performance_acceptance_inputs"][0]["threshold"] = (
            "__REQUIRED_INPUT__:OPEN-015:THRESHOLD_UNIT__"
        )
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains(
            "performance_acceptance_inputs.[0].threshold: forbidden concrete default"
        )

    def test_hash_placeholder_swap_breaks_exact_and_cross_template_invariants(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["evidence"]["workload_profile_hash_sha256"] = (
            "__REQUIRED_INPUT__:DATASET_HASH_SHA256__"
        )
        self._write_json(WORKLOAD_TEMPLATE, payload)

        errors = validate_repository(self.root)
        self.assertTrue(
            any(
                "evidence.workload_profile_hash_sha256: expected exact placeholder"
                in error
                for error in errors
            )
        )
        self.assertTrue(
            any("cross-template invariant 'workload_profile_hash' mismatch" in error for error in errors)
        )

    def test_environment_inventory_requires_every_field(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        line = (
            "| source_commit | __REQUIRED_INPUT__:SOURCE_COMMIT__ | "
            "__REQUIRED_INPUT__:COMMIT_EVIDENCE_REF__ |\n"
        )
        path.write_text(raw.replace(line, ""), encoding="utf-8")

        self.assert_error_contains("missing required field 'source_commit'")

    def test_environment_row_hidden_in_html_comment_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        row = (
            "| owner | __REQUIRED_INPUT__:OWNER__ | "
            "__REQUIRED_INPUT__:OWNER_EVIDENCE_REF__ |"
        )
        path.write_text(raw.replace(row, f"<!--\n{row}\n-->", 1), encoding="utf-8")

        self.assert_error_contains("table rows hidden in HTML comments are forbidden")

    def test_environment_row_in_wrong_table_context_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        row = (
            "| provider_candidate | __REQUIRED_INPUT__:ADR-003:PROVIDER__ | "
            "__REQUIRED_INPUT__:PROVIDER_EVIDENCE_REF__ |\n"
        )
        raw = raw.replace(row, "", 1)
        insertion = "## Kimlik ve güven sınırları\n\n"
        path.write_text(raw.replace(insertion, insertion + row, 1), encoding="utf-8")

        self.assert_error_contains("canonical table structure mismatch")

    def test_environment_tables_hidden_in_markdown_fences_are_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        table_pattern = re.compile(
            r"(^\| Field ID \| Value \| Evidence reference \|\n"
            r"^\|---\|---\|---\|\n(?:^\|.*\|\n)+)",
            flags=re.MULTILINE,
        )
        fenced, replacement_count = table_pattern.subn(
            lambda match: f"```text\n{match.group(1)}```\n",
            raw,
        )
        self.assertEqual(5, replacement_count)
        path.write_text(fenced, encoding="utf-8")

        self.assert_error_contains("table rows hidden in Markdown fences are forbidden")

    def test_environment_table_header_removal_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace("| Field ID | Value | Evidence reference |\n", "", 1),
            encoding="utf-8",
        )

        self.assert_error_contains("canonical table structure mismatch")

    def test_duplicate_environment_headers_and_separators_are_rejected(self) -> None:
        mutations = (
            (
                "| Field ID | Value | Evidence reference |\n",
                "| Field ID | Value | Evidence reference |\n"
                "| Field ID | Value | Evidence reference |\n",
            ),
            (
                "|---|---|---|\n",
                "|---|---|---|\n|---|---|---|\n",
            ),
        )
        for original, replacement in mutations:
            with self.subTest(original=original.strip()):
                self._restore_templates()
                path = self.root / ENVIRONMENT_TEMPLATE
                raw = path.read_text(encoding="utf-8")
                path.write_text(
                    raw.replace(original, replacement, 1),
                    encoding="utf-8",
                )
                self.assert_error_contains("canonical table structure mismatch")

    def test_environment_canonical_hash_rejects_benign_drift(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical environment hash drift")

    def test_environment_decision_fields_reject_concrete_values(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        raw = raw.replace(
            "| topology | __REQUIRED_INPUT__:OPEN-017:TOPOLOGY__ |",
            "| topology | fixture-topology |",
        )
        path.write_text(raw, encoding="utf-8")

        self.assert_error_contains("topology: forbidden concrete default")

    def test_environment_evidence_reference_must_be_placeholder(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        raw = raw.replace(
            "__REQUIRED_INPUT__:OWNER_EVIDENCE_REF__",
            "docs/local-owner-note.txt",
            1,
        )
        path.write_text(raw, encoding="utf-8")

        self.assert_error_contains(
            "owner: evidence reference must remain exact placeholder"
        )

    def test_sensitive_value_in_environment_prose_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        shaped_value = "\nproof: " + "pass" + "word=not-a-real-value\n"
        path.write_text(
            path.read_text(encoding="utf-8") + shaped_value,
            encoding="utf-8",
        )

        self.assert_error_contains("sensitive-looking raw content is forbidden")

    def test_sensitive_key_names_in_prose_are_rejected(self) -> None:
        shaped_names = (
            "sec" + "ret",
            "to" + "ken",
            "creden" + "tial",
            "connection" + "_string",
        )
        for shaped_name in shaped_names:
            with self.subTest(shaped_name=shaped_name):
                self._restore_templates()
                path = self.root / ENVIRONMENT_TEMPLATE
                path.write_text(
                    path.read_text(encoding="utf-8")
                    + f"\n{shaped_name}: REDACTED\n",
                    encoding="utf-8",
                )
                self.assert_error_contains(
                    "sensitive-looking raw key/value name is forbidden"
                )

    def test_sas_like_signature_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        shaped_query = "?sv=future&" + "sig=" + ("A" * 32)
        path.write_text(
            path.read_text(encoding="utf-8") + f"\n{shaped_query}\n",
            encoding="utf-8",
        )

        self.assert_error_contains("sensitive-looking raw content is forbidden")

    def test_decision_assignment_drift_shapes_are_rejected(self) -> None:
        fields = (
            "provider",
            "edition",
            "collation",
            "isolation_level",
            "topology",
            "threshold",
        )
        for field in fields:
            with self.subTest(field=field):
                self._restore_templates()
                path = self.root / README_TEMPLATE
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{field}=fixture-value\n",
                    encoding="utf-8",
                )
                self.assert_error_contains(
                    "decision/default assignment drift is forbidden"
                )

    def test_closed_gate_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\ngates=CLOSED\n",
            encoding="utf-8",
        )

        self.assert_error_contains("closed-gate drift is forbidden")

    def test_implementation_go_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace(
                "Implementation state: `NO-GO / BLOCKED`",
                "Implementation state: `GO`",
                1,
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("implementation GO drift is forbidden")

    def test_readme_no_go_gate_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace(
                "- Implementation state: `NO-GO / BLOCKED`",
                "- Implementation state: `GO`",
                1,
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("required decision-boundary line drifted")

    def test_readme_provider_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        provider_name = "SQL" + " Server"
        path.write_text(
            path.read_text(encoding="utf-8") + f"\nChosen provider: {provider_name}\n",
            encoding="utf-8",
        )

        self.assert_error_contains("forbidden concrete default detected")

    def test_readme_benign_hash_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical README hash drift")

    def test_extra_sensitive_file_is_rejected_and_scanned(self) -> None:
        path = self.root / DOCS_RELATIVE_ROOT / "unreviewed-input.txt"
        shaped_value = "gh" + "p_" + ("A" * 32)
        path.write_text(shaped_value, encoding="utf-8")

        errors = validate_repository(self.root)
        self.assertTrue(any("unreviewed file is forbidden" in error for error in errors))
        self.assertTrue(
            any("sensitive-looking raw content is forbidden" in error for error in errors)
        )

    def assert_error_contains(self, expected: str) -> None:
        errors = validate_repository(self.root)
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected an error containing {expected!r}, got: {errors!r}",
        )

    def _restore_templates(self) -> None:
        destination = self.root / DOCS_RELATIVE_ROOT
        shutil.rmtree(destination)
        shutil.copytree(REPOSITORY_ROOT / DOCS_RELATIVE_ROOT, destination)

    def _read_json(self, relative_path: Path) -> dict[str, Any]:
        return json.loads((self.root / relative_path).read_text(encoding="utf-8"))

    def _write_json(self, relative_path: Path, payload: dict[str, Any]) -> None:
        (self.root / relative_path).write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    @staticmethod
    def _set_nested(payload: Any, path_parts: tuple[Any, ...], value: Any) -> None:
        target = payload
        for part in path_parts[:-1]:
            target = target[part]
        target[path_parts[-1]] = value


if __name__ == "__main__":
    unittest.main()
