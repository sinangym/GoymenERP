#!/usr/bin/env python3
"""Deterministic, versioned decision index for the AI knowledge base (Y-A1, AI-D12, DALGA_PLANI_v2 A.4 (1)).

Reads ONLY ``docs/decisions/`` (KARAR_SICILI.md/.csv, ACIK_KARARLAR.md, SINAN_KARARLARI_*.md and the formula
golden files) and writes ``contracts/ai/decision-index-v1.json``. The same inputs always give the same bytes: no
clock, no network, no environment value and no model is used. Nothing is guessed:

* a decision row is read only from a table whose header names an identity column and a decision column;
* a relation ("günceller / yerine geçer / düzeltir / geçersiz / daraltır / genişletir") is recorded as explicit only
  when the clause names exactly one subject (the row itself) and active voice; every other case is ``belirsiz``;
* a record becomes ``gecersiz`` only by an explicit, whole-decision supersede/invalidate; partial updates keep the
  status and are listed as relations;
* the same identity is never emitted twice: identical repeats are listed under ``also_seen_in`` and different texts
  under the separate ``conflicts`` list;
* status mapping ASSUMPTION-AI-INDEX-STATUS-1 (reversible technical reading): ACIK_KARARLAR KAPANDI -> kilitli,
  KISMEN/AÇIK -> acik, registry ERTELENDI -> ertelendi (its own status), formula goldens -> belirsiz;
* the data class candidate (AI-D34 d) is content based: a person name taken from the registry's own ``Sahip`` column,
  an e-mail, a phone, an 11-digit national id or an IBAN marks the record ``kisisel_veri_adayi``;
* WP-A4 / plan D2 (AI-D36 "kişi adları dizinde maskelenir"): the index text is masked HERE, at generation. Person names
  become ``[KİŞİ]``, e-mail/phone/national id/IBAN become ``[GİZLİ]`` in the title, text and scope of every record that
  carries a signal; the name terms themselves are not written to the index (only their count). A masked record that
  still contains a term is a generation error;
* the cloud eligibility label (AI-D34 d, AI-D49) is derived from content and data class, never from the file name: a
  record with a money amount OR a person name (even masked) is ``yerel_kalir`` (processed only by the in-house model);
  every other rule text is ``bulut_uygun_aday``. Which provider actually runs is the gateway policy (human gate).

Usage::

    python3 -I tools/generate_ai_decision_index.py            # rewrite the index
    python3 -I tools/generate_ai_decision_index.py --check    # exit 1 when the committed index is stale
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import re
import sys
import unicodedata
from pathlib import Path
from typing import Any

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
DECISIONS_DIRECTORY = "docs/decisions"
INDEX_PATH = "contracts/ai/decision-index-v1.json"
SCHEMA_PATH = "contracts/ai/decision-index-v1.schema.json"
FORMAT = "goymen.ai.decision-index.v1"
GENERATOR = "tools/generate_ai_decision_index.py"

REGISTRY_MD = "KARAR_SICILI.md"
REGISTRY_CSV = "KARAR_SICILI.csv"
OPEN_DECISIONS = "ACIK_KARARLAR.md"
GOLDEN_SUFFIX = "_GOLDEN_V1.md"
SINAN_PREFIX = "SINAN_KARARLARI_"

STATUS_ASSUMPTION = "ASSUMPTION-AI-INDEX-STATUS-1"
STATUSES = ("kilitli", "gecici", "acik", "ertelendi", "gecersiz", "belirsiz")
KINDS = ("sicil_karari", "sinan_karari", "varsayim", "acik_karar", "formul_golden")
RELATION_TYPES = ("yerine_gecer", "gecersiz_kilar", "gunceller", "duzeltir", "daraltir", "genisletir")

REGISTRY_STATUS = {
    "KILITLI": "kilitli",
    "GECICI_VARSAYIM": "gecici",
    "ACIK": "acik",
    "ERTELENDI": "ertelendi",
}
OPEN_STATUS = {"AÇIK": "acik", "KISMEN": "acik", "KAPANDI": "kilitli"}

ID_CELL = re.compile(r"\A`?([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-[A-Z]?[0-9]+[a-z]?|OPEN-[A-Z0-9-]+|ASSUMPTION-[A-Z0-9-]+)`?\Z")
NUMERIC_CELL = re.compile(r"\A(K?[0-9]{1,4})\Z")
REFERENCE = re.compile(
    r"(?<![A-Za-z0-9-])("
    r"DEC-[A-Z]+-[0-9]{3}"
    r"|OPEN-[A-Z0-9]+(?:-[A-Z0-9]+)*"
    r"|ASSUMPTION-[A-Z0-9]+(?:-[A-Z0-9]+)*"
    r"|[A-Z]{2,5}-D[0-9]+"
    r"|BS-[0-9]+"
    r")(?![A-Za-z0-9])"
)
SHORTHAND = re.compile(r"\b([A-Z]{2,5})-D([0-9]+)((?:/D[0-9]+)+)")
RANGE = re.compile(r"\b([A-Z]{2,5})-D([0-9]+)\s*(?:–|—|\.\.|…)\s*(?:\1-)?D([0-9]+)\b")
FILE_DATE = re.compile(r"\ASINAN_KARARLARI_([0-9]{4}-[0-9]{2}-[0-9]{2})")
MAXIMUM_RANGE = 60

# Active-voice relation verbs; the order is the reporting order. Passive forms are always ambiguous.
RELATION_VERBS = (
    ("yerine_gecer", re.compile(r"yerine\s+geçer\b")),
    ("gecersiz_kilar", re.compile(r"geçersiz\s+kılar\b|geçersizdir\b|geçersiz\s+sayar\b")),
    ("gunceller", re.compile(r"\bgünceller\b")),
    ("duzeltir", re.compile(r"\bdüzeltir\b")),
    ("daraltir", re.compile(r"\bdaraltır\b")),
    ("genisletir", re.compile(r"\bgenişletir\b")),
)
PASSIVE = re.compile(r"güncellendi|güncellenir|düzeltildi|yerine\s+geçildi|geçersiz\s+kılındı|daraltıldı|genişletildi")
NEGATION = re.compile(r"değil\b|geçmez\b|güncellemez\b|düzeltmez\b|değişmez\b|korur\b|sürer\b")
PARTIAL = re.compile(r"kısm|cümle|ifade|satır|sınır|aşama|madde|bölüm|yaklaşım|sıras|davranış|koşul|değer|yön|süre|kural"
                     r"|[\"“”«»]|\([0-9]+\)")
# "X'teki ..." (a part of X) and "X'in <noun>" (X's <noun>) name a part; "X'in yerine" names X itself.
LOCATIVE_PART = r"['’](?:n?d|t)[ae]ki\b"
GENITIVE_PART = r"['’]n?[ıiuü]n\s+(?!yerine\b)"
INSTRUMENT = r"\s+ile\b"

EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
PHONE = re.compile(r"(?<![0-9])(?:\+90|0)\s?5[0-9]{2}\s?[0-9]{3}\s?[0-9]{2}\s?[0-9]{2}(?![0-9])")
NATIONAL_ID = re.compile(r"(?<![0-9])[1-9][0-9]{10}(?![0-9])")
IBAN = re.compile(r"\bTR[0-9]{2}(?:\s?[0-9A-Z]{4}){5}\s?[0-9A-Z]{2}\b")
# Commercially sensitive candidate (AI-D34 d): a money amount (number + currency, or currency sign + number).
MONEY = re.compile(r"(?<![\w.,])[0-9][0-9.,]*\s?(?:TL|TRY|USD|EUR|GBP|₺|€|\$)(?![A-Za-z])|(?:₺|€|\$)\s?[0-9]")

PERSON_MASK = "[KİŞİ]"
SECRET_MASK = "[GİZLİ]"
MASK_POLICY = "ai-d36-person-name-mask-at-generation@1"
CLOUD_LABELS = ("bulut_uygun_aday", "yerel_kalir")
CLOUD_DATA_CLASS = {"bulut_uygun_aday": "internal", "yerel_kalir": "confidential"}
CLOUD_BASIS = "AI-D34 d, AI-D49: para tutarı veya kişi adı (maskeli olsa da) içeren kayıt yerel kalır, diğer kural metinleri bulut adayıdır; belge adı kullanılmaz"


class DecisionIndexError(Exception):
    """The decision sources cannot be indexed deterministically."""


def _clean(cell: str) -> str:
    text = cell.strip()
    text = re.sub(r"\*\*|__", "", text)
    text = re.sub(r"\s+", " ", text)
    return text


def _split_row(line: str) -> list[str]:
    body = line.strip()
    if body.startswith("|"):
        body = body[1:]
    if body.endswith("|"):
        body = body[:-1]
    # Escaped pipes stay inside the cell.
    parts = re.split(r"(?<!\\)\|", body)
    return [_clean(part.replace("\\|", "|")) for part in parts]


def _is_separator(cells: list[str]) -> bool:
    return all(cell and set(cell) <= set("-: ") for cell in cells)


def _references(text: str) -> list[str]:
    found: list[str] = []
    expanded = SHORTHAND.sub(
        lambda m: m.group(0) + " " + " ".join(f"{m.group(1)}-{d[1:]}" for d in m.group(3).split("/")[1:]), text)
    for match in RANGE.finditer(expanded):
        prefix, start, end = match.group(1), int(match.group(2)), int(match.group(3))
        if start < end and end - start <= MAXIMUM_RANGE:
            found.extend(f"{prefix}-D{number}" for number in range(start, end + 1))
    found.extend(match.group(1) for match in REFERENCE.finditer(expanded))
    unique: list[str] = []
    for value in found:
        if value not in unique:
            unique.append(value)
    return unique


def _title(subject: str | None, text: str) -> str:
    if subject:
        return subject
    first = re.split(r"(?<=[.;])\s", text, maxsplit=1)[0]
    return first if len(first) <= 120 else first[:117].rstrip() + "..."


def _sentences(text: str) -> list[str]:
    return [part.strip() for part in re.split(r"(?<=[.;])\s+|\n", text) if part.strip()]


def _relations(record_id: str, text: str, known_here: set[str], line: int) -> list[dict[str, Any]]:
    """Relations named by a decision row. The row is the subject unless another identity leads the clause."""
    relations: list[dict[str, Any]] = []
    for sentence in _sentences(text):
        verbs = [kind for kind, pattern in RELATION_VERBS if pattern.search(sentence)]
        passive = bool(PASSIVE.search(sentence))
        if not verbs and not passive:
            continue
        targets = [ref for ref in _references(sentence) if ref != record_id]
        if not targets:
            continue
        leading = REFERENCE.match(sentence.lstrip("(- "))
        foreign_subject = leading is not None and leading.group(1) != record_id and not re.match(
            r"\A" + re.escape(leading.group(1)) + r"['’]", sentence.lstrip("(- "))
        # "X ile geçersizdir": X is the agent, so the row is not the subject; a bare copula without "bu karar" is ambiguous.
        agents = [ref for ref in targets if re.search(re.escape(ref) + INSTRUMENT, sentence)]
        copula_only = verbs == ["gecersiz_kilar"] and not re.search(r"geçersiz\s+kılar", sentence) and "bu karar" not in sentence
        explicit = (len(verbs) == 1 and not passive and not NEGATION.search(sentence) and not foreign_subject
                    and not agents and not copula_only)
        kinds = verbs if verbs else ["belirsiz"]
        for kind in kinds:
            for target in targets:
                relations.append({
                    "type": kind,
                    "target": target,
                    "confidence": "acik" if explicit else "belirsiz",
                    "extent": _extent(sentence, target),
                    "line": line,
                })
    return relations


def _extent(sentence: str, target: str) -> str:
    escaped = re.escape(target)
    if (PARTIAL.search(sentence) or re.search(escaped + LOCATIVE_PART, sentence)
            or re.search(escaped + GENITIVE_PART, sentence)):
        return "kismi"
    return "tam_veya_belirsiz"


def _file_relations(path: str, lines: list[str], defined: set[str]) -> list[tuple[str, dict[str, Any]]]:
    """Relations stated in a file's introduction (non-table lines). Ambiguous unless exactly one subject."""
    found: list[tuple[str, dict[str, Any]]] = []
    for number, raw in enumerate(lines, start=1):
        if raw.lstrip().startswith("|"):
            continue
        for sentence in _sentences(_clean(raw)):
            verbs = [kind for kind, pattern in RELATION_VERBS if pattern.search(sentence)]
            passive = bool(PASSIVE.search(sentence))
            if not verbs and not passive:
                continue
            refs = _references(sentence)
            subjects = [ref for ref in refs if ref in defined and not re.search(re.escape(ref) + r"['’]", sentence)]
            objects = [ref for ref in refs if ref not in subjects]
            if not subjects or not objects:
                continue
            agents = [ref for ref in refs if re.search(re.escape(ref) + INSTRUMENT, sentence)]
            copula_only = (verbs == ["gecersiz_kilar"] and not re.search(r"geçersiz\s+kılar", sentence)
                           and "bu karar" not in sentence)
            explicit = (len(subjects) == 1 and len(verbs) == 1 and not passive and not NEGATION.search(sentence)
                        and not agents and not copula_only)
            for subject in subjects:
                for kind in (verbs if verbs else ["belirsiz"]):
                    for target in objects:
                        found.append((subject, {
                            "type": kind,
                            "target": target,
                            "confidence": "acik" if explicit else "belirsiz",
                            "extent": _extent(sentence, target),
                            "line": number,
                            "basis": "dosya_girisi",
                            "path": path,
                        }))
    return found


