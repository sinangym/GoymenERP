"""Tests for the offline secret scanner (DALGA_PLANI_v2 1.18, WP16).

Fake credentials are assembled at run time from pieces, so no tracked file ever contains a value the scanner (or
GitHub's own secret scanning) would flag.
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SCANNER_PATH = REPOSITORY_ROOT / "tools/security/credential_scan.py"
SPEC = importlib.util.spec_from_file_location("goymen_credential_scan", SCANNER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {SCANNER_PATH}")
SCANNER = importlib.util.module_from_spec(SPEC)
sys.modules["goymen_credential_scan"] = SCANNER
SPEC.loader.exec_module(SCANNER)


def fake(*parts: str) -> str:
    return "".join(parts)


FAKE_VALUES = {
    "private-key-block": fake("-----BEGIN RSA ", "PRIVATE KEY-----"),
    "aws-access-key-id": fake("AK", "IA", "Q3EXAMPLEQ3EXAMP"),
    "github-token": fake("gh", "p_", "a" * 36),
    "slack-token": fake("xo", "xb-", "1234567890-abcdef"),
    "google-api-key": fake("AI", "za", "B" * 35),
    "stripe-live-key": fake("sk", "_live_", "c" * 24),
    "anthropic-api-key": fake("sk-", "ant-", "d" * 24),
    "openai-api-key": fake("sk-", "proj-", "e" * 20, "T3Blbk", "FJ", "f" * 20),
    "azure-storage-key": fake("Account", "Key=", "g" * 86, "=="),
    "npm-token": fake("np", "m_", "h" * 36),
}


class Workspace:
    def __init__(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.allowlist([])

    def write(self, relative: str, content: str) -> None:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def allowlist(self, entries: list[dict[str, str]]) -> None:
        self.write(
            SCANNER.ALLOWLIST_PATH,
            json.dumps({"format": SCANNER.ALLOWLIST_FORMAT, "entries": entries}),
        )

    def scan(self, *paths: str) -> dict[str, object]:
        return SCANNER.scan(self.root, list(paths))

    def close(self) -> None:
        self.directory.cleanup()


class SecretScannerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.workspace = Workspace()
        self.addCleanup(self.workspace.close)

    def test_every_content_rule_finds_its_fake_value(self) -> None:
        self.assertEqual(sorted(FAKE_VALUES), sorted(rule.rule_id for rule in SCANNER.CONTENT_RULES))
        for rule_id, value in FAKE_VALUES.items():
            with self.subTest(rule=rule_id):
                self.workspace.write("src/config.txt", f"first line\nkey = {value}\n")
                report = self.workspace.scan("src/config.txt")
                self.assertFalse(report["ok"])
                self.assertEqual([item["rule"] for item in report["findings"]], [rule_id])
                self.assertEqual(report["findings"][0]["line"], 2)

    def test_report_never_contains_the_secret_value(self) -> None:
        value = FAKE_VALUES["github-token"]
        self.workspace.write("a.txt", value)
        report = self.workspace.scan("a.txt")
        self.assertNotIn(value, json.dumps(report))
        self.assertEqual(report["findings"][0]["sha256"], SCANNER.fingerprint(value))

    def test_placeholders_and_ordinary_text_are_clean(self) -> None:
        self.workspace.write(
            "appsettings.json",
            '{"ConnectionStrings": {"Erp": "Server=localhost;Password=${SQL_PASSWORD}"},'
            ' "Storage": "AccountKey=<placeholder>", "Note": "sk-ant- prefix only", "Id": "AKIA"}\n',
        )
        self.workspace.write(".env.example", "API_KEY=changeme\n")
        report = self.workspace.scan("appsettings.json", ".env.example")
        self.assertTrue(report["ok"], report)

    def test_forbidden_file_names_fail_whatever_they_contain(self) -> None:
        for relative, rule in (
            (".env", "env-file"),
            ("deploy/.env.production", "env-file"),
            ("certs/signing.pfx", "key-store-file"),
            ("keys/id_ed25519", "ssh-private-key-file"),
            ("site.PublishSettings", "publish-profile-file"),
        ):
            with self.subTest(path=relative):
                self.workspace.write(relative, "empty\n")
                report = self.workspace.scan(relative)
                self.assertEqual([item["rule"] for item in report["findings"]], [rule])
        for allowed in (".env.example", ".env.sample", ".env.template"):
            with self.subTest(path=allowed):
                self.workspace.write(allowed, "X=placeholder\n")
                self.assertTrue(self.workspace.scan(allowed)["ok"])

    def test_allowlist_entry_suppresses_exactly_one_value_and_must_stay_in_use(self) -> None:
        value = FAKE_VALUES["aws-access-key-id"]
        self.workspace.write("tests/fixture.txt", value)
        entry = {
            "rule": "aws-access-key-id",
            "path": "tests/fixture.txt",
            "sha256": SCANNER.fingerprint(value),
            "reason": "AWS documentation example value used by a parser test.",
        }
        self.workspace.allowlist([entry])
        self.assertTrue(self.workspace.scan("tests/fixture.txt")["ok"])
        self.workspace.write("tests/other.txt", value)
        self.assertFalse(self.workspace.scan("tests/fixture.txt", "tests/other.txt")["ok"])
        self.workspace.write("tests/fixture.txt", "removed\n")
        report = self.workspace.scan("tests/fixture.txt")
        self.assertFalse(report["ok"])
        self.assertEqual(len(report["stale_allowlist_entries"]), 1)

    def test_malformed_allowlist_fails_closed(self) -> None:
        bad_entries = (
            [{"rule": "unknown", "path": "a", "sha256": "0" * 64, "reason": "long enough reason"}],
            [{"rule": "github-token", "path": "a", "sha256": "XYZ", "reason": "long enough reason"}],
            [{"rule": "github-token", "path": "a", "sha256": "0" * 64, "reason": "short"}],
            [{"rule": "github-token", "path": "a", "sha256": "0" * 64}],
        )
        for entries in bad_entries:
            with self.subTest(entries=entries):
                self.workspace.allowlist(entries)
                with self.assertRaises(SCANNER.ScanError):
                    self.workspace.scan()

    def test_binary_files_are_skipped(self) -> None:
        target = self.workspace.root / "image.png"
        target.write_bytes(b"\x89PNG\0\0" + FAKE_VALUES["github-token"].encode())
        self.assertTrue(self.workspace.scan("image.png")["ok"])

    def test_scanner_source_and_this_test_do_not_match_their_own_rules(self) -> None:
        for relative in ("tools/security/credential_scan.py", "tests/security_scan/test_credential_scan.py"):
            with self.subTest(path=relative):
                self.assertEqual(SCANNER.scan_file(REPOSITORY_ROOT, relative), [])


class RepositorySecretScanTests(unittest.TestCase):
    def test_tracked_files_of_this_repository_are_clean(self) -> None:
        inside_git = subprocess.run(
            ["git", "-C", str(REPOSITORY_ROOT), "rev-parse", "--is-inside-work-tree"],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(inside_git.stdout.strip(), "true", "the repository scan needs a git checkout")
        report = SCANNER.scan(REPOSITORY_ROOT)
        self.assertEqual(report["findings"], [], "remove the secret or add a reviewed allowlist entry")
        self.assertEqual(report["stale_allowlist_entries"], [])
        self.assertGreater(report["files_scanned"], 0)


if __name__ == "__main__":
    unittest.main()
