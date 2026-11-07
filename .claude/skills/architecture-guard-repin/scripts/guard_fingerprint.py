#!/usr/bin/env python3
"""Print the PlatformTenantQueryFilterBypassGuardTests fingerprint of each given file.

Mirrors ComputeReviewedSourceFingerprint in
tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.cs:
File.ReadAllText (UTF-8, BOM dropped) -> CRLF/CR to LF -> TrimEnd() every line ->
TrimEnd('\\n') + "\\n" -> SHA-256 lowercase hex.

Usage (from the repository root):
    python3 -I .claude/skills/architecture-guard-repin/scripts/guard_fingerprint.py <path> [<path> ...]

It only reads; it never edits the guard. The authoritative value is still the guard's
own "Expected ..., found ..." output in CI.
"""

import hashlib
import sys


def fingerprint(path: str) -> str:
    # utf-8-sig drops the BOM that EF-generated migrations carry (File.ReadAllText does the same).
    with open(path, encoding="utf-8-sig", newline="") as handle:
        source = handle.read()
    source = source.replace("\r\n", "\n").replace("\r", "\n")
    normalized = "\n".join(line.rstrip() for line in source.split("\n")).rstrip("\n") + "\n"
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def main(paths: list[str]) -> int:
    if not paths:
        print(__doc__, file=sys.stderr)
        return 2
    for path in paths:
        print(f"{fingerprint(path)}  {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
