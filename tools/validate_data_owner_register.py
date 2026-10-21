#!/usr/bin/env python3
"""Validate the named data and activation owner register (E03.1) and print its status.

The register records declarations of people Sinan named. This offline tool cannot
verify a person's identity, an Issue comment's author or human approval. It refuses placeholders, agent
identities, identifiers (GUID / VKN / TCKN-like digit runs) and secret-looking
references; it never invents, fills or approves a row.

Source-only delivery: unit/CLI/build/SQL/browser/CI execution is NOT_RUN. The
committed eleven gates and seven environment roles remain PENDING/null. Existing
assignment accountability and CODEX-ORCHESTRATOR tracking are not operational
owner assignments. No PartyId, date, grant or production authority is inferred.

Later human workflow (not performed here): Sinan names people in Issue #66; a
human-reviewed change binds that exact naming comment as decisionRecord and sets
RECORDED. A distinct owner-authored confirmation is reviewed before CONFIRMED.
This offline validator only checks record shape; a different URL is not proof of
the author. E03.1 closure remains Sinan's action. DATA-PRODUCT's question stays at
#66/6207285823. sameAs is limited to the matrix's three pairs, never a new grant.

Authorized future source checks (not run in this delivery):
  python -I -S -B -m unittest discover -s tests/data_owner_register -p "test_*.py" -v
  python -I -S -B tools/validate_data_owner_register.py

CLI prints only record-state summaries and generic errors, not names, source
references or person identifiers. It never writes a register or calls Git/network,
providers or ERP. This new helper/register does not enable R04.1 real input; that
requires its own separately admitted source change and named-owner/data gates.
"""

from __future__ import annotations

import argparse
from datetime import date
import hashlib
import json
from pathlib import Path
import re
import sys
from types import ModuleType
from typing import Any, Sequence

SCHEMA_SUBSET_SHA256 = "0ae967ad5f1199e69bc763f56e925e1eb30f900466a3d625f4e518bea4c98b37"


def _load_schema_subset() -> ModuleType:
    path = Path(__file__).resolve().with_name("contract_schema_subset.py")
    name = "_goymenerp_contract_schema_subset"
    try:
        if path.is_symlink() or not path.is_file():
            raise ValueError("schema helper is not a regular source file")
        content = path.read_bytes().replace(b"\r\n", b"\n")
    except OSError:
        raise ValueError("schema helper unavailable") from None
    if hashlib.sha256(content).hexdigest() != SCHEMA_SUBSET_SHA256:
        raise ValueError("schema helper source changed")
    module = ModuleType(name)
    module.__file__ = str(path)
    sys.modules[name] = module
    exec(compile(content, str(path), "exec", dont_inherit=True), module.__dict__)
    return module


