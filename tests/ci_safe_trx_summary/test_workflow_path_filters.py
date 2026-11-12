"""CIH-D5 guard for the trigger path filter of the CI workflows.

GitHub starts a workflow only when at least one changed file is included by ``paths``. Patterns
are read in order, the last matching one decides, and ``!`` excludes. These tests pin the
reviewed list, prove against the repository's own files that exactly the reviewed prose is
skipped, and fail when checked-in code starts to read a skipped file.
"""
from __future__ import annotations

import os
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO / ".github" / "workflows"
THIS_FILE = "tests/ci_safe_trx_summary/test_workflow_path_filters.py"

FILTERED_WORKFLOWS = ("ci.yml", "cari-c02-link-acceptance.yml")

# Markdown below these folders is prose for people; no CI job opens it.
PROSE_ROOTS = ("docs/decisions/", "docs/delivery/", "docs/handoffs/", "docs/operations/")

# Markdown below those folders that a CI validator does read; it must keep starting CI.
READ_BY_CI = (
    "docs/decisions/KARAR_SICILI.md",
    "docs/decisions/ACIK_KARARLAR.md",
    "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
    "docs/delivery/DECISION_PACK.md",
)

EXPECTED_FILTER = (
    "**",
    "!docs/decisions/**/*.md",
    "docs/decisions/KARAR_SICILI.md",
    "docs/decisions/ACIK_KARARLAR.md",
    "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
    "!docs/delivery/**/*.md",
    "docs/delivery/DECISION_PACK.md",
    "!docs/handoffs/**/*.md",
    "!docs/operations/**/*.md",
)

CODE_ROOTS = ("src", "tests", "tools", ".github")
CODE_SUFFIXES = (".cs", ".py", ".ps1", ".sh", ".ts", ".tsx", ".js", ".mjs", ".cjs", ".yml", ".yaml")
PRUNED_DIRECTORIES = frozenset(
    {".git", "node_modules", "bin", "obj", "dist", "TestResults", "artifacts", "coverage"}
)
PROSE_MENTION = re.compile(
    r"docs[/\\]+(?:decisions|delivery|handoffs|operations)(?![A-Za-z0-9_-])[A-Za-z0-9_.*/\\-]*"
)


def read_triggers(name: str) -> dict[str, dict[str, list[str]]]:
    """Reads the ``on:`` block. Any shape other than the reviewed one fails closed."""
    lines = (WORKFLOWS / name).read_text(encoding="utf-8").splitlines()
    if lines.count("on:") != 1:
        raise AssertionError(f"{name}: expected exactly one top-level 'on:' key")
    triggers: dict[str, dict[str, list[str]]] = {}
    event: str | None = None
    key: str | None = None
    for raw in lines[lines.index("on:") + 1:]:
        if raw and not raw.startswith(" "):
            break
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        indent = len(raw) - len(raw.lstrip(" "))
        if indent == 2 and line.endswith(":"):
            event, key = line[:-1], None
            if event in triggers:
                raise AssertionError(f"{name}: duplicate event {event!r}")
            triggers[event] = {}
        elif indent == 4 and event is not None and not line.startswith("-") and ":" in line:
            key, _, inline = line.partition(":")
            inline = inline.strip()
            if key in triggers[event]:
                raise AssertionError(f"{name}: duplicate key {event}.{key}")
            if not inline:
                triggers[event][key] = []
            elif inline.startswith("[") and inline.endswith("]"):
                triggers[event][key] = [item.strip() for item in inline[1:-1].split(",")]
            else:
                raise AssertionError(f"{name}: unsupported value in {raw!r}")
        elif indent == 6 and key is not None and line.startswith("- "):
            item = line[2:].strip()
            if len(item) >= 2 and item[0] == item[-1] == "'":
                item = item[1:-1]
            triggers[event][key].append(item)
        else:
            raise AssertionError(f"{name}: unexpected trigger line {raw!r}")
    return triggers


def pattern_regex(pattern: str) -> re.Pattern[str]:
    """GitHub filter pattern subset: literals, ``*`` (no slash), ``**`` (any), ``**/`` (0+ dirs)."""
    parts: list[str] = []
    index = 0
    while index < len(pattern):
        if pattern.startswith("**/", index):
            parts.append("(?:.*/)?")
            index += 3
        elif pattern.startswith("**", index):
            parts.append(".*")
            index += 2
        elif pattern[index] == "*":
            parts.append("[^/]*")
            index += 1
        elif pattern[index] in "?+[]!\\":
            raise AssertionError(f"unsupported character in filter pattern {pattern!r}")
        else:
            parts.append(re.escape(pattern[index]))
            index += 1
    return re.compile("".join(parts))


def starts_workflow(path: str, patterns: tuple[str, ...] = EXPECTED_FILTER) -> bool:
    included = False
    for pattern in patterns:
        negative = pattern.startswith("!")
        if pattern_regex(pattern[1:] if negative else pattern).fullmatch(path):
            included = not negative
    return included


def repository_files(top: str) -> list[str]:
    found: list[str] = []
    for directory, names, files in os.walk(REPO / top):
        names[:] = sorted(name for name in names if name not in PRUNED_DIRECTORIES)
        relative = Path(directory).relative_to(REPO).as_posix()
        found.extend(f"{relative}/{name}" for name in sorted(files))
    return found