def _owner_names(registry_rows: list[dict[str, str]]) -> list[str]:
    names: set[str] = set()
    for row in registry_rows:
        owner = (row.get("Sahip") or "").strip()
        parts = owner.split()
        if len(parts) >= 2 and all(part[:1].isupper() for part in parts):
            names.add(owner)
            names.update(parts)
    return sorted(names, key=lambda value: (-len(value), value))


def owner_name_terms(root: Path = REPOSITORY_ROOT) -> list[str]:
    """The person-name terms (registry ``Sahip`` column), read from the sources; the index itself never carries them (D2)."""
    path = root / DECISIONS_DIRECTORY / REGISTRY_CSV
    if not path.is_file():
        return []
    return _owner_names(list(_registry_csv(path).values()))


def _name_pattern(name: str) -> re.Pattern[str]:
    return re.compile(r"(?<![\wÇĞİÖŞÜçğıöşü])" + re.escape(name) + r"(?![A-Za-zÇĞİÖŞÜçğıöşü])")


def _personal_signals(text: str, names: list[str]) -> list[str]:
    signals: list[str] = []
    for name in names:
        if _name_pattern(name).search(text):
            signals.append("kisi_adi")
            break
    if EMAIL.search(text):
        signals.append("e_posta")
    if PHONE.search(text):
        signals.append("telefon")
    if NATIONAL_ID.search(text):
        signals.append("kimlik_numarasi")
    if IBAN.search(text):
        signals.append("iban")
    return signals


