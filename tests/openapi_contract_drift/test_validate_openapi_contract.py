from __future__ import annotations

import ast
import importlib.util
import io
import json
import os
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_openapi_contract.py"
MODULE_NAME = "goymen_openapi_contract_validator"
MODULE_SPEC = importlib.util.spec_from_file_location(MODULE_NAME, VALIDATOR_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load OpenAPI contract validator from {VALIDATOR_PATH}")
VALIDATOR_MODULE = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_NAME] = VALIDATOR_MODULE
MODULE_SPEC.loader.exec_module(VALIDATOR_MODULE)

ContractValidationError = VALIDATOR_MODULE.ContractValidationError
compare_contracts = VALIDATOR_MODULE.compare_contracts
main = VALIDATOR_MODULE.main


def contract_bytes(marker: str = "same") -> bytes:
    return (
        json.dumps(
            {
                "openapi": "3.1.1",
                "info": {"title": "fixture", "version": "v1"},
                "paths": {},
                "marker": marker,
            },
            ensure_ascii=False,
            separators=(",", ":"),
        )
        + "\n"
    ).encode("utf-8")


class OpenApiContractValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.baseline = self.root / "baseline.json"
        self.generated = self.root / "generated.json"
        self.baseline.write_bytes(contract_bytes())
        self.generated.write_bytes(contract_bytes())

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def assert_rejected(self, expected: str) -> None:
        with self.assertRaisesRegex(ContractValidationError, expected):
            compare_contracts(self.baseline, self.generated)
        stderr = io.StringIO()
        with redirect_stderr(stderr):
            exit_code = main(
                [
                    "--baseline",
                    str(self.baseline),
                    "--generated",
                    str(self.generated),
                ]
            )
        self.assertNotEqual(0, exit_code)
        self.assertRegex(stderr.getvalue(), expected)

    def test_distinct_byte_identical_contracts_pass(self) -> None:
        snapshot = compare_contracts(self.baseline, self.generated)

        self.assertEqual(len(contract_bytes()), snapshot.size)
        self.assertEqual(64, len(snapshot.sha256))

    def test_valid_json_byte_mutations_fail_closed_without_echoing_content(self) -> None:
        secret_baseline = "baseline-secret-marker"
        secret_generated = "generated-secret-marker"
        self.baseline.write_bytes(contract_bytes(secret_baseline))
        self.generated.write_bytes(contract_bytes(secret_generated))
        stderr = io.StringIO()

        with redirect_stderr(stderr):
            exit_code = main(
                [
                    "--baseline",
                    str(self.baseline),
                    "--generated",
                    str(self.generated),
                ]
            )

        output = stderr.getvalue()
        self.assertEqual(2, exit_code)
        self.assertIn("OpenAPI contract mismatch", output)
        self.assertRegex(output, r"canonical sha256=[0-9a-f]{64} size=\d+")
        self.assertRegex(output, r"generated sha256=[0-9a-f]{64} size=\d+")
        for forbidden in (
            secret_baseline,
            secret_generated,
            str(self.baseline),
            str(self.generated),
            '"marker"',
        ):
            self.assertNotIn(forbidden, output)

    def test_semantically_equal_but_byte_different_contracts_are_rejected(self) -> None:
        document = json.loads(contract_bytes())
        self.generated.write_text(
            json.dumps(document, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

        self.assert_rejected("OpenAPI contract mismatch")

    def test_missing_contracts_are_rejected(self) -> None:
        for path, expected in (
            (self.baseline, "Canonical contract is missing"),
            (self.generated, "Generated contract is missing"),
        ):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.unlink()
                self.assert_rejected(expected)
                path.write_bytes(original)

    def test_empty_contracts_are_rejected(self) -> None:
        for path, expected in (
            (self.baseline, "Canonical contract must not be empty"),
            (self.generated, "Generated contract must not be empty"),
        ):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(b"")
                self.assert_rejected(expected)
                path.write_bytes(original)

    def test_invalid_json_and_encoding_are_rejected(self) -> None:
        invalid_documents = (
            b'{"openapi":',
            b'{"openapi":"3.1.1","openapi":"3.1.0"}',
            b'{"value":NaN}',
            b"[1,2,3]",
            b"\xff\xfe",
        )
        for path, label in (
            (self.baseline, "Canonical contract"),
            (self.generated, "Generated contract"),
        ):
            for index, invalid in enumerate(invalid_documents):
                with self.subTest(path=path.name, case=index):
                    original = path.read_bytes()
                    path.write_bytes(invalid)
                    self.assert_rejected(label)
                    path.write_bytes(original)

    def test_directories_are_rejected(self) -> None:
        self.baseline.unlink()
        self.baseline.mkdir()

        self.assert_rejected("Canonical contract must be a regular file")

    def test_same_path_and_hard_link_are_rejected(self) -> None:
        with self.assertRaisesRegex(
            ContractValidationError,
            "must be different files",
        ):
            compare_contracts(self.baseline, self.baseline)
        stderr = io.StringIO()
        with redirect_stderr(stderr):
            exit_code = main(
                [
                    "--baseline",
                    str(self.baseline),
                    "--generated",
                    str(self.baseline),
                ]
            )
        self.assertNotEqual(0, exit_code)
        self.assertIn("must be different files", stderr.getvalue())

        self.generated.unlink()
        os.link(self.baseline, self.generated)
        self.assert_rejected("must be different files")

    def test_file_symlinks_are_rejected_if_supported(self) -> None:
        self.generated.unlink()
        try:
            self.generated.symlink_to(self.baseline)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("symlink or reparse point")

    def test_symlinked_parent_is_rejected_if_supported(self) -> None:
        real_parent = self.root / "real-parent"
        real_parent.mkdir()
        target = real_parent / "generated.json"
        target.write_bytes(contract_bytes())
        linked_parent = self.root / "linked-parent"
        try:
            linked_parent.symlink_to(real_parent, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Directory symlinks are unavailable.")
        self.generated = linked_parent / "generated.json"

        self.assert_rejected("symlink or reparse point")

    def test_parent_directory_traversal_is_rejected(self) -> None:
        traversing = self.root / "child" / ".." / self.generated.name

        with self.assertRaisesRegex(
            ContractValidationError,
            "parent-directory traversal",
        ):
            compare_contracts(self.baseline, traversing)

    def test_cli_success_reports_only_hash_and_size(self) -> None:
        stdout = io.StringIO()
        stderr = io.StringIO()

        with redirect_stdout(stdout), redirect_stderr(stderr):
            exit_code = main(
                [
                    "--baseline",
                    str(self.baseline),
                    "--generated",
                    str(self.generated),
                ]
            )

        self.assertEqual(0, exit_code)
        self.assertEqual("", stderr.getvalue())
        self.assertRegex(stdout.getvalue(), r"sha256=[0-9a-f]{64} size=\d+")
        self.assertNotIn(str(self.baseline), stdout.getvalue())
        self.assertNotIn(str(self.generated), stdout.getvalue())

    def test_validator_uses_only_python_standard_library(self) -> None:
        allowed_roots = {
            "__future__",
            "argparse",
            "hashlib",
            "json",
            "math",
            "os",
            "stat",
            "sys",
            "pathlib",
            "typing",
        }
        tree = ast.parse(VALIDATOR_PATH.read_text(encoding="utf-8"))
        imported_roots: set[str] = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported_roots.update(alias.name.split(".", 1)[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imported_roots.add(node.module.split(".", 1)[0])

        self.assertEqual(set(), imported_roots - allowed_roots)


class ContractDriftWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = (
            REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml"
        ).read_text(encoding="utf-8")

    def job_body(self, job_name: str) -> str:
        match = re.search(
            rf"(?ms)^  {re.escape(job_name)}:\n(.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
            self.workflow,
        )
        self.assertIsNotNone(match, f"Missing workflow job: {job_name}")
        return match.group(1)

    def test_contract_drift_job_is_fail_closed_and_read_only(self) -> None:
        body = self.job_body("contract-drift")
        expected_steps = (
            "actions/checkout@v7",
            ".NET bootstrap and drift validator preflight",
            "Setup .NET 10",
            "Restore OpenAPI contract test packages",
            "Generate and verify deterministic OpenAPI contract",
            "Compare generated OpenAPI with canonical baseline",
        )
        indexes = [body.index(value) for value in expected_steps]

        self.assertEqual(indexes, sorted(indexes))
        self.assertIn("persist-credentials: false", body)
        self.assertIn(
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            body,
        )
        self.assertIn(
            "-s tests/openapi_contract_drift",
            body,
        )
        self.assertIn(
            "dotnet restore tests/GoymenERP.Api.IntegrationTests/"
            "GoymenERP.Api.IntegrationTests.csproj",
            body,
        )
        self.assertIn("-noAutoResponse", body)
        self.assertIn("--configfile NuGet.Config", body)
        self.assertIn(
            "--filter \"FullyQualifiedName~OpenApiContractGenerationTests\"",
            body,
        )
        self.assertIn("--no-restore", body)
        self.assertIn(
            "--baseline contracts/openapi/goymen-erp-v1.json",
            body,
        )
        self.assertIn("--generated artifacts/openapi.json", body)
        self.assertNotRegex(
            body,
            r"(?m)\b(?:cp|mv|install)\s+.*contracts/openapi|"
            r"contracts/openapi.*(?:>|tee)",
        )
        for forbidden in ("pull_request_target", "secrets.", "upload-artifact"):
            self.assertNotIn(forbidden, body)

    def test_workflow_has_repository_read_permission_only(self) -> None:
        permissions = re.search(
            r"(?ms)^permissions:\n(.*?)(?=^[A-Za-z0-9_-]+:|\Z)",
            self.workflow,
        )
        self.assertIsNotNone(permissions)
        self.assertEqual("contents: read", permissions.group(1).strip())

    def test_container_waits_for_contract_drift(self) -> None:
        container = self.job_body("containers")
        needs = re.search(
            r"(?ms)^    needs:\n(.*?)(?=^    [A-Za-z0-9_-]+:)",
            container,
        )
        self.assertIsNotNone(needs)
        dependencies = set(
            re.findall(r"^      - ([A-Za-z0-9_-]+)$", needs.group(1), re.M)
        )
        self.assertIn("contract-drift", dependencies)


if __name__ == "__main__":
    unittest.main()
