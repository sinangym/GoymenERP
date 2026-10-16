import contextlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
VALIDATOR_PATH = ROOT / "tools" / "validate_browser_layout_diagnostic.py"
SPEC = importlib.util.spec_from_file_location("validate_browser_layout_diagnostic", VALIDATOR_PATH)
assert SPEC is not None and SPEC.loader is not None
validator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)


def valid_record() -> dict:
    return {
        "schemaVersion": "browser-layout-diagnostic-v1",
        "suite": "r14",
        "caseId": "E05-R14-LONG-LABEL-320",
        "testedCommit": "a" * 40,
        "selectorAlias": "stock-owner-label",
        "boundaryClass": "long-wide-label",
        "viewport": {"widthCssPx": 320, "heightCssPx": 844},
        "page": {"clientWidthCssPx": 320, "scrollWidthCssPx": 320},
        "target": {
            "clientWidthCssPx": 288,
            "scrollWidthCssPx": 612,
            "present": True,
            "visible": True,
        },
        "seed": {
            "fixtureId": "e05-wide-label-v1",
            "labelCharacterCount": 96,
            "wideCharacterCount": 96,
        },
    }


class ContractTests(unittest.TestCase):
    def test_320px_long_wide_label_measurement_passes(self):
        validator.validate(valid_record())

    def test_schema_is_closed_and_pins_the_contract_version(self):
        schema = json.loads(
            (ROOT / "contracts/browser/safe-layout-diagnostic-v1.schema.json").read_text(encoding="utf-8")
        )
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual("browser-layout-diagnostic-v1", schema["properties"]["schemaVersion"]["const"])
        self.assertEqual("r14", schema["properties"]["suite"]["const"])
        self.assertEqual("stock-owner-label", schema["properties"]["selectorAlias"]["const"])
        self.assertEqual(320, schema["$defs"]["viewport"]["properties"]["widthCssPx"]["const"])
        self.assertEqual(80, schema["properties"]["seed"]["properties"]["wideCharacterCount"]["minimum"])
        self.assertEqual(set(validator.TOP_LEVEL_KEYS), set(schema["required"]))

    def test_raw_or_unreviewed_channels_have_no_property(self):
        for key in validator.FORBIDDEN_CHANNELS:
            with self.subTest(key=key):
                record = valid_record()
                record[key] = "not-admitted"
                with self.assertRaisesRegex(validator.ContractError, "property set is not exact"):
                    validator.validate(record)

    def test_raw_css_selector_is_refused(self):
        for selector in ('[data-testid="owner"]', '#owner', '.owner', 'owner label'):
            with self.subTest(selector=selector):
                record = valid_record()
                record["selectorAlias"] = selector
                with self.assertRaisesRegex(validator.ContractError, "reviewed E05"):
                    validator.validate(record)

    def test_scope_is_only_r14_and_an_e05_synthetic_fixture(self):
        record = valid_record()
        record["suite"] = "c01"
        with self.assertRaisesRegex(validator.ContractError, "reviewed R14"):
            validator.validate(record)

        record = valid_record()
        record["seed"]["fixtureId"] = "real-company-product"
        with self.assertRaisesRegex(validator.ContractError, "reviewed E05"):
            validator.validate(record)

    def test_long_label_boundary_is_not_satisfied_by_a_short_or_hidden_target(self):
        for mutation in (
            lambda record: record["viewport"].update(widthCssPx=390),
            lambda record: record["seed"].update(labelCharacterCount=79, wideCharacterCount=79),
            lambda record: record["target"].update(visible=False),
        ):
            record = valid_record()
            mutation(record)
            with self.assertRaises(validator.ContractError):
                validator.validate(record)

    def test_widths_are_bounded_and_self_consistent(self):
        record = valid_record()
        record["target"]["scrollWidthCssPx"] = 200
        with self.assertRaisesRegex(validator.ContractError, "scroll width"):
            validator.validate(record)

        record = valid_record()
        record["page"]["clientWidthCssPx"] = 321
        record["page"]["scrollWidthCssPx"] = 321
        with self.assertRaisesRegex(validator.ContractError, "exceeds the measured viewport"):
            validator.validate(record)

    def test_duplicate_properties_non_finite_numbers_and_oversized_inputs_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "diagnostic.json"
            serialized = json.dumps(valid_record())
            path.write_text(serialized.replace('"suite": "r14"', '"suite": "r14", "suite": "r14"'), encoding="utf-8")
            with self.assertRaisesRegex(validator.ContractError, "duplicate property"):
                validator.read_and_validate(path)

            path.write_text(serialized.replace("612", "NaN"), encoding="utf-8")
            with self.assertRaisesRegex(validator.ContractError, "non-finite"):
                validator.read_and_validate(path)

            path.write_bytes(b" " * (validator.MAX_INPUT_BYTES + 1))
            with self.assertRaisesRegex(validator.ContractError, "input size"):
                validator.read_and_validate(path)

    def test_cli_does_not_echo_a_rejected_secret(self):
        secret = "runtime-secret-that-must-not-leak"
        record = valid_record()
        record["selectorAlias"] = secret
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "diagnostic.json"
            path.write_text(json.dumps(record), encoding="utf-8")
            stdout = io.StringIO()
            stderr = io.StringIO()
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = validator.main([
                    "--input", str(path),
                    "--forbidden-value", secret,
                ])
        self.assertEqual(1, result)
        self.assertNotIn(secret, stdout.getvalue())
        self.assertNotIn(secret, stderr.getvalue())

    def test_cli_accepts_the_safe_record(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "diagnostic.json"
            path.write_text(json.dumps(valid_record()), encoding="utf-8")
            stdout = io.StringIO()
            stderr = io.StringIO()
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = validator.main(["--input", str(path)])
        self.assertEqual(0, result)
        self.assertIn("validation passed", stdout.getvalue())
        self.assertEqual("", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
