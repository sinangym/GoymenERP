#!/usr/bin/env python3
"""Deterministic, offline secret scanner for the tracked files of the repository (DALGA_PLANI_v2 1.18).

It reads only the files ``git ls-files`` lists (or the paths given with ``--paths`` in tests), never the network,
the clock or the environment. The same tree always gives the same report.

Rules are deliberately high-confidence (a hit is almost certainly a real credential or a file that should never be
committed). Known, reviewed test values are listed in the allowlist by rule, path and the SHA-256 of the matched
text; the allowlist never stores the value itself. An allowlist entry that no longer matches anything fails the scan,
so the list cannot silently grow stale.

GitHub's own secret scanning and push protection are switched on in Settings by a person (human gate); this tool is
the repository-side, always-on check that does not depend on that setting.

Usage::

    python3 -I -S -B tools/security/credential_scan.py            # exit 1 on any finding or stale allowlist entry
    python3 -I -S -B tools/security/credential_scan.py --json     # machine-readable report (no secret values)
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ALLOWLIST_PATH = "contracts/security/secret-scan-allowlist-v1.json"
ALLOWLIST_FORMAT = "goymen.security.secret-scan-allowlist.v1"
MAXIMUM_FILE_BYTES = 2 * 1024 * 1024


def _joined(*parts: str) -> str:
    """Builds a pattern from pieces so this source file never matches its own rules."""
    return "".join(parts)


@dataclass(frozen=True)
class ContentRule:
    rule_id: str
    description: str
    pattern: re.Pattern[str]


CONTENT_RULES: tuple[ContentRule, ...] = (
    ContentRule(
        "private-key-block",
        "PEM private key block",
        re.compile(_joined("-----BEGIN ", r"(?:RSA |DSA |EC |OPENSSH |ENCRYPTED |PGP )?", "PRIVATE KEY", r"(?: BLOCK)?-----")),
    ),
    ContentRule(
        "aws-access-key-id",
        "AWS access key id",
        re.compile(_joined(r"(?<![A-Z0-9])(?:A", r"KIA|A", r"SIA)[A-Z0-9]{16}(?![A-Z0-9])")),
    ),
    ContentRule(
        "github-token",
        "GitHub token",
        re.compile(_joined(r"(?<![A-Za-z0-9_])(?:gh", r"[pousr]_[A-Za-z0-9]{36,255}|github", r"_pat_[A-Za-z0-9_]{60,255})")),
    ),
    ContentRule(
        "slack-token",
        "Slack token",
        re.compile(_joined(r"(?<![A-Za-z0-9])xo", r"x[abprs]-[0-9A-Za-z-]{10,250}")),
    ),
    ContentRule(
        "google-api-key",
        "Google API key",
        re.compile(_joined(r"(?<![A-Za-z0-9_-])AI", r"za[0-9A-Za-z_-]{35}(?![A-Za-z0-9_-])")),
    ),
    ContentRule(
        "stripe-live-key",
        "Stripe live secret key",
        re.compile(_joined(r"(?<![A-Za-z0-9])(?:sk|rk)_", r"live_[0-9A-Za-z]{20,250}")),
    ),
    ContentRule(
        "anthropic-api-key",
        "Anthropic API key",
        re.compile(_joined(r"(?<![A-Za-z0-9])sk-", r"ant-[A-Za-z0-9_-]{20,250}")),
    ),
    ContentRule(
        "openai-api-key",
        "OpenAI API key",
        re.compile(_joined(r"(?<![A-Za-z0-9])sk-", r"(?:proj-)?[A-Za-z0-9_-]{20,}T3Blbk", r"FJ[A-Za-z0-9_-]{20,}")),
    ),
    ContentRule(
        "azure-storage-key",
        "Azure storage account key in a connection string",
        re.compile(_joined(r"AccountKey=(?!\{|<|\$)[A-Za-z0-9+/]{80,}={0,2}")),
    ),
    ContentRule(
        "npm-token",
        "npm access token",
        re.compile(_joined(r"(?<![A-Za-z0-9_])np", r"m_[A-Za-z0-9]{36}(?![A-Za-z0-9])")),
    ),
)

# File names that must never be tracked, whatever they contain. ``.env.example`` (placeholders only) is allowed.
FORBIDDEN_NAME_RULES: tuple[tuple[str, str, re.Pattern[str]], ...] = (
    ("env-file", "environment file with real values", re.compile(r"\A\.env(?:\.(?!example\Z|sample\Z|template\Z)[A-Za-z0-9_.-]+)?\Z")),
    ("key-store-file", "certificate or key store", re.compile(r"\A.+\.(?:pfx|p12|jks|keystore|snk)\Z", re.IGNORECASE)),
    ("ssh-private-key-file", "SSH private key file", re.compile(r"\Aid_(?:rsa|dsa|ecdsa|ed25519)\Z")),
    ("publish-profile-file", "publish profile with deployment credentials", re.compile(r"\A.+\.PublishSettings\Z", re.IGNORECASE)),
)


@dataclass(frozen=True)
class Finding:
    rule_id: str
    path: str
    line: int
    fingerprint: str

    def as_json(self) -> dict[str, object]:
        return {"rule": self.rule_id, "path": self.path, "line": self.line, "sha256": self.fingerprint}


class ScanError(Exception):
    pass


def fingerprint(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def tracked_files(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z", "--cached", "--full-name"],
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise ScanError("git ls-files failed; the scan needs a git checkout.")
    return sorted(item.decode("utf-8") for item in result.stdout.split(b"\0") if item)


def scan_file(root: Path, relative: str) -> list[Finding]:
    findings: list[Finding] = []
    name = PurePosixPath(relative).name
    for rule_id, _, pattern in FORBIDDEN_NAME_RULES:
        if pattern.fullmatch(name):
            findings.append(Finding(rule_id, relative, 0, fingerprint(relative)))
    path = root / relative
    if not path.is_file() or path.is_symlink():
        return findings
    data = path.read_bytes()[:MAXIMUM_FILE_BYTES]
    if b"\0" in data[:8192]:
        return findings
    text = data.decode("utf-8", errors="replace")
    for number, line in enumerate(text.splitlines(), start=1):
        for rule in CONTENT_RULES:
            for match in rule.pattern.finditer(line):
                findings.append(Finding(rule.rule_id, relative, number, fingerprint(match.group(0))))
    return findings


def load_allowlist(root: Path) -> list[dict[str, str]]:
    path = root / ALLOWLIST_PATH
    if not path.is_file():
        raise ScanError(f"{ALLOWLIST_PATH} is missing.")
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or sorted(document) != ["entries", "format"]:
        raise ScanError("allowlist must have exactly the keys 'format' and 'entries'.")
    if document["format"] != ALLOWLIST_FORMAT:
        raise ScanError(f"allowlist format must be {ALLOWLIST_FORMAT!r}.")
    entries = document["entries"]
    if not isinstance(entries, list):
        raise ScanError("allowlist entries must be a list.")
    known_rules = {rule.rule_id for rule in CONTENT_RULES} | {rule[0] for rule in FORBIDDEN_NAME_RULES}
    seen: set[tuple[str, str, str]] = set()
    for entry in entries:
        if not isinstance(entry, dict) or sorted(entry) != ["path", "reason", "rule", "sha256"]:
            raise ScanError("each allowlist entry needs exactly 'rule', 'path', 'sha256' and 'reason'.")
        if entry["rule"] not in known_rules:
            raise ScanError(f"unknown rule in allowlist: {entry['rule']!r}.")
        if not re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]):
            raise ScanError("allowlist sha256 must be 64 lower-case hex characters.")
        if not isinstance(entry["reason"], str) or len(entry["reason"].strip()) < 10:
            raise ScanError("allowlist reason must explain why the value is not a secret.")
        key = (entry["rule"], entry["path"], entry["sha256"])
        if key in seen:
            raise ScanError(f"duplicate allowlist entry: {key!r}.")
        seen.add(key)
    return entries


def scan(root: Path, paths: list[str] | None = None) -> dict[str, object]:
    files = tracked_files(root) if paths is None else sorted(paths)
    allowlist = load_allowlist(root)
    allowed = {(entry["rule"], entry["path"], entry["sha256"]) for entry in allowlist}
    findings: list[Finding] = []
    used: set[tuple[str, str, str]] = set()
    for relative in files:
        for finding in scan_file(root, relative):
            key = (finding.rule_id, finding.path, finding.fingerprint)
            if key in allowed:
                used.add(key)
            else:
                findings.append(finding)
    stale = sorted(allowed - used)
    return {
        "files_scanned": len(files),
        "rules": sorted([rule.rule_id for rule in CONTENT_RULES] + [rule[0] for rule in FORBIDDEN_NAME_RULES]),
        "findings": [finding.as_json() for finding in findings],
        "stale_allowlist_entries": [{"rule": r, "path": p, "sha256": h} for r, p, h in stale],
        "ok": not findings and not stale,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument("--json", action="store_true", help="print the report as JSON")
    arguments = parser.parse_args(argv)
    try:
        report = scan(arguments.root.resolve())
    except ScanError as error:
        print(f"HATA: {error}", file=sys.stderr)
        return 2
    if arguments.json:
        print(json.dumps(report, ensure_ascii=False, indent=1, sort_keys=True))
    else:
        for finding in report["findings"]:
            print(f"BULGU {finding['rule']}: {finding['path']}:{finding['line']} (sha256 {finding['sha256'][:12]}…)")
        for entry in report["stale_allowlist_entries"]:
            print(f"BAYAT izin listesi girdisi {entry['rule']}: {entry['path']}")
        status = "temiz" if report["ok"] else "BULGU VAR"
        print(f"Sır taraması: {report['files_scanned']} dosya, {len(report['findings'])} bulgu — {status}.")
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