def commercial_signals(text: str) -> list[str]:
    return ["para_tutari"] if MONEY.search(text) else []


def mask_text(text: str | None, names: list[str]) -> str | None:
    """Person names (longest first) -> [KİŞİ]; e-mail, IBAN, phone, national id -> [GİZLİ]. Same order as the C# reader."""
    if text is None:
        return None
    masked = text
    for name in names:
        masked = _name_pattern(name).sub(PERSON_MASK, masked)
    masked = EMAIL.sub(SECRET_MASK, masked)
    masked = IBAN.sub(SECRET_MASK, masked)
    masked = PHONE.sub(SECRET_MASK, masked)
    return NATIONAL_ID.sub(SECRET_MASK, masked)


def cloud_label(personal: list[str], commercial: list[str]) -> str:
    # AI-D49: a money amount or a person name (even after masking) keeps the record on the in-house model.
    return "yerel_kalir" if commercial or personal else "bulut_uygun_aday"


def _header_role(header: list[str]) -> tuple[int, int, int | None, int | None, int | None, str] | None:
    """(id, text, subject, scope, status) column indexes and kind for a decision table, or None."""
    lowered = [unicodedata.normalize("NFC", cell).lower() for cell in header]
    if not lowered or lowered[0] not in ("no", "kimlik", "id"):
        return None

    def find(*names: str) -> int | None:
        for name in names:
            for index, cell in enumerate(lowered):
                if cell == name or cell.startswith(name + " "):
                    return index
        return None

    subject = find("konu", "madde", "alan")
    scope = find("etkilenen yer", "etkilenen yüzey", "etkilenen", "yer", "bağlı kimlik")
    status = find("durum")
    if (text := find("karar")) is not None:
        return 0, text, subject, scope, status, "karar"
    if (text := find("seçilen")) is not None:
        return 0, text, subject, scope, status, "karar"
    if (text := find("varsayım")) is not None:
        return 0, text, subject, scope, status, "varsayim"
    return None


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _source_files(root: Path) -> list[Path]:
    directory = root / DECISIONS_DIRECTORY
    if not directory.is_dir():
        raise DecisionIndexError(f"{DECISIONS_DIRECTORY} bulunamadı")
    names = sorted(path.name for path in directory.iterdir() if path.is_file())
    chosen = [
        name for name in names
        if name in (REGISTRY_MD, REGISTRY_CSV, OPEN_DECISIONS)
        or (name.startswith(SINAN_PREFIX) and name.endswith(".md"))
        or name.endswith(GOLDEN_SUFFIX)
    ]
    for required in (REGISTRY_MD, REGISTRY_CSV):
        if required not in chosen:
            raise DecisionIndexError(f"{DECISIONS_DIRECTORY}/{required} bulunamadı")
    # Registry first, then open decisions, then dated user decisions, then goldens: a stable source order.
    def order(name: str) -> tuple[int, str]:
        if name == REGISTRY_MD:
            return (0, name)
        if name == REGISTRY_CSV:
            return (1, name)
        if name == OPEN_DECISIONS:
            return (2, name)
        if name.startswith(SINAN_PREFIX):
            return (3, name)
        return (4, name)
    return [directory / name for name in sorted(chosen, key=order)]


