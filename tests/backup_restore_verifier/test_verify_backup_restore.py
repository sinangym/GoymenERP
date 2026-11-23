from __future__ import annotations

import copy
import dataclasses
import datetime as dt
import hashlib
import importlib.util
import io
import json
import os
import sys
from pathlib import Path
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = REPOSITORY_ROOT / "tools" / "verify_backup_restore.py"
SPEC = importlib.util.spec_from_file_location("verify_backup_restore", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = VERIFIER
SPEC.loader.exec_module(VERIFIER)

POLICY_PATH = REPOSITORY_ROOT / "infra" / "backup" / "backup-restore-policy.v1.json"
EXAMPLE_EVIDENCE_PATH = REPOSITORY_ROOT / "infra" / "backup" / "restore-drill-evidence.example.json"
UTC = dt.timezone.utc
OFFSET_MINUTES = 180
STEP = dt.timedelta(minutes=5)


def utc(text: str) -> dt.datetime:
    return dt.datetime.strptime(text, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)


def stamp(moment: dt.datetime) -> str:
    return moment.strftime("%Y-%m-%dT%H:%M:%SZ")


def policy():
    return VERIFIER.parse_policy(json.loads(POLICY_PATH.read_text(encoding="utf-8")))


def backup_row(backup_id: int, kind: str, start: dt.datetime, finish: dt.datetime, first: int, last: int) -> dict:
    return {
        "backupSetId": backup_id,
        "type": kind,
        "isCopyOnly": False,
        "startUtc": stamp(start),
        "finishUtc": stamp(finish),
        "timeZoneOffsetMinutes": OFFSET_MINUTES,
        "firstLsn": str(first),
        "lastLsn": str(last),
        "checkpointLsn": str(first),
        "databaseBackupLsn": "0",
        "hasBackupChecksums": True,
        "isDamaged": False,
        "keyAlgorithm": "aes_256",
        "position": 1,
        "mediaFamilies": [{"physicalDeviceName": f"D:\\Backup\\GoymenErp\\{kind}_{backup_id}.bak"}],
        "files": (
            [{"logicalName": "GoymenErp", "fileType": "D"}, {"logicalName": "GoymenErp_log", "fileType": "L"}]
            if kind == "D"
            else []
        ),
    }


def history_document(
    start: str,
    end: str,
    *,
    skip_full_days: frozenset[str] = frozenset(),
    exported: str | None = None,
    recovery_model: str = "FULL",
) -> dict:
    """A full backup every UTC day at 01:00 and a log backup at minutes 5, 20, 35 and 50."""
    moment, finish_limit = utc(start), utc(exported or end)
    last_moment = utc(end)
    lsn = 1_000_000
    rows: list[dict] = []
    backup_id = 1
    while moment <= last_moment:
        if moment.hour == 1 and moment.minute == 0 and moment.date().isoformat() not in skip_full_days:
            rows.append(backup_row(backup_id, "D", moment, moment + dt.timedelta(minutes=10), lsn + 10, lsn + 50))
            backup_id += 1
        if moment.minute % 15 == 5:
            rows.append(backup_row(backup_id, "L", moment, moment + dt.timedelta(minutes=1), lsn, lsn + 100))
            backup_id += 1
            lsn += 100
        moment += STEP
    return {
        "schema": "goymen-erp-backup-history/v1",
        "databaseName": "GoymenErp",
        "recoveryModel": recovery_model,
        "exportedAtUtc": stamp(finish_limit),
        "backups": [row for row in rows if utc(row["finishUtc"]) <= finish_limit],
    }


def two_day_history(**overrides) -> dict:
    return history_document("2026-11-14T00:00:00Z", "2026-11-15T02:05:00Z", **overrides)


def row_at(document: dict, kind: str, start: str) -> dict:
    return next(row for row in document["backups"] if row["type"] == kind and row["startUtc"] == start)


def check(document: dict, *, as_of: str | None = None, encryption: bool = False):
    return VERIFIER.check_history(
        VERIFIER.parse_history(document),
        policy(),
        as_of=None if as_of is None else utc(as_of),
        require_native_encryption=encryption,
    )


def evidence(**overrides) -> dict:
    document = json.loads(EXAMPLE_EVIDENCE_PATH.read_text(encoding="utf-8"))
    document["isExample"] = False
    document["drillId"] = "DRILL-TEST"
    document["notes"] = ""
    document.update(overrides)
    return document


def drill_report(document: dict):
    return VERIFIER.check_drill(VERIFIER.parse_evidence(document), policy())


class PolicyTests(unittest.TestCase):
    def test_repository_policy_carries_the_kln_d36_target(self) -> None:
        document = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        parsed = VERIFIER.parse_policy(document)
        self.assertEqual(parsed.decision, "KLN-D36")
        self.assertEqual(parsed.rpo, dt.timedelta(minutes=15))
        self.assertEqual(parsed.rto, dt.timedelta(hours=4))
        self.assertEqual(parsed.log_backup_interval, dt.timedelta(minutes=15))
        self.assertEqual(document["openItems"], ["OPEN-017", "OPEN-014"])

    def test_log_interval_longer_than_rpo_is_rejected(self) -> None:
        document = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        document["logBackupIntervalMinutes"] = document["rpoMinutes"] + 1
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_policy(document)

    def test_unknown_key_and_boolean_minutes_are_rejected(self) -> None:
        document = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_policy({**document, "retentionDays": 30})
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_policy({**document, "rpoMinutes": True})

    def test_duplicate_json_keys_are_rejected(self) -> None:
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.load_json_text('{"a": 1, "a": 2}', "test")

    def test_cli_policy_passes(self) -> None:
        out = io.StringIO()
        self.assertEqual(VERIFIER.run(["policy", "--policy", str(POLICY_PATH)], out), VERIFIER.EXIT_PASSED)
        self.assertIn("KLN-D36", out.getvalue())


class HistoryTests(unittest.TestCase):
    def test_contiguous_history_passes_with_exposure_exactly_at_rpo(self) -> None:
        report = check(two_day_history())
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("HISTORY_SUMMARY", report.codes(failed=False))

    def test_logs_before_the_first_full_are_not_reported(self) -> None:
        report = check(two_day_history())
        self.assertNotIn("LOG_UNANCHORED", report.codes())

    def test_exposure_one_second_over_rpo_fails(self) -> None:
        document = two_day_history(exported="2026-11-15T02:10:00Z")
        document["backups"] = [row for row in document["backups"] if utc(row["startUtc"]) <= utc("2026-11-15T01:50:00Z")]
        report = check(document, as_of="2026-11-15T02:05:01Z")
        self.assertEqual(report.codes(failed=True), ["RPO_EXPOSURE"])

    def test_missing_log_breaks_the_chain_and_opens_an_rpo_gap(self) -> None:
        document = two_day_history()
        document["backups"].remove(row_at(document, "L", "2026-11-14T12:20:00Z"))
        failed = check(document).codes(failed=True)
        self.assertIn("LOG_CHAIN_BREAK", failed)
        self.assertIn("RPO_GAP", failed)
        self.assertIn("LOG_UNANCHORED", failed)

    def test_damaged_log_is_excluded_from_the_chain(self) -> None:
        document = two_day_history()
        row_at(document, "L", "2026-11-14T12:20:00Z")["isDamaged"] = True
        failed = check(document).codes(failed=True)
        self.assertIn("BACKUP_DAMAGED", failed)
        self.assertIn("LOG_CHAIN_BREAK", failed)
        self.assertIn("RPO_GAP", failed)

    def test_missing_full_backup_day_fails(self) -> None:
        document = history_document(
            "2026-11-14T00:00:00Z", "2026-11-16T02:05:00Z", skip_full_days=frozenset({"2026-11-15"})
        )
        report = check(document)
        self.assertEqual(report.codes(failed=True), ["FULL_BACKUP_MISSING_DAY"])

    def test_today_without_full_is_due_not_failed(self) -> None:
        document = history_document("2026-11-14T00:00:00Z", "2026-11-15T00:55:00Z")
        report = check(document)
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("FULL_BACKUP_DUE_TODAY", report.codes(failed=False))

    def test_checksum_and_encryption_flags(self) -> None:
        document = two_day_history()
        row_at(document, "L", "2026-11-15T01:20:00Z")["hasBackupChecksums"] = False
        row_at(document, "L", "2026-11-15T01:35:00Z")["keyAlgorithm"] = None
        self.assertEqual(check(document).codes(failed=True), ["BACKUP_WITHOUT_CHECKSUM"])
        self.assertEqual(
            check(document, encryption=True).codes(failed=True), ["BACKUP_WITHOUT_CHECKSUM", "BACKUP_NOT_ENCRYPTED"]
        )

    def test_simple_recovery_model_fails(self) -> None:
        self.assertIn("RECOVERY_MODEL_NOT_FULL", check(two_day_history(recovery_model="SIMPLE")).codes(failed=True))

    def test_copy_only_backup_is_ignored(self) -> None:
        document = two_day_history()
        extra = copy.deepcopy(row_at(document, "D", "2026-11-15T01:00:00Z"))
        extra.update({"backupSetId": 99_999, "isCopyOnly": True})
        document["backups"].append(extra)
        report = check(document)
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("COPY_ONLY_IGNORED", report.codes(failed=False))

    def test_no_full_backup_fails(self) -> None:
        document = two_day_history()
        document["backups"] = [row for row in document["backups"] if row["type"] == "L"]
        self.assertEqual(check(document).codes(failed=True), ["NO_FULL_BACKUP"])

    def test_unknown_time_zone_is_an_input_error(self) -> None:
        document = two_day_history()
        document["backups"][0]["timeZoneOffsetMinutes"] = None
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_history(document)

    def test_as_of_after_export_is_an_input_error(self) -> None:
        with self.assertRaises(VERIFIER.InputError):
            check(two_day_history(), as_of="2026-11-15T02:05:01Z")

    def test_timestamps_without_z_are_rejected(self) -> None:
        document = two_day_history()
        document["exportedAtUtc"] = "2026-11-15T02:05:00"
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_history(document)

    def test_cli_history_exit_codes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            good = Path(directory) / "good.json"
            good.write_text(json.dumps(two_day_history()), encoding="utf-8")
            bad = Path(directory) / "bad.json"
            bad.write_text(json.dumps(two_day_history(recovery_model="SIMPLE")), encoding="utf-8")
            broken = Path(directory) / "broken.json"
            broken.write_text("{", encoding="utf-8")
            base = ["history", "--policy", str(POLICY_PATH), "--require-native-encryption", "no", "--history"]
            self.assertEqual(VERIFIER.run([*base, str(good)], io.StringIO()), VERIFIER.EXIT_PASSED)
            self.assertEqual(VERIFIER.run([*base, str(bad)], io.StringIO()), VERIFIER.EXIT_FAILED)
            self.assertEqual(VERIFIER.run([*base, str(broken)], io.StringIO()), VERIFIER.EXIT_INPUT_ERROR)

    def test_cli_history_requires_an_explicit_encryption_choice(self) -> None:
        self.assertEqual(
            VERIFIER.run(["history", "--history", "x.json"], io.StringIO()), VERIFIER.EXIT_INPUT_ERROR
        )


class RestorePlanTests(unittest.TestCase):
    def plan(self, document: dict, point: str | None, **overrides):
        options = {
            "recovery_point": None if point is None else utc(point),
            "target_database": "GoymenErp_Drill",
            "data_directory": "E:\\SqlData",
            "log_directory": "F:\\SqlLog",
            "allow_source_name": False,
        }
        options.update(overrides)
        return VERIFIER.build_restore_plan(VERIFIER.parse_history(document), **options)

    def test_point_in_time_plan_uses_latest_full_and_contiguous_logs(self) -> None:
        plan = self.plan(two_day_history(), "2026-11-15T01:42:30Z")
        self.assertEqual(plan.full.start, utc("2026-11-15T01:00:00Z"))
        self.assertEqual(
            [stamp(log.start) for log in plan.logs],
            ["2026-11-15T01:05:00Z", "2026-11-15T01:20:00Z", "2026-11-15T01:35:00Z", "2026-11-15T01:50:00Z"],
        )
        self.assertEqual(plan.script.count("RESTORE LOG [GoymenErp_Drill]"), 4)
        self.assertEqual(plan.script.count("STOPAT = N'2026-11-15T04:42:30'"), 4)
        self.assertIn("THROW 59171", plan.script)
        self.assertIn("MOVE N'GoymenErp' TO N'E:\\SqlData\\GoymenErp_Drill_GoymenErp.mdf'", plan.script)
        self.assertIn("MOVE N'GoymenErp_log' TO N'F:\\SqlLog\\GoymenErp_Drill_GoymenErp_log.ldf'", plan.script)
        self.assertTrue(plan.script.rstrip().endswith("RESTORE DATABASE [GoymenErp_Drill] WITH RECOVERY;\nGO"))
        self.assertNotIn("REPLACE", plan.script)

    def test_latest_plan_restores_the_whole_chain_without_stopat(self) -> None:
        plan = self.plan(two_day_history(), None)
        self.assertEqual(stamp(plan.reached_point), "2026-11-15T01:50:00Z")
        self.assertNotIn("STOPAT", plan.script)

    def test_source_database_rejects_comment_escape_at_parser_and_plan_boundaries(self) -> None:
        clean = VERIFIER.parse_history(two_day_history())
        for separator in ("\n", "\r", "\r\n", "\x00", "\t", "\x1b", "\x7f", "\x85", "\u2028", "\u2029",
                          "\u202a", "\u202e", "\u2066", "\u2069", "\u200b", "\ufeff"):
            with self.subTest(separator=repr(separator)):
                name = f"Source{separator}SELECT 1201 AS injected_statement;"
                document = two_day_history()
                document["databaseName"] = name
                with self.assertRaises(VERIFIER.InputError):
                    VERIFIER.parse_history(document)
                with self.assertRaises(VERIFIER.InputError):
                    VERIFIER.build_restore_plan(
                        dataclasses.replace(clean, database_name=name), recovery_point=None,
                        target_database="GoymenErp_Drill", data_directory="E:\\SqlData",
                        log_directory="F:\\SqlLog", allow_source_name=False,
                    )
        document = two_day_history()
        document["databaseName"] = "Göymen's ERP -- source"
        script = self.plan(document, None).script
        self.assertIn("-- Source database: Göymen's ERP -- source;", script)

    def test_nonadvancing_log_is_rejected_before_latest_or_point_plan(self) -> None:
        document = two_day_history()
        row = row_at(document, "L", "2026-11-15T01:20:00Z")
        row["lastLsn"] = row["firstLsn"]
        for point in (None, "2026-11-15T01:42:30Z"):
            with self.subTest(point=point), self.assertRaises(VERIFIER.InputError):
                self.plan(document, point)

    def test_direct_plan_rejects_nonadvancing_or_repeated_log_identifiers(self) -> None:
        history = VERIFIER.parse_history(two_day_history())
        previous = next(log for log in history.backups if log.type == "L" and stamp(log.start) == "2026-11-15T01:05:00Z")
        selected = next(log for log in history.backups if log.type == "L" and stamp(log.start) == "2026-11-15T01:20:00Z")
        for malformed in (
            dataclasses.replace(selected, last_lsn=selected.first_lsn),
            dataclasses.replace(selected, last_lsn=selected.first_lsn - 1),
            dataclasses.replace(selected, backup_set_id=previous.backup_set_id),
        ):
            with self.subTest(malformed=malformed):
                replaced = dataclasses.replace(history, backups=tuple(malformed if row is selected else row for row in history.backups))
                with self.assertRaises(VERIFIER.PlanError) as raised:
                    VERIFIER.build_restore_plan(
                        replaced, recovery_point=None, target_database="GoymenErp_Drill",
                        data_directory="E:\\SqlData", log_directory="F:\\SqlLog", allow_source_name=False,
                    )
                self.assertEqual(raised.exception.code, "LOG_CHAIN_NOT_ADVANCING")

    def test_cli_malformed_metadata_or_log_creates_no_script(self) -> None:
        documents = [two_day_history(), two_day_history()]
        documents[0]["databaseName"] = "Source\nSELECT 1201 AS injected_statement;"
        row = row_at(documents[1], "L", "2026-11-15T01:20:00Z")
        row["lastLsn"] = row["firstLsn"]
        with tempfile.TemporaryDirectory() as directory:
            history = Path(directory) / "history.json"
            output = Path(directory) / "restore.sql"
            for document in documents:
                with self.subTest(document=document["databaseName"]):
                    history.write_text(json.dumps(document), encoding="utf-8")
                    result = VERIFIER.run([
                        "restore-plan", "--history", str(history), "--recovery-point", "latest",
                        "--target-database", "GoymenErp_Drill", "--data-directory", "E:\\SqlData",
                        "--log-directory", "F:\\SqlLog", "--output", str(output),
                    ], io.StringIO())
                    self.assertEqual(result, VERIFIER.EXIT_INPUT_ERROR)
                    self.assertFalse(output.exists())

    def test_point_beyond_the_last_log_is_not_covered(self) -> None:
        with self.assertRaises(VERIFIER.PlanError) as raised:
            self.plan(two_day_history(), "2026-11-15T02:00:00Z")
        self.assertEqual(raised.exception.code, "POINT_NOT_COVERED")

    def test_damaged_log_stops_the_chain(self) -> None:
        document = two_day_history()
        row_at(document, "L", "2026-11-15T01:20:00Z")["isDamaged"] = True
        with self.assertRaises(VERIFIER.PlanError) as raised:
            self.plan(document, "2026-11-15T01:42:30Z")
        self.assertEqual(raised.exception.code, "POINT_NOT_COVERED")

    def test_point_before_any_full_has_no_plan(self) -> None:
        with self.assertRaises(VERIFIER.PlanError) as raised:
            self.plan(two_day_history(), "2026-11-14T00:30:00Z")
        self.assertEqual(raised.exception.code, "NO_FULL_BEFORE_POINT")

    def test_target_name_rules(self) -> None:
        with self.assertRaises(VERIFIER.InputError):
            self.plan(two_day_history(), None, target_database="goymenerp")
        self.assertIsNotNone(self.plan(two_day_history(), None, target_database="GoymenErp", allow_source_name=True))
        with self.assertRaises(VERIFIER.InputError):
            self.plan(two_day_history(), None, target_database="x];DROP")
        with self.assertRaises(VERIFIER.InputError):
            self.plan(two_day_history(), None, data_directory="E:\\SqlData\\")

    def test_backup_without_checksums_is_restored_without_the_checksum_option(self) -> None:
        document = two_day_history()
        row_at(document, "L", "2026-11-15T01:20:00Z")["hasBackupChecksums"] = False
        plan = self.plan(document, "2026-11-15T01:42:30Z")
        self.assertEqual(plan.script.count("CHECKSUM"), 4)

    def test_cli_writes_a_new_file_and_refuses_to_overwrite(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            history = Path(directory) / "history.json"
            history.write_text(json.dumps(two_day_history()), encoding="utf-8")
            output = Path(directory) / "restore.sql"
            arguments = [
                "restore-plan",
                "--history", str(history),
                "--recovery-point", "2026-11-15T01:42:30Z",
                "--target-database", "GoymenErp_Drill",
                "--data-directory", "E:\\SqlData",
                "--log-directory", "F:\\SqlLog",
                "--output", str(output),
            ]
            self.assertEqual(VERIFIER.run(arguments, io.StringIO()), VERIFIER.EXIT_PASSED)
            self.assertIn("RESTORE DATABASE [GoymenErp_Drill]", output.read_text(encoding="utf-8"))
            self.assertEqual(VERIFIER.run(arguments, io.StringIO()), VERIFIER.EXIT_INPUT_ERROR)

    def test_cli_uncovered_point_fails_without_writing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            history = Path(directory) / "history.json"
            history.write_text(json.dumps(two_day_history()), encoding="utf-8")
            output = Path(directory) / "restore.sql"
            code = VERIFIER.run(
                [
                    "restore-plan",
                    "--history", str(history),
                    "--recovery-point", "2026-11-15T02:00:00Z",
                    "--target-database", "GoymenErp_Drill",
                    "--data-directory", "E:\\SqlData",
                    "--log-directory", "F:\\SqlLog",
                    "--output", str(output),
                ],
                io.StringIO(),
            )
            self.assertEqual(code, VERIFIER.EXIT_FAILED)
            self.assertFalse(output.exists())


class DrillTests(unittest.TestCase):
    def test_valid_evidence_passes(self) -> None:
        report = drill_report(evidence())
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("DRILL_MEASURED", report.codes(failed=False))

    def test_repository_example_is_well_formed_but_never_passes(self) -> None:
        document = json.loads(EXAMPLE_EVIDENCE_PATH.read_text(encoding="utf-8"))
        self.assertEqual(drill_report(document).codes(failed=True), ["EXAMPLE_EVIDENCE"])

    def test_observed_rpo_boundary_is_exact(self) -> None:
        self.assertEqual(drill_report(evidence(recoveredToUtc="2026-11-15T09:45:00Z")).codes(failed=True), [])
        self.assertEqual(
            drill_report(evidence(recoveredToUtc="2026-11-15T09:44:59Z")).codes(failed=True), ["DRILL_RPO_EXCEEDED"]
        )

    def test_observed_rto_boundary_is_exact(self) -> None:
        self.assertEqual(drill_report(evidence(serviceVerifiedUtc="2026-11-15T14:00:00Z")).codes(failed=True), [])
        self.assertEqual(
            drill_report(evidence(serviceVerifiedUtc="2026-11-15T14:00:01Z")).codes(failed=True), ["DRILL_RTO_EXCEEDED"]
        )

    def test_production_and_source_target_guards(self) -> None:
        self.assertIn("DRILL_IN_PRODUCTION", drill_report(evidence(environment="Production")).codes(failed=True))
        self.assertIn("DRILL_TARGET_IS_SOURCE", drill_report(evidence(targetDatabase="goymenerp")).codes(failed=True))

    def test_not_passed_checks_and_incomplete_objects_fail(self) -> None:
        document = evidence()
        document["checks"]["dispatchersDisabled"] = "not-run"
        document["documentObjects"] = {"expected": 3, "present": 2, "missing": 1, "mismatched": 0}
        failed = drill_report(document).codes(failed=True)
        self.assertIn("DRILL_CHECK_NOT_PASSED", failed)
        self.assertIn("DRILL_DOCUMENT_OBJECTS_INCOMPLETE", failed)

    def test_timeline_must_be_ordered(self) -> None:
        failed = drill_report(evidence(restoreStartedUtc="2026-11-15T09:59:00Z")).codes(failed=True)
        self.assertEqual(failed, ["DRILL_TIMELINE_INVALID"])

    def test_schema_violations_are_input_errors(self) -> None:
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_evidence({**evidence(), "operator": "x"})
        document = evidence()
        document["checks"]["checkDb"] = "ok"
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_evidence(document)


class DrillCadenceTests(unittest.TestCase):
    def drills(self, *incidents: str, failed: frozenset[str] = frozenset()):
        result = []
        for index, incident in enumerate(incidents):
            moment = utc(incident)
            document = evidence(
                drillId=f"DRILL-{index}",
                simulatedIncidentUtc=stamp(moment),
                recoveredToUtc=stamp(moment - dt.timedelta(minutes=5)),
                restoreStartedUtc=stamp(moment + dt.timedelta(minutes=5)),
                serviceVerifiedUtc=stamp(moment + dt.timedelta(hours=1)),
            )
            if incident in failed:
                document["checks"]["checkDb"] = "failed"
            result.append(VERIFIER.parse_evidence(document))
        return result

    def cadence(self, evidences, as_of: str, since: tuple[int, int] | None = None):
        return VERIFIER.check_drill_cadence(evidences, policy(), as_of=utc(as_of), since=since)

    def test_monthly_drills_pass_and_the_current_month_is_due(self) -> None:
        report = self.cadence(self.drills("2026-09-10T10:00:00Z", "2026-10-08T10:00:00Z"), "2026-11-15T00:00:00Z")
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("DRILL_DUE_THIS_MONTH", report.codes(failed=False))

    def test_missing_month_fails(self) -> None:
        report = self.cadence(self.drills("2026-08-10T10:00:00Z", "2026-10-08T10:00:00Z"), "2026-10-20T00:00:00Z")
        self.assertEqual(report.codes(failed=True), ["DRILL_MISSING_MONTH"])

    def test_failed_drill_does_not_count(self) -> None:
        incidents = ("2026-09-10T10:00:00Z", "2026-10-08T10:00:00Z")
        report = self.cadence(self.drills(*incidents, failed=frozenset({incidents[1]})), "2026-11-15T00:00:00Z")
        self.assertEqual(report.codes(failed=True), ["DRILL_MISSING_MONTH"])
        self.assertIn("DRILL_FAILED_IGNORED", report.codes(failed=False))

    def test_since_reaches_back_before_the_first_drill_and_across_the_year(self) -> None:
        report = self.cadence(self.drills("2027-01-10T10:00:00Z"), "2027-01-20T00:00:00Z", since=(2026, 11))
        self.assertEqual(report.codes(failed=True), ["DRILL_MISSING_MONTH", "DRILL_MISSING_MONTH"])

    def test_duplicate_drill_ids_are_input_errors(self) -> None:
        drills = self.drills("2026-09-10T10:00:00Z")
        with self.assertRaises(VERIFIER.InputError):
            self.cadence(drills + drills, "2026-10-01T00:00:00Z")


class ObjectTests(unittest.TestCase):
    COMPANY = "0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0"

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name) / "objects"
        self.root.mkdir()
        self.blobs = [self.store(b"first document"), self.store(b"second document")]

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def store(self, content: bytes) -> dict:
        digest = hashlib.sha256(content).hexdigest()
        key = f"companies/{self.COMPANY.replace('-', '')}/sha256/{digest}"
        path = self.root.joinpath(*key.split("/"))
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return {"companyId": self.COMPANY, "sha256": digest, "sizeBytes": len(content), "objectKey": key}

    def manifest(self, blobs: list[dict]) -> dict:
        return {
            "schema": "goymen-erp-document-blob-manifest/v1",
            "databaseName": "GoymenErp_Drill",
            "exportedAtUtc": "2026-11-15T10:30:00Z",
            "blobs": blobs,
        }

    def check(self, blobs: list[dict] | None = None):
        return VERIFIER.check_objects(VERIFIER.parse_manifest(self.manifest(self.blobs if blobs is None else blobs)), self.root)

    def test_complete_object_directory_passes(self) -> None:
        report, counts = self.check()
        self.assertEqual(report.codes(failed=True), [])
        self.assertEqual((counts.expected, counts.present, counts.missing, counts.mismatched), (2, 2, 0, 0))

    def test_missing_object_fails(self) -> None:
        self.root.joinpath(*self.blobs[0]["objectKey"].split("/")).unlink()
        report, counts = self.check()
        self.assertEqual(report.codes(failed=True), ["OBJECT_MISSING"])
        self.assertEqual(counts.missing, 1)

    def test_size_and_hash_mismatches_fail(self) -> None:
        first = self.root.joinpath(*self.blobs[0]["objectKey"].split("/"))
        first.write_bytes(b"first document, longer")
        second = self.root.joinpath(*self.blobs[1]["objectKey"].split("/"))
        second.write_bytes(b"SECOND DOCUMENT")
        report, counts = self.check()
        self.assertEqual(report.codes(failed=True), ["OBJECT_SIZE_MISMATCH", "OBJECT_HASH_MISMATCH"])
        self.assertEqual(counts.mismatched, 2)

    def test_extra_and_partial_files_are_informational(self) -> None:
        self.store(b"written after the recovery point")
        partial = self.root / "companies" / self.COMPANY.replace("-", "") / "sha256" / ".unfinished.partial"
        partial.write_bytes(b"x")
        report, _ = self.check()
        self.assertEqual(report.codes(failed=True), [])
        self.assertIn("EXTRA_OBJECTS", report.codes(failed=False))
        self.assertIn("PARTIAL_FILES", report.codes(failed=False))

    def test_symlinked_object_fails(self) -> None:
        target = self.root.joinpath(*self.blobs[0]["objectKey"].split("/"))
        outside = Path(self.temporary.name) / "outside"
        outside.write_bytes(target.read_bytes())
        target.unlink()
        try:
            os.symlink(outside, target)
        except (OSError, NotImplementedError):
            self.skipTest("symbolic links are not available")
        report, _ = self.check()
        self.assertEqual(report.codes(failed=True), ["OBJECT_SYMLINK"])

    def test_manifest_key_must_match_company_and_hash(self) -> None:
        broken = dict(self.blobs[0], objectKey=self.blobs[1]["objectKey"])
        with self.assertRaises(VERIFIER.InputError):
            VERIFIER.parse_manifest(self.manifest([broken]))

    def test_cli_objects_exit_codes(self) -> None:
        manifest = Path(self.temporary.name) / "manifest.json"
        manifest.write_text(json.dumps(self.manifest(self.blobs)), encoding="utf-8")
        arguments = ["objects", "--manifest", str(manifest), "--root", str(self.root)]
        self.assertEqual(VERIFIER.run(arguments, io.StringIO()), VERIFIER.EXIT_PASSED)
        self.root.joinpath(*self.blobs[0]["objectKey"].split("/")).unlink()
        self.assertEqual(VERIFIER.run(arguments, io.StringIO()), VERIFIER.EXIT_FAILED)


if __name__ == "__main__":
    unittest.main()