SCHEMA_SUBSET = _load_schema_subset()

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = Path("contracts/governance/data-owner-register-v1.schema.json")
REGISTER_PATH = Path("docs/operations/DATA_OWNER_REGISTER_v1.json")
GATE_ORDER = (
    "DATA-OWNERS",
    "DATA-FINANCE",
    "DATA-IDP",
    "DATA-KG-M",
    "DATA-MFG",
    "DATA-LEGACY",
    "DATA-PARTY",
    "DATA-OPS",
    "DATA-DEMO",
    "DATA-UAT",
    "DATA-PRODUCT",
)
# The owner matrix (#66/6207373183) allows exactly these shortcuts.
ALLOWED_SAME_AS = {"DATA-KG-M": "DATA-PRODUCT", "DATA-PARTY": "DATA-LEGACY", "DATA-DEMO": "DATA-OPS"}
# Source matrix links are informational scopes; none authorize activation.
GATE_UNLOCKS = {
    "DATA-OWNERS": (("E03.1", "DATA_ACTIVATION"),),
    "DATA-FINANCE": tuple((item, "DATA_ACTIVATION") for item in
                          ("F08.1", "F08.2", "F09.1", "F09.2", "F09.3", "F09.4", "F09.5", "F10.1", "F10.2")),
    "DATA-IDP": (("R01.2", "DATA_ACTIVATION"),),
    "DATA-KG-M": tuple((item, "DATA_ACTIVATION") for item in ("P01.3", "P01.4", "P04.2")),
    "DATA-MFG": (("M04.1", "REAL_INPUT"), ("M04.2", "DATA_ACTIVATION")),
    "DATA-LEGACY": (("R04.1", "REAL_INPUT"), ("R04.2", "REAL_INPUT"), ("R06.3", "DATA_ACTIVATION"), ("R07.1", "DATA_ACTIVATION")),
    "DATA-PARTY": (("O02.3", "DATA_ACTIVATION"), ("R04.1", "REAL_INPUT")),
    "DATA-OPS": (("W06.3", "DATA_ACTIVATION"), ("R02.2", "DATA_ACTIVATION"), ("R03.1", "REAL_INPUT"), ("R05.2", "DATA_ACTIVATION")),
    "DATA-DEMO": (("R06.1", "DATA_ACTIVATION"),),
    "DATA-UAT": tuple((item, "UAT") for item in ("R06.2", "R06.3", "R07.1", "R07.2")),
    "DATA-PRODUCT": (*tuple((item, "DATA_ACTIVATION") for item in ("P01.2", "P01.3", "P01.4", "P01.5", "P04.1", "P04.2")),
                     ("R04.1", "REAL_INPUT")),
}
NAME_PATTERN = re.compile(r"^[^\W\d_]+(?:[ '.-][^\W\d_]+)*$")
PLACEHOLDER_NAMES = frozenset(
    name.casefold()
    for name in ("AD SOYAD", "İsim Soyisim", "Isim Soyisim", "Person Name", "First Last", "John Doe", "Jane Doe",
                 "TBD", "TODO", "N/A", "NA", "Bilinmiyor", "Belirlenecek", "Yok", "Örnek", "Test")
)
AGENT_WORDS = frozenset({"codex", "claude", "chatgpt", "orchestrator", "copilot", "agent", "bot", "sentetik", "synthetic"})
NAME_SEPARATORS = re.compile(r"[ '.-]")
GUID_PATTERN = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
IDENTIFIER_DIGITS_PATTERN = re.compile(r"(?<!\d)\d{10,11}(?!\d)")
RECORD_PATTERN = re.compile(r"https://github\.com/sinangym/GoymenERP/issues/66#issuecomment-[1-9][0-9]*")
SECRET_PATTERNS = (
    re.compile(r"(?i)\b(?:password|passwd|pwd|secret|token|apikey|api_key)\s*[=:]"),
    re.compile(r"-----BEGIN [A-Z ]+-----"),
    re.compile(r"(?i)\b(?:server|data source)\s*=.*;"),
)


class RegisterViolation(ValueError):
    """The register is not a truthful record of named owners."""


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise RegisterViolation(message)


def _check_name(value: Any, label: str) -> str:
    _require(isinstance(value, dict) and isinstance(value.get("name"), str), f"{label} has no name")
    name = value["name"]
    _require(name == name.strip() and "  " not in name, f"{label} name is not trimmed")
    _require(NAME_PATTERN.fullmatch(name) is not None, f"{label} name must be a person's name, not an identifier")
    _require(name.casefold() not in PLACEHOLDER_NAMES, f"{label} name is a placeholder")
    words = NAME_SEPARATORS.split(name.casefold())
    _require(not any(word in AGENT_WORDS for word in words), f"{label} name is not a person")
    _require(len(name.split(" ")) >= 2, f"{label} needs a given name and a surname")
    return name


def _check_free_text(value: str, label: str) -> None:
    _require(isinstance(value, str) and value.strip() and value == value.strip(), f"{label} is blank or untrimmed")
    _require(not any(ord(char) < 32 or ord(char) == 127 for char in value), f"{label} contains control characters")
    _require(GUID_PATTERN.search(value) is None, f"{label} must not contain a GUID (no PartyId)")
    _require(IDENTIFIER_DIGITS_PATTERN.search(value) is None, f"{label} must not contain a VKN/TCKN-like number")
    _require(not any(pattern.search(value) for pattern in SECRET_PATTERNS), f"{label} looks like a secret")
    # Plain commit/SHA digests are useful source references. Long non-hex opaque
    # credentials are not. No credential value belongs in this register.
    opaque = re.findall(r"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{40,}={0,2}(?![A-Za-z0-9+/])", value)
    _require(not any(re.fullmatch(r"(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})", part) is None for part in opaque),
             f"{label} looks like a secret")