def _read_text(path: Path) -> str:
    data = path.read_bytes()
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise DecisionIndexError(f"{path.name} UTF-8 değil") from error
    return unicodedata.normalize("NFC", text.replace("\r\n", "\n"))


class _Builder:
    def __init__(self) -> None:
        self.records: dict[str, dict[str, Any]] = {}
        self.order: list[str] = []
        self.conflicts: list[dict[str, Any]] = []
        self.pending_relations: list[tuple[str, dict[str, Any]]] = []

    def add(self, record: dict[str, Any]) -> None:
        identity = record["id"]
        existing = self.records.get(identity)
        if existing is None:
            self.records[identity] = record
            self.order.append(identity)
            return
        location = dict(record["source"])
        if existing["text"] == record["text"]:
            existing["also_seen_in"].append(location)
            return
        self.conflicts.append({
            "id": identity,
            "kind": "ayni_kimlik_farkli_metin",
            "kept": dict(existing["source"]),
            "other": location,
            "other_text_sha256": _sha256(record["text"].encode("utf-8")),
        })
        existing["has_conflict"] = True


def _record(identity: str, kind: str, title: str, text: str, status: str, status_basis: str,
            effective_date: str | None, effective_basis: str, path: str, line: int,
            area: str | None, affected: str | None) -> dict[str, Any]:
    return {
        "id": identity,
        "kind": kind,
        "title": title,
        "text": text,
        "status": status,
        "status_basis": status_basis,
        "effective_date": effective_date,
        "effective_date_basis": effective_basis,
        "source": {"path": path, "line": line},
        "also_seen_in": [],
        "scope": {"area": area, "affected": affected},
        "references": [],
        "relations": [],
        "superseded_by": [],
        "data_class_candidate": "kisisel_veri_isareti_yok",
        "personal_data_signals": [],
        "commercial_signals": [],
        "masked": False,
        "cloud_eligibility": "bulut_uygun_aday",
        "cloud_data_class": "internal",
        "has_conflict": False,
    }


