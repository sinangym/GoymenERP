#!/usr/bin/env python3
"""Print the actual values behind every pin of tools/generate_recovery_surface_inventory.py.

Read-only helper for the recovery-inventory-repin skill. It imports the generator from the
repository's own tools/ directory and evaluates the same functions the generator uses, for the
current HEAD commit, and prints:

  * narrow risky source surface (EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT / _FINGERPRINT)
  * DbContext and Migration declaration sets (EXPECTED_*_COUNT / _DECLARATION_FINGERPRINT)
  * Audit live-source set (EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT / _FINGERPRINT)
  * every drifted REVIEWED_OPERATIVE_METHOD_FINGERPRINTS entry
  * every drifted REVIEWED_LIVE_SOURCE_SHA256 entry

The generator's snapshot loader requires a CLEAN worktree (no tracked or untracked change) and
reads the committed blobs of HEAD, so commit first (a WIP commit is fine), then run:

    python3 -I .claude/skills/recovery-inventory-repin/scripts/print_recovery_actuals.py

It never writes. Copy the printed values into the generator/schema/tests by hand, review the
diff, amend the commit and re-run the real generator.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def main() -> int:
    root = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    sys.path.insert(0, str(root / "tools"))
    import generate_recovery_surface_inventory as g  # noqa: E402  (repository generator)

    head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    snapshot = g._load_source_snapshot(root, head)

    def line(label: str, actual: tuple[int, str], expected: tuple[int, str]) -> None:
        mark = "ok   " if actual == expected else "DRIFT"
        print(f"{mark} {label}: actual count={actual[0]} fingerprint={actual[1]}")
        if actual != expected:
            print(f"      {label}: pinned count={expected[0]} fingerprint={expected[1]}")

    line("risky source surface", g._risky_source_surface_fingerprint(snapshot),
         (g.EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT, g.EXPECTED_RISKY_SOURCE_SURFACE_FINGERPRINT))
    for label, base, count, fingerprint in (
        ("DbContext declarations", "DbContext", g.EXPECTED_DB_CONTEXT_COUNT,
         g.EXPECTED_DB_CONTEXT_DECLARATION_FINGERPRINT),
        ("Migration declarations", "Migration", g.EXPECTED_MIGRATION_COUNT,
         g.EXPECTED_MIGRATION_DECLARATION_FINGERPRINT),
    ):
        declarations = g._discover_csharp_subclasses(snapshot, base)
        line(label, (len(declarations), g._declaration_set_fingerprint(declarations)), (count, fingerprint))
    line("audit live source", g._audit_live_source_fingerprint(snapshot),
         (g.EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT, g.EXPECTED_AUDIT_LIVE_SOURCE_FINGERPRINT))

    for (path, method), expected in g.REVIEWED_OPERATIVE_METHOD_FINGERPRINTS.items():
        try:
            actual = g._csharp_method_fingerprint(g._read_csharp(snapshot, path), method, path)
        except g.InventoryError as exc:
            print(f"ERROR method {path}:{method}: {exc}")
            continue
        if actual != expected:
            print(f"DRIFT method ({path!r}, {method!r}): {actual}")
    for path, expected in g.REVIEWED_LIVE_SOURCE_SHA256.items():
        try:
            actual = g._source_file_record(snapshot, path)["sha256"]
        except g.InventoryError as exc:
            print(f"ERROR blob {path}: {exc}")
            continue
        if actual != expected:
            print(f"DRIFT blob {path}: {actual}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
