"""Pins the prepared-but-inactive Dependabot configuration (DALGA_PLANI_v2 1.18, WP16).

Turning dependency updates on is a human gate (Settings, and raising ``open-pull-requests-limit``). Until that
decision the file must open no pull request, configure no registry, secret or token, and cover every manifest the
repository already uses. Parsed without PyYAML (the CI preflight runs ``python3 -I -S``): the file must keep the
simple, reviewed shape below or the test fails closed.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONFIG = REPOSITORY_ROOT / ".github" / "dependabot.yml"

EXPECTED_UPDATES = [
    ("github-actions", "/"),
    ("nuget", "/"),
    ("npm", "/src/frontend/goymen-erp-web"),
    ("npm", "/tests/e2e"),
    ("docker", "/src/backend/GoymenERP.Api"),
    ("docker", "/src/frontend/goymen-erp-web"),
]
ALLOWED_KEYS = {"package-ecosystem", "directory", "schedule", "interval", "open-pull-requests-limit"}
FORBIDDEN_WORDS = ("registries", "secrets.", "token", "password", "insecure-external-code-execution")


def read_updates() -> list[dict[str, str]]:
    lines = [line for line in CONFIG.read_text(encoding="utf-8").splitlines()
             if line.strip() and not line.lstrip().startswith("#")]
    if lines[:2] != ["version: 2", "updates:"]:
        raise AssertionError("dependabot.yml must start with 'version: 2' and 'updates:'")
    updates: list[dict[str, str]] = []
    for line in lines[2:]:
        match = re.fullmatch(r"(?P<indent> *)(?P<dash>- )?(?P<key>[a-z-]+):(?: (?P<value>\S+))?", line)
        if match is None:
            raise AssertionError(f"unsupported dependabot.yml line: {line!r}")
        if match.group("dash"):
            if match.group("indent") != "  ":
                raise AssertionError(f"unexpected list indentation: {line!r}")
            updates.append({})
        if not updates:
            raise AssertionError("key outside an update entry")
        key = match.group("key")
        if key not in ALLOWED_KEYS:
            raise AssertionError(f"unreviewed dependabot.yml key: {key!r}")
        if key in updates[-1]:
            raise AssertionError(f"duplicate key {key!r}")
        updates[-1][key] = match.group("value") or ""
    return updates


class DependabotConfigTests(unittest.TestCase):
    def test_every_manifest_location_is_covered_once(self) -> None:
        updates = read_updates()
        self.assertEqual([(item["package-ecosystem"], item["directory"]) for item in updates], EXPECTED_UPDATES)
        for ecosystem, directory in EXPECTED_UPDATES:
            with self.subTest(ecosystem=ecosystem, directory=directory):
                folder = REPOSITORY_ROOT / directory.lstrip("/")
                manifest = {
                    "github-actions": ".github/workflows",
                    "nuget": "GoymenERP.slnx",
                    "npm": "package-lock.json",
                    "docker": "Dockerfile",
                }[ecosystem]
                self.assertTrue((folder / manifest).exists(), f"{directory}: {manifest} is missing")

    def test_no_pull_request_is_opened_until_a_person_enables_it(self) -> None:
        for item in read_updates():
            with self.subTest(item=item):
                self.assertEqual(item["open-pull-requests-limit"], "0")
                self.assertEqual(item["interval"], "weekly")

    def test_no_registry_secret_or_token_is_configured(self) -> None:
        text = CONFIG.read_text(encoding="utf-8").lower()
        code = "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))
        for word in FORBIDDEN_WORDS:
            with self.subTest(word=word):
                self.assertNotIn(word, code)


if __name__ == "__main__":
    unittest.main()
