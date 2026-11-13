"""CIH-D5 guard for the trigger path filter of the CI workflows.

GitHub starts a workflow only when at least one changed file is included by ``paths``. Patterns
are read in order, the last matching one decides, and ``!`` excludes. These tests pin the
reviewed list, prove against the repository's own files that exactly the reviewed prose is
skipped, and fail when checked-in code starts to read a skipped file.

GOV-D5: the Faz 0 tier (c) commands in AGENTS.md let a merge go ahead when ``main`` moved only in
that skipped prose. The last two tests keep those commands equal to the filter.
"""
from __future__ import annotations

import os
import re
import subprocess
import tempfile
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

# Paths that must start CI, and prose that must not. Both the filter model and the Faz 0 (c)
# commands are checked against them.
MUST_START = (
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
SKIPPED_PROSE = (
    "docs/decisions/SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md", "docs/delivery/PLAN.md",
    "docs/delivery/nested/deeper/PLAN.md", "docs/handoffs/NOTE.md", "docs/operations/RUNBOOK.md",
)

# GOV-D5: the Faz 0 tier (c) commands, exactly as AGENTS.md gives them to the merger.
FAZ0_SKIPPED_ONLY_COMMANDS = (
    'MB="$(git merge-base origin/main <expected-head>)"',
    'git diff --quiet "$MB" origin/main -- \':(top)\' "${SKIPPED[@]}" '
    '&& git diff --quiet "$MB" origin/main -- "${READ_BY_CI[@]}"',
)

# Folders below the root from which the commands are also run in the test repository.
WORKING_FOLDERS = ("workdir", "workdir/deeper")

# CIH-D6: the C006 workflow replaces the older run of a pull request only. On ``main`` every run
# is alone in its group, so it is neither cancelled nor replaced while it waits.
C006_CONCURRENCY = (
    "concurrency:\n"
    "  group: cari-c006-${{ github.event_name == 'pull_request' && github.ref || github.run_id }}\n"
    "  cancel-in-progress: ${{ github.event_name == 'pull_request' }}\n"
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


def read_faz0_skipped_only_script() -> str:
    """The one fenced block of AGENTS.md that defines the Faz 0 tier (c) check."""
    fenced = (REPO / "AGENTS.md").read_text(encoding="utf-8").split("```")[1::2]
    blocks = [block for block in fenced if "SKIPPED=(" in block or "READ_BY_CI=(" in block]
    if len(blocks) != 1:
        raise AssertionError(f"AGENTS.md: expected one Faz 0 (c) block, found {len(blocks)}")
    return blocks[0].strip("\n")


def shell_array(script: str, name: str) -> list[str]:
    found = re.findall(rf"^{name}=\((.*?)\)$", script, flags=re.MULTILINE | re.DOTALL)
    if len(found) != 1:
        raise AssertionError(f"AGENTS.md: expected one {name}=(…) array, found {len(found)}")
    items = found[0].replace("\\\n", " ").split()
    return [item[1:-1] if len(item) >= 2 and item[0] == item[-1] == "'" else item for item in items]


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

    def test_no_other_workflow_carries_a_path_filter(self):
        # ``paths-ignore`` of the C-01 workflow has its own guard in tests/c01_e2e_contract.
        filtered = sorted(
            path.name for path in WORKFLOWS.iterdir()
            if path.suffix in (".yml", ".yaml")
            and re.search(r"^\s*paths\s*:", path.read_text(encoding="utf-8"), re.MULTILINE)
        )
        self.assertEqual(filtered, sorted(FILTERED_WORKFLOWS))

    def test_c006_concurrency_replaces_pull_request_runs_only(self):
        text = (WORKFLOWS / "cari-c02-link-acceptance.yml").read_text(encoding="utf-8")
        self.assertEqual(text.count("concurrency:"), 1)
        self.assertEqual(text.count("cancel-in-progress"), 1)
        self.assertIn("\n" + C006_CONCURRENCY + "\njobs:\n", text)

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
        for path in MUST_START:
            with self.subTest(path=path):
                self.assertTrue(starts_workflow(path))
        for path in SKIPPED_PROSE:
            with self.subTest(path=path):
                self.assertFalse(starts_workflow(path))
        for path in MUST_START + SKIPPED_PROSE:
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

    def test_faz0_skipped_only_commands_carry_the_filter_lists(self):
        script = read_faz0_skipped_only_script()
        self.assertEqual(
            shell_array(script, "SKIPPED"),
            [f":(top,exclude,glob){root}**/*.md" for root in PROSE_ROOTS],
        )
        self.assertEqual(
            tuple(shell_array(script, "READ_BY_CI")), tuple(f":(top){path}" for path in READ_BY_CI)
        )
        self.assertEqual(tuple(script.splitlines()[-2:]), FAZ0_SKIPPED_ONLY_COMMANDS)
        self.assertEqual(len(script.splitlines()), 6)

    def test_faz0_skipped_only_commands_agree_with_the_filter_in_git(self):
        script = read_faz0_skipped_only_script()
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory) / "repository"
            repository.mkdir()
            environment = {
                "PATH": os.environ["PATH"],
                "HOME": directory,
                "GIT_CONFIG_GLOBAL": os.devnull,
                "GIT_CONFIG_NOSYSTEM": "1",
                "GIT_AUTHOR_NAME": "Faz 0 test",
                "GIT_AUTHOR_EMAIL": "faz0-test@example.invalid",
                "GIT_COMMITTER_NAME": "Faz 0 test",
                "GIT_COMMITTER_EMAIL": "faz0-test@example.invalid",
            }

            def git(*arguments):
                return subprocess.run(
                    ["git", "-C", str(repository), *arguments],
                    env=environment, check=True, capture_output=True, text=True,
                ).stdout.strip()

            def commit(changes):
                for relative, content in changes.items():
                    target = repository / relative
                    if content is None:
                        target.unlink()
                    else:
                        target.parent.mkdir(parents=True, exist_ok=True)
                        target.write_text(content, encoding="utf-8")
                git("add", "-A")
                git("commit", "-q", "-m", "change")
                return git("rev-parse", "HEAD")

            def run_commands(head):
                """Runs the block as a merger pastes it; its status is that of the last line.

                The block is run at the root and in two subfolders: the answer must not depend
                on the working folder (a relative pathspec would see only that folder).
                """
                codes = {
                    subprocess.run(
                        ["bash", "-c", script.replace("<expected-head>", head)],
                        cwd=repository / folder, env=environment, capture_output=True, text=True,
                    ).returncode
                    for folder in (".", *WORKING_FOLDERS)
                }
                self.assertEqual(len(codes), 1, f"working folder changed the result: {codes}")
                return codes.pop()

            def main_moved_only_in_skipped_prose(head, *main_commits):
                """Builds ``main`` on top of the fork point and runs the AGENTS.md commands."""
                git("checkout", "-q", "--detach", fork)
                tip = fork
                for changes in main_commits:
                    tip = commit(changes)
                git("update-ref", "refs/remotes/origin/main", tip)
                return run_commands(head)

            git("init", "-q")
            for folder in WORKING_FOLDERS:  # untracked and empty: git never removes them
                (repository / folder).mkdir(parents=True)
            existing_prose = "docs/operations/OLD_RUNBOOK.md"
            fork = commit({
                "src/app.cs": "one\n", existing_prose: "one\n",
                **{path: "one\n" for path in READ_BY_CI},
            })
            head = commit({"src/app.cs": "two\n"})

            self.assertEqual(main_moved_only_in_skipped_prose(head), 0)
            for path in SKIPPED_PROSE:
                with self.subTest(skipped=path):
                    self.assertEqual(main_moved_only_in_skipped_prose(head, {path: "new\n"}), 0)
            self.assertEqual(main_moved_only_in_skipped_prose(head, {existing_prose: "two\n"}), 0)
            self.assertEqual(main_moved_only_in_skipped_prose(head, {existing_prose: None}), 0)
            self.assertEqual(
                main_moved_only_in_skipped_prose(
                    head, {SKIPPED_PROSE[0]: "new\n"}, {SKIPPED_PROSE[1]: "new\n"}
                ),
                0,
            )

            for path in MUST_START + READ_BY_CI:
                with self.subTest(starts_ci=path):
                    self.assertEqual(main_moved_only_in_skipped_prose(head, {path: "new\n"}), 1)
            for label, main_commits in (
                ("prose and code in one commit", ({SKIPPED_PROSE[0]: "new\n", "src/new.cs": "new\n"},)),
                ("code after prose", ({SKIPPED_PROSE[0]: "new\n"}, {"src/app.cs": "main\n"})),
                ("re-included file deleted", ({READ_BY_CI[0]: None},)),
                ("prose moved to a code path", ({existing_prose: None, "src/OLD_RUNBOOK.md": "one\n"},)),
                ("code moved into a prose folder", ({"src/app.cs": None, "docs/delivery/app.md": "one\n"},)),
            ):
                with self.subTest(case=label):
                    self.assertEqual(main_moved_only_in_skipped_prose(head, *main_commits), 1)

            # Fail closed: an unknown head or a missing origin/main is never "prose only".
            self.assertNotIn(
                main_moved_only_in_skipped_prose("0" * 40, {SKIPPED_PROSE[0]: "new\n"}), (0, 1)
            )
            git("update-ref", "-d", "refs/remotes/origin/main")
            self.assertNotIn(run_commands(head), (0, 1))


if __name__ == "__main__":
    unittest.main()