def _check_record(value: Any, label: str) -> None:
    _require(isinstance(value, str) and RECORD_PATTERN.fullmatch(value) is not None,
             f"{label} is not an Issue #66 comment reference")


def _check_date(value: Any, label: str) -> None:
    _require(isinstance(value, str) and re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value) is not None,
             f"{label} needs a target date")
    try:
        date.fromisoformat(value)
    except ValueError as exc:
        raise RegisterViolation(f"{label} is not a calendar date") from exc


def _check_gate(gate: dict[str, Any], by_id: dict[str, dict[str, Any]]) -> None:
    label = gate["id"]
    status = gate["status"]
    named = ("primary", "backup", "sourceCustodian", "targetDate")
    _require(tuple((item["item"], item["scope"]) for item in gate["unlocks"]) == GATE_UNLOCKS[label],
             f"{label} unlock scopes differ from the owner matrix")
    _require(gate["responsibility"].strip() == gate["responsibility"] and bool(gate["responsibility"].strip()),
             f"{label} responsibility is blank or untrimmed")
    # The canonical Product paragraph links its existing Issue package. That
    # specific comment ID is provenance, not a business/person identifier.
    _check_free_text(gate["responsibility"].replace("#66/6207285823", "#66/Product-package"),
                     f"{label} responsibility")
    for item in gate["unlocks"]:
        _require(item["item"] != "E03.1" or label == "DATA-OWNERS", f"{label} cannot unlock E03.1")
    if status == "PENDING":
        _require(all(gate[field] is None for field in (*named, "sameAs", "decisionRecord", "confirmationRecord")),
                 f"{label} is PENDING but carries owner data")
        return
    _require(isinstance(gate["decisionRecord"], str), f"{label} is {status} without the Issue #66 record")
    _check_record(gate["decisionRecord"], f"{label} decisionRecord")
    if status == "RECORDED":
        _require(gate["confirmationRecord"] is None, f"{label} is RECORDED but carries a confirmation")
    else:
        _require(isinstance(gate["confirmationRecord"], str), f"{label} is CONFIRMED without the owner's confirmation")
        _check_record(gate["confirmationRecord"], f"{label} confirmationRecord")
        _require(gate["confirmationRecord"] != gate["decisionRecord"],
                 f"{label} confirmation must be the owner's own record, not the naming record")
    if gate["sameAs"] is not None:
        _require(ALLOWED_SAME_AS.get(label) == gate["sameAs"], f"{label} cannot be the same as {gate['sameAs']}")
        target = by_id[gate["sameAs"]]
        _require(target["status"] in {"RECORDED", "CONFIRMED"}, f"{label} points at an unnamed gate")
        _require(status != "CONFIRMED" or target["status"] == "CONFIRMED",
                 f"{label} cannot confirm an unconfirmed sameAs target")
        _require(gate["primary"] is None and gate["backup"] is None and gate["sourceCustodian"] is None,
                 f"{label} uses sameAs and must not repeat names")
        _check_date(gate["targetDate"], f"{label} targetDate")
        return
    primary = _check_name(gate["primary"], f"{label} primary")
    backup = _check_name(gate["backup"], f"{label} backup")
    _require(primary.casefold() != backup.casefold(), f"{label} backup must be a different person")
    custodian = gate["sourceCustodian"]
    _check_name(custodian, f"{label} source custodian")
    for field in ("sourceName", "sourceIdentityRoute"):
        _check_free_text(custodian[field], f"{label} source custodian {field}")
    _check_date(gate["targetDate"], f"{label} targetDate")


