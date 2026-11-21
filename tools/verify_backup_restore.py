#!/usr/bin/env python3
"""Offline backup/restore verifier for Göymen ERP (KLN-D36: RPO 15 min / RTO 4 h).

The tool never connects to SQL Server, never runs a backup or a restore and never
schedules anything. It only reads files the operator exported:

* ``policy``        validates ``infra/backup/backup-restore-policy.v1.json``.
* ``history``       checks an msdb export (``infra/backup/sql/40_export_backup_history.sql``)
                    against the policy: FULL recovery model, one full backup per UTC day,
                    damaged/checksum/encryption flags, log chain continuity and RPO gaps.
* ``drill``         checks one restore drill evidence file (observed RPO/RTO and checks).
* ``drill-cadence`` checks that every UTC month has at least one passed drill.
* ``restore-plan``  writes a reviewable T-SQL restore script (full + log chain with
                    STOPAT) for a recovery point; a human reviews and runs it.
* ``objects``       compares a Belge Kasası blob manifest (``50_export_document_blob_manifest.sql``)
                    with a restored object directory (size and SHA-256 of every object).

Measurements are exact: there is no tolerance. Exit codes: 0 passed, 1 a check failed,
2 the input could not be read or is malformed.

"""

# Human runbook (not an executable input): docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import hashlib
import json
import os
import re
import stat
import sys
from pathlib import Path
from typing import Any, Iterable, Sequence

EXIT_PASSED = 0
EXIT_FAILED = 1
EXIT_INPUT_ERROR = 2

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_POLICY_PATH = REPOSITORY_ROOT / "infra" / "backup" / "backup-restore-policy.v1.json"

POLICY_SCHEMA = "goymen-erp-backup-restore-policy/v1"
HISTORY_SCHEMA = "goymen-erp-backup-history/v1"
EVIDENCE_SCHEMA = "goymen-erp-restore-drill-evidence/v1"
MANIFEST_SCHEMA = "goymen-erp-document-blob-manifest/v1"

POLICY_KEYS = frozenset(
    {
        "schema",
        "decision",
        "decisionSource",
        "openItems",
        "rpoMinutes",
        "rtoMinutes",
        "fullBackupCadence",
        "logBackupIntervalMinutes",
        "restoreDrillCadence",
        "calendarTimeZone",
    }
)
HISTORY_KEYS = frozenset({"schema", "databaseName", "recoveryModel", "exportedAtUtc", "backups"})
BACKUP_KEYS = frozenset(
    {
        "backupSetId",
        "type",
        "isCopyOnly",
        "startUtc",
        "finishUtc",
        "timeZoneOffsetMinutes",
        "firstLsn",
        "lastLsn",
        "checkpointLsn",
        "databaseBackupLsn",
        "hasBackupChecksums",
        "isDamaged",
        "keyAlgorithm",
        "position",
        "mediaFamilies",
        "files",
    }
)
EVIDENCE_KEYS = frozenset(
    {
        "schema",
        "isExample",
        "drillId",
        "environment",
        "sourceDatabase",
        "targetDatabase",
        "simulatedIncidentUtc",
        "recoveredToUtc",
        "restoreStartedUtc",
        "serviceVerifiedUtc",
        "checks",
        "documentObjects",
        "notes",
    }
)
EVIDENCE_CHECK_KEYS = (
    "backupVerifyOnly",
    "restoreCompleted",
    "checkDb",
    "migrationHistoryPresent",
    "documentObjects",
    "dispatchersDisabled",
    "applicationSmoke",
)
EVIDENCE_CHECK_STATES = frozenset({"passed", "failed", "not-run"})
DOCUMENT_OBJECT_KEYS = ("expected", "present", "missing", "mismatched")
MANIFEST_KEYS = frozenset({"schema", "databaseName", "exportedAtUtc", "blobs"})
BLOB_KEYS = frozenset({"companyId", "sha256", "sizeBytes", "objectKey"})

FULL_BACKUP = "D"
LOG_BACKUP = "L"
# msdb backupset.type values other than full and log (differential, file, partial)
# are accepted in the export but are not part of the KLN-D36 chain.
OTHER_BACKUP_TYPES = frozenset({"I", "F", "G", "P", "Q"})
DATA_FILE_TYPES = frozenset({"D", "F"})
LOG_FILE_TYPE = "L"
PRODUCTION_ENVIRONMENT = "production"

UTC = dt.timezone.utc
UTC_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
MONTH_PATTERN = re.compile(r"^\d{4}-\d{2}$")
LSN_PATTERN = re.compile(r"^[0-9]{1,25}$")
DATABASE_NAME_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,99}$")
LOGICAL_FILE_NAME_PATTERN = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")
COMPANY_ID_PATTERN = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
OBJECT_KEY_PATTERN = re.compile(r"^companies/[0-9a-f]{32}/sha256/[0-9a-f]{64}$")
TEMPORARY_OBJECT_SUFFIX = ".partial"
HASH_CHUNK_BYTES = 1 << 20
SECONDS_PER_MINUTE = 60
SECONDS_PER_HOUR = 3600


class InputError(Exception):
    """The input file is unreadable or does not follow its schema."""


@dataclasses.dataclass(frozen=True)
class Finding:
    code: str
    failed: bool
    message: str


@dataclasses.dataclass
class Report:
    findings: list[Finding] = dataclasses.field(default_factory=list)

    def fail(self, code: str, message: str) -> None:
        self.findings.append(Finding(code, True, message))

    def info(self, code: str, message: str) -> None:
        self.findings.append(Finding(code, False, message))

    @property
    def passed(self) -> bool:
        return not any(finding.failed for finding in self.findings)

    def codes(self, failed: bool | None = None) -> list[str]:
        return [f.code for f in self.findings if failed is None or f.failed == failed]


# --------------------------------------------------------------------------- input helpers


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise InputError(f"JSON içinde yinelenen anahtar: {key}")
        result[key] = value
    return result


def _reject_constant(name: str) -> Any:
    raise InputError(f"JSON içinde geçersiz sayı: {name}")


def load_json_text(text: str, where: str) -> Any:
    try:
        return json.loads(
            text.lstrip("﻿"),
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_constant,
        )
    except json.JSONDecodeError as error:
        raise InputError(f"{where}: JSON okunamadı ({error.msg}, satır {error.lineno}).") from error


