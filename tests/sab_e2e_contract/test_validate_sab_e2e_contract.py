import json
import pathlib
import tempfile
import unittest
import xml.etree.ElementTree as et

import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_sab_e2e_contract as validator  # noqa: E402


def write_complete_source(root: pathlib.Path) -> None:
    package = {
        "scripts": {"sab:ci": validator.EXPECTED_SCRIPT},
        "devDependencies": {"@playwright/test": "1.62.1"},
    }
    lockfile = {
        "packages": {
            "": {"devDependencies": {"@playwright/test": "1.62.1"}},
            "node_modules/@playwright/test": {"version": "1.62.1"},
            "node_modules/playwright-core": {"version": "1.62.1"},
        }
    }
    files = {
        validator.PACKAGE_PATH: json.dumps(package),
        validator.LOCKFILE_PATH: json.dumps(lockfile),
        validator.CONFIG_PATH: "export default {}",
        validator.SPEC_PATH: "// SAB-006 SAB-007 SAB-008 SAB-009",
    }
    for relative, content in files.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


def junit_xml(
    case_ids=tuple(sorted(validator.EXPECTED_CASES)),
    *,
    root_counters=(0, 0, 0),
    suite_counters=(0, 0, 0),
    child_outcome=None,
) -> str:
    root_failures, root_errors, root_skipped = root_counters
    suite_failures, suite_errors, suite_skipped = suite_counters
    cases = []
    for index, case_id in enumerate(case_ids):
        child = f"<{child_outcome} />" if child_outcome and index == 0 else ""
        cases.append(f'<testcase name="{case_id} browser acceptance">{child}</testcase>')
    return (
        f'<testsuites tests="{len(case_ids)}" failures="{root_failures}" '
        f'errors="{root_errors}" skipped="{root_skipped}">'
        f'<testsuite tests="{len(case_ids)}" failures="{suite_failures}" '
        f'errors="{suite_errors}" skipped="{suite_skipped}">'
        f'{"".join(cases)}</testsuite></testsuites>'
    )


class PreflightContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_absent_source_fails_closed(self):
        with self.assertRaisesRegex(validator.ContractError, "SAB_SOURCE_REQUIRED"):
            validator.validate_preflight(self.root)

    def test_partial_source_fails_closed(self):
        config = self.root / validator.CONFIG_PATH
        config.parent.mkdir(parents=True)
        config.write_text("export default {}", encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "SAB_SOURCE_REQUIRED"):
            validator.validate_preflight(self.root)

    def test_complete_source_enables_gate(self):
        write_complete_source(self.root)
        result = validator.validate_preflight(self.root)
        self.assertTrue(result.enabled)
        self.assertEqual(result.reason, "source-present-and-complete")

    def test_missing_package_script_fails_closed(self):
        write_complete_source(self.root)
        package_path = self.root / validator.PACKAGE_PATH
        package = json.loads(package_path.read_text(encoding="utf-8"))
        package["scripts"].pop("sab:ci")
        package_path.write_text(json.dumps(package), encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "SAB_SCRIPT_MISMATCH"):
            validator.validate_preflight(self.root)

    def test_floating_playwright_version_fails_closed(self):
        write_complete_source(self.root)
        package_path = self.root / validator.PACKAGE_PATH
        package = json.loads(package_path.read_text(encoding="utf-8"))
        package["devDependencies"]["@playwright/test"] = "^1.62.1"
        package_path.write_text(json.dumps(package), encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "NOT_PINNED"):
            validator.validate_preflight(self.root)

    def test_lockfile_version_drift_fails_closed(self):
        write_complete_source(self.root)
        lock_path = self.root / validator.LOCKFILE_PATH
        lockfile = json.loads(lock_path.read_text(encoding="utf-8"))
        lockfile["packages"]["node_modules/playwright-core"]["version"] = "1.62.0"
        lock_path.write_text(json.dumps(lockfile), encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "LOCK_DRIFT"):
            validator.validate_preflight(self.root)


class EvidenceContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.junit = self.root / "sab-junit.xml"
        self.html = self.root / "sab-html-report" / "index.html"
        self.html.parent.mkdir(parents=True)
        self.html.write_text("<html>SAB acceptance</html>", encoding="utf-8")
        self.junit.write_text(junit_xml(), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def validate(self):
        validator.validate_evidence(self.junit, self.html)

    def test_exact_clean_case_set_and_html_pass(self):
        self.validate()

    def test_duplicate_case_id_fails(self):
        self.junit.write_text(
            junit_xml(("SAB-006", "SAB-006", "SAB-008", "SAB-009")),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "CASE_SET"):
            self.validate()

    def test_unexpected_case_id_fails(self):
        self.junit.write_text(
            junit_xml(("SAB-006", "SAB-007", "SAB-008", "SAB-010")),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "CASE_SET"):
            self.validate()

    def test_unlabelled_case_fails(self):
        self.junit.write_text(
            junit_xml(("SAB-006", "SAB-007", "SAB-008", "browser-only")),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "CASE_ID"):
            self.validate()

    def test_embedded_case_id_fails(self):
        self.junit.write_text(
            junit_xml(
                (
                    "SAB-006",
                    "SAB-007",
                    "SAB-008",
                    "unrelated SAB-009 text",
                )
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "CASE_ID"):
            self.validate()

    def test_second_case_id_in_one_name_fails(self):
        self.junit.write_text(
            junit_xml(
                (
                    "SAB-006",
                    "SAB-007",
                    "SAB-008",
                    "SAB-009 also claims SAB-010",
                )
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "exactly one SAB ID"):
            self.validate()

    def test_nonzero_root_or_suite_counter_fails(self):
        for level in ("root", "suite"):
            for index, counter in enumerate(("failures", "errors", "skipped")):
                with self.subTest(level=level, counter=counter):
                    values = [0, 0, 0]
                    values[index] = 1
                    options = {f"{level}_counters": tuple(values)}
                    self.junit.write_text(junit_xml(**options), encoding="utf-8")
                    with self.assertRaisesRegex(validator.ContractError, "NOT_CLEAN"):
                        self.validate()

    def test_failure_error_and_skip_elements_fail(self):
        for outcome in ("failure", "error", "skipped"):
            with self.subTest(outcome=outcome):
                self.junit.write_text(
                    junit_xml(child_outcome=outcome), encoding="utf-8"
                )
                with self.assertRaisesRegex(validator.ContractError, "NOT_CLEAN"):
                    self.validate()

    def test_suite_level_outcome_element_fails(self):
        self.junit.write_text(
            junit_xml().replace("</testsuite>", "<failure /></testsuite>"),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "NOT_CLEAN"):
            self.validate()

    def test_missing_counter_fails(self):
        self.junit.write_text(
            junit_xml().replace(' errors="0"', "", 1), encoding="utf-8"
        )
        with self.assertRaisesRegex(validator.ContractError, "COUNTER_MISSING"):
            self.validate()

    def test_missing_and_empty_html_fail(self):
        self.html.unlink()
        with self.assertRaisesRegex(validator.ContractError, "HTML_MISSING"):
            self.validate()
        self.html.write_bytes(b"")
        with self.assertRaisesRegex(validator.ContractError, "HTML_EMPTY"):
            self.validate()

    def test_malformed_xml_fails(self):
        self.junit.write_text("<testsuites>", encoding="utf-8")
        with self.assertRaises(et.ParseError):
            self.validate()


if __name__ == "__main__":
    unittest.main()