def _check_environment(owners: dict[str, Any]) -> None:
    roles = ("opsPrimary", "opsBackup", "infraCustodian", "artifactCustodian", "secretCustodian", "dataCustodian",
             "costOwner")
    if owners["status"] == "PENDING":
        _require(all(owners[role] is None for role in roles) and owners["targetDate"] is None
                 and owners["decisionRecord"] is None, "environment owners are PENDING but carry owner data")
        return
    _require(isinstance(owners["decisionRecord"], str), "environment owners need the Issue #66 record")
    _check_record(owners["decisionRecord"], "environment decisionRecord")
    names = {role: _check_name(owners[role], f"environment {role}") for role in roles}
    _require(names["opsPrimary"].casefold() != names["opsBackup"].casefold(),
             "environment OpsBackup must be a different person from OpsPrimary")
    for role in ("infraCustodian", "artifactCustodian", "secretCustodian", "costOwner"):
        _check_free_text(owners[role]["reference"], f"environment {role} reference")
    for field in ("packId", "packVersion"):
        _check_free_text(owners["dataCustodian"][field], f"environment dataCustodian {field}")
    _check_date(owners["targetDate"], "environment targetDate")


def validate_register(register: Any, schema: Any) -> dict[str, Any]:
    SCHEMA_SUBSET.require_closed_objects(schema)
    try:
        SCHEMA_SUBSET.validate(register, schema)
    except SCHEMA_SUBSET.SchemaViolation as exc:
        raise RegisterViolation(str(exc)) from exc
    gates = register["gates"]
    _require(tuple(gate["id"] for gate in gates) == GATE_ORDER, "gates must be the eleven matrix gates in order")
    by_id = {gate["id"]: gate for gate in gates}
    for gate in gates:
        _check_gate(gate, by_id)
    _check_environment(register["environmentOwners"])

    counts = {status: sum(1 for gate in gates if gate["status"] == status)
              for status in ("PENDING", "RECORDED", "CONFIRMED")}
    if counts["CONFIRMED"] == len(gates):
        e031 = "CONFIRMED"
    elif counts["PENDING"] == 0:
        e031 = "READY_FOR_HUMAN_CONFIRMATION"
    else:
        e031 = "NOT_CLOSED"
    data_ops_named = by_id["DATA-OPS"]["status"] in {"RECORDED", "CONFIRMED"}
    scope_states = {}
    for scope in ("DATA_ACTIVATION", "REAL_INPUT", "UAT"):
        relevant = [gate for gate in gates if any(item["scope"] == scope for item in gate["unlocks"])]
        scope_states[scope] = {
            "gateStatusCounts": {status: sum(gate["status"] == status for gate in relevant)
                                 for status in ("PENDING", "RECORDED", "CONFIRMED")},
            "pendingGates": [gate["id"] for gate in relevant if gate["status"] == "PENDING"],
            "authorizesActivation": False,
        }
    return {
        "summaryVersion": "goymenerp.data-owner-register-summary.v1",
        "gateStatusCounts": counts,
        "pendingGates": [gate["id"] for gate in gates if gate["status"] == "PENDING"],
        "e031": e031,
        "environmentOwners": register["environmentOwners"]["status"],
        "environmentActivationOwnersNamed": data_ops_named and register["environmentOwners"]["status"] == "RECORDED",
        "blocksSourceDevelopment": False,
        "scopeRecordStates": scope_states,
        "e031ClosureAuthorized": False,
        "receiptAuthenticity": "NOT_VERIFIED_OFFLINE",
        "isHumanApproval": False,
        "grantsPermissions": False,
        "authorizesActivation": False,
        "authorizesDeployment": False,
        "requiresHumanReview": True,
    }


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument("--register", type=Path, default=None)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    root = arguments.repository_root.resolve()
    if root != REPOSITORY_ROOT:
        print("data owner register error: repository_root_mismatch", file=sys.stderr)
        return 2
    register_path = arguments.register if arguments.register is not None else root / REGISTER_PATH
    try:
        schema = SCHEMA_SUBSET.load_strict_json(root / SCHEMA_PATH, "data owner register schema")
        register = SCHEMA_SUBSET.load_strict_json(register_path, "data owner register")
    except SCHEMA_SUBSET.StrictJsonError:
        print("data owner register error: input_unavailable_or_invalid_json", file=sys.stderr)
        return 2
    try:
        summary = validate_register(register, schema)
    except (RegisterViolation, SCHEMA_SUBSET.SchemaViolation):
        print("data owner register violation: invalid_owner_contract", file=sys.stderr)
        return 1
    sys.stdout.write(json.dumps(summary, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
