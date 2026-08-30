from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from tools import validate_dotnet_bootstrap_integrity as validator  # noqa: E402


VALID_DOCUMENT = {
    "sdk": {
        "version": "10.0.300",
        "rollForward": "latestFeature",
        "allowPrerelease": False,
    }
}
DIRECTORY_BUILD_PROPS_BYTES = b"""<Project>
  <PropertyGroup>
    <Deterministic>true</Deterministic>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
"""
NUGET_CONFIG_BYTES = b"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"""
DOTNET_TOOLS_BYTES = b"""{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "10.0.8",
      "commands": [
        "dotnet-ef"
      ],
      "rollForward": false
    }
  }
}"""


class BootstrapIntegrityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.repository = Path(self.temporary_directory.name) / "repository"
        self.repository.mkdir()
        self.write_global_json(VALID_DOCUMENT)
        (self.repository / "Directory.Build.props").write_bytes(
            DIRECTORY_BUILD_PROPS_BYTES
        )
        (self.repository / "NuGet.Config").write_bytes(NUGET_CONFIG_BYTES)
        (self.repository / "dotnet-tools.json").write_bytes(DOTNET_TOOLS_BYTES)

    def write_global_json(self, document: object) -> None:
        (self.repository / "global.json").write_text(
            json.dumps(document), encoding="utf-8"
        )

    def write_raw_global_json(self, content: str) -> None:
        (self.repository / "global.json").write_text(content, encoding="utf-8")

    def assert_rejected(self, pattern: str) -> None:
        with self.assertRaisesRegex(validator.BootstrapIntegrityError, pattern):
            validator.validate_repository(self.repository)

    def test_accepts_exact_policy_and_unrelated_regular_files(self) -> None:
        nested = self.repository / "src" / "feature"
        nested.mkdir(parents=True)
        (nested / "README.txt").write_text("unrelated", encoding="utf-8")

        validator.validate_repository(self.repository)

    def test_rejects_missing_or_hash_drifted_root_directory_build_props(self) -> None:
        policy = self.repository / "Directory.Build.props"
        policy.unlink()
        self.assert_rejected("exactly one exact-case regular Directory.Build.props")

        policy.write_bytes(DIRECTORY_BUILD_PROPS_BYTES + b"\n")
        self.assert_rejected("byte length drifted")

        mutated = bytearray(DIRECTORY_BUILD_PROPS_BYTES)
        mutated[10] = ord("X")
        policy.write_bytes(mutated)
        self.assert_rejected("SHA-256 drifted")

    def test_rejects_nested_or_case_variant_directory_build_props(self) -> None:
        root_policy = self.repository / "Directory.Build.props"
        case_variant = self.repository / "DIRECTORY.BUILD.PROPS"
        root_policy.rename(case_variant)
        self.assert_rejected("allowed only with exact casing")
        case_variant.rename(root_policy)

        for index, name in enumerate(
            ("Directory.Build.props", "directory.build.PROPS")
        ):
            with self.subTest(name=name):
                nested = self.repository / f"nested-policy-{index}" / name
                nested.parent.mkdir(parents=True)
                nested.write_bytes(DIRECTORY_BUILD_PROPS_BYTES)
                self.assert_rejected("allowed only with exact casing")
                nested.unlink()

    def test_rejects_additional_msbuild_auto_import_surfaces_case_insensitively(self) -> None:
        names = (
            "Directory.Build.targets",
            "directory.build.TARGETS",
            "Directory.Packages.props",
            "directory.packages.PROPS",
            "Project.csproj.user",
            "PROJECT.CSPROJ.USER",
        )
        for index, name in enumerate(names):
            with self.subTest(name=name):
                path = self.repository / f"auto-import-{index}" / name
                path.parent.mkdir(parents=True)
                path.write_text("override", encoding="utf-8")
                self.assert_rejected("Forbidden")
                path.unlink()

    def test_rejects_missing_or_drifted_root_nuget_config(self) -> None:
        policy = self.repository / "NuGet.Config"
        policy.unlink()
        self.assert_rejected("exactly one exact-case regular NuGet.Config")

        policy.write_bytes(NUGET_CONFIG_BYTES + b"\n")
        self.assert_rejected("NuGet.Config byte length drifted")

        policy.write_bytes(NUGET_CONFIG_BYTES.replace(b"nuget.org", b"evil.test"))
        self.assert_rejected("NuGet.Config SHA-256 drifted")

    def test_rejects_nuget_feed_mapping_or_credentials_drift(self) -> None:
        mutations = {
            "feed": NUGET_CONFIG_BYTES.replace(
                b"https://api.nuget.org/v3/index.json",
                b"https://packages.invalid/v3/index.json",
            ),
            "source-mapping": NUGET_CONFIG_BYTES.replace(
                b'<package pattern="*" />',
                b'<package pattern="Contoso.*" />',
            ),
            "credentials": NUGET_CONFIG_BYTES.replace(
                b"</configuration>",
                b"  <packageSourceCredentials><nuget.org>"
                b"<add key=\"ClearTextPassword\" value=\"secret\" />"
                b"</nuget.org></packageSourceCredentials>\n</configuration>",
            ),
            "additional-source": NUGET_CONFIG_BYTES.replace(
                b"</packageSources>",
                b'  <add key="extra" value="https://packages.invalid/v3/index.json" />'
                b"\n  </packageSources>",
            ),
        }
        for name, content in mutations.items():
            with self.subTest(mutation=name):
                (self.repository / "NuGet.Config").write_bytes(content)
                self.assert_rejected(r"NuGet.Config (?:byte length|SHA-256) drifted")

    def test_rejects_nested_or_case_variant_nuget_config(self) -> None:
        root_policy = self.repository / "NuGet.Config"
        case_variant = self.repository / "NUGET.CONFIG"
        root_policy.rename(case_variant)
        self.assert_rejected("allowed only with exact casing")
        case_variant.rename(root_policy)

        for index, name in enumerate(("NuGet.Config", "nuget.CONFIG")):
            with self.subTest(name=name):
                nested = self.repository / f"nested-nuget-{index}" / name
                nested.parent.mkdir(parents=True)
                nested.write_bytes(NUGET_CONFIG_BYTES)
                self.assert_rejected("allowed only with exact casing")
                nested.unlink()

    def test_rejects_root_nuget_config_symlink_if_supported(self) -> None:
        policy = self.repository / "NuGet.Config"
        outside = Path(self.temporary_directory.name) / "outside-nuget.config"
        outside.write_bytes(NUGET_CONFIG_BYTES)
        policy.unlink()
        try:
            policy.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("Symlink or reparse point")

    def test_rejects_missing_or_drifted_root_dotnet_tools_manifest(self) -> None:
        policy = self.repository / "dotnet-tools.json"
        policy.unlink()
        self.assert_rejected("exactly one exact-case regular dotnet-tools.json")

        policy.write_bytes(DOTNET_TOOLS_BYTES + b"\n")
        self.assert_rejected("dotnet-tools.json byte length drifted")

        policy.write_bytes(DOTNET_TOOLS_BYTES.replace(b"10.0.8", b"10.0.9"))
        self.assert_rejected("dotnet-ef.version must be exactly '10.0.8'")

    def test_rejects_dotnet_tools_schema_and_strict_json_drift(self) -> None:
        valid = json.loads(DOTNET_TOOLS_BYTES)
        variants: list[tuple[str, bytes, str]] = []

        unknown_root = dict(valid)
        unknown_root["unknown"] = True
        variants.append(
            ("unknown-root", json.dumps(unknown_root).encode(), "root keys")
        )
        missing_root = dict(valid)
        del missing_root["isRoot"]
        variants.append(
            ("missing-root", json.dumps(missing_root).encode(), "root keys")
        )
        wrong_commands = json.loads(DOTNET_TOOLS_BYTES)
        wrong_commands["tools"]["dotnet-ef"]["commands"] = ["other-command"]
        variants.append(
            ("commands", json.dumps(wrong_commands).encode(), "commands must be exactly")
        )
        wrong_roll_forward = json.loads(DOTNET_TOOLS_BYTES)
        wrong_roll_forward["tools"]["dotnet-ef"]["rollForward"] = True
        variants.append(
            (
                "roll-forward",
                json.dumps(wrong_roll_forward).encode(),
                "rollForward must be exactly false",
            )
        )
        variants.extend(
            (
                (
                    "duplicate-key",
                    DOTNET_TOOLS_BYTES.replace(
                        b'"version": 1,', b'"version": 1,\n  "version": 1,'
                    ),
                    "Duplicate JSON key",
                ),
                (
                    "non-finite",
                    DOTNET_TOOLS_BYTES[:-1] + b',\n  "unexpected": NaN\n}',
                    "Non-finite JSON constant",
                ),
            )
        )

        for name, content, error in variants:
            with self.subTest(mutation=name):
                (self.repository / "dotnet-tools.json").write_bytes(content)
                self.assert_rejected(error)

    def test_rejects_nested_or_case_variant_dotnet_tools_manifest(self) -> None:
        root_policy = self.repository / "dotnet-tools.json"
        case_variant = self.repository / "DOTNET-TOOLS.JSON"
        root_policy.rename(case_variant)
        self.assert_rejected("allowed only with exact casing")
        case_variant.rename(root_policy)

        for index, name in enumerate(("dotnet-tools.json", "DOTNET-TOOLS.JSON")):
            with self.subTest(name=name):
                nested = self.repository / f"nested-tools-{index}" / name
                nested.parent.mkdir(parents=True)
                nested.write_bytes(DOTNET_TOOLS_BYTES)
                self.assert_rejected("allowed only with exact casing")
                nested.unlink()

    def test_rejects_root_dotnet_tools_symlink_if_supported(self) -> None:
        policy = self.repository / "dotnet-tools.json"
        outside = Path(self.temporary_directory.name) / "outside-tools.json"
        outside.write_bytes(DOTNET_TOOLS_BYTES)
        policy.unlink()
        try:
            policy.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("Symlink or reparse point")

    def test_rejects_missing_global_json(self) -> None:
        (self.repository / "global.json").unlink()

        self.assert_rejected("exactly one case-insensitive global.json")

    def test_rejects_nested_global_json_case_insensitively(self) -> None:
        for index, name in enumerate(("global.json", "GLOBAL.JSON")):
            with self.subTest(name=name):
                nested = self.repository / f"nested-global-{index}" / name
                nested.parent.mkdir(parents=True)
                nested.write_text(json.dumps(VALID_DOCUMENT), encoding="utf-8")
                self.assert_rejected("Nested global.json policy is not allowed")
                nested.unlink()

    def test_rejects_inherited_bootstrap_files_case_insensitively(self) -> None:
        names = (
            "Directory.Build.rsp",
            "directory.build.RSP",
            "MSBuild.rsp",
            "msbuild.RSP",
            "Directory.Build.targets",
            "directory.build.TARGETS",
            "Directory.Packages.props",
            "directory.packages.PROPS",
            "Directory.Solution.props",
            "directory.solution.PROPS",
            "Directory.Solution.targets",
            "directory.solution.TARGETS",
        )
        for name in names:
            with self.subTest(name=name):
                inherited = self.repository.parent / name
                inherited.write_text("inherited bootstrap", encoding="utf-8")
                self.assert_rejected("Forbidden inherited .NET bootstrap entry")
                inherited.unlink()

    def test_root_policies_shadow_irrelevant_parent_policy_names(self) -> None:
        parent = self.repository.parent
        shadowed = {
            "Directory.Build.props": "<Project><Invalid /></Project>",
            "global.json": "not-json",
            "NuGet.Config": "<invalid />",
            "dotnet-tools.json": "not-json",
        }
        for name, content in shadowed.items():
            (parent / name).write_text(content, encoding="utf-8")

        validator.validate_repository(self.repository)

    def test_next_preflight_rejects_temporally_created_parent_bootstrap(self) -> None:
        validator.validate_repository(self.repository)
        inherited = self.repository.parent / "Directory.Build.targets"
        inherited.write_text("created after first pass", encoding="utf-8")

        self.assert_rejected("Forbidden inherited .NET bootstrap entry")

    def test_rejects_inherited_bootstrap_symlink_if_supported(self) -> None:
        outside = self.repository.parent / "outside-inherited.txt"
        outside.write_text("outside", encoding="utf-8")
        inherited = self.repository.parent / "Directory.Packages.props"
        try:
            inherited.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("Forbidden inherited .NET bootstrap entry")

    def test_rejects_unreadable_ancestor_enumeration_fail_closed(self) -> None:
        repository_parent = self.repository.parent
        original_scandir = os.scandir

        def fail_for_parent(path: object):
            if Path(path) == repository_parent:
                raise PermissionError("simulated unreadable ancestor")
            return original_scandir(path)

        with mock.patch.object(validator.os, "scandir", fail_for_parent):
            self.assert_rejected("Cannot enumerate repository ancestor")

    def test_rejects_malformed_global_json(self) -> None:
        self.write_raw_global_json('{"sdk":')

        self.assert_rejected("not valid strict JSON")

    def test_rejects_non_utf8_global_json(self) -> None:
        (self.repository / "global.json").write_bytes(b"\xff\xfe\x00")

        self.assert_rejected("strict UTF-8")

    def test_rejects_duplicate_keys_at_every_policy_level(self) -> None:
        documents = (
            '{"sdk":{"version":"10.0.300","rollForward":"latestFeature",'
            '"allowPrerelease":false},"sdk":{}}',
            '{"sdk":{"version":"10.0.300","version":"10.0.300",'
            '"rollForward":"latestFeature","allowPrerelease":false}}',
        )
        for document in documents:
            with self.subTest(document=document):
                self.write_raw_global_json(document)
                self.assert_rejected("Duplicate JSON key")

    def test_rejects_every_non_finite_json_constant(self) -> None:
        for constant in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(constant=constant):
                self.write_raw_global_json(
                    '{"sdk":{"version":"10.0.300","rollForward":"latestFeature",'
                    f'"allowPrerelease":false}},"unexpected":{constant}}}'
                )
                self.assert_rejected("Non-finite JSON constant")

    def test_rejects_non_object_policy_levels(self) -> None:
        for document in ([], {"sdk": []}):
            with self.subTest(document=document):
                self.write_global_json(document)
                self.assert_rejected("must be a JSON object")

    def test_rejects_unknown_or_missing_root_keys(self) -> None:
        for document in ({}, {**VALID_DOCUMENT, "msbuild-sdks": {}}):
            with self.subTest(document=document):
                self.write_global_json(document)
                self.assert_rejected("root keys do not match policy")

    def test_rejects_unknown_or_missing_sdk_keys(self) -> None:
        variants = []
        for key in VALID_DOCUMENT["sdk"]:
            sdk = dict(VALID_DOCUMENT["sdk"])
            del sdk[key]
            variants.append({"sdk": sdk})
        variants.extend(
            (
                {"sdk": {**VALID_DOCUMENT["sdk"], "paths": [".dotnet"]}},
                {"sdk": {**VALID_DOCUMENT["sdk"], "unknown": True}},
            )
        )

        for document in variants:
            with self.subTest(document=document):
                self.write_global_json(document)
                self.assert_rejected("sdk keys do not match policy")

    def test_rejects_every_sdk_value_or_type_drift(self) -> None:
        replacements = {
            "version": ("10.0.301", 10),
            "rollForward": ("latestMajor", False),
            "allowPrerelease": (True, 0, "false"),
        }
        for key, values in replacements.items():
            for value in values:
                with self.subTest(key=key, value=value):
                    sdk = dict(VALID_DOCUMENT["sdk"])
                    sdk[key] = value
                    self.write_global_json({"sdk": sdk})
                    self.assert_rejected(rf"sdk\.{key} must be exactly")

    def test_rejects_case_variant_root_global_json_file(self) -> None:
        original = self.repository / "global.json"
        case_variant = self.repository / "GLOBAL.JSON"
        try:
            original.rename(case_variant)
        except OSError:
            self.skipTest("Filesystem cannot create a distinct case variant.")

        self.assert_rejected("exact name 'global.json'")

    def test_rejects_multiple_case_variant_root_global_json_files_if_supported(self) -> None:
        original = self.repository / "global.json"
        case_variant = self.repository / "GLOBAL.JSON"
        case_variant.write_text(json.dumps(VALID_DOCUMENT), encoding="utf-8")
        variants = [
            entry
            for entry in self.repository.iterdir()
            if entry.name.casefold() == "global.json"
        ]
        if len(variants) != 2:
            self.skipTest("Filesystem cannot hold two case-variant names.")

        self.assert_rejected("exactly one case-insensitive global.json")

    def test_rejects_global_json_directory(self) -> None:
        (self.repository / "global.json").unlink()
        (self.repository / "global.json").mkdir()

        self.assert_rejected("must be a JSON object|Cannot read global.json|non-regular")

    def test_rejects_forbidden_files_nested_and_case_insensitively(self) -> None:
        names = (
            "Directory.Build.rsp",
            "directory.build.RSP",
            "MSBuild.rsp",
            "msbuild.RSP",
            "Directory.Solution.props",
            "directory.solution.PROPS",
            "Directory.Solution.targets",
            "DIRECTORY.SOLUTION.TARGETS",
        )
        for index, name in enumerate(names):
            with self.subTest(name=name):
                directory = self.repository / f"nested-{index}" / "deeper"
                directory.mkdir(parents=True)
                forbidden = directory / name
                forbidden.write_text("bootstrap override", encoding="utf-8")
                self.assert_rejected("Forbidden .NET bootstrap file")
                forbidden.unlink()

    def test_rejects_forbidden_directory_name_too(self) -> None:
        (self.repository / "nested" / "MsBuIlD.RsP").mkdir(parents=True)

        self.assert_rejected("Forbidden .NET bootstrap file")

    def test_rejects_parent_directory_traversal_in_root_argument(self) -> None:
        traversing_root = self.repository / "child" / ".."

        with self.assertRaisesRegex(
            validator.BootstrapIntegrityError, "parent-directory traversal"
        ):
            validator.validate_repository(traversing_root)

    def test_rejects_unreadable_global_json_fail_closed(self) -> None:
        original_read_text = Path.read_text

        def fail_for_global_json(path: Path, *args: object, **kwargs: object) -> str:
            if path.name == "global.json":
                raise PermissionError("simulated unreadable policy")
            return original_read_text(path, *args, **kwargs)

        with mock.patch.object(Path, "read_text", fail_for_global_json):
            self.assert_rejected("Cannot read global.json")

    def test_rejects_unreadable_root_directory_build_props_fail_closed(self) -> None:
        original_read_bytes = Path.read_bytes

        def fail_for_build_props(path: Path, *args: object, **kwargs: object) -> bytes:
            if path.name == "Directory.Build.props":
                raise PermissionError("simulated unreadable MSBuild policy")
            return original_read_bytes(path, *args, **kwargs)

        with mock.patch.object(Path, "read_bytes", fail_for_build_props):
            self.assert_rejected("Cannot read root Directory.Build.props")

    def test_rejects_unreadable_directory_fail_closed(self) -> None:
        nested = self.repository / "nested"
        nested.mkdir()
        original_scandir = os.scandir

        def fail_for_nested(path: object):
            if Path(path) == nested:
                raise PermissionError("simulated unreadable directory")
            return original_scandir(path)

        with mock.patch.object(validator.os, "scandir", fail_for_nested):
            self.assert_rejected("Cannot enumerate repository path")

    def test_rejects_file_symlink_if_supported(self) -> None:
        outside = Path(self.temporary_directory.name) / "outside.txt"
        outside.write_text("outside", encoding="utf-8")
        link = self.repository / "ordinary.txt"
        try:
            link.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("Symlink or reparse point")

    def test_rejects_root_directory_build_props_symlink_if_supported(self) -> None:
        policy = self.repository / "Directory.Build.props"
        outside = Path(self.temporary_directory.name) / "outside.props"
        outside.write_bytes(DIRECTORY_BUILD_PROPS_BYTES)
        policy.unlink()
        try:
            policy.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("File symlinks are unavailable.")

        self.assert_rejected("Symlink or reparse point")

    def test_isolated_python_invocations_resist_repo_module_shadowing(self) -> None:
        sandbox = Path(self.temporary_directory.name) / "shadow-probe"
        tools = sandbox / "tools"
        tests = sandbox / "tests"
        tools.mkdir(parents=True)
        tests.mkdir()
        shutil.copyfile(
            REPOSITORY_ROOT / "tools" / "validate_dotnet_bootstrap_integrity.py",
            tools / "validate_dotnet_bootstrap_integrity.py",
        )
        (sandbox / "global.json").write_text(
            json.dumps(VALID_DOCUMENT), encoding="utf-8"
        )
        (sandbox / "Directory.Build.props").write_bytes(DIRECTORY_BUILD_PROPS_BYTES)
        (sandbox / "NuGet.Config").write_bytes(NUGET_CONFIG_BYTES)
        (sandbox / "dotnet-tools.json").write_bytes(DOTNET_TOOLS_BYTES)
        poison = "raise RuntimeError('repository module shadow executed')\n"
        (tools / "json.py").write_text(poison, encoding="utf-8")
        (sandbox / "unittest.py").write_text(poison, encoding="utf-8")
        (tests / "test_probe.py").write_text(
            "import unittest\n\n"
            "class Probe(unittest.TestCase):\n"
            "    def test_stdlib_unittest_loaded(self):\n"
            "        self.assertIn('unittest', unittest.__name__)\n",
            encoding="utf-8",
        )

        unsafe_validator = subprocess.run(
            [sys.executable, "tools/validate_dotnet_bootstrap_integrity.py"],
            cwd=sandbox,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(unsafe_validator.returncode, 0)
        safe_validator = subprocess.run(
            [
                sys.executable,
                "-I",
                "-S",
                "-B",
                "tools/validate_dotnet_bootstrap_integrity.py",
            ],
            cwd=sandbox,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(safe_validator.returncode, 0, safe_validator.stderr)

        unsafe_unittest = subprocess.run(
            [sys.executable, "-m", "unittest", "discover", "-s", "tests"],
            cwd=sandbox,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(unsafe_unittest.returncode, 0)
        safe_unittest = subprocess.run(
            [
                sys.executable,
                "-I",
                "-S",
                "-B",
                "-m",
                "unittest",
                "discover",
                "-s",
                "tests",
                "-p",
                "test_*.py",
                "-v",
            ],
            cwd=sandbox,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(safe_unittest.returncode, 0, safe_unittest.stderr)

    def test_rejects_symlinked_directory_traversal_if_supported(self) -> None:
        outside = Path(self.temporary_directory.name) / "outside"
        outside.mkdir()
        (outside / "DIRECTORY.BUILD.RSP").write_text("outside", encoding="utf-8")
        link = self.repository / "linked-directory"
        try:
            link.symlink_to(outside, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Directory symlinks are unavailable.")

        self.assert_rejected("Symlink or reparse point")

    def test_rejects_symlinked_repository_root_if_supported(self) -> None:
        link = Path(self.temporary_directory.name) / "repository-link"
        try:
            link.symlink_to(self.repository, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Directory symlinks are unavailable.")

        with self.assertRaisesRegex(
            validator.BootstrapIntegrityError, "root must not be a symlink"
        ):
            validator.validate_repository(link)

    def test_rejects_repository_root_below_symlinked_parent_if_supported(self) -> None:
        real_parent = Path(self.temporary_directory.name) / "real-parent"
        nested_repository = real_parent / "nested-repository"
        nested_repository.mkdir(parents=True)
        (nested_repository / "global.json").write_text(
            json.dumps(VALID_DOCUMENT), encoding="utf-8"
        )
        linked_parent = Path(self.temporary_directory.name) / "linked-parent"
        try:
            linked_parent.symlink_to(real_parent, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Directory symlinks are unavailable.")

        with self.assertRaisesRegex(
            validator.BootstrapIntegrityError, "must not traverse a symlink"
        ):
            validator.validate_repository(linked_parent / "nested-repository")


class WorkflowOrderingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8"
        )

    def job_body(self, job_name: str) -> str:
        match = re.search(
            rf"(?ms)^  {re.escape(job_name)}:\n(.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
            self.workflow,
        )
        self.assertIsNotNone(match, f"Missing workflow job: {job_name}")
        return match.group(1)

    def step_body(self, job_body: str, step_name: str) -> str:
        match = re.search(
            rf"(?ms)^      - name: {re.escape(step_name)}\n"
            r"(.*?)(?=^      - (?:name:|uses:|run:)|\Z)",
            job_body,
        )
        self.assertIsNotNone(match, f"Missing workflow step: {step_name}")
        return match.group(1)

    def run_commands(self, step_body: str) -> list[str]:
        lines = step_body.splitlines()
        self.assertIn("        run: |", lines, "Expected a literal run block.")
        run_index = lines.index("        run: |")
        commands: list[str] = []
        for line in lines[run_index + 1 :]:
            if not line.strip():
                continue
            self.assertTrue(
                line.startswith("          "),
                f"Unexpected content outside the run block: {line!r}",
            )
            commands.append(line.strip())
        return commands

    def test_backend_and_relational_run_preflight_immediately_after_checkout(self) -> None:
        checkout_step = "      - uses: actions/checkout@v7"
        preflight_step = "      - name: .NET bootstrap integrity preflight"
        setup_step = "      - name: Setup .NET 10"
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        tests_command = (
            "python3 -I -S -B -m unittest discover "
            "-s tests/dotnet_bootstrap_integrity "
            '-p "test_*.py" -v'
        )

        for job_name in ("backend", "relational"):
            with self.subTest(job=job_name):
                body = self.job_body(job_name)
                self.assertEqual(body.count(checkout_step), 1)
                self.assertEqual(body.count(preflight_step), 1)
                self.assertEqual(body.count(setup_step), 1)
                self.assertRegex(
                    body,
                    re.escape(checkout_step)
                    + r"\n\n"
                    + re.escape(preflight_step),
                )
                preflight_index = body.index(preflight_step)
                setup_index = body.index(setup_step)
                before_setup = body[:setup_index]
                self.assertLess(preflight_index, setup_index)
                self.assertEqual(before_setup.count(validator_command), 1)
                self.assertEqual(before_setup.count(tests_command), 1)
                self.assertLess(
                    before_setup.index(validator_command),
                    before_setup.index(tests_command),
                )
                self.assertEqual(before_setup.count("python3 "), 2)

    def test_supported_msbuild_commands_disable_auto_response_files(self) -> None:
        backend = self.job_body("backend")
        relational = self.job_body("relational")
        self.assertIn(
            "dotnet restore GoymenERP.slnx -noAutoResponse",
            self.step_body(backend, "Restore tools and packages"),
        )
        self.assertIn(
            "dotnet build GoymenERP.slnx -noAutoResponse",
            self.step_body(backend, "Build"),
        )
        for step_name in ("Test", "Architecture dependency guard"):
            with self.subTest(step=step_name):
                step = self.step_body(backend, step_name)
                self.assertIn("dotnet test ", step)
                self.assertIn("-noAutoResponse", step)

        relational_test = self.step_body(relational, "Run relational acceptance test")
        self.assertIn("dotnet test ", relational_test)
        self.assertIn("-noAutoResponse", relational_test)
        relational_restore = self.step_body(relational, "Restore relational test packages")
        self.assertIn("dotnet restore ", relational_restore)
        self.assertIn("-noAutoResponse", relational_restore)

    def test_all_package_restores_use_locked_root_inputs(self) -> None:
        backend = self.job_body("backend")
        relational = self.job_body("relational")
        restore_step = self.step_body(backend, "Restore tools and packages")
        self.assertIn(
            "dotnet tool restore --tool-manifest dotnet-tools.json "
            "--configfile NuGet.Config",
            restore_step,
        )
        self.assertIn(
            "dotnet restore GoymenERP.slnx -noAutoResponse "
            "--configfile NuGet.Config",
            restore_step,
        )

        relational_restore = self.step_body(relational, "Restore relational test packages")
        self.assertIn("--configfile NuGet.Config", relational_restore)
        relational_test = self.step_body(relational, "Run relational acceptance test")
        self.assertIn("--no-restore", relational_test)
        vulnerable_step = self.step_body(backend, "Check vulnerable dependencies")
        self.assertIn("--no-restore", vulnerable_step)
        self.assertIn("--configfile NuGet.Config", vulnerable_step)

    def test_tool_restore_is_immediately_preflighted_with_locked_inputs(self) -> None:
        backend = self.job_body("backend")
        restore_step = self.step_body(backend, "Restore tools and packages")
        commands = self.run_commands(restore_step)
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        tool_restore_command = (
            "dotnet tool restore --tool-manifest dotnet-tools.json "
            "--configfile NuGet.Config"
        )

        self.assertEqual(commands.count(tool_restore_command), 1)
        tool_restore_index = commands.index(tool_restore_command)
        self.assertGreater(tool_restore_index, 0)
        self.assertEqual(commands[tool_restore_index - 1], validator_command)

    def test_unsupported_dotnet_boundaries_repeat_preflight_fail_closed(self) -> None:
        backend = self.job_body("backend")
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        vulnerable_step = self.step_body(backend, "Check vulnerable dependencies")
        self.assertLess(
            vulnerable_step.index(validator_command),
            vulnerable_step.index("dotnet list "),
        )

        migration_steps = (
            "Generate idempotent SQL migration script",
            "Generate identity idempotent SQL migration script",
            "Generate audit idempotent SQL migration script",
            "Generate idempotency idempotent SQL migration script",
        )
        for step_name in migration_steps:
            with self.subTest(step=step_name):
                step = self.step_body(backend, step_name)
                self.assertEqual(step.count(validator_command), 1)
                self.assertEqual(step.count("dotnet tool run dotnet-ef"), 1)
                self.assertLess(
                    step.index(validator_command),
                    step.index("dotnet tool run dotnet-ef"),
                )
                self.assertIn("--no-build", step)
                self.assertIn("--configuration Release", step)

    def test_host_msbuild_boundaries_revalidate_after_prior_repo_execution(self) -> None:
        backend = self.job_body("backend")
        relational = self.job_body("relational")
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        backend_boundaries = {
            "Build": "dotnet build GoymenERP.slnx",
            "Test": "dotnet test GoymenERP.slnx",
            "Architecture dependency guard": (
                "dotnet test tests/GoymenERP.ArchitectureTests/"
                "GoymenERP.ArchitectureTests.csproj"
            ),
            "Check vulnerable dependencies": "dotnet list GoymenERP.slnx",
        }
        for step_name, command in backend_boundaries.items():
            with self.subTest(step=step_name):
                step = self.step_body(backend, step_name)
                self.assertEqual(step.count(validator_command), 1)
                self.assertLess(step.index(validator_command), step.index(command))

        restore_commands = self.run_commands(
            self.step_body(backend, "Restore tools and packages")
        )
        restore_boundaries = (
            "dotnet tool restore --tool-manifest dotnet-tools.json "
            "--configfile NuGet.Config",
            "dotnet restore GoymenERP.slnx -noAutoResponse "
            "--configfile NuGet.Config",
        )
        self.assertEqual(restore_commands.count(validator_command), 2)
        for command in restore_boundaries:
            with self.subTest(step="Restore tools and packages", command=command):
                self.assertEqual(restore_commands.count(command), 1)
                command_index = restore_commands.index(command)
                self.assertGreater(command_index, 0)
                self.assertEqual(
                    restore_commands[command_index - 1], validator_command
                )

        relational_step = self.step_body(relational, "Run relational acceptance test")
        relational_restore_step = self.step_body(
            relational, "Restore relational test packages"
        )
        self.assertEqual(relational_restore_step.count(validator_command), 1)
        self.assertLess(
            relational_restore_step.index(validator_command),
            relational_restore_step.index("dotnet restore "),
        )
        relational_command = (
            "dotnet test tests/GoymenERP.Api.IntegrationTests/"
            "GoymenERP.Api.IntegrationTests.csproj"
        )
        self.assertEqual(relational_step.count(validator_command), 1)
        self.assertLess(
            relational_step.index(validator_command),
            relational_step.index(relational_command),
        )

    def test_container_runner_preflights_its_checkout_before_repo_execution(self) -> None:
        body = self.job_body("containers")
        needs_block = re.search(r"(?ms)^    needs:\n(.*?)(?=^    [A-Za-z0-9_-]+:)", body)
        self.assertIsNotNone(needs_block, "Container job must have an explicit needs gate.")
        dependencies = set(re.findall(r"^      - ([A-Za-z0-9_-]+)$", needs_block.group(1), re.M))
        self.assertEqual(dependencies, {"backend", "frontend", "relational"})

        checkout_step = "      - uses: actions/checkout@v7"
        preflight_step = (
            "      - name: Container runner .NET bootstrap integrity preflight"
        )
        compose_step = "      - name: Validate compose model"
        build_step = "      - name: Build application images"
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )

        self.assertEqual(body.count(checkout_step), 1)
        self.assertEqual(body.count(preflight_step), 1)
        self.assertRegex(
            body,
            re.escape(checkout_step) + r"\n\n" + re.escape(preflight_step),
        )
        self.assertLess(body.index(preflight_step), body.index(compose_step))
        self.assertLess(body.index(compose_step), body.index(build_step))
        self.assertEqual(
            self.run_commands(
                self.step_body(
                    body, "Container runner .NET bootstrap integrity preflight"
                )
            ),
            [validator_command],
        )

    def test_backend_container_disables_auto_response_for_build_commands(self) -> None:
        dockerfile = (
            REPOSITORY_ROOT / "src" / "backend" / "GoymenERP.Api" / "Dockerfile"
        ).read_text(encoding="utf-8")
        logical_lines = re.sub(r"\\\r?\n\s*", " ", dockerfile).splitlines()
        build_commands = [
            " ".join(line.split())
            for line in logical_lines
            if re.search(r"^RUN dotnet (?:restore|publish)\b", line.strip())
        ]
        self.assertEqual(len(build_commands), 2)
        self.assertEqual(
            build_commands[0],
            "RUN dotnet restore GoymenERP.slnx -noAutoResponse "
            "--configfile NuGet.Config",
        )
        self.assertRegex(
            build_commands[1],
            r"^RUN dotnet publish "
            r"src/backend/GoymenERP\.Api/GoymenERP\.Api\.csproj "
            r"-noAutoResponse ",
        )
        for command in build_commands:
            self.assertEqual(command.count("-noAutoResponse"), 1)


if __name__ == "__main__":
    unittest.main()
