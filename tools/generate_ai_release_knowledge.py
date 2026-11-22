#!/usr/bin/env python3
"""Deterministic release behaviour knowledge package for the AI knowledge base (WP-A4, DALGA_PLANI_v2 A.4 (3)).

Reads ONLY ``docs/release-knowledge/RK-*.md`` (usage notes of the software as published), the registry's owner column
(``docs/decisions/KARAR_SICILI.csv``, person names to mask) and ``ErpPermissions.cs`` (permission names), and writes
``contracts/ai/release-knowledge-v1.json``. The package is copied into the API publish output, so the knowledge travels
with the very build it describes. The same inputs always give the same bytes: no clock, no network, no model.

Rules (nothing is guessed):

* a note is RELEASED only when its ``durum`` is ``yayinda`` AND every ``kanit`` line names a file that exists in this
  tree and contains the given text. Any other note is listed under ``excluded`` with its identity and reason only: its
  text never enters the package, so a not-yet-implemented feature cannot be told (A.4 (3));
* ``yetki`` lists the permissions a reader must hold to be shown the note; every name must be registered in
  ``ErpPermissions`` (unknown name = generation error);
* person names and e-mail/phone/national id/IBAN are masked at generation (plan D2, AI-D36), with the same masking as the
  decision index generator; the cloud eligibility label is content based (AI-D34 d).

Usage::

    python3 -I tools/generate_ai_release_knowledge.py            # rewrite the package
    python3 -I tools/generate_ai_release_knowledge.py --check    # exit 1 when the committed package is stale
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import sys
import unicodedata
from pathlib import Path
from typing import Any

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
NOTES_DIRECTORY = "docs/release-knowledge"
PACKAGE_PATH = "contracts/ai/release-knowledge-v1.json"
SCHEMA_PATH = "contracts/ai/release-knowledge-v1.schema.json"
FORMAT = "goymen.ai.release-knowledge.v1"
GENERATOR = "tools/generate_ai_release_knowledge.py"
PERMISSIONS_PATH = "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs"
REGISTRY_CSV_PATH = "docs/decisions/KARAR_SICILI.csv"
NOTE_PREFIX = "RK-"
STATUSES = ("yayinda", "uygulanmadi")
EXCLUSION_REASONS = ("uygulanmadi", "kanit_bulunamadi")
EVIDENCE_BASIS = "her kanıt dosyası bu ağaçta var ve verilen ifadeyi içeriyor (yayımlanan derlemeyle aynı ağaç)"
NOTE_ID = re.compile(r"\ARK-[A-Z0-9]+(?:-[A-Z0-9]+)*\Z")
PERMISSION_CONST = re.compile(r'public const string [A-Za-z0-9_]+ = "([a-z0-9][a-z0-9.\-]*)";')
FIELDS = ("kimlik", "baslik", "durum", "yetki", "kanit")
MAXIMUM_TEXT = 4000


def _load_decision_generator() -> Any:
    # python -I does not put tools/ on sys.path; the masking rules are shared, not copied.
    name = "goymen_ai_decision_index_generator_for_release"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, Path(__file__).resolve().parent / "generate_ai_decision_index.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("generate_ai_decision_index.py yüklenemedi")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


DECISIONS = _load_decision_generator()


class ReleaseKnowledgeError(Exception):
    """The release notes cannot be packaged deterministically."""


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _read_text(path: Path) -> str:
    try:
        text = path.read_bytes().decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ReleaseKnowledgeError(f"{path.name} UTF-8 değil") from error
    return unicodedata.normalize("NFC", text.replace("\r\n", "\n"))


def _registered_permissions(root: Path) -> set[str]:
    path = root / PERMISSIONS_PATH
    if not path.is_file():
        raise ReleaseKnowledgeError(f"{PERMISSIONS_PATH} bulunamadı")
    return set(PERMISSION_CONST.findall(_read_text(path)))


def _owner_names(root: Path) -> list[str]:
    return DECISIONS.owner_name_terms(root)


def _parse_note(relative: str, text: str) -> tuple[dict[str, Any], str, int]:
    lines = text.split("\n")
    if not lines or lines[0].strip() != "---":
        raise ReleaseKnowledgeError(f"{relative}: başlık bloğu '---' ile başlamalı")
    try:
        end = next(index for index in range(1, len(lines)) if lines[index].strip() == "---")
    except StopIteration as error:
        raise ReleaseKnowledgeError(f"{relative}: başlık bloğu kapanmamış") from error
    fields: dict[str, Any] = {"kanit": []}
    for number in range(1, end):
        raw = lines[number]
        if not raw.strip():
            continue
        key, separator, value = raw.partition(":")
        key, value = key.strip(), value.strip()
        if not separator or key not in FIELDS or not value:
            raise ReleaseKnowledgeError(f"{relative}:{number + 1} bilinmeyen ya da boş alan")
        if key == "kanit":
            path, bar, token = value.partition("|")
            if not bar or not path.strip() or not token.strip():
                raise ReleaseKnowledgeError(f"{relative}:{number + 1} kanıt '<yol> | <ifade>' biçiminde olmalı")
            fields["kanit"].append({"path": path.strip(), "token": token.strip()})
        elif key in fields:
            raise ReleaseKnowledgeError(f"{relative}:{number + 1} '{key}' iki kez yazılmış")
        else:
            fields[key] = value
    for required in FIELDS:
        if required not in fields or (required == "kanit" and not fields["kanit"]):
            raise ReleaseKnowledgeError(f"{relative}: '{required}' eksik")
    body_lines = lines[end + 1:]
    paragraphs = [" ".join(part.split()) for part in "\n".join(body_lines).split("\n\n")]
    body = "\n\n".join(part for part in paragraphs if part)
    return fields, body, end + 2


def _evidence_ok(root: Path, evidence: dict[str, str]) -> bool:
    relative = evidence["path"]
    if relative.startswith("/") or ".." in Path(relative).parts:
        raise ReleaseKnowledgeError(f"kanıt yolu depo içinde olmalı: {relative}")
    path = root / relative
    if not path.is_file():
        return False
    return evidence["token"] in _read_text(path)


def build_package(root: Path = REPOSITORY_ROOT) -> dict[str, Any]:
    directory = root / NOTES_DIRECTORY
    if not directory.is_dir():
        raise ReleaseKnowledgeError(f"{NOTES_DIRECTORY} bulunamadı")
    files = sorted(path for path in directory.iterdir()
                   if path.is_file() and path.name.startswith(NOTE_PREFIX) and path.name.endswith(".md"))
    permissions = _registered_permissions(root)
    names = _owner_names(root)
    sources: list[dict[str, str]] = []
    notes: list[dict[str, Any]] = []
    excluded: list[dict[str, Any]] = []
    seen: set[str] = set()
    for path in files:
        relative = path.relative_to(root).as_posix()
        sources.append({"path": relative, "sha256": _sha256(path.read_bytes())})
        fields, body, body_line = _parse_note(relative, _read_text(path))
        identity = fields["kimlik"]
        if not NOTE_ID.match(identity) or path.name != f"{identity}.md":
            raise ReleaseKnowledgeError(f"{relative}: kimlik dosya adıyla aynı ve RK-... biçiminde olmalı")
        if identity in seen:
            raise ReleaseKnowledgeError(f"{identity} iki kez tanımlanmış")
        seen.add(identity)
        if fields["durum"] not in STATUSES:
            raise ReleaseKnowledgeError(f"{relative}: bilinmeyen durum '{fields['durum']}'")
        required = [] if fields["yetki"] == "-" else sorted({part.strip() for part in fields["yetki"].split(",")})
        unknown = [name for name in required if name not in permissions or not name]
        if unknown:
            raise ReleaseKnowledgeError(f"{relative}: kayıtlı olmayan izin: {', '.join(unknown)}")
        source = {"path": relative, "line": 1}
        if fields["durum"] != "yayinda":
            excluded.append({"id": identity, "reason": "uygulanmadi", "source": source})
            continue
        if not all(_evidence_ok(root, evidence) for evidence in fields["kanit"]):
            excluded.append({"id": identity, "reason": "kanit_bulunamadi", "source": source})
            continue
        if not body or len(body) > MAXIMUM_TEXT:
            raise ReleaseKnowledgeError(f"{relative}: not metni boş ya da {MAXIMUM_TEXT} karakterden uzun")
        title = fields["baslik"]
        content = title + "\n" + body
        signals = DECISIONS._personal_signals(content, names)
        commercial = DECISIONS.commercial_signals(content)
        label = DECISIONS.cloud_label(signals, commercial)
        if signals:
            title, body = DECISIONS.mask_text(title, names), DECISIONS.mask_text(body, names)
            if DECISIONS._personal_signals(title + "\n" + body, names):
                raise ReleaseKnowledgeError(f"{identity}: maskeleme sonrası kişisel veri kaldı")
        notes.append({
            "id": identity,
            "title": title,
            "text": body,
            "required_permissions": required,
            "evidence": fields["kanit"],
            "source": {"path": relative, "line": body_line},
            "data_class_candidate": "kisisel_veri_adayi" if signals else "kisisel_veri_isareti_yok",
            "personal_data_signals": signals,
            "commercial_signals": commercial,
            "masked": bool(signals),
            "cloud_eligibility": label,
            "cloud_data_class": DECISIONS.CLOUD_DATA_CLASS[label],
        })
    if not notes:
        raise ReleaseKnowledgeError("yayında hiçbir not yok; boş paket üretilmez")
    source_set = _sha256("\n".join(f"{s['path']}\t{s['sha256']}" for s in sources).encode("utf-8"))
    return {
        "format": FORMAT,
        "schema": SCHEMA_PATH,
        "generator": GENERATOR,
        "source_root": NOTES_DIRECTORY,
        "source_set_sha256": source_set,
        "sources": sources,
        "evidence_basis": EVIDENCE_BASIS,
        "mask_policy": DECISIONS.MASK_POLICY,
        "cloud_eligibility_basis": DECISIONS.CLOUD_BASIS,
        "statistics": {
            "note_count": len(notes),
            "excluded_count": len(excluded),
            "masked_note_count": sum(1 for note in notes if note["masked"]),
            "cloud_eligibility_counts": {label: sum(1 for note in notes if note["cloud_eligibility"] == label)
                                         for label in DECISIONS.CLOUD_LABELS},
        },
        "notes": notes,
        "excluded": excluded,
    }


def render(package: dict[str, Any]) -> bytes:
    return (json.dumps(package, ensure_ascii=False, indent=1) + "\n").encode("utf-8")


def check_invariants(package: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    identities = [note["id"] for note in package["notes"]] + [item["id"] for item in package["excluded"]]
    if len(identities) != len(set(identities)):
        errors.append("aynı kimlik birden fazla kez")
    if package["statistics"]["note_count"] != len(package["notes"]):
        errors.append("not sayısı istatistiği tutmuyor")
    if package["statistics"]["excluded_count"] != len(package["excluded"]):
        errors.append("dışlanan sayısı istatistiği tutmuyor")
    for note in package["notes"]:
        if note["cloud_eligibility"] != DECISIONS.cloud_label(note["personal_data_signals"], note["commercial_signals"]):
            errors.append(f"{note['id']}: bulut uygunluğu içerikten türetilmemiş")
        if note["masked"] != bool(note["personal_data_signals"]):
            errors.append(f"{note['id']}: maskeleme işareti tutmuyor")
    for item in package["excluded"]:
        if set(item) != {"id", "reason", "source"}:
            errors.append(f"{item['id']}: dışlanan not metin taşıyamaz")
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="paket güncel değilse 1 ile çık")
    parser.add_argument("--root", type=Path, default=REPOSITORY_ROOT)
    arguments = parser.parse_args(argv)
    root = arguments.root.resolve()
    try:
        package = build_package(root)
    except ReleaseKnowledgeError as error:
        print(f"HATA: {error}", file=sys.stderr)
        return 2
    schema = json.loads((root / SCHEMA_PATH).read_text(encoding="utf-8"))
    problems = DECISIONS.validate_schema(package, schema) + check_invariants(package)
    if problems:
        for problem in problems[:50]:
            print(f"HATA: {problem}", file=sys.stderr)
        return 2
    expected = render(package)
    target = root / PACKAGE_PATH
    if arguments.check:
        if not target.is_file() or target.read_bytes() != expected:
            print(f"HATA: {PACKAGE_PATH} güncel değil; 'python3 -I {GENERATOR}' ile yeniden üretin.", file=sys.stderr)
            return 1
        print(f"{PACKAGE_PATH} güncel ({package['statistics']['note_count']} not).")
        return 0
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(expected)
    stats = package["statistics"]
    print(f"{PACKAGE_PATH}: {stats['note_count']} not, {stats['excluded_count']} dışlanan.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
