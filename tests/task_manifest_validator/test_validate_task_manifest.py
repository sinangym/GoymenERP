from __future__ import annotations

import importlib.util
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from typing import Any
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_task_manifest.py"
MODULE_NAME = "goymen_task_manifest_validator"
MODULE_SPEC = importlib.util.spec_from_file_location(MODULE_NAME, VALIDATOR_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load task manifest validator from {VALIDATOR_PATH}")
VALIDATOR_MODULE = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_NAME] = VALIDATOR_MODULE
MODULE_SPEC.loader.exec_module(VALIDATOR_MODULE)

main = VALIDATOR_MODULE.main
validate_repository = VALIDATOR_MODULE.validate_repository
validate_repository_with_statistics = (
    VALIDATOR_MODULE.validate_repository_with_statistics
)


class TaskManifestValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        self.manifest_path = self.root / "docs/backlog/task_manifest.json"
        self.manifest_path.parent.mkdir(parents=True)
        self._write_manifest(
            {
                "version": "1.0",
                "tasks": [
                    {"id": "ERP-001", "depends_on": []},
                    {"id": "ERP-002", "depends_on": ["ERP-001"]},
                    {"id": "ERP-003", "depends_on": ["ERP-001", "ERP-002"]},
                ],
            }
        )

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_valid_manifest_passes(self) -> None:
        self.assertEqual([], validate_repository(self.root))

    def test_current_repository_manifest_matches_supported_structure(self) -> None:
        errors, statistics = validate_repository_with_statistics(REPOSITORY_ROOT)

        self.assertEqual([], errors)
        self.assertIsNotNone(statistics)

    def test_additional_task_fields_do_not_create_business_policy(self) -> None:
        self._write_manifest(
            {
                "version": "1.0",
                "tasks": [
                    {
                        "id": "ERP-001",
                        "depends_on": [],
                        "status": "custom-status-not-validated-here",
                        "blocked_by": ["OPEN-999"],
                        "scope": "The structural validator does not interpret policy.",
                    }
                ],
            }
        )

        self.assertEqual([], validate_repository(self.root))

    def test_missing_manifest_is_rejected(self) -> None:
        self.manifest_path.unlink()

        self.assert_error_contains("Missing task manifest")

    def test_invalid_json_is_rejected(self) -> None:
        self.manifest_path.write_text('{"tasks": [', encoding="utf-8")

        self.assert_error_contains("Invalid JSON in task manifest")

    def test_duplicate_json_object_key_is_rejected(self) -> None:
        self.manifest_path.write_text(
            '{"tasks":[{"id":"ERP-001","depends_on":["ERP-404"],'
            '"depends_on":[]}]}' + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("Duplicate JSON object key: 'depends_on'")

    def test_non_standard_numeric_constants_are_rejected(self) -> None:
        for constant in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(constant=constant):
                self.manifest_path.write_text(
                    '{"tasks":[{"id":"ERP-001","depends_on":[],'
                    f'"priority":{constant}'
                    '}]}' + "\n",
                    encoding="utf-8",
                )

                self.assert_error_contains(
                    f"Non-standard JSON numeric constant: {constant}"
                )

    def test_root_must_be_an_object(self) -> None:
        self._write_manifest([])

        self.assert_error_contains("root must be a JSON object")

    def test_tasks_must_be_an_array(self) -> None:
        self._write_manifest({"tasks": {}})

        self.assert_error_contains("field 'tasks' must be a JSON array")

    def test_tasks_must_not_be_empty(self) -> None:
        self._write_manifest({"tasks": []})

        self.assert_error_contains("field 'tasks' must not be empty")

    def test_task_must_be_an_object(self) -> None:
        self._write_manifest({"tasks": ["ERP-001"]})

        self.assert_error_contains("Task at index 0 must be a JSON object")

    def test_task_id_must_be_a_trimmed_non_empty_string(self) -> None:
        self._write_manifest({"tasks": [{"id": " ERP-001 ", "depends_on": []}]})

        self.assert_error_contains("field 'id' must be a non-empty trimmed string")

    def test_duplicate_task_id_is_rejected(self) -> None:
        self._write_manifest(
            {
                "tasks": [
                    {"id": "ERP-001", "depends_on": []},
                    {"id": "ERP-001", "depends_on": []},
                ]
            }
        )

        self.assert_error_contains("Duplicate task id: 'ERP-001'")

    def test_depends_on_must_be_an_array(self) -> None:
        self._write_manifest(
            {"tasks": [{"id": "ERP-001", "depends_on": "ERP-000"}]}
        )

        self.assert_error_contains("field 'depends_on' must be a JSON array")

    def test_dependency_must_be_a_trimmed_non_empty_string(self) -> None:
        self._write_manifest({"tasks": [{"id": "ERP-001", "depends_on": [""]}]})

        self.assert_error_contains(
            "dependency at index 0 must be a non-empty trimmed string"
        )

    def test_duplicate_dependency_is_rejected(self) -> None:
        self._write_manifest(
            {
                "tasks": [
                    {"id": "ERP-001", "depends_on": []},
                    {
                        "id": "ERP-002",
                        "depends_on": ["ERP-001", "ERP-001"],
                    },
                ]
            }
        )

        self.assert_error_contains("has duplicate dependency 'ERP-001'")

    def test_unknown_dependency_is_rejected(self) -> None:
        self._write_manifest(
            {"tasks": [{"id": "ERP-001", "depends_on": ["ERP-404"]}]}
        )

        self.assert_error_contains("depends on unknown task 'ERP-404'")

    def test_self_dependency_is_rejected(self) -> None:
        self._write_manifest(
            {"tasks": [{"id": "ERP-001", "depends_on": ["ERP-001"]}]}
        )

        errors = validate_repository(self.root)
        self.assertTrue(
            any("cannot depend on itself" in error for error in errors),
            errors,
        )
        self.assertTrue(
            any(
                "Dependency cycle detected: ERP-001 -> ERP-001" in error
                for error in errors
            ),
            errors,
        )

    def test_multi_task_cycle_is_rejected_with_canonical_path(self) -> None:
        self._write_manifest(
            {
                "tasks": [
                    {"id": "ERP-002", "depends_on": ["ERP-003"]},
                    {"id": "ERP-003", "depends_on": ["ERP-001"]},
                    {"id": "ERP-001", "depends_on": ["ERP-002"]},
                ]
            }
        )

        self.assert_error_contains(
            "Dependency cycle detected: ERP-001 -> ERP-002 -> ERP-003 -> ERP-001"
        )

    def test_deep_acyclic_chain_is_validated_without_recursion_failure(self) -> None:
        tasks = [{"id": "ERP-0000", "depends_on": []}]
        for index in range(1, 2500):
            tasks.append(
                {
                    "id": f"ERP-{index:04d}",
                    "depends_on": [f"ERP-{index - 1:04d}"],
                }
            )
        self._write_manifest({"tasks": tasks})

        self.assertEqual([], validate_repository(self.root))

    def test_cli_reads_once_and_reports_validated_snapshot_statistics(self) -> None:
        original_read_text = Path.read_text
        manifest_reads = 0

        def read_text_once(path: Path, *args: Any, **kwargs: Any) -> str:
            nonlocal manifest_reads
            if path.resolve() == self.manifest_path.resolve():
                manifest_reads += 1
                if manifest_reads > 1:
                    raise AssertionError("Manifest was read more than once")
            return original_read_text(path, *args, **kwargs)

        output = io.StringIO()
        with mock.patch.object(Path, "read_text", new=read_text_once):
            with redirect_stdout(output):
                exit_code = main(["--root", str(self.root)])

        self.assertEqual(0, exit_code)
        self.assertEqual(1, manifest_reads)
        self.assertIn("- tasks: 3", output.getvalue())
        self.assertIn("- dependency edges: 3", output.getvalue())

    def assert_error_contains(self, expected: str) -> None:
        errors = validate_repository(self.root)
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected {expected!r} in {errors!r}",
        )

    def _write_manifest(self, manifest: Any) -> None:
        self.manifest_path.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
