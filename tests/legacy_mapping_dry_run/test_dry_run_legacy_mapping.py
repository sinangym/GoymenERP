"""Behaviour tests for the read-only legacy mapping dry-run (R04.1).

Every value in these fixtures (codes, names, the kg scale) is invented for the test
and labelled synthetic; none of it is a Settings profile or a real legacy record.
"""

from __future__ import annotations

from contextlib import redirect_stderr, redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
TOOL_PATH = REPOSITORY_ROOT / "tools" / "dry_run_legacy_mapping.py"


def _load_tool():
    spec = importlib.util.spec_from_file_location("legacy_mapping_dry_run_tool", TOOL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("tool cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


tool = _load_tool()
MANIFEST_SCHEMA = tool.strict_json_bytes((REPOSITORY_ROOT / tool.MANIFEST_SCHEMA_PATH).read_bytes())
PROFILE_SCHEMA = tool.strict_json_bytes((REPOSITORY_ROOT / tool.PROFILE_SCHEMA_PATH).read_bytes())
SOURCE_NAME = "Eski stok sistemi dışa aktarımı"
FIRST_CUTOFF = "2026-10-01T00:00:00Z"
SECOND_CUTOFF = "2026-10-08T00:00:00Z"

PARTY_CSV = "KOD;UNVAN;DURUM\nC1;Deneme Cari Bir;A\nC2;Deneme Cari İki;P\n;Kimliksiz Cari;A\n"
PRODUCT_CSV = (
    "STOK_KODU;AD;BIRIM;DURUM\n"
    "P1;Deneme Ürün Bir;KG;A\n"
    "P2;Deneme Ürün İki;KGM;A\n"
    "P3;Deneme Ürün Üç;KG;A\n"
    "P3;Deneme Ürün Üç kopya;KG;A\n"
    "P4;Deneme Ürün Dört;KG;I\n"
)
ORDER_CSV = (
    "SIPARIS;CARI;STOK;MIKTAR;TUTAR;DOVIZ\n"
    "S1;C1;P1;0,1;10,50;TL\n"
    "S2;C1;P1;0,2;5;TL\n"
    "S3;C9;P1;1;1;TL\n"
    "S4;C1;P3;1;1;TL\n"
    "S5;C1;P1;0,1234;1;TL\n"
)
UNIT_MAP = "sourceValue,targetKey\nKG,kg\nKGM,kg\nKGM,adet\n"
CURRENCY_MAP = "sourceValue,targetKey\nTL,TRY\n"


def _write(path: Path, text: str) -> str:
    content = text.encode("utf-8")
    path.write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def _bytes(value: dict) -> bytes:
    return json.dumps(value, ensure_ascii=False, indent=2).encode("utf-8")


def build(directory: Path, *, party: str = PARTY_CSV, product: str = PRODUCT_CSV, order: str = ORDER_CSV,
          cutoff: str = FIRST_CUTOFF, source_class: str = "SYNTHETIC_FIXTURE",
          register_sha256: str | None = None) -> tuple[dict, dict]:
    extracts_root = directory / "extracts"
    maps_root = directory / "maps"
    extracts_root.mkdir(exist_ok=True)
    maps_root.mkdir(exist_ok=True)
    extracts = []
    for name, text in (("party", party), ("product", product), ("open-order", order)):
        extracts.append({
            "name": name,
            "fileName": f"{name}.csv",
            "sha256": _write(extracts_root / f"{name}.csv", text),
            "rowCount": len(text.splitlines()) - 1,
            "delimiter": ";",
            "decimalSeparator": ",",
        })
    manifest = {
        "contractVersion": "goymenerp.legacy-source-manifest.v1",
        "sourceClass": source_class,
        "sourceSystem": SOURCE_NAME,
        "snapshotId": f"synthetic-{cutoff[:10]}",
        "sourceVersion": "synthetic-1",
        "cutoffUtc": cutoff,
        "dataOwnerRegisterSha256": register_sha256,
        "extracts": extracts,
    }
    profile = {
        "contractVersion": "goymenerp.legacy-mapping-profile.v1",
        "mappingId": "synthetic-legacy",
        "mappingVersion": 1,
        "valueMaps": [
            {"name": "unit-map", "fileName": "unit-map.csv", "sha256": _write(maps_root / "unit-map.csv", UNIT_MAP)},
            {"name": "currency-map", "fileName": "currency-map.csv",
             "sha256": _write(maps_root / "currency-map.csv", CURRENCY_MAP)},
        ],
        "entities": [
            {
                "entity": "party", "extract": "party", "ownerModule": "counterparty", "dataGate": "DATA-PARTY",
                "sourceIdColumn": "KOD", "sourceVersionColumn": None, "requiredColumns": ["UNVAN"],
                "lifecycle": {"column": "DURUM", "values": [{"sourceValue": "A", "state": "ACTIVE"},
                                                            {"sourceValue": "P", "state": "INACTIVE"}]},
                "valueColumns": [], "references": [], "measures": [],
            },
            {
                "entity": "product", "extract": "product", "ownerModule": "product", "dataGate": "DATA-PRODUCT",
                "sourceIdColumn": "STOK_KODU", "sourceVersionColumn": None, "requiredColumns": ["AD"],
                "lifecycle": {"column": "DURUM", "values": [{"sourceValue": "A", "state": "ACTIVE"},
                                                            {"sourceValue": "I", "state": "CANCELLED"}]},
                "valueColumns": [{"column": "BIRIM", "valueMap": "unit-map", "required": True}],
                "references": [], "measures": [],
            },
            {
                "entity": "open-order", "extract": "open-order", "ownerModule": "sales", "dataGate": "DATA-LEGACY",
                "sourceIdColumn": "SIPARIS", "sourceVersionColumn": None, "requiredColumns": [],
                "lifecycle": None, "valueColumns": [],
                "references": [{"column": "CARI", "entity": "party", "required": True},
                               {"column": "STOK", "entity": "product", "required": True}],
                "measures": [
                    {"column": "MIKTAR", "kind": "QUANTITY", "unitCode": "kg", "unitColumn": None,
                     "unitValueMap": None, "required": True,
                     "scales": [{"unitCode": "kg", "approvedScale": 3,
                                 "scaleSource": "Test fixture; not a Settings unit profile"}]},
                    {"column": "TUTAR", "kind": "AMOUNT", "unitCode": None, "unitColumn": "DOVIZ",
                     "unitValueMap": "currency-map", "required": True, "scales": []},
                ],
            },
        ],
    }
    return manifest, profile


def run(directory: Path, manifest: dict, profile: dict, *, register_bytes: bytes | None = None,
        previous: dict | None = None) -> dict:
    return tool.run_dry_run(
        manifest_bytes=_bytes(manifest),
        profile_bytes=_bytes(profile),
        manifest_schema=MANIFEST_SCHEMA,
        profile_schema=PROFILE_SCHEMA,
        extract_root=directory / "extracts",
        value_map_root=directory / "maps",
        synthetic_fixture=register_bytes is None,
        register_bytes=register_bytes,
        register_schema=None,
        previous_report_bytes=None if previous is None else tool.canonical_bytes(previous),
    )


def entity(report: dict, name: str) -> dict:
    return next(item for item in report["entities"] if item["entity"] == name)


def rows_by_locator(report: dict, name: str) -> dict:
    return {row["locator"]: row for row in entity(report, name)["rows"]}


class DryRunTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def assertViolation(self, fragment: str, **arguments) -> None:
        manifest, profile = arguments.pop("documents")
        with self.assertRaises(tool.DryRunViolation) as raised:
            run(self.directory, manifest, profile, **arguments)
        self.assertIn(fragment, str(raised.exception))

    def test_synthetic_fixture_report_is_never_ready_and_never_writes_erp(self) -> None:
        report = run(self.directory, *build(self.directory))
        self.assertIs(False, report["writesErp"])
        self.assertIs(False, report["productionReady"])
        self.assertEqual("NOT_READY", report["readiness"])
        self.assertIn("SYNTHETIC_FIXTURE", report["notReadyReasons"])
        self.assertIsNone(report["dataOwnerRegisterSha256"])

    def test_missing_duplicate_and_malformed_source_rows_are_quarantined(self) -> None:
        party = PARTY_CSV + "C7;yalnız iki alan\n"
        report = run(self.directory, *build(self.directory, party=party))
        party_rows = rows_by_locator(report, "party")
        self.assertEqual(("QUARANTINE", None, ["MISSING_SOURCE_ID"]),
                         (party_rows["party:3"]["action"], party_rows["party:3"]["sourceId"],
                          party_rows["party:3"]["reasons"]))
        self.assertEqual(["MALFORMED_ROW"], party_rows["party:4"]["reasons"])
        product_rows = rows_by_locator(report, "product")
        for locator in ("product:3", "product:4"):
            self.assertEqual(("P3", "QUARANTINE", ["DUPLICATE_SOURCE_ID"]),
                             (product_rows[locator]["sourceId"], product_rows[locator]["action"],
                              product_rows[locator]["reasons"]))
        self.assertEqual(2, entity(report, "product")["quarantineReasons"]["DUPLICATE_SOURCE_ID"])
        self.assertIn("QUARANTINE:party", report["notReadyReasons"])

    def test_lifecycle_plans_deactivate_and_cancel_not_only_new_rows(self) -> None:
        report = run(self.directory, *build(self.directory))
        self.assertEqual({"UPSERT": 1, "DEACTIVATE": 1, "CANCEL": 0, "NO_CHANGE": 0, "QUARANTINE": 1},
                         entity(report, "party")["actions"])
        self.assertEqual("CANCEL", rows_by_locator(report, "product")["product:5"]["action"])
        unknown = PARTY_CSV + "C8;Deneme Cari Sekiz;X\n"
        report = run(self.directory, *build(self.directory, party=unknown))
        self.assertEqual(["UNKNOWN_LIFECYCLE_VALUE"], rows_by_locator(report, "party")["party:4"]["reasons"])

    def test_unmapped_and_ambiguous_values_are_quarantined(self) -> None:
        product = PRODUCT_CSV + "P5;Deneme Ürün Beş;LT;A\n"
        report = run(self.directory, *build(self.directory, product=product))
        rows = rows_by_locator(report, "product")
        self.assertEqual(["AMBIGUOUS_MAPPING"], rows["product:2"]["reasons"])
        self.assertEqual(["UNMAPPED_VALUE"], rows["product:6"]["reasons"])
        self.assertEqual("UPSERT", rows["product:1"]["action"])

    def test_references_to_missing_or_quarantined_rows_are_quarantined(self) -> None:
        rows = rows_by_locator(run(self.directory, *build(self.directory)), "open-order")
        self.assertEqual(["UNRESOLVED_REFERENCE"], rows["open-order:3"]["reasons"])
        self.assertEqual(["REFERENCED_ROW_QUARANTINED"], rows["open-order:4"]["reasons"])
        self.assertEqual(("UPSERT", []), (rows["open-order:1"]["action"], rows["open-order:1"]["reasons"]))

    def test_measure_totals_are_exact_and_scale_comes_from_the_profile(self) -> None:
        report = run(self.directory, *build(self.directory))
        order = entity(report, "open-order")
        totals = {(item["column"], item["unitCode"]): item for item in order["measureTotals"]}
        self.assertEqual(("2.4234", "0.3", "2.1234", 5),
                         tuple(totals[("MIKTAR", "kg")][key] for key in ("all", "applicable", "quarantined", "rows")))
        self.assertEqual(("18.5", "15.5", "3"),
                         tuple(totals[("TUTAR", "TRY")][key] for key in ("all", "applicable", "quarantined")))
        self.assertEqual(["SCALE_EXCEEDED"], rows_by_locator(report, "open-order")["open-order:5"]["reasons"])
        self.assertIn("SCALE_POLICY_MISSING:open-order.TUTAR:TRY", report["notReadyReasons"])

    def test_extract_bytes_and_record_count_are_bound_to_the_manifest(self) -> None:
        manifest, profile = build(self.directory)
        (self.directory / "extracts" / "party.csv").write_bytes(PARTY_CSV.replace("C2", "C3").encode("utf-8"))
        self.assertViolation("do not match the manifest hash", documents=(manifest, profile))
        manifest, profile = build(self.directory)
        manifest["extracts"][0]["rowCount"] = 2
        self.assertViolation("has 3 records but the manifest says 2", documents=(manifest, profile))

    def test_real_extract_is_disabled_even_with_supplied_register_bytes(self) -> None:
        register_bytes = b'{"syntheticCounterexample":"not an E03.1 contract"}'
        manifest, profile = build(self.directory, source_class="LEGACY_EXTRACT",
                                  register_sha256=hashlib.sha256(register_bytes).hexdigest())
        tool._engine()  # Freeze/cache only the reviewed schema-source module.
        with patch.object(tool, "_read_input_file", side_effect=AssertionError("data read before fence")):
            self.assertViolation("INPUT_DEPENDENCY_UNAVAILABLE:E03.1",
                                 documents=(manifest, profile), register_bytes=register_bytes)

    def test_cli_real_mode_rejects_before_reading_any_input_or_creating_report(self) -> None:
        stdout, stderr = io.StringIO(), io.StringIO()
        output = self.directory / "must-not-exist.json"
        with patch.object(tool, "_read_input_file", side_effect=AssertionError("any input read")), \
                redirect_stdout(stdout), redirect_stderr(stderr):
            code = tool.main(["--manifest", "missing.json", "--profile", "missing-profile.json",
                              "--extract-root", "missing-extracts", "--value-map-root", "missing-maps",
                              "--data-owner-register", "missing-register.json", "--previous-report", "missing-previous.json",
                              "--output", str(output)])
        self.assertEqual(1, code)
        self.assertEqual("", stdout.getvalue())
        self.assertIn("INPUT_DEPENDENCY_UNAVAILABLE:E03.1", stderr.getvalue())
        self.assertFalse(output.exists())

    def test_second_snapshot_classifies_unchanged_changed_new_and_absent_rows(self) -> None:
        first = run(self.directory, *build(self.directory))
        party = "KOD;UNVAN;DURUM\nC1;Deneme Cari Bir;A\nC3;Deneme Cari Üç;A\n;Kimliksiz Cari;A\n"
        product = PRODUCT_CSV.replace("P1;Deneme Ürün Bir", "P1;Deneme Ürün Bir yeni ad")
        manifest, profile = build(self.directory, party=party, product=product, cutoff=SECOND_CUTOFF)
        second = run(self.directory, manifest, profile, previous=first)
        party_report = entity(second, "party")
        self.assertEqual({"NEW": 1, "CHANGED": 0, "UNCHANGED": 1, "ABSENT": 1}, party_report["changes"])
        self.assertEqual(["C2"], party_report["absentSourceIds"])
        rows = rows_by_locator(second, "party")
        self.assertEqual(("NO_CHANGE", "UPSERT", "UNCHANGED"),
                         (rows["party:1"]["action"], rows["party:1"]["mappedAction"], rows["party:1"]["change"]))
        self.assertEqual(("UPSERT", "CHANGED"),
                         tuple(rows_by_locator(second, "product")["product:1"][key] for key in ("action", "change")))
        self.assertIn("ABSENT_ROWS_NEED_DECISION:party", second["notReadyReasons"])
        self.assertEqual(hashlib.sha256(tool.canonical_bytes(first)).hexdigest(),
                         second["previousReportSha256"])
        manifest, profile = build(self.directory, party=party, product=product, cutoff=FIRST_CUTOFF)
        self.assertViolation("earlier cutoff", documents=(manifest, profile), previous=first)

    def test_reference_cycles_and_missing_columns_are_refused(self) -> None:
        manifest, profile = build(self.directory)
        profile["entities"][0]["references"] = [{"column": "UNVAN", "entity": "open-order", "required": False}]
        self.assertViolation("cycle", documents=(manifest, profile))
        manifest, profile = build(self.directory)
        profile["entities"][1]["sourceIdColumn"] = "YOK"
        self.assertViolation("needs columns the extract lacks: ['YOK']", documents=(manifest, profile))

    def test_unprofiled_extract_keeps_the_inventory_not_ready(self) -> None:
        manifest, profile = build(self.directory)
        manifest["extracts"].append({
            "name": "warehouse", "fileName": "warehouse.csv",
            "sha256": _write(self.directory / "extracts" / "warehouse.csv", "DEPO\nD1\n"),
            "rowCount": 1, "delimiter": ";", "decimalSeparator": ",",
        })
        report = run(self.directory, manifest, profile)
        self.assertEqual([{"extract": "warehouse", "rowCount": 1}], report["unprofiledExtracts"])
        self.assertIn("EXTRACT_NOT_PROFILED:warehouse", report["notReadyReasons"])

    def test_schema_rejects_unknown_fields_boolean_integers_bad_paths_and_scale_above_28(self) -> None:
        for mutate in (
            lambda m, p: m.update(unexpected=True),
            lambda m, p: m["extracts"][0].update(rowCount=True),
            lambda m, p: m["extracts"][0].update(fileName="../party.csv"),
            lambda m, p: m["extracts"][0].update(fileName="party.csv\n"),
            lambda m, p: p["entities"][2]["measures"][0]["scales"][0].update(approvedScale=29),
            lambda m, p: p["entities"][2]["measures"][0]["scales"][0].update(approvedScale=True),
        ):
            with self.subTest(mutate=mutate):
                manifest, profile = build(self.directory)
                mutate(manifest, profile)
                with self.assertRaises(tool.DryRunViolation):
                    run(self.directory, manifest, profile)

    def test_json_duplicate_keys_floats_and_nonfinite_numbers_are_rejected(self) -> None:
        for content in (b'{"a":1,"a":2}', b'{"scale":1.5}', b'{"value":NaN}', b'{"value":Infinity}'):
            with self.subTest(content=content), self.assertRaises(tool.DryRunViolation):
                tool.strict_json_bytes(content)

    def test_contract_definitions_are_closed_even_under_nullable_branches(self) -> None:
        manifest, profile = build(self.directory)
        schema = copy.deepcopy(PROFILE_SCHEMA)
        schema["properties"]["entities"]["items"]["properties"]["lifecycle"]["additionalProperties"] = True
        with self.assertRaisesRegex(tool.DryRunViolation, "open_schema_object"):
            tool._schema_checked(_bytes(profile), schema, "profile")
        schema = copy.deepcopy(PROFILE_SCHEMA)
        schema["properties"]["entities"]["items"]["properties"]["lifecycle"]["notSupported"] = True
        with self.assertRaisesRegex(tool.DryRunViolation, "unsupported_schema_keyword"):
            tool._schema_checked(_bytes(profile), schema, "profile")

    def test_missing_fixed_scale_is_reported_even_when_extract_is_empty(self) -> None:
        manifest, profile = build(self.directory, order=ORDER_CSV.splitlines()[0] + "\n")
        profile["entities"][2]["measures"][0]["scales"] = []
        report = run(self.directory, manifest, profile)
        self.assertIn("SCALE_POLICY_MISSING:open-order.MIKTAR:kg", report["notReadyReasons"])
        self.assertEqual(0, entity(report, "open-order")["rowCount"])

    def test_source_identity_is_preserved_and_never_replaced_with_target_or_trimmed_identity(self) -> None:
        report = run(self.directory, *build(self.directory, party="KOD;UNVAN;DURUM\n C1 ;Deneme;A\n"))
        row = entity(report, "party")["rows"][0]
        self.assertEqual(" C1 ", row["sourceId"])
        self.assertEqual(["UNTRIMMED_KEY"], row["reasons"])
        self.assertEqual("QUARANTINE", row["action"])
        self.assertNotIn("targetId", row)
        self.assertNotIn("targetKey", row)
        self.assertIn("UNRESOLVED_REFERENCE", entity(report, "open-order")["rows"][0]["reasons"])

    def test_version_and_required_value_are_quarantined_when_blank(self) -> None:
        manifest, profile = build(self.directory, party="KOD;UNVAN;DURUM\nC1;   ;A\n")
        profile["entities"][0]["sourceVersionColumn"] = "UNVAN"
        row = entity(run(self.directory, manifest, profile), "party")["rows"][0]
        self.assertEqual(["MISSING_SOURCE_VERSION", "MISSING_REQUIRED_VALUE"], row["reasons"])

    def test_value_map_hash_changes_and_case_colliding_files_are_rejected(self) -> None:
        manifest, profile = build(self.directory)
        (self.directory / "maps" / "unit-map.csv").write_text("sourceValue,targetKey\nKG,litre\n", encoding="utf-8")
        self.assertViolation("bytes do not match the profile hash", documents=(manifest, profile))
        manifest, profile = build(self.directory)
        manifest["extracts"][1]["fileName"] = "PARTY.csv"
        self.assertViolation("file names must be unique", documents=(manifest, profile))

    def test_csv_bom_quoted_fields_and_newlines_use_csv_record_count(self) -> None:
        manifest, profile = build(self.directory, party='\ufeffKOD;UNVAN;DURUM\nC1;"Deneme; unvan\nyeni satır";A\n')
        manifest["extracts"][0]["rowCount"] = 1
        report = run(self.directory, manifest, profile)
        self.assertEqual(1, entity(report, "party")["rowCount"])
        self.assertEqual("UPSERT", entity(report, "party")["rows"][0]["action"])

    def test_invalid_numeric_formats_are_quarantined_without_float_or_separator_guessing(self) -> None:
        order = ORDER_CSV.splitlines()[0] + "\n" + "\n".join(
            f"S{i};C1;P1;{value};1;TL" for i, value in enumerate(("1.234,50", "1e3", "NaN", " 1", "+1"))) + "\n"
        report = run(self.directory, *build(self.directory, order=order))
        self.assertTrue(all("INVALID_DECIMAL" in row["reasons"] for row in entity(report, "open-order")["rows"]))

    def test_decimal_totals_do_not_use_the_process_default_precision(self) -> None:
        order = ORDER_CSV.splitlines()[0] + "\nS1;C1;P1;123456789012345678901234567890,1;1;TL\nS2;C1;P1;0,2;1;TL\n"
        report = run(self.directory, *build(self.directory, order=order))
        total = next(t for t in entity(report, "open-order")["measureTotals"] if t["column"] == "MIKTAR")
        self.assertEqual("123456789012345678901234567890.3", total["all"])
        self.assertEqual(total["all"], total["applicable"])
        self.assertEqual("0", total["quarantined"])

    def test_changed_mapping_cannot_produce_no_change_for_unchanged_csv_bytes(self) -> None:
        first = run(self.directory, *build(self.directory))
        manifest, profile = build(self.directory, cutoff=SECOND_CUTOFF)
        profile["mappingVersion"] = 2
        profile["entities"][1]["lifecycle"]["values"][0]["state"] = "INACTIVE"
        second = run(self.directory, manifest, profile, previous=first)
        row = rows_by_locator(second, "product")["product:1"]
        self.assertEqual(("UNCHANGED", "DEACTIVATE"), (row["change"], row["action"]))
        self.assertEqual(0, entity(second, "party")["actions"]["NO_CHANGE"])

    def test_mapping_edits_without_version_increment_and_version_regression_are_rejected(self) -> None:
        manifest, profile = build(self.directory)
        profile["mappingVersion"] = 2
        first = run(self.directory, manifest, profile)
        for version, change in ((1, False), (2, True)):
            later_manifest, later_profile = build(self.directory, cutoff=SECOND_CUTOFF)
            later_profile["mappingVersion"] = version
            if change:
                later_profile["entities"][0]["requiredColumns"] = []
            self.assertViolation("mapping_", documents=(later_manifest, later_profile), previous=first)

    def test_previous_report_identity_and_entity_changes_are_not_silently_dropped(self) -> None:
        first = run(self.directory, *build(self.directory))
        for mutate in (
            lambda p: p["entities"][0].update(sourceIdColumn="UNVAN"),
            lambda p: p["entities"].append(dict(p["entities"][0], entity="other", extract="extra")),
        ):
            manifest, profile = build(self.directory, cutoff=SECOND_CUTOFF)
            mutate(profile)
            with self.assertRaises(tool.DryRunViolation):
                run(self.directory, manifest, profile, previous=first)
        altered = copy.deepcopy(first)
        altered["source"]["sourceSystem"] = "Başka sentetik kaynak"
        self.assertViolation("another source", documents=build(self.directory, cutoff=SECOND_CUTOFF), previous=altered)

    def test_previous_report_fences_duplicate_rows_and_summaries_are_validated(self) -> None:
        first = run(self.directory, *build(self.directory))
        for mutate in (
            lambda r: r.update(writesErp=True),
            lambda r: r["entities"][0]["rows"][0].update(mappedAction="DELETE"),
            lambda r: r["entities"][0]["actions"].update(UPSERT=999),
            lambda r: r["entities"][0]["rows"][0].update(rowSha256="bad"),
            lambda r: r["entities"].append(copy.deepcopy(r["entities"][0])),
            lambda r: r["entities"][0]["rows"][1].update(sourceId="C1"),
        ):
            altered = copy.deepcopy(first)
            mutate(altered)
            with self.assertRaises(tool.DryRunViolation):
                run(self.directory, *build(self.directory, cutoff=SECOND_CUTOFF), previous=altered)

    def test_frozen_schema_engine_hash_is_checked_without_a_fallback(self) -> None:
        tool._engine.cache_clear()
        try:
            with patch.object(tool, "_read_input_file", return_value=b"raise RuntimeError('must not execute')\n"):
                with self.assertRaisesRegex(tool.DryRunViolation, "contract_engine_changed"):
                    tool._engine()
        finally:
            tool._engine.cache_clear()

    def test_extract_file_limit_and_non_regular_input_are_rejected(self) -> None:
        path = self.directory / "small.csv"
        path.write_bytes(b"abc")
        with self.assertRaises(tool.InputUnavailable):
            tool._read_input_file(path, "fixture", 2)
        with self.assertRaises(tool.InputUnavailable):
            tool._read_input_file(self.directory, "fixture", 10)

    def test_dry_run_preserves_all_input_bytes_and_is_deterministic(self) -> None:
        manifest, profile = build(self.directory)
        before = {p.relative_to(self.directory).as_posix(): p.read_bytes()
                  for p in self.directory.rglob("*") if p.is_file()}
        first = tool.canonical_bytes(run(self.directory, manifest, profile))
        second = tool.canonical_bytes(run(self.directory, manifest, profile))
        after = {p.relative_to(self.directory).as_posix(): p.read_bytes()
                 for p in self.directory.rglob("*") if p.is_file()}
        self.assertEqual(before, after)
        self.assertEqual(first, second)

    def test_cli_diagnostics_do_not_echo_source_identity_on_contract_failure(self) -> None:
        manifest, profile = build(self.directory, party="KOD;UNVAN;DURUM\nSENSITIVE-SYNTHETIC-ID;Deneme;A\n")
        manifest["extracts"][0]["rowCount"] = 99
        (self.directory / "manifest.json").write_bytes(_bytes(manifest))
        (self.directory / "profile.json").write_bytes(_bytes(profile))
        stdout, stderr = io.StringIO(), io.StringIO()
        output = self.directory / "not-created.json"
        with redirect_stdout(stdout), redirect_stderr(stderr):
            code = tool.main(["--manifest", str(self.directory / "manifest.json"), "--profile", str(self.directory / "profile.json"),
                              "--extract-root", str(self.directory / "extracts"), "--value-map-root", str(self.directory / "maps"),
                              "--synthetic-fixture", "--output", str(output)])
        self.assertEqual(1, code)
        self.assertEqual("", stdout.getvalue())
        self.assertNotIn("SENSITIVE-SYNTHETIC-ID", stderr.getvalue())
        self.assertNotIn(str(self.directory), stderr.getvalue())
        self.assertFalse(output.exists())

    def test_cli_report_is_deterministic_and_never_overwritten_or_written_into_the_repository(self) -> None:
        manifest, profile = build(self.directory)
        (self.directory / "manifest.json").write_bytes(_bytes(manifest))
        (self.directory / "profile.json").write_bytes(_bytes(profile))

        def cli(output: Path) -> tuple[int, str, str]:
            stdout, stderr = io.StringIO(), io.StringIO()
            with redirect_stdout(stdout), redirect_stderr(stderr):
                code = tool.main([
                    "--repository-root", str(REPOSITORY_ROOT),
                    "--manifest", str(self.directory / "manifest.json"),
                    "--extract-root", str(self.directory / "extracts"),
                    "--profile", str(self.directory / "profile.json"),
                    "--value-map-root", str(self.directory / "maps"),
                    "--synthetic-fixture",
                    "--output", str(output),
                ])
            return code, stdout.getvalue(), stderr.getvalue()

        first_output = self.directory / "first.json"
        second_output = self.directory / "second.json"
        self.assertEqual(0, cli(first_output)[0])
        code, stdout, _ = cli(second_output)
        self.assertEqual(0, code)
        self.assertEqual(first_output.read_bytes(), second_output.read_bytes())
        self.assertIs(False, json.loads(stdout)["writesErp"])
        code, _, stderr = cli(first_output)
        self.assertEqual(2, code)
        self.assertIn("never overwritten", stderr)
        inside = REPOSITORY_ROOT / "legacy-dry-run-must-not-exist.json"
        code, _, stderr = cli(inside)
        self.assertEqual(2, code)
        self.assertFalse(inside.exists())
        self.assertIn("never written inside the repository", stderr)


if __name__ == "__main__":
    unittest.main()
