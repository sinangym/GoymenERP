#!/usr/bin/env python3
"""Deterministic audit of GitHub Action version pinning in the active workflows (DALGA_PLANI_v2 1.18).

Every ``uses:`` reference in ``.github/workflows/*.yml`` / ``*.yaml`` (``*.disabled`` files are not active and are
skipped) is classified:

* ``local``   — ``./path`` in this repository;
* ``sha``     — ``owner/repo[/path]@<40 hex>``: immutable, the target state;
* ``tag``     — anything else (``@v7``, ``@main`` …): mutable.

A mutable reference is allowed only when the reviewed baseline lists exactly that ``action@ref`` pair. The baseline
can only shrink: a new action, a new mutable ref of a listed action, a branch-like ref, a ``docker://`` reference or a
baseline entry nobody uses any more all fail. Moving a listed action to a full commit SHA needs no baseline change
except deleting its entry. No network is used: the audit never resolves a tag; resolving and moving a tag to a SHA is
a reviewed change (Dependabot's github-actions ecosystem can propose it once enabled in Settings, a human gate).

Usage::

    python3 -I -S -B tools/security/audit_action_pins.py           # exit 1 on any violation
    python3 -I -S -B tools/security/audit_action_pins.py --json    # machine-readable report
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_DIRECTORY = ".github/workflows"
BASELINE_PATH = "contracts/security/action-pin-baseline-v1.json"
BASELINE_FORMAT = "goymen.security.action-pin-baseline.v1"

USES_LINE = re.compile(r"^\s*(?:-\s+)?uses:\s*(?P<value>\S.*?)\s*(?:#.*)?$")
ACTION = re.compile(r"\A(?P<action>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_./-]+)?)@(?P<ref>[A-Za-z0-9_./-]+)\Z")
FULL_SHA = re.compile(r"\A[0-9a-f]{40}\Z")
# A mutable ref may be kept only while it is a release tag (v<major>[.<minor>[.<patch>]]), never a branch.
RELEASE_TAG = re.compile(r"\Av[0-9]+(?:\.[0-9]+){0,2}\Z")


class AuditError(Exception):
    pass


def workflow_files(root: Path) -> list[Path]:
    directory = root / WORKFLOW_DIRECTORY
    return sorted(path for path in directory.iterdir() if path.is_file() and path.suffix in (".yml", ".yaml"))


def references(root: Path) -> list[dict[str, object]]:
    found: list[dict[str, object]] = []
    for path in workflow_files(root):
        relative = path.relative_to(root).as_posix()
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            match = USES_LINE.match(line)
            if match is None:
                continue
            value = match.group("value").strip("'\"")
            found.append({"workflow": relative, "line": number, "uses": value})
    return found


def classify(value: str) -> tuple[str, str | None, str | None]:
    if value.startswith("./"):
        return "local", None, None
    if value.startswith("docker://"):
        return "docker", None, None
    match = ACTION.fullmatch(value)
    if match is None:
        return "invalid", None, None
    action, ref = match.group("action"), match.group("ref")
    if FULL_SHA.fullmatch(ref):
        return "sha", action, ref
    return "tag", action, ref


def load_baseline(root: Path) -> set[str]:
    path = root / BASELINE_PATH
    if not path.is_file():
        raise AuditError(f"{BASELINE_PATH} is missing.")
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or sorted(document) != ["format", "mutable_refs", "note"]:
        raise AuditError("baseline must have exactly the keys 'format', 'mutable_refs' and 'note'.")
    if document["format"] != BASELINE_FORMAT:
        raise AuditError(f"baseline format must be {BASELINE_FORMAT!r}.")
    entries = document["mutable_refs"]
    if not isinstance(entries, list) or not all(isinstance(entry, str) for entry in entries):
        raise AuditError("baseline mutable_refs must be a list of 'owner/repo@ref' strings.")
    if entries != sorted(entries) or len(set(entries)) != len(entries):
        raise AuditError("baseline mutable_refs must be sorted and unique.")
    for entry in entries:
        kind, _, ref = classify(entry)
        if kind != "tag" or ref is None or not RELEASE_TAG.fullmatch(ref):
            raise AuditError(f"baseline entry {entry!r} is not an owner/repo@v<release> tag.")
    return set(entries)


def audit(root: Path) -> dict[str, object]:
    baseline = load_baseline(root)
    violations: list[dict[str, object]] = []
    counts = {"local": 0, "sha": 0, "tag": 0}
    used_baseline: set[str] = set()
    found = references(root)
    for item in found:
        kind, action, ref = classify(str(item["uses"]))
        if kind in ("invalid", "docker"):
            violations.append({**item, "reason": f"{kind} reference is not allowed"})
            continue
        counts[kind] += 1
        if kind != "tag":
            continue
        key = f"{action}@{ref}"
        if key in baseline:
            used_baseline.add(key)
        else:
            violations.append({**item, "reason": "mutable ref; pin to a full 40-character commit SHA"})
    stale = sorted(baseline - used_baseline)
    return {
        "references": len(found),
        "counts": counts,
        "violations": violations,
        "stale_baseline_entries": stale,
        "ok": not violations and not stale,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument("--json", action="store_true")
    arguments = parser.parse_args(argv)
    try:
        report = audit(arguments.root.resolve())
    except (AuditError, json.JSONDecodeError) as error:
        print(f"HATA: {error}", file=sys.stderr)
        return 2
    if arguments.json:
        print(json.dumps(report, ensure_ascii=False, indent=1, sort_keys=True))
    else:
        for item in report["violations"]:
            print(f"İHLAL {item['workflow']}:{item['line']}: {item['uses']} — {item['reason']}")
        for entry in report["stale_baseline_entries"]:
            print(f"BAYAT taban girdisi: {entry} (artık kullanılmıyor; tabandan silin)")
        counts = report["counts"]
        print(
            f"Action sabitleme: {report['references']} referans — SHA {counts['sha']}, "
            f"taban listesindeki etiket {counts['tag']}, yerel {counts['local']}; "
            + ("uygun." if report["ok"] else "İHLAL VAR.")
        )
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