def _registry_csv(path: Path) -> dict[str, dict[str, str]]:
    rows = list(csv.DictReader(io.StringIO(_read_text(path))))
    result: dict[str, dict[str, str]] = {}
    for row in rows:
        identity = (row.get("DecisionId") or "").strip()
        if not identity:
            raise DecisionIndexError(f"{REGISTRY_CSV} kimliksiz satır içeriyor")
        result[identity] = row
    return result


def _parse_tables(builder: _Builder, path: str, lines: list[str], file_kind: str, file_date: str | None,
                  registry_csv: dict[str, dict[str, str]] | None) -> set[str]:
    defined: set[str] = set()
    header: list[str] | None = None
    role = None
    stem = Path(path).name[: -len(".md")]
    for number, raw in enumerate(lines, start=1):
        if not raw.lstrip().startswith("|"):
            header, role = None, None
            continue
        cells = _split_row(raw)
        if header is None:
            header, role = cells, _header_role(cells)
            continue
        if _is_separator(cells) or role is None:
            continue
        id_index, text_index, subject_index, scope_index, status_index, table_kind = role
        if len(cells) <= text_index:
            continue
        first = cells[id_index]
        matched = ID_CELL.match(first)
        if matched:
            identity = matched.group(1)
        elif NUMERIC_CELL.match(first) and file_kind == "sinan_karari" and file_date:
            identity = f"SK-{file_date}-{first}"
        else:
            continue
        text = cells[text_index]
        if not text:
            continue
        subject = cells[subject_index] if subject_index is not None and subject_index < len(cells) else None
        scope = cells[scope_index] if scope_index is not None and scope_index < len(cells) else None
        status_cell = cells[status_index] if status_index is not None and status_index < len(cells) else None

        if file_kind == "sicil_karari":
            status = REGISTRY_STATUS.get(status_cell or "")
            if status is None:
                raise DecisionIndexError(f"{path}:{number} bilinmeyen sicil durumu")
            basis = f"KARAR_SICILI Durum={status_cell}"
            if status == "ertelendi":
                basis += f" ({STATUS_ASSUMPTION})"
            record = _record(identity, "sicil_karari", _title(None, text), text, status,
                             basis, None, "belirsiz", path, number, subject, None)
            csv_row = (registry_csv or {}).get(identity)
            if csv_row is None:
                builder.conflicts.append({"id": identity, "kind": "sicil_csv_satiri_yok",
                                          "kept": {"path": path, "line": number}, "other": None,
                                          "other_text_sha256": None})
                record["has_conflict"] = True
            elif _clean(csv_row.get("Karar") or "") != text or (csv_row.get("Durum") or "").strip() != status_cell:
                builder.conflicts.append({"id": identity, "kind": "sicil_md_csv_farki",
                                          "kept": {"path": path, "line": number},
                                          "other": {"path": f"{DECISIONS_DIRECTORY}/{REGISTRY_CSV}", "line": None},
                                          "other_text_sha256": _sha256(_clean(csv_row.get("Karar") or "").encode("utf-8"))})
                record["has_conflict"] = True
        elif file_kind == "acik_karar":
            if not identity.startswith("OPEN-"):
                continue
            status = OPEN_STATUS.get(status_cell or "", "belirsiz")
            # Columns: ID | Durum | decided part | still-open part | decision evidence | code evidence.
            decided = cells[2] if len(cells) > 2 else ""
            open_part = cells[3] if len(cells) > 3 else ""
            text = f"Kapanan bölüm: {decided} | Açık kalan: {open_part}"
            record = _record(identity, "acik_karar", identity, text, status,
                             f"ACIK_KARARLAR Durum={status_cell} ({STATUS_ASSUMPTION})",
                             None, "belirsiz", path, number, None, None)
        elif identity.startswith("OPEN-"):
            lowered = (status_cell or text).lower()
            status = "acik" if "açık" in lowered else "belirsiz"
            record = _record(identity, "acik_karar", subject or identity, text, status,
                             f"{stem} Durum={status_cell}" if status_cell else f"{stem} durum sütunu yok",
                             file_date, "dosya_adi_tarihi", path, number, subject, scope)
        elif table_kind == "varsayim" or identity.startswith("ASSUMPTION-"):
            record = _record(identity, "varsayim", _title(subject, text), text, "gecici",
                             f"{stem} varsayım tablosu", file_date, "dosya_adi_tarihi", path, number, subject, scope)
        else:
            record = _record(identity, "sinan_karari", _title(subject, text), text, "kilitli",
                             f"{stem} kullanıcı kararı tablosu", file_date,
                             "dosya_adi_tarihi" if file_date else "belirsiz", path, number, subject, scope)
        record["references"] = [ref for ref in _references(text) if ref != identity]
        record["relations"] = _relations(identity, text, defined, number)
        defined.add(identity)
        builder.add(record)
    return defined