def code_part(line: str, suffix: str) -> str:
    """Drops comments: a mention in a comment does not make CI read a file."""
    stripped = line.lstrip()
    if stripped.startswith(("//", "#", "*", "/*", "<!--")):
        return ""
    if suffix in (".cs", ".ts", ".tsx", ".js", ".mjs", ".cjs"):
        return re.split(r"(?<!:)//", line, maxsplit=1)[0]
    return re.split(r"(?:^|\s)#", line, maxsplit=1)[0]


class WorkflowPathFilterTests(unittest.TestCase):
    def test_both_events_of_each_filtered_workflow_carry_the_reviewed_list(self):
        for name in FILTERED_WORKFLOWS:
            with self.subTest(workflow=name):
                triggers = read_triggers(name)
                self.assertEqual(sorted(triggers), ["pull_request", "push"])
                self.assertEqual(sorted(triggers["pull_request"]), ["paths"])
                self.assertEqual(sorted(triggers["push"]), ["branches", "paths"])
                self.assertEqual(triggers["push"]["branches"], ["main"])
                self.assertEqual(tuple(triggers["pull_request"]["paths"]), EXPECTED_FILTER)
                self.assertEqual(tuple(triggers["push"]["paths"]), EXPECTED_FILTER)

    def test_only_markdown_in_the_reviewed_folders_can_be_ignored(self):
        self.assertEqual(EXPECTED_FILTER[0], "**")
        negatives = [pattern for pattern in EXPECTED_FILTER if pattern.startswith("!")]
        self.assertEqual(negatives, [f"!{root}**/*.md" for root in PROSE_ROOTS])
        positives = [pattern for pattern in EXPECTED_FILTER[1:] if not pattern.startswith("!")]
        self.assertEqual(tuple(positives), READ_BY_CI)
        for path in READ_BY_CI:
            with self.subTest(path=path):
                self.assertNotIn("*", path)
                self.assertTrue((REPO / path).is_file(), f"{path} is re-included but missing")
                self.assertTrue(starts_workflow(path))

    def test_exactly_the_reviewed_prose_is_skipped(self):
        documents = repository_files("docs")
        expected = {
            path for path in documents
            if path.startswith(PROSE_ROOTS) and path.endswith(".md") and path not in READ_BY_CI
        }
        self.assertTrue(expected)
        self.assertEqual({path for path in documents if not starts_workflow(path)}, expected)

    def test_everything_else_still_starts_ci(self):
        must_start = (
            ".github/workflows/ci.yml", ".editorconfig", "AGENTS.md", "global.json",
            "Directory.Build.props", "src/.editorconfig", "src/backend/GoymenERP.Api/Program.cs",
            "src/frontend/goymen-erp-web/src/main.tsx", "contracts/openapi/goymen-erp-v1.json",
            "contracts/recovery/recovery-surface-inventory-v1.schema.json",
            "tools/generate_recovery_surface_inventory.py", "tools/relational_inventory/sales.txt",
            "tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs",
            "tests/release_manifest/test_generate_release_manifest.py",
            "docs/backlog/DELIVERY_BACKLOG_v1.json", "docs/backlog/task_manifest.json",
            "docs/acceptance/ACCEPTANCE_70.md", "docs/ui/CANONICAL_SCREEN_STATUS.csv",
            "docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json",
            "docs/features/CARI_UI_VERTICAL_SLICE_v1.md", "docs/governance/ANY.md",
            "docs/decisions/KARAR_SICILI.csv", "docs/decisions/NOTE.MD", "docs/decisions/note.txt",
            # File names with a build meaning never end in .md, so they are never ignored.
            "docs/decisions/global.json", "docs/delivery/Directory.Build.props",
            "docs/handoffs/NuGet.Config", "docs/operations/.config/dotnet-tools.json",
            "docs/decisionsX/note.md", "other/docs/decisions/note.md", "docs/note.md",
        )
        skipped = (
            "docs/decisions/SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md", "docs/delivery/PLAN.md",
            "docs/delivery/nested/deeper/PLAN.md", "docs/handoffs/NOTE.md", "docs/operations/RUNBOOK.md",
        )
        for path in must_start:
            with self.subTest(path=path):
                self.assertTrue(starts_workflow(path))
        for path in skipped:
            with self.subTest(path=path):
                self.assertFalse(starts_workflow(path))
        for path in must_start + skipped:
            self.assertFalse(starts_workflow(path, ("**", "!**")))
        with self.assertRaises(AssertionError):
            pattern_regex("docs/[a-z]*.md")

    def test_checked_in_code_does_not_read_a_skipped_file(self):
        exempt = {THIS_FILE, *(f".github/workflows/{name}" for name in FILTERED_WORKFLOWS)}
        offenders: list[str] = []
        for root in CODE_ROOTS:
            for path in repository_files(root):
                if path in exempt or not path.endswith(CODE_SUFFIXES):
                    continue
                text = (REPO / path).read_text(encoding="utf-8", errors="replace")
                if "docs" not in text:
                    continue
                suffix = Path(path).suffix
                for number, line in enumerate(text.splitlines(), start=1):
                    for mention in PROSE_MENTION.findall(code_part(line, suffix)):
                        target = re.sub(r"[/\\]+", "/", mention).rstrip("./")
                        is_file = "*" not in target and (REPO / target).is_file()
                        if not (is_file and starts_workflow(target)):
                            offenders.append(f"{path}:{number}: {mention}")
        self.assertEqual(
            offenders,
            [],
            "Code refers to prose that the CI path filter ignores (CIH-D5). A change to that file "
            "alone would not start CI. Re-include the file in the filter of every workflow in "
            "FILTERED_WORKFLOWS (K3 review) or stop reading it.",
        )


if __name__ == "__main__":
    unittest.main()