def load_json_file(path: Path) -> Any:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as error:
        raise InputError(f"{path.name}: dosya okunamadı ({error.__class__.__name__}).") from error
    return load_json_text(text, path.name)


def require_object(value: Any, keys: frozenset[str], where: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise InputError(f"{where}: JSON nesnesi bekleniyor.")
    actual = set(value)
    missing = sorted(keys - actual)
    extra = sorted(actual - keys)
    if missing or extra:
        raise InputError(f"{where}: anahtarlar şemayla aynı değil (eksik: {missing}, fazla: {extra}).")
    return value


def require_string(value: Any, where: str, *, allow_empty: bool = False) -> str:
    if not isinstance(value, str) or (not allow_empty and not value.strip()):
        raise InputError(f"{where}: boş olmayan metin bekleniyor.")
    return value


def require_single_line_text(value: Any, where: str) -> str:
    text = require_string(value, where)
    # Metadata emitted into SQL comments must never terminate that comment or
    # inject control characters into the generated, human-reviewed script.
    if any(ord(char) < 32 or 127 <= ord(char) <= 159 or char in "\u2028\u2029" for char in text):
        raise InputError(f"{where}: satır sonu veya kontrol karakteri içeremez.")
    return text


def require_int(value: Any, where: str, *, minimum: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise InputError(f"{where}: en az {minimum} olan tam sayı bekleniyor.")
    return value


def require_bool(value: Any, where: str) -> bool:
    if not isinstance(value, bool):
        raise InputError(f"{where}: true/false bekleniyor.")
    return value


def parse_utc(value: Any, where: str) -> dt.datetime:
    if not isinstance(value, str) or not UTC_PATTERN.match(value):
        raise InputError(f"{where}: YYYY-MM-DDTHH:MM:SSZ biçiminde UTC zaman bekleniyor.")
    try:
        return dt.datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=UTC)
    except ValueError as error:
        raise InputError(f"{where}: geçersiz tarih/saat.") from error


def format_utc(moment: dt.datetime) -> str:
    return moment.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


def format_duration(delta: dt.timedelta) -> str:
    seconds = int(delta.total_seconds())
    sign = "-" if seconds < 0 else ""
    seconds = abs(seconds)
    hours, remainder = divmod(seconds, SECONDS_PER_HOUR)
    minutes, seconds = divmod(remainder, SECONDS_PER_MINUTE)
    return f"{sign}{hours:02d}:{minutes:02d}:{seconds:02d}"


def parse_lsn(value: Any, where: str) -> int:
    if not isinstance(value, str) or not LSN_PATTERN.match(value):
        raise InputError(f"{where}: ondalık LSN metni bekleniyor.")
    return int(value)


def same_database_name(left: str, right: str) -> bool:
    # SQL Server database names compare case-insensitively under the default collation.
    return left.casefold() == right.casefold()


# --------------------------------------------------------------------------- policy


@dataclasses.dataclass(frozen=True)
class Policy:
    decision: str
    rpo: dt.timedelta
    rto: dt.timedelta
    log_backup_interval: dt.timedelta


def parse_policy(document: Any) -> Policy:
    data = require_object(document, POLICY_KEYS, "politika")
    if data["schema"] != POLICY_SCHEMA:
        raise InputError(f"politika: schema {POLICY_SCHEMA} olmalıdır.")
    decision = require_string(data["decision"], "politika.decision")
    require_string(data["decisionSource"], "politika.decisionSource")
    open_items = data["openItems"]
    if not isinstance(open_items, list) or not all(isinstance(item, str) and item.startswith("OPEN-") for item in open_items):
        raise InputError("politika.openItems: OPEN-* kimliklerinden oluşan liste bekleniyor.")
    rpo_minutes = require_int(data["rpoMinutes"], "politika.rpoMinutes", minimum=1)
    rto_minutes = require_int(data["rtoMinutes"], "politika.rtoMinutes", minimum=1)
    log_minutes = require_int(data["logBackupIntervalMinutes"], "politika.logBackupIntervalMinutes", minimum=1)
    if log_minutes > rpo_minutes:
        raise InputError("politika: işlem günlüğü yedek aralığı RPO'dan büyük olamaz.")
    if data["fullBackupCadence"] != "daily":
        raise InputError("politika.fullBackupCadence: yalnız 'daily' desteklenir.")
    if data["restoreDrillCadence"] != "monthly":
        raise InputError("politika.restoreDrillCadence: yalnız 'monthly' desteklenir.")
    if data["calendarTimeZone"] != "UTC":
        raise InputError("politika.calendarTimeZone: yalnız 'UTC' desteklenir.")
    return Policy(
        decision=decision,
        rpo=dt.timedelta(minutes=rpo_minutes),
        rto=dt.timedelta(minutes=rto_minutes),
        log_backup_interval=dt.timedelta(minutes=log_minutes),
    )


# --------------------------------------------------------------------------- backup history


@dataclasses.dataclass(frozen=True)
class BackupFile:
    logical_name: str
    file_type: str


@dataclasses.dataclass(frozen=True)
class Backup:
    backup_set_id: int
    type: str
    is_copy_only: bool
    start: dt.datetime
    finish: dt.datetime
    offset_minutes: int
    first_lsn: int
    last_lsn: int
    checkpoint_lsn: int
    database_backup_lsn: int
    has_checksums: bool
    is_damaged: bool
    key_algorithm: str | None
    position: int
    devices: tuple[str, ...]
    files: tuple[BackupFile, ...]

    @property
    def label(self) -> str:
        return f"#{self.backup_set_id} {self.type} {format_utc(self.start)}"


@dataclasses.dataclass(frozen=True)
class History:
    database_name: str
    recovery_model: str
    exported_at: dt.datetime
    backups: tuple[Backup, ...]


def parse_backup(value: Any, index: int) -> Backup:
    where = f"backups[{index}]"
    data = require_object(value, BACKUP_KEYS, where)
    backup_type = require_string(data["type"], f"{where}.type")
    if backup_type not in {FULL_BACKUP, LOG_BACKUP} | OTHER_BACKUP_TYPES:
        raise InputError(f"{where}.type: bilinmeyen yedek türü.")
    if data["startUtc"] is None or data["finishUtc"] is None or data["timeZoneOffsetMinutes"] is None:
        raise InputError(f"{where}: sunucu saat dilimi bilinmiyor (msdb time_zone=127); zaman tahmin edilmez.")
    start = parse_utc(data["startUtc"], f"{where}.startUtc")
    finish = parse_utc(data["finishUtc"], f"{where}.finishUtc")
    if finish < start:
        raise InputError(f"{where}: bitiş başlangıçtan önce olamaz.")
    offset = data["timeZoneOffsetMinutes"]
    if isinstance(offset, bool) or not isinstance(offset, int):
        raise InputError(f"{where}.timeZoneOffsetMinutes: tam sayı bekleniyor.")
    first_lsn = parse_lsn(data["firstLsn"], f"{where}.firstLsn")
    last_lsn = parse_lsn(data["lastLsn"], f"{where}.lastLsn")
    if last_lsn < first_lsn:
        raise InputError(f"{where}: lastLsn firstLsn'den küçük olamaz.")
    if backup_type == LOG_BACKUP and last_lsn == first_lsn:
        raise InputError(f"{where}: günlük yedeğinin lastLsn değeri firstLsn'den büyük olmalıdır.")
    key_algorithm = data["keyAlgorithm"]
    if key_algorithm is not None:
        key_algorithm = require_string(key_algorithm, f"{where}.keyAlgorithm")
    families = data["mediaFamilies"]
    if not isinstance(families, list) or not families:
        raise InputError(f"{where}.mediaFamilies: en az bir yedek dosyası bekleniyor.")
    devices = tuple(
        require_string(
            require_object(family, frozenset({"physicalDeviceName"}), f"{where}.mediaFamilies[{n}]")["physicalDeviceName"],
            f"{where}.mediaFamilies[{n}].physicalDeviceName",
        )
        for n, family in enumerate(families)
    )
    raw_files = data["files"]
    if not isinstance(raw_files, list):
        raise InputError(f"{where}.files: liste bekleniyor.")
    files = []
    for n, raw in enumerate(raw_files):
        item = require_object(raw, frozenset({"logicalName", "fileType"}), f"{where}.files[{n}]")
        files.append(
            BackupFile(
                require_string(item["logicalName"], f"{where}.files[{n}].logicalName"),
                require_string(item["fileType"], f"{where}.files[{n}].fileType"),
            )
        )
    return Backup(
        backup_set_id=require_int(data["backupSetId"], f"{where}.backupSetId", minimum=1),
        type=backup_type,
        is_copy_only=require_bool(data["isCopyOnly"], f"{where}.isCopyOnly"),
        start=start,
        finish=finish,
        offset_minutes=offset,
        first_lsn=first_lsn,
        last_lsn=last_lsn,
        checkpoint_lsn=parse_lsn(data["checkpointLsn"], f"{where}.checkpointLsn"),
        database_backup_lsn=parse_lsn(data["databaseBackupLsn"], f"{where}.databaseBackupLsn"),
        has_checksums=require_bool(data["hasBackupChecksums"], f"{where}.hasBackupChecksums"),
        is_damaged=require_bool(data["isDamaged"], f"{where}.isDamaged"),
        key_algorithm=key_algorithm,
        position=require_int(data["position"], f"{where}.position", minimum=1),
        devices=devices,
        files=tuple(files),
    )


def parse_history(document: Any) -> History:
    data = require_object(document, HISTORY_KEYS, "yedek geçmişi")
    if data["schema"] != HISTORY_SCHEMA:
        raise InputError(f"yedek geçmişi: schema {HISTORY_SCHEMA} olmalıdır.")
    backups_raw = data["backups"]
    if not isinstance(backups_raw, list):
        raise InputError("yedek geçmişi.backups: liste bekleniyor.")
    backups = [parse_backup(item, index) for index, item in enumerate(backups_raw)]
    identifiers = [backup.backup_set_id for backup in backups]
    if len(set(identifiers)) != len(identifiers):
        raise InputError("yedek geçmişi: yinelenen backupSetId.")
    backups.sort(key=lambda backup: (backup.start, backup.backup_set_id))
    return History(
        database_name=require_single_line_text(data["databaseName"], "yedek geçmişi.databaseName"),
        recovery_model=require_string(data["recoveryModel"], "yedek geçmişi.recoveryModel"),
        exported_at=parse_utc(data["exportedAtUtc"], "yedek geçmişi.exportedAtUtc"),
        backups=tuple(backups),
    )


def find_anchor_full(log: Backup, fulls: Sequence[Backup]) -> Backup | None:
    """The latest usable full backup whose last LSN falls inside this log backup."""
    anchors = [full for full in fulls if log.first_lsn <= full.last_lsn < log.last_lsn]
    return max(anchors, key=lambda full: (full.finish, full.backup_set_id)) if anchors else None


def anchored_logs(fulls: Sequence[Backup], logs: Sequence[Backup]) -> tuple[set[int], list[tuple[Backup, Backup, bool]]]:
    """Returns the ids of logs restorable from some full and the chain breaks (prev, cur, re-anchored)."""
    anchored: set[int] = set()
    breaks: list[tuple[Backup, Backup, bool]] = []
    previous: Backup | None = None
    for log in sorted(logs, key=lambda item: (item.first_lsn, item.start, item.backup_set_id)):
        contiguous = previous is not None and log.first_lsn == previous.last_lsn
        anchor = find_anchor_full(log, fulls)
        if contiguous and previous is not None and previous.backup_set_id in anchored:
            anchored.add(log.backup_set_id)
        elif anchor is not None:
            anchored.add(log.backup_set_id)
        if previous is not None and not contiguous:
            breaks.append((previous, log, log.backup_set_id in anchored))
        previous = log
    return anchored, breaks


def utc_day(moment: dt.datetime) -> dt.date:
    return moment.astimezone(UTC).date()


def check_history(
    history: History,
    policy: Policy,
    *,
    as_of: dt.datetime | None,
    require_native_encryption: bool,
) -> Report:
    report = Report()
    point = history.exported_at if as_of is None else as_of
    if point > history.exported_at:
        raise InputError("--as-of dışa aktarma zamanından sonra olamaz; o aralıktaki yedekler bilinmiyor.")

    if history.recovery_model != "FULL":
        report.fail(
            "RECOVERY_MODEL_NOT_FULL",
            f"Kurtarma modeli {history.recovery_model}; 15 dakikalık işlem günlüğü yedeği yalnız FULL modelde mümkündür.",
        )

    considered: list[Backup] = []
    for backup in history.backups:
        if backup.finish > point:
            continue
        if backup.is_copy_only:
            report.info("COPY_ONLY_IGNORED", f"{backup.label}: COPY_ONLY yedek zincire katılmaz.")
            continue
        if backup.type in OTHER_BACKUP_TYPES:
            report.info("OTHER_BACKUP_TYPE_IGNORED", f"{backup.label}: tam/günlük dışındaki yedek türü değerlendirilmez.")
            continue
        considered.append(backup)

    usable: list[Backup] = []
    for backup in considered:
        if backup.is_damaged:
            report.fail("BACKUP_DAMAGED", f"{backup.label}: msdb yedeği hasarlı olarak işaretlemiş; zincirde kullanılmaz.")
            continue
        if not backup.has_checksums:
            report.fail("BACKUP_WITHOUT_CHECKSUM", f"{backup.label}: yedek CHECKSUM ile alınmamış.")
        if require_native_encryption and backup.key_algorithm is None:
            report.fail("BACKUP_NOT_ENCRYPTED", f"{backup.label}: yedek SQL Server şifrelemesiyle alınmamış.")
        usable.append(backup)

    fulls = [backup for backup in usable if backup.type == FULL_BACKUP]
    if not fulls:
        report.fail("NO_FULL_BACKUP", "Değerlendirme aralığında kullanılabilir tam yedek yok.")
        return report
    first_full = min(fulls, key=lambda full: (full.start, full.backup_set_id))

    full_days = {utc_day(full.start) for full in fulls}
    day = utc_day(first_full.start)
    today = utc_day(point)
    while day < today:
        if day not in full_days:
            report.fail("FULL_BACKUP_MISSING_DAY", f"{day.isoformat()} (UTC) gününde tam yedek yok.")
        day += dt.timedelta(days=1)
    if today not in full_days:
        report.info("FULL_BACKUP_DUE_TODAY", f"{today.isoformat()} (UTC) günü için tam yedek henüz alınmadı.")

    # Log backups that end before the first full backup's last LSN (including those taken
    # while that full backup was running) are not needed to restore from it.
    logs = [backup for backup in usable if backup.type == LOG_BACKUP and backup.last_lsn > first_full.last_lsn]
    anchored, breaks = anchored_logs(fulls, logs)
    for previous, current, re_anchored in breaks:
        if re_anchored:
            report.info(
                "LOG_CHAIN_RESTARTED",
                f"{previous.label} → {current.label}: günlük zinciri kesildi, yeni tam yedekten yeniden başladı.",
            )
        else:
            report.fail(
                "LOG_CHAIN_BREAK",
                f"{previous.label} → {current.label}: LSN zinciri kesik (firstLsn {current.first_lsn} ≠ önceki lastLsn {previous.last_lsn}).",
            )
    for log in logs:
        if log.backup_set_id not in anchored and not any(current is log for _, current, _ in breaks):
            report.fail("LOG_UNANCHORED", f"{log.label}: bir tam yedeğe bağlı LSN zincirinde değil.")

    # Conservative recovery points: the start of each restorable backup.
    points = sorted({full.start for full in fulls} | {log.start for log in logs if log.backup_set_id in anchored})
    for earlier, later in zip(points, points[1:]):
        gap = later - earlier
        if gap > policy.rpo:
            report.fail(
                "RPO_GAP",
                f"{format_utc(earlier)} → {format_utc(later)}: kurtarma noktaları arası {format_duration(gap)} "
                f"(RPO {format_duration(policy.rpo)}).",
            )
    exposure = point - points[-1]
    if exposure > policy.rpo:
        report.fail(
            "RPO_EXPOSURE",
            f"Son kurtarma noktası {format_utc(points[-1])}; {format_utc(point)} itibarıyla açık süre "
            f"{format_duration(exposure)} (RPO {format_duration(policy.rpo)}).",
        )
    report.info(
        "HISTORY_SUMMARY",
        f"{len(fulls)} tam, {len(logs)} günlük yedeği değerlendirildi; son kurtarma noktası {format_utc(points[-1])}.",
    )
    return report


# --------------------------------------------------------------------------- drill evidence


@dataclasses.dataclass(frozen=True)
class DrillEvidence:
    drill_id: str
    is_example: bool
    environment: str
    source_database: str
    target_database: str
    simulated_incident: dt.datetime
    recovered_to: dt.datetime
    restore_started: dt.datetime
    service_verified: dt.datetime
    checks: dict[str, str]
    document_objects: dict[str, int]


def parse_evidence(document: Any) -> DrillEvidence:
    data = require_object(document, EVIDENCE_KEYS, "deneme kanıtı")
    if data["schema"] != EVIDENCE_SCHEMA:
        raise InputError(f"deneme kanıtı: schema {EVIDENCE_SCHEMA} olmalıdır.")
    checks_raw = require_object(data["checks"], frozenset(EVIDENCE_CHECK_KEYS), "deneme kanıtı.checks")
    checks: dict[str, str] = {}
    for key in EVIDENCE_CHECK_KEYS:
        value = checks_raw[key]
        if value not in EVIDENCE_CHECK_STATES:
            raise InputError(f"deneme kanıtı.checks.{key}: passed, failed veya not-run olmalıdır.")
        checks[key] = value
    objects_raw = require_object(data["documentObjects"], frozenset(DOCUMENT_OBJECT_KEYS), "deneme kanıtı.documentObjects")
    document_objects = {
        key: require_int(objects_raw[key], f"deneme kanıtı.documentObjects.{key}", minimum=0) for key in DOCUMENT_OBJECT_KEYS
    }
    require_string(data["notes"], "deneme kanıtı.notes", allow_empty=True)
    return DrillEvidence(
        drill_id=require_string(data["drillId"], "deneme kanıtı.drillId"),
        is_example=require_bool(data["isExample"], "deneme kanıtı.isExample"),
        environment=require_string(data["environment"], "deneme kanıtı.environment"),
        source_database=require_string(data["sourceDatabase"], "deneme kanıtı.sourceDatabase"),
        target_database=require_string(data["targetDatabase"], "deneme kanıtı.targetDatabase"),
        simulated_incident=parse_utc(data["simulatedIncidentUtc"], "deneme kanıtı.simulatedIncidentUtc"),
        recovered_to=parse_utc(data["recoveredToUtc"], "deneme kanıtı.recoveredToUtc"),
        restore_started=parse_utc(data["restoreStartedUtc"], "deneme kanıtı.restoreStartedUtc"),
        service_verified=parse_utc(data["serviceVerifiedUtc"], "deneme kanıtı.serviceVerifiedUtc"),
        checks=checks,
        document_objects=document_objects,
    )


def check_drill(evidence: DrillEvidence, policy: Policy) -> Report:
    report = Report()
    prefix = evidence.drill_id
    if evidence.is_example:
        report.fail("EXAMPLE_EVIDENCE", f"{prefix}: örnek dosya gerçek deneme kanıtı yerine kullanılamaz.")
    if evidence.environment.casefold() == PRODUCTION_ENVIRONMENT:
        report.fail("DRILL_IN_PRODUCTION", f"{prefix}: geri yükleme denemesi production ortamında yapılamaz.")
    if same_database_name(evidence.source_database, evidence.target_database):
        report.fail("DRILL_TARGET_IS_SOURCE", f"{prefix}: deneme hedefi kaynak veritabanıyla aynı olamaz.")

    ordered = (
        evidence.recovered_to <= evidence.simulated_incident
        and evidence.simulated_incident <= evidence.restore_started
        and evidence.restore_started <= evidence.service_verified
    )
    if not ordered:
        report.fail(
            "DRILL_TIMELINE_INVALID",
            f"{prefix}: zaman sırası recoveredTo ≤ simulatedIncident ≤ restoreStarted ≤ serviceVerified olmalıdır.",
        )
    else:
        observed_rpo = evidence.simulated_incident - evidence.recovered_to
        observed_rto = evidence.service_verified - evidence.simulated_incident
        if observed_rpo > policy.rpo:
            report.fail(
                "DRILL_RPO_EXCEEDED",
                f"{prefix}: gözlenen veri kaybı {format_duration(observed_rpo)} > RPO {format_duration(policy.rpo)}.",
            )
        if observed_rto > policy.rto:
            report.fail(
                "DRILL_RTO_EXCEEDED",
                f"{prefix}: gözlenen kesinti {format_duration(observed_rto)} > RTO {format_duration(policy.rto)}.",
            )
        report.info(
            "DRILL_MEASURED",
            f"{prefix}: gözlenen RPO {format_duration(observed_rpo)}, gözlenen RTO {format_duration(observed_rto)}.",
        )

    for key in EVIDENCE_CHECK_KEYS:
        if evidence.checks[key] != "passed":
            report.fail("DRILL_CHECK_NOT_PASSED", f"{prefix}: {key} kontrolü {evidence.checks[key]}.")

    objects = evidence.document_objects
    if objects["missing"] or objects["mismatched"] or objects["present"] != objects["expected"]:
        report.fail(
            "DRILL_DOCUMENT_OBJECTS_INCOMPLETE",
            f"{prefix}: belge nesneleri beklenen {objects['expected']}, mevcut {objects['present']}, "
            f"eksik {objects['missing']}, uyumsuz {objects['mismatched']}.",
        )
    return report


def month_of(moment: dt.datetime) -> tuple[int, int]:
    utc = moment.astimezone(UTC)
    return utc.year, utc.month


def next_month(month: tuple[int, int]) -> tuple[int, int]:
    year, number = month
    return (year + 1, 1) if number == 12 else (year, number + 1)


def parse_month(value: str) -> tuple[int, int]:
    if not MONTH_PATTERN.match(value):
        raise InputError("--since YYYY-MM biçiminde olmalıdır.")
    year, number = int(value[:4]), int(value[5:])
    if not 1 <= number <= 12:
        raise InputError("--since geçersiz ay.")
    return year, number


def check_drill_cadence(
    evidences: Sequence[DrillEvidence],
    policy: Policy,
    *,
    as_of: dt.datetime,
    since: tuple[int, int] | None,
) -> Report:
    report = Report()
    identifiers = [evidence.drill_id for evidence in evidences]
    if len(set(identifiers)) != len(identifiers):
        raise InputError("Yinelenen drillId.")
    passed_months: set[tuple[int, int]] = set()
    months_seen: list[tuple[int, int]] = []
    for evidence in evidences:
        if evidence.simulated_incident > as_of:
            report.info("DRILL_AFTER_AS_OF_IGNORED", f"{evidence.drill_id}: --as-of sonrasındaki deneme sayılmaz.")
            continue
        month = month_of(evidence.simulated_incident)
        months_seen.append(month)
        if check_drill(evidence, policy).passed:
            passed_months.add(month)
        else:
            report.info("DRILL_FAILED_IGNORED", f"{evidence.drill_id}: geçmeyen deneme aylık sayıma katılmaz.")
    start = since if since is not None else (min(months_seen) if months_seen else None)
    if start is None:
        report.fail("NO_DRILL_EVIDENCE", "Hiç geri yükleme denemesi kanıtı yok.")
        return report
    current = month_of(as_of)
    if start > current:
        raise InputError("--since --as-of ayından sonra olamaz.")
    month = start
    while month < current:
        if month not in passed_months:
            report.fail("DRILL_MISSING_MONTH", f"{month[0]:04d}-{month[1]:02d} ayında geçen geri yükleme denemesi yok.")
        month = next_month(month)
    if current not in passed_months:
        report.info("DRILL_DUE_THIS_MONTH", f"{current[0]:04d}-{current[1]:02d} ayının geri yükleme denemesi henüz yapılmadı.")
    return report


# --------------------------------------------------------------------------- restore plan


@dataclasses.dataclass(frozen=True)
class RestorePlan:
    full: Backup
    logs: tuple[Backup, ...]
    recovery_point: dt.datetime | None
    reached_point: dt.datetime
    script: str


def sql_literal(text: str) -> str:
    return "N'" + text.replace("'", "''") + "'"


def join_path(directory: str, name: str) -> str:
    separator = "\\" if "\\" in directory else "/"
    return directory + separator + name


def local_stop_at(moment: dt.datetime, offset_minutes: int) -> str:
    # STOPAT is interpreted in the server's local time; msdb recorded the offset per backup.
    local = moment.astimezone(UTC) + dt.timedelta(minutes=offset_minutes)
    return local.strftime("%Y-%m-%dT%H:%M:%S")


def restore_source(backup: Backup) -> str:
    return ", ".join(f"DISK = {sql_literal(device)}" for device in backup.devices)


def build_restore_plan(
    history: History,
    *,
    recovery_point: dt.datetime | None,
    target_database: str,
    data_directory: str,
    log_directory: str,
    allow_source_name: bool,
) -> RestorePlan:
    # Also guard direct callers that constructed History without parse_history.
    require_single_line_text(history.database_name, "yedek geçmişi.databaseName")
    if not DATABASE_NAME_PATTERN.match(target_database):
        raise InputError("--target-database harf ile başlamalı; yalnız harf, rakam ve alt çizgi içerebilir.")
    if same_database_name(target_database, history.database_name) and not allow_source_name:
        raise InputError("--target-database kaynak veritabanıyla aynı; bu yalnız --allow-source-name ile ve insan kararıyla seçilir.")
    for name, directory in (("--data-directory", data_directory), ("--log-directory", log_directory)):
        if not directory or directory[-1] in "\\/" or "'" in directory:
            raise InputError(f"{name} boş olamaz, dizin ayıracıyla bitemez ve tek tırnak içeremez.")
    if recovery_point is not None and recovery_point > history.exported_at:
        raise InputError("--recovery-point dışa aktarma zamanından sonra olamaz.")

    usable = [
        backup
        for backup in history.backups
        if not backup.is_copy_only and not backup.is_damaged and backup.type in {FULL_BACKUP, LOG_BACKUP}
    ]
    fulls = [
        backup
        for backup in usable
        if backup.type == FULL_BACKUP and (recovery_point is None or backup.finish <= recovery_point)
    ]
    if not fulls:
        raise PlanError("NO_FULL_BEFORE_POINT", "Kurtarma noktasından önce bitmiş kullanılabilir tam yedek yok.")
    full = max(fulls, key=lambda backup: (backup.finish, backup.backup_set_id))

    logs_by_first = sorted(
        (backup for backup in usable if backup.type == LOG_BACKUP),
        key=lambda backup: (backup.first_lsn, backup.start, backup.backup_set_id),
    )
    if any(log.last_lsn <= log.first_lsn for log in logs_by_first):
        raise PlanError("LOG_CHAIN_NOT_ADVANCING", "Günlük yedeği LSN aralığı ilerlemiyor.")
    chain: list[Backup] = []
    first = [log for log in logs_by_first if log.first_lsn <= full.last_lsn < log.last_lsn]
    if first:
        chain.append(first[0])
        visited = {first[0].backup_set_id}
        while recovery_point is None or chain[-1].start < recovery_point:
            following = [log for log in logs_by_first if log.first_lsn == chain[-1].last_lsn]
            if not following:
                break
            candidate = following[0]
            if candidate.backup_set_id in visited or candidate.last_lsn <= chain[-1].last_lsn:
                raise PlanError("LOG_CHAIN_NOT_ADVANCING", "Günlük zinciri yineleniyor veya LSN ilerlemiyor.")
            visited.add(candidate.backup_set_id)
            chain.append(candidate)

    if recovery_point is None:
        reached = chain[-1].start if chain else full.start
    else:
        if recovery_point > full.finish and (not chain or chain[-1].start < recovery_point):
            reachable = chain[-1].start if chain else full.finish
            raise PlanError(
                "POINT_NOT_COVERED",
                f"Kurtarma noktası kesintisiz günlük zinciriyle kapsanmıyor; ulaşılabilen son nokta {format_utc(reachable)}.",
            )
        reached = recovery_point
        if recovery_point <= full.finish:
            chain = []

    data_files = [item for item in full.files if item.file_type in DATA_FILE_TYPES]
    log_files = [item for item in full.files if item.file_type == LOG_FILE_TYPE]
    if not data_files or not log_files:
        raise InputError(f"{full.label}: tam yedeğin veri ve günlük dosya listesi eksik.")
    moves = []
    for item in full.files:
        if not LOGICAL_FILE_NAME_PATTERN.match(item.logical_name):
            raise InputError(f"{full.label}: mantıksal dosya adı güvenli dosya adına çevrilemiyor.")
        if item.file_type == LOG_FILE_TYPE:
            physical = join_path(log_directory, f"{target_database}_{item.logical_name}.ldf")
        elif item.file_type == "F":
            physical = join_path(data_directory, f"{target_database}_{item.logical_name}")
        else:
            physical = join_path(data_directory, f"{target_database}_{item.logical_name}.mdf")
        moves.append(f"MOVE {sql_literal(item.logical_name)} TO {sql_literal(physical)}")

    target = f"[{target_database}]"
    lines = [
        "-- Göymen ERP restore plan (generated by tools/verify_backup_restore.py restore-plan).",
        "-- REVIEW BEFORE RUNNING. The repository and CI never run this script.",
        f"-- Source database: {history.database_name}; history exported at {format_utc(history.exported_at)}.",
        f"-- Requested recovery point (UTC): {'latest' if recovery_point is None else format_utc(recovery_point)}.",
        f"-- Reached recovery point (UTC, conservative): {format_utc(reached)}.",
        f"-- Full backup: {full.label}; log backups: {', '.join(log.label for log in chain) or 'none'}.",
        "-- Keep the outbox dispatchers disabled (Work:OutboxDispatcher:Enabled=false) until the",
        "-- restored database and the Belge Kasası objects are verified (runbook §7).",
        ":on error exit",
        "SET NOCOUNT ON;",
        f"IF DB_ID({sql_literal(target_database)}) IS NOT NULL",
        "    THROW 59171, N'Hedef veritabanı zaten var; plan mevcut veritabanının üzerine yazmaz.', 1;",
        "GO",
        f"RESTORE DATABASE {target} FROM {restore_source(full)}",
        f"    WITH FILE = {full.position},{' CHECKSUM,' if full.has_checksums else ''} NORECOVERY,",
        "    " + ",\n    ".join(moves) + ";",
        "GO",
    ]
    for log in chain:
        options = [f"FILE = {log.position}"] + (["CHECKSUM"] if log.has_checksums else []) + ["NORECOVERY"]
        if recovery_point is not None:
            options.append(f"STOPAT = {sql_literal(local_stop_at(recovery_point, log.offset_minutes))}")
        lines.extend(
            [
                f"RESTORE LOG {target} FROM {restore_source(log)}",
                "    WITH " + ", ".join(options) + ";",
                "GO",
            ]
        )
    lines.extend([f"RESTORE DATABASE {target} WITH RECOVERY;", "GO", ""])
    return RestorePlan(full=full, logs=tuple(chain), recovery_point=recovery_point, reached_point=reached, script="\n".join(lines))


class PlanError(Exception):
    """The history cannot reach the requested recovery point."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


# --------------------------------------------------------------------------- document objects


@dataclasses.dataclass(frozen=True)
class Blob:
    company_id: str
    sha256: str
    size_bytes: int
    object_key: str


@dataclasses.dataclass(frozen=True)
class ObjectCounts:
    expected: int
    present: int
    missing: int
    mismatched: int


def parse_manifest(document: Any) -> list[Blob]:
    data = require_object(document, MANIFEST_KEYS, "belge manifestosu")
    if data["schema"] != MANIFEST_SCHEMA:
        raise InputError(f"belge manifestosu: schema {MANIFEST_SCHEMA} olmalıdır.")
    require_string(data["databaseName"], "belge manifestosu.databaseName")
    parse_utc(data["exportedAtUtc"], "belge manifestosu.exportedAtUtc")
    raw_blobs = data["blobs"]
    if not isinstance(raw_blobs, list):
        raise InputError("belge manifestosu.blobs: liste bekleniyor.")
    blobs: list[Blob] = []
    keys: set[str] = set()
    for index, raw in enumerate(raw_blobs):
        where = f"blobs[{index}]"
        item = require_object(raw, BLOB_KEYS, where)
        company_id = require_string(item["companyId"], f"{where}.companyId")
        sha256 = require_string(item["sha256"], f"{where}.sha256")
        object_key = require_string(item["objectKey"], f"{where}.objectKey")
        if not COMPANY_ID_PATTERN.match(company_id) or not SHA256_PATTERN.match(sha256):
            raise InputError(f"{where}: companyId veya sha256 biçimi geçersiz.")
        if object_key != f"companies/{company_id.replace('-', '')}/sha256/{sha256}":
            raise InputError(f"{where}: objectKey şirket ve özetle uyuşmuyor.")
        if object_key in keys:
            raise InputError(f"{where}: yinelenen objectKey.")
        keys.add(object_key)
        blobs.append(Blob(company_id, sha256, require_int(item["sizeBytes"], f"{where}.sizeBytes", minimum=1), object_key))
    return blobs


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(HASH_CHUNK_BYTES):
            digest.update(chunk)
    return digest.hexdigest()


def has_symlink_component(root: Path, relative: str) -> bool:
    current = root
    for part in relative.split("/"):
        current = current / part
        try:
            if stat.S_ISLNK(os.lstat(current).st_mode):
                return True
        except FileNotFoundError:
            return False
    return False


def check_objects(blobs: Sequence[Blob], root: Path) -> tuple[Report, ObjectCounts]:
    report = Report()
    if root.is_symlink() or not root.is_dir():
        raise InputError("--root mevcut bir dizin olmalı ve sembolik bağlantı olmamalıdır.")
    present = missing = mismatched = 0
    expected_keys = {blob.object_key for blob in blobs}
    for blob in blobs:
        path = root.joinpath(*blob.object_key.split("/"))
        if has_symlink_component(root, blob.object_key):
            report.fail("OBJECT_SYMLINK", f"{blob.object_key}: yol sembolik bağlantı içeriyor.")
            mismatched += 1
            continue
        try:
            info = os.lstat(path)
        except FileNotFoundError:
            report.fail("OBJECT_MISSING", f"{blob.object_key}: nesne geri yüklenen dizinde yok.")
            missing += 1
            continue
        if not stat.S_ISREG(info.st_mode):
            report.fail("OBJECT_NOT_FILE", f"{blob.object_key}: normal dosya değil.")
            mismatched += 1
            continue
        if info.st_size != blob.size_bytes:
            report.fail(
                "OBJECT_SIZE_MISMATCH",
                f"{blob.object_key}: boyut {info.st_size} bayt, beklenen {blob.size_bytes} bayt.",
            )
            mismatched += 1
            continue
        if file_sha256(path) != blob.sha256:
            report.fail("OBJECT_HASH_MISMATCH", f"{blob.object_key}: SHA-256 özeti uyuşmuyor.")
            mismatched += 1
            continue
        present += 1

    extra = partial = unexpected = 0
    for directory, subdirectories, files in os.walk(root, followlinks=False):
        relative_directory = Path(directory).relative_to(root)
        entries = list(files) + [name for name in subdirectories if os.path.islink(os.path.join(directory, name))]
        for name in entries:
            relative = (relative_directory / name).as_posix()
            if relative in expected_keys:
                continue
            if name.endswith(TEMPORARY_OBJECT_SUFFIX):
                partial += 1
            elif OBJECT_KEY_PATTERN.match(relative):
                extra += 1
            else:
                unexpected += 1
    if extra:
        report.info("EXTRA_OBJECTS", f"{extra} nesne manifestoda yok (geri yükleme noktasından sonra yazılmış; zararsız).")
    if partial:
        report.info("PARTIAL_FILES", f"{partial} yarım kalmış geçici dosya ({TEMPORARY_OBJECT_SUFFIX}) var; okunmaz.")
    if unexpected:
        report.info("UNEXPECTED_FILES", f"{unexpected} dosya nesne anahtarı biçiminde değil.")
    counts = ObjectCounts(expected=len(blobs), present=present, missing=missing, mismatched=mismatched)
    report.info(
        "OBJECT_SUMMARY",
        f"beklenen {counts.expected}, mevcut {counts.present}, eksik {counts.missing}, uyumsuz {counts.mismatched}.",
    )
    return report, counts


# --------------------------------------------------------------------------- command line


def print_report(report: Report, out: Any) -> int:
    for finding in report.findings:
        marker = "HATA" if finding.failed else "BİLGİ"
        print(f"{marker} {finding.code}: {finding.message}", file=out)
    print("SONUÇ: GEÇTİ" if report.passed else "SONUÇ: KALDI", file=out)
    return EXIT_PASSED if report.passed else EXIT_FAILED


def load_policy(path: Path) -> Policy:
    return parse_policy(load_json_file(path))


def yes_no(value: str) -> bool:
    if value not in {"yes", "no"}:
        raise argparse.ArgumentTypeError("yes veya no olmalıdır")
    return value == "yes"


def utc_argument(value: str) -> dt.datetime:
    try:
        return parse_utc(value, "zaman")
    except InputError as error:
        raise argparse.ArgumentTypeError(str(error)) from error


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Göymen ERP çevrimdışı yedek/geri yükleme doğrulayıcısı (KLN-D36).")
    commands = parser.add_subparsers(dest="command", required=True)

    def with_policy(command: argparse.ArgumentParser) -> argparse.ArgumentParser:
        command.add_argument("--policy", type=Path, default=DEFAULT_POLICY_PATH)
        return command

    with_policy(commands.add_parser("policy", help="politika dosyasını doğrular"))

    history = with_policy(commands.add_parser("history", help="msdb yedek geçmişini politikaya göre denetler"))
    history.add_argument("--history", type=Path, required=True)
    history.add_argument("--as-of", type=utc_argument)
    history.add_argument(
        "--require-native-encryption",
        type=yes_no,
        required=True,
        help="SQL Server yedek şifrelemesi zorunlu mu (site kararı; OPEN-017/OPEN-014)",
    )

    drill = with_policy(commands.add_parser("drill", help="tek bir geri yükleme denemesi kanıtını denetler"))
    drill.add_argument("--evidence", type=Path, required=True)

    cadence = with_policy(commands.add_parser("drill-cadence", help="aylık deneme sıklığını denetler"))
    cadence.add_argument("--evidence", type=Path, nargs="+", required=True)
    cadence.add_argument("--as-of", type=utc_argument, required=True)
    cadence.add_argument("--since")

    plan = commands.add_parser("restore-plan", help="incelenecek T-SQL geri yükleme planı yazar")
    plan.add_argument("--history", type=Path, required=True)
    plan.add_argument("--recovery-point", required=True, help="YYYY-MM-DDTHH:MM:SSZ veya latest")
    plan.add_argument("--target-database", required=True)
    plan.add_argument("--data-directory", required=True)
    plan.add_argument("--log-directory", required=True)
    plan.add_argument("--output", type=Path, required=True)
    plan.add_argument("--allow-source-name", action="store_true")

    objects = commands.add_parser("objects", help="Belge Kasası nesnelerini manifestoyla karşılaştırır")
    objects.add_argument("--manifest", type=Path, required=True)
    objects.add_argument("--root", type=Path, required=True)
    return parser


def run(arguments: Sequence[str], out: Any = None) -> int:
    out = sys.stdout if out is None else out
    parser = build_parser()
    try:
        options = parser.parse_args(list(arguments))
    except SystemExit as error:
        return EXIT_INPUT_ERROR if error.code else EXIT_PASSED
    try:
        if options.command == "policy":
            policy = load_policy(options.policy)
            print(
                f"BİLGİ POLICY_VALID: {policy.decision}: RPO {format_duration(policy.rpo)}, "
                f"RTO {format_duration(policy.rto)}, günlük yedeği {format_duration(policy.log_backup_interval)}.",
                file=out,
            )
            print("SONUÇ: GEÇTİ", file=out)
            return EXIT_PASSED
        if options.command == "history":
            report = check_history(
                parse_history(load_json_file(options.history)),
                load_policy(options.policy),
                as_of=options.as_of,
                require_native_encryption=options.require_native_encryption,
            )
            return print_report(report, out)
        if options.command == "drill":
            return print_report(check_drill(parse_evidence(load_json_file(options.evidence)), load_policy(options.policy)), out)
        if options.command == "drill-cadence":
            report = check_drill_cadence(
                [parse_evidence(load_json_file(path)) for path in options.evidence],
                load_policy(options.policy),
                as_of=options.as_of,
                since=None if options.since is None else parse_month(options.since),
            )
            return print_report(report, out)
        if options.command == "restore-plan":
            return run_restore_plan(options, out)
        if options.command == "objects":
            report, _ = check_objects(parse_manifest(load_json_file(options.manifest)), options.root)
            return print_report(report, out)
    except InputError as error:
        print(f"GİRDİ HATASI: {error}", file=out)
        return EXIT_INPUT_ERROR
    raise AssertionError(options.command)


def run_restore_plan(options: argparse.Namespace, out: Any) -> int:
    point = None if options.recovery_point == "latest" else parse_utc(options.recovery_point, "--recovery-point")
    output: Path = options.output
    if output.exists() or output.is_symlink():
        raise InputError("--output zaten var; plan mevcut dosyanın üzerine yazılmaz.")
    try:
        plan = build_restore_plan(
            parse_history(load_json_file(options.history)),
            recovery_point=point,
            target_database=options.target_database,
            data_directory=options.data_directory,
            log_directory=options.log_directory,
            allow_source_name=options.allow_source_name,
        )
    except PlanError as error:
        report = Report()
        report.fail(error.code, error.message)
        return print_report(report, out)
    try:
        with output.open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(plan.script)
    except OSError as error:
        raise InputError(f"--output yazılamadı ({error.__class__.__name__}).") from error
    report = Report()
    report.info(
        "RESTORE_PLAN_WRITTEN",
        f"{plan.full.label} + {len(plan.logs)} günlük yedeği; ulaşılan nokta {format_utc(plan.reached_point)}. "
        "Plan insan incelemesinden sonra çalıştırılır.",
    )
    return print_report(report, out)


def main(argv: Iterable[str] | None = None) -> int:
    return run(list(sys.argv[1:] if argv is None else argv))


if __name__ == "__main__":
    raise SystemExit(main())