def _parse_golden(builder: _Builder, path: str, lines: list[str]) -> None:
    title_line = next((n for n, raw in enumerate(lines, start=1) if raw.startswith("# ")), None)
    if title_line is None:
        raise DecisionIndexError(f"{path} başlıksız golden")
    title = _clean(lines[title_line - 1][2:])
    sections = [_clean(raw.lstrip("#").strip()) for raw in lines if raw.startswith("## ")]
    text = title + " | Bölümler: " + "; ".join(sections)
    identity = "GOLDEN-" + re.sub(r"[^A-Z0-9]+", "-", Path(path).name[: -len(".md")].upper()).strip("-")
    record = _record(identity, "formul_golden", title, text, "belirsiz",
                     f"golden dosyası; durum satırı yok ({STATUS_ASSUMPTION})", None, "belirsiz", path, title_line, "Formül golden", None)
    record["references"] = _references(title)
    builder.add(record)


def build_index(root: Path = REPOSITORY_ROOT) -> dict[str, Any]:
    files = _source_files(root)
    builder = _Builder()
    sources: list[dict[str, Any]] = []
    registry_csv: dict[str, dict[str, str]] = {}
    texts: dict[str, list[str]] = {}
    for file_path in files:
        relative = file_path.relative_to(root).as_posix()
        data = file_path.read_bytes()
        name = file_path.name
        kind = ("sicil" if name in (REGISTRY_MD, REGISTRY_CSV) else "acik_kararlar" if name == OPEN_DECISIONS
                else "sinan_kararlari" if name.startswith(SINAN_PREFIX) else "formul_golden")
        sources.append({"path": relative, "kind": kind, "sha256": _sha256(data)})
        if name == REGISTRY_CSV:
            registry_csv = _registry_csv(file_path)
        else:
            texts[relative] = _read_text(file_path).split("\n")

    owner_names = _owner_names(list(registry_csv.values()))
    file_relations: list[tuple[str, dict[str, Any]]] = []
    for file_path in files:
        name = file_path.name
        relative = file_path.relative_to(root).as_posix()
        if name == REGISTRY_CSV:
            continue
        lines = texts[relative]
        if name == REGISTRY_MD:
            _parse_tables(builder, relative, lines, "sicil_karari", None, registry_csv)
            missing = sorted(set(registry_csv) - set(builder.records))
            for identity in missing:
                builder.conflicts.append({"id": identity, "kind": "sicil_md_satiri_yok", "kept": None,
                                          "other": {"path": f"{DECISIONS_DIRECTORY}/{REGISTRY_CSV}", "line": None},
                                          "other_text_sha256": None})
        elif name == OPEN_DECISIONS:
            _parse_tables(builder, relative, lines, "acik_karar", None, None)
        elif name.startswith(SINAN_PREFIX):
            date = FILE_DATE.match(name)
            defined = _parse_tables(builder, relative, lines, "sinan_karari", date.group(1) if date else None, None)
            file_relations.extend(_file_relations(relative, lines, defined))
        else:
            _parse_golden(builder, relative, lines)

    for subject, relation in file_relations:
        if subject in builder.records:
            builder.records[subject]["relations"].append(relation)

    records = [builder.records[identity] for identity in builder.order]
    for record in records:
        content = "\n".join(value for value in (record["title"], record["text"], record["scope"]["area"],
                                                 record["scope"]["affected"]) if value)
        signals = _personal_signals(content, owner_names)
        commercial = commercial_signals(content)
        record["personal_data_signals"] = signals
        record["data_class_candidate"] = "kisisel_veri_adayi" if signals else "kisisel_veri_isareti_yok"
        record["commercial_signals"] = commercial
        record["cloud_eligibility"] = cloud_label(signals, commercial)
        record["cloud_data_class"] = CLOUD_DATA_CLASS[record["cloud_eligibility"]]
        # Plan D2 / AI-D36: masked at generation; the published index never carries the plain name.
        if signals:
            record["title"] = mask_text(record["title"], owner_names)
            record["text"] = mask_text(record["text"], owner_names)
            record["scope"] = {"area": mask_text(record["scope"]["area"], owner_names),
                               "affected": mask_text(record["scope"]["affected"], owner_names)}
            record["masked"] = True
            leaked = _personal_signals("\n".join(value for value in (record["title"], record["text"],
                                                                     record["scope"]["area"], record["scope"]["affected"])
                                                 if value), owner_names)
            if leaked:
                raise DecisionIndexError(f"{record['id']}: maskeleme sonrası kişisel veri kaldı ({', '.join(leaked)})")
        unique: list[dict[str, Any]] = []
        for relation in record["relations"]:
            if relation not in unique:
                unique.append(relation)
        record["relations"] = unique

    # Back-links and the conservative status change: only an explicit whole-decision supersede/invalidate.
    for record in records:
        for relation in record["relations"]:
            target = builder.records.get(relation["target"])
            if target is None:
                relation["target_known"] = False
                continue
            relation["target_known"] = True
            link = {"by": record["id"], "type": relation["type"], "confidence": relation["confidence"],
                    "extent": relation["extent"]}
            if link not in target["superseded_by"]:
                target["superseded_by"].append(link)
            if (relation["confidence"] == "acik" and relation["extent"] == "tam_veya_belirsiz"
                    and relation["type"] in ("yerine_gecer", "gecersiz_kilar")
                    and target["status"] in ("kilitli", "gecici")):
                target["status"] = "gecersiz"
                target["status_basis"] = f"{record['id']} açıkça {relation['type']}"
    for record in records:
        record["superseded_by"].sort(key=lambda link: (link["by"], link["type"]))

    ambiguous = sum(1 for record in records for relation in record["relations"] if relation["confidence"] == "belirsiz")
    conflicts = sorted(builder.conflicts, key=lambda c: (c["id"], c["kind"], json.dumps(c["other"], sort_keys=True)))
    source_set = _sha256("\n".join(f"{s['path']}\t{s['sha256']}" for s in sources).encode("utf-8"))
    return {
        "format": FORMAT,
        "schema": SCHEMA_PATH,
        "generator": GENERATOR,
        "source_root": DECISIONS_DIRECTORY,
        "source_set_sha256": source_set,
        "sources": sources,
        "personal_name_terms_basis": "KARAR_SICILI.csv Sahip sütunu",
        "personal_name_term_count": len(owner_names),
        "mask_policy": MASK_POLICY,
        "cloud_eligibility_basis": CLOUD_BASIS,
        "statistics": {
            "record_count": len(records),
            "conflict_count": len(conflicts),
            "ambiguous_relation_count": ambiguous,
            "status_counts": {status: sum(1 for r in records if r["status"] == status) for status in STATUSES},
            "kind_counts": {kind: sum(1 for r in records if r["kind"] == kind) for kind in KINDS},
            "personal_data_candidate_count": sum(1 for r in records if r["data_class_candidate"] == "kisisel_veri_adayi"),
            "masked_record_count": sum(1 for r in records if r["masked"]),
            "cloud_eligibility_counts": {label: sum(1 for r in records if r["cloud_eligibility"] == label)
                                         for label in CLOUD_LABELS},
        },
        "records": records,
        "conflicts": conflicts,
    }


