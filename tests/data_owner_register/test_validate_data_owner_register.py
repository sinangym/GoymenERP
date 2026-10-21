"""Synthetic contract counterexamples only; not human names or approval receipts.

The committed register stays PENDING. Test-only people, dates, source labels and
comment numbers below are invented locally and never saved to that register.
No test or CLI execution is part of this source-only delivery.
"""

from __future__ import annotations

import copy
from contextlib import redirect_stdout, redirect_stderr
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
TOOL_PATH = REPOSITORY_ROOT / "tools" / "validate_data_owner_register.py"


def _load_tool():
    spec = importlib.util.spec_from_file_location("data_owner_register_tool", TOOL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("tool cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


tool = _load_tool()
SCHEMA = tool.SCHEMA_SUBSET.load_strict_json(REPOSITORY_ROOT / tool.SCHEMA_PATH, "schema")
REGISTER = tool.SCHEMA_SUBSET.load_strict_json(REPOSITORY_ROOT / tool.REGISTER_PATH, "register")
NAMING_RECORD = "https://github.com/sinangym/GoymenERP/issues/66#issuecomment-1000000001"
CONFIRMATION_RECORD = "https://github.com/sinangym/GoymenERP/issues/66#issuecomment-1000000002"


def _gate(register: dict, gate_id: str) -> dict:
    return next(gate for gate in register["gates"] if gate["id"] == gate_id)


def _named(gate: dict) -> None:
    """Test-only people: invented for the test, never written to the register."""
    gate.update(
        status="RECORDED",
        primary={"name": "Deneme Birinci"},
        backup={"name": "Deneme İkinci"},
        sourceCustodian={"name": "Deneme Üçüncü", "sourceName": "Eski stok sistemi dışa aktarımı",
                         "sourceIdentityRoute": "dosya adı ve SHA-256 kayıt yeri"},
        targetDate="2026-11-30",
        decisionRecord=NAMING_RECORD,
    )


class RegisterTests(unittest.TestCase):
    def assertViolation(self, register: dict, fragment: str) -> None:
        with self.assertRaises((tool.RegisterViolation, tool.SCHEMA_SUBSET.SchemaViolation)) as raised:
            tool.validate_register(register, SCHEMA)
        self.assertIn(fragment, str(raised.exception))

    def test_the_committed_register_is_valid_and_every_gate_is_pending(self) -> None:
        summary = tool.validate_register(REGISTER, SCHEMA)
        self.assertEqual({"PENDING": 11, "RECORDED": 0, "CONFIRMED": 0}, summary["gateStatusCounts"])
        self.assertEqual(("NOT_CLOSED", "PENDING", False), (summary["e031"], summary["environmentOwners"],
                                                              summary["environmentActivationOwnersNamed"]))
        self.assertEqual(list(tool.GATE_ORDER), summary["pendingGates"])

    def test_no_gate_blocks_source_development(self) -> None:
        register = copy.deepcopy(REGISTER)
        _gate(register, "DATA-OPS")["blocksSourceDevelopment"] = True
        self.assertViolation(register, "const")

    def test_a_named_gate_needs_the_issue_record(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-LEGACY")
        _named(gate)
        gate["decisionRecord"] = None
        self.assertViolation(register, "without the Issue #66 record")

    def test_placeholders_and_agents_are_not_people(self) -> None:
        for name in ("AD SOYAD", "Codex Orchestrator", "Claude Asistan", "SENTETIK Kişi"):
            with self.subTest(name=name):
                register = copy.deepcopy(REGISTER)
                gate = _gate(register, "DATA-LEGACY")
                _named(gate)
                gate["primary"] = {"name": name}
                with self.assertRaises(tool.RegisterViolation):
                    tool.validate_register(register, SCHEMA)

    def test_identifiers_are_refused_in_names_and_sources(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-PARTY")
        _named(gate)
        gate["primary"] = {"name": "b2000000-0000-4000-8000-000000000301"}
        self.assertViolation(register, "not an identifier")
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-PARTY")
        _named(gate)
        gate["sourceCustodian"]["sourceIdentityRoute"] = "cari 4100046586"
        self.assertViolation(register, "VKN/TCKN-like")

    def test_the_backup_is_a_different_person(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-UAT")
        _named(gate)
        gate["backup"] = {"name": "deneme birinci"}
        self.assertViolation(register, "different person")

    def test_same_as_is_limited_to_the_matrix_pairs_and_needs_a_named_target(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-FINANCE")
        gate.update(status="RECORDED", sameAs="DATA-OPS", targetDate="2026-11-30", decisionRecord=NAMING_RECORD)
        self.assertViolation(register, "cannot be the same as")
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-KG-M")
        gate.update(status="RECORDED", sameAs="DATA-PRODUCT", targetDate="2026-11-30", decisionRecord=NAMING_RECORD)
        self.assertViolation(register, "points at an unnamed gate")
        _named(_gate(register, "DATA-PRODUCT"))
        self.assertEqual("NOT_CLOSED", tool.validate_register(register, SCHEMA)["e031"])

    def test_pending_rows_cannot_carry_owner_data(self) -> None:
        register = copy.deepcopy(REGISTER)
        _gate(register, "DATA-IDP")["targetDate"] = "2026-11-30"
        self.assertViolation(register, "PENDING but carries owner data")

    def test_confirmation_cannot_repeat_the_naming_record(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-MFG")
        _named(gate)
        gate.update(status="CONFIRMED", confirmationRecord=NAMING_RECORD)
        self.assertViolation(register, "owner's own record")

    def test_all_confirmation_records_are_record_state_only(self) -> None:
        register = copy.deepcopy(REGISTER)
        for gate in register["gates"]:
            _named(gate)
        self.assertEqual("READY_FOR_HUMAN_CONFIRMATION", tool.validate_register(register, SCHEMA)["e031"])
        for gate in register["gates"]:
            gate.update(status="CONFIRMED", confirmationRecord=CONFIRMATION_RECORD)
        self.assertEqual("CONFIRMED", tool.validate_register(register, SCHEMA)["e031"])
        summary = tool.validate_register(register, SCHEMA)
        for flag in ("e031ClosureAuthorized", "isHumanApproval", "grantsPermissions", "authorizesActivation", "authorizesDeployment"):
            self.assertIs(False, summary[flag])
        self.assertEqual("NOT_VERIFIED_OFFLINE", summary["receiptAuthenticity"])

    def test_environment_owners_refuse_a_secret_value_as_reference(self) -> None:
        register = copy.deepcopy(REGISTER)
        owners = register["environmentOwners"]
        person = {"name": "Deneme Dördüncü"}
        owners.update(status="RECORDED", decisionRecord=NAMING_RECORD, targetDate="2026-11-30",
                      opsPrimary={"name": "Deneme Beşinci"}, opsBackup={"name": "Deneme Altıncı"},
                      infraCustodian={**person, "reference": "sunucu envanteri kayıt yeri"},
                      artifactCustodian={**person, "reference": "imaj kaynağı kayıt yeri"},
                      secretCustodian={**person, "reference": "password=hunter2"},
                      dataCustodian={**person, "packId": "synthetic-pack", "packVersion": "1", "packSha256": "0" * 64},
                      costOwner={**person, "reference": "onaylı kapasite kaydı"})
        self.assertViolation(register, "looks like a secret")
        owners["secretCustodian"]["reference"] = "secret store kayıt yeri"
        summary = tool.validate_register(register, SCHEMA)
        self.assertEqual(("RECORDED", False), (summary["environmentOwners"], summary["environmentActivationOwnersNamed"]))

    def test_the_schema_is_closed_at_every_object(self) -> None:
        tool.SCHEMA_SUBSET.require_closed_objects(SCHEMA)
        register = copy.deepcopy(REGISTER)
        register["gates"][0]["partyId"] = "x"
        self.assertViolation(register, "unexpected fields")

    def test_scopes_are_separate_record_states_and_never_source_or_activation_approval(self) -> None:
        summary = tool.validate_register(REGISTER, SCHEMA)
        self.assertIs(False, summary["blocksSourceDevelopment"])
        self.assertEqual({"DATA_ACTIVATION", "REAL_INPUT", "UAT"}, set(summary["scopeRecordStates"]))
        for scope, state in summary["scopeRecordStates"].items():
            self.assertGreater(state["gateStatusCounts"]["PENDING"], 0, scope)
            self.assertEqual(0, state["gateStatusCounts"]["RECORDED"])
            self.assertEqual(0, state["gateStatusCounts"]["CONFIRMED"])
            self.assertIs(False, state["authorizesActivation"])

    def test_missing_reordered_and_duplicate_gate_rows_are_rejected(self) -> None:
        for mutate in (
            lambda r: r["gates"].pop(),
            lambda r: r["gates"].reverse(),
            lambda r: r["gates"].__setitem__(1, copy.deepcopy(r["gates"][0])),
        ):
            register = copy.deepcopy(REGISTER)
            mutate(register)
            with self.assertRaises((tool.RegisterViolation, tool.SCHEMA_SUBSET.SchemaViolation)):
                tool.validate_register(register, SCHEMA)

    def test_unlock_scopes_cannot_be_repurposed_to_another_task_or_gate(self) -> None:
        for item, scope in (("R04.2", "UAT"), ("E03.1", "DATA_ACTIVATION"), ("R07.2", "REAL_INPUT")):
            register = copy.deepcopy(REGISTER)
            _gate(register, "DATA-IDP")["unlocks"] = [{"item": item, "scope": scope}]
            self.assertViolation(register, "unlock scopes differ")

    def test_pending_environment_cannot_carry_names_dates_or_receipts(self) -> None:
        for field, value in (("opsPrimary", {"name": "Deneme Birinci"}), ("targetDate", "2026-11-30"),
                             ("decisionRecord", NAMING_RECORD)):
            register = copy.deepcopy(REGISTER)
            register["environmentOwners"][field] = value
            self.assertViolation(register, "PENDING but carry owner data")

    def test_same_as_confirmation_cannot_outrun_its_target_or_duplicate_names(self) -> None:
        register = copy.deepcopy(REGISTER)
        _named(_gate(register, "DATA-PRODUCT"))
        gate = _gate(register, "DATA-KG-M")
        gate.update(status="CONFIRMED", sameAs="DATA-PRODUCT", targetDate="2026-11-30",
                    decisionRecord=NAMING_RECORD, confirmationRecord=CONFIRMATION_RECORD)
        self.assertViolation(register, "unconfirmed sameAs target")
        _gate(register, "DATA-PRODUCT").update(status="CONFIRMED", confirmationRecord=CONFIRMATION_RECORD)
        self.assertEqual("NOT_CLOSED", tool.validate_register(register, SCHEMA)["e031"])
        gate["primary"] = {"name": "Deneme Birinci"}
        self.assertViolation(register, "must not repeat names")

    def test_calendar_dates_and_receipt_uri_are_exact(self) -> None:
        for field, value in (("targetDate", "2026-02-30"), ("targetDate", "20261130"),
                             ("decisionRecord", NAMING_RECORD + "\n"),
                             ("decisionRecord", NAMING_RECORD.replace("/66#", "/67#"))):
            register = copy.deepcopy(REGISTER)
            gate = _gate(register, "DATA-LEGACY")
            _named(gate)
            gate[field] = value
            with self.assertRaises((tool.RegisterViolation, tool.SCHEMA_SUBSET.SchemaViolation)):
                tool.validate_register(register, SCHEMA)

    def test_sources_reject_blank_controls_guid_and_unicode_fake_identifier(self) -> None:
        for text in ("   ", "satır\nyeni satır", "source=b2000000-0000-4000-8000-000000000301", "kod ١٢٣٤٥٦٧٨٩٠"):
            register = copy.deepcopy(REGISTER)
            gate = _gate(register, "DATA-LEGACY")
            _named(gate)
            gate["sourceCustodian"]["sourceIdentityRoute"] = text
            with self.assertRaises(tool.RegisterViolation):
                tool.validate_register(register, SCHEMA)

    def test_hex_source_digest_is_metadata_but_opaque_credentials_are_refused(self) -> None:
        register = copy.deepcopy(REGISTER)
        gate = _gate(register, "DATA-LEGACY")
        _named(gate)
        gate["sourceCustodian"]["sourceIdentityRoute"] = "SHA256 " + "a" * 64
        self.assertEqual("NOT_CLOSED", tool.validate_register(register, SCHEMA)["e031"])
        gate["sourceCustodian"]["sourceIdentityRoute"] = "opaque " + "xyzXYZ+/" * 6
        self.assertViolation(register, "looks like a secret")

    def test_schema_helper_is_bound_to_exact_normalized_source_bytes(self) -> None:
        content = (REPOSITORY_ROOT / "tools/contract_schema_subset.py").read_bytes().replace(b"\r\n", b"\n")
        self.assertEqual(tool.SCHEMA_SUBSET_SHA256, hashlib.sha256(content).hexdigest())

    def test_validation_does_not_fill_mutate_or_write_the_pending_register(self) -> None:
        before = (REPOSITORY_ROOT / tool.REGISTER_PATH).read_bytes()
        register = copy.deepcopy(REGISTER)
        snapshot = copy.deepcopy(register)
        summary = tool.validate_register(register, SCHEMA)
        self.assertEqual(snapshot, register)
        self.assertEqual(before, (REPOSITORY_ROOT / tool.REGISTER_PATH).read_bytes())
        self.assertIs(False, summary["isHumanApproval"])

    def test_cli_violations_do_not_print_supplied_person_or_source_values(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "invalid-register.json"
            register = copy.deepcopy(REGISTER)
            _gate(register, "DATA-IDP")["primary"] = {"name": "Deneme Gizli"}
            path.write_bytes(tool.SCHEMA_SUBSET.canonical_bytes(register))
            stdout, stderr = io.StringIO(), io.StringIO()
            with redirect_stdout(stdout), redirect_stderr(stderr):
                code = tool.main(["--register", str(path)])
            self.assertEqual(1, code)
            self.assertEqual("", stdout.getvalue())
            self.assertNotIn("Deneme Gizli", stderr.getvalue())
            self.assertNotIn(str(path), stderr.getvalue())

    def test_cli_prints_the_summary_and_rejects_duplicate_keys(self) -> None:
        completed = subprocess.run([sys.executable, "-I", "-S", "-B", str(TOOL_PATH)], check=False,
                                   capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("NOT_CLOSED", json.loads(completed.stdout)["e031"])
        with self.assertRaises(tool.SCHEMA_SUBSET.StrictJsonError):
            tool.SCHEMA_SUBSET.load_strict_json_bytes(b'{"a":1,"a":2}', "duplicate")


class SchemaSubsetTests(unittest.TestCase):
    def test_nonfinite_float_bom_invalid_utf8_and_deep_json_are_rejected(self) -> None:
        for content in (b'{"n":NaN}', b'{"n":Infinity}', b'{"n":1e999}', b'{"n":0.5}',
                        b'\xef\xbb\xbf{}', b'{"n":"\xff"}', b'[' * 70 + b'0' + b']' * 70):
            with self.subTest(content=content[:40]), self.assertRaises(tool.SCHEMA_SUBSET.StrictJsonError):
                tool.SCHEMA_SUBSET.load_strict_json_bytes(content, "synthetic JSON")

    def test_unpaired_unicode_surrogates_in_keys_and_values_are_rejected(self) -> None:
        for content in (b'{"name":"\\ud800"}', b'{"\\udfff":1}'):
            with self.assertRaises(tool.SCHEMA_SUBSET.StrictJsonError):
                tool.SCHEMA_SUBSET.load_strict_json_bytes(content, "synthetic JSON")

    def test_nullable_and_maximum_constraints_are_enforced_with_boolean_integer_separation(self) -> None:
        schema = {"type": ["integer", "null"], "minimum": 0, "maximum": 28}
        for value in (None, 0, 28):
            tool.SCHEMA_SUBSET.validate(value, schema)
        for value in (True, False, -1, 29, "3"):
            with self.subTest(value=value), self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
                tool.SCHEMA_SUBSET.validate(value, schema)

    def test_const_enum_and_unique_items_use_json_equality(self) -> None:
        with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
            tool.SCHEMA_SUBSET.validate(True, {"const": 1})
        with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
            tool.SCHEMA_SUBSET.validate(1, {"enum": [True]})
        schema = {"type": "array", "uniqueItems": True, "items": {}}
        tool.SCHEMA_SUBSET.validate([True, 1], schema)
        with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
            tool.SCHEMA_SUBSET.validate([{"a": 1}, {"a": 1}], schema)

    def test_unsupported_rules_are_refused_under_absent_and_null_branches(self) -> None:
        for value in ({}, {"optional": None}):
            schema = {"type": "object", "additionalProperties": False,
                      "properties": {"optional": {"type": ["string", "null"], "unknownRule": True}}}
            with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
                tool.SCHEMA_SUBSET.validate(value, schema)
        schema = {"type": "object", "additionalProperties": False, "properties": {
            "optional": {"type": ["object", "null"], "properties": {}}}}
        with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
            tool.SCHEMA_SUBSET.require_closed_objects(schema)

    def test_malformed_schema_rules_are_rejected_before_value_validation(self) -> None:
        for schema in ({"type": []}, {"type": ["null", "null"]}, {"type": "integer", "maximum": True},
                       {"type": "array", "minItems": 2, "maxItems": 1}, {"type": "string", "pattern": "["},
                       {"type": "object", "properties": {}, "required": ["absent"]}):
            with self.subTest(schema=schema), self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
                tool.SCHEMA_SUBSET.validate(None, schema)

    def test_pattern_is_json_schema_substring_search_and_required_extra_fields_are_checked(self) -> None:
        tool.SCHEMA_SUBSET.validate("before-needle-after", {"type": "string", "pattern": "needle"})
        schema = {"type": "object", "additionalProperties": False, "required": ["value"],
                  "properties": {"value": {"type": "string", "minLength": 1, "maxLength": 2}}}
        for value in ({}, {"value": "ok", "extra": 1}, {"value": ""}, {"value": "long"}):
            with self.assertRaises(tool.SCHEMA_SUBSET.SchemaViolation):
                tool.SCHEMA_SUBSET.validate(value, schema)

    def test_reader_limits_and_canonical_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.json"
            path.write_bytes(b'{"x":1}')
            with self.assertRaises(tool.SCHEMA_SUBSET.StrictJsonError):
                tool.SCHEMA_SUBSET.load_strict_json(path, "synthetic", maximum_bytes=2)
            self.assertEqual({"x": 1}, tool.SCHEMA_SUBSET.load_strict_json(path, "synthetic"))
            with self.assertRaises(tool.SCHEMA_SUBSET.StrictJsonError):
                tool.SCHEMA_SUBSET.load_strict_json(Path(directory), "synthetic")
        self.assertEqual(b'{"a":2,"z":1}\n', tool.SCHEMA_SUBSET.canonical_bytes({"z": 1, "a": 2}))


if __name__ == "__main__":
    unittest.main()