def render(index: dict[str, Any]) -> bytes:
    return (json.dumps(index, ensure_ascii=False, indent=1, sort_keys=False) + "\n").encode("utf-8")


# ---- minimal JSON Schema subset (the CI runs python -I -S: no third-party validator) ---------------------------

def validate_schema(instance: Any, schema: dict[str, Any], path: str = "$") -> list[str]:
    errors: list[str] = []
    types = schema.get("type")
    if types is not None:
        allowed = types if isinstance(types, list) else [types]
        if not any(_type_ok(instance, name) for name in allowed):
            return [f"{path}: tür {allowed} bekleniyordu"]
    if "const" in schema and instance != schema["const"]:
        errors.append(f"{path}: sabit değer değil")
    if "enum" in schema and instance not in schema["enum"]:
        errors.append(f"{path}: izinli değerlerden değil")
    if isinstance(instance, str):
        if "pattern" in schema and not re.search(schema["pattern"], instance):
            errors.append(f"{path}: biçim uymuyor")
        if "minLength" in schema and len(instance) < schema["minLength"]:
            errors.append(f"{path}: çok kısa")
        if "maxLength" in schema and len(instance) > schema["maxLength"]:
            errors.append(f"{path}: çok uzun")
    if isinstance(instance, int) and not isinstance(instance, bool):
        if "minimum" in schema and instance < schema["minimum"]:
            errors.append(f"{path}: alt sınırın altında")
    if isinstance(instance, dict):
        for name in schema.get("required", []):
            if name not in instance:
                errors.append(f"{path}: '{name}' eksik")
        properties = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        for name, value in instance.items():
            if name in properties:
                errors.extend(validate_schema(value, properties[name], f"{path}.{name}"))
            elif extra is False:
                errors.append(f"{path}: beklenmeyen alan '{name}'")
            elif isinstance(extra, dict):
                errors.extend(validate_schema(value, extra, f"{path}.{name}"))
    if isinstance(instance, list):
        if "minItems" in schema and len(instance) < schema["minItems"]:
            errors.append(f"{path}: en az {schema['minItems']} öğe")
        if "maxItems" in schema and len(instance) > schema["maxItems"]:
            errors.append(f"{path}: en çok {schema['maxItems']} öğe")
        if schema.get("uniqueItems") and len({json.dumps(v, sort_keys=True) for v in instance}) != len(instance):
            errors.append(f"{path}: tekrar eden öğe")
        if "items" in schema:
            for position, value in enumerate(instance):
                errors.extend(validate_schema(value, schema["items"], f"{path}[{position}]"))
    return errors


def _type_ok(value: Any, name: str) -> bool:
    return {
        "object": isinstance(value, dict),
        "array": isinstance(value, list),
        "string": isinstance(value, str),
        "integer": isinstance(value, int) and not isinstance(value, bool),
        "boolean": isinstance(value, bool),
        "null": value is None,
    }.get(name, False)


def check_invariants(index: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    identities = [record["id"] for record in index["records"]]
    if len(identities) != len(set(identities)):
        errors.append("aynı kimlik birden fazla kayıtta")
    if index["statistics"]["record_count"] != len(identities):
        errors.append("kayıt sayısı istatistiği tutmuyor")
    if index["statistics"]["conflict_count"] != len(index["conflicts"]):
        errors.append("çelişki sayısı istatistiği tutmuyor")
    for record in index["records"]:
        if record["masked"] != (record["data_class_candidate"] == "kisisel_veri_adayi"):
            errors.append(f"{record['id']}: maskeleme işareti veri sınıfıyla tutmuyor")
        if record["cloud_eligibility"] != cloud_label(record["personal_data_signals"], record["commercial_signals"]):
            errors.append(f"{record['id']}: bulut uygunluğu içerikten türetilmemiş")
        if record["status"] == "gecersiz" and not any(
                link["confidence"] == "acik" and link["type"] in ("yerine_gecer", "gecersiz_kilar")
                for link in record["superseded_by"]):
            errors.append(f"{record['id']}: açık dayanaksız geçersiz durumu")
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="dizin güncel değilse 1 ile çık")
    parser.add_argument("--root", type=Path, default=REPOSITORY_ROOT)
    arguments = parser.parse_args(argv)
    root = arguments.root.resolve()
    try:
        index = build_index(root)
    except DecisionIndexError as error:
        print(f"HATA: {error}", file=sys.stderr)
        return 2
    schema = json.loads((root / SCHEMA_PATH).read_text(encoding="utf-8"))
    problems = validate_schema(index, schema) + check_invariants(index)
    if problems:
        for problem in problems[:50]:
            print(f"HATA: {problem}", file=sys.stderr)
        return 2
    expected = render(index)
    target = root / INDEX_PATH
    if arguments.check:
        if not target.is_file() or target.read_bytes() != expected:
            print(f"HATA: {INDEX_PATH} güncel değil; 'python3 -I {GENERATOR}' ile yeniden üretin.", file=sys.stderr)
            return 1
        print(f"{INDEX_PATH} güncel ({index['statistics']['record_count']} kayıt).")
        return 0
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(expected)
    stats = index["statistics"]
    print(f"{INDEX_PATH}: {stats['record_count']} kayıt, {stats['conflict_count']} çelişki, "
          f"{stats['ambiguous_relation_count']} belirsiz ilişki.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
