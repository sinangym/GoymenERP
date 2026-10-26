from __future__ import annotations

import hashlib
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
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
  </ItemGroup>
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
    RELATIONAL_EXECUTION_JOBS = ("relational-shard", "relational-rcsi")

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
        safe_trx_summary_tests_command = (
            "python3 -I -S -B -m unittest discover "
            "-s tests/ci_safe_trx_summary "
            '-p "test_*.py" -v'
        )

        for job_name in ("backend-build", "backend-test-group", *self.RELATIONAL_EXECUTION_JOBS):
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
                self.assertEqual(before_setup.count(safe_trx_summary_tests_command), 1)
                self.assertLess(
                    before_setup.index(validator_command),
                    before_setup.index(tests_command),
                )
                self.assertLess(
                    before_setup.index(tests_command),
                    before_setup.index(safe_trx_summary_tests_command),
                )
                verifier_suite = (
                    "python3 -I -S -B -m unittest discover "
                    '-s tests/backend_test_group_verifier -p "test_*.py" -v'
                )
                if job_name == "backend-test-group":
                    self.assertEqual(before_setup.count(verifier_suite), 1)
                    self.assertEqual(before_setup.count("python3 "), 4)
                else:
                    self.assertNotIn(verifier_suite, before_setup)
                    self.assertEqual(before_setup.count("python3 "), 3)

    def test_supported_msbuild_commands_disable_auto_response_files(self) -> None:
        backend = self.job_body("backend-build")
        self.assertIn(
            "dotnet restore GoymenERP.slnx -noAutoResponse",
            self.step_body(backend, "Restore tools and packages"),
        )
        self.assertIn(
            "dotnet build GoymenERP.slnx -noAutoResponse",
            self.step_body(backend, "Build"),
        )
        for step_name in ("Architecture dependency guard",):
            with self.subTest(step=step_name):
                step = self.step_body(backend, step_name)
                self.assertIn("dotnet test ", step)
                self.assertIn("-noAutoResponse", step)
        worker = self.job_body("backend-test-group")
        self.assertIn(
            "dotnet restore GoymenERP.slnx -noAutoResponse --configfile NuGet.Config",
            self.step_body(worker, "Restore locked solution packages"),
        )
        verifier_source = (REPOSITORY_ROOT / "tools" / "verify_backend_test_groups.py").read_text(
            encoding="utf-8"
        )
        self.assertIn('"-noAutoResponse"', verifier_source)

        verifier_source = (REPOSITORY_ROOT / "tools" / "verify_rcsi_relational_acceptance.py").read_text(
            encoding="utf-8"
        )
        self.assertEqual(verifier_source.count('"-noAutoResponse"'), 2)
        for job_name in self.RELATIONAL_EXECUTION_JOBS:
            relational_restore = self.step_body(
                self.job_body(job_name), "Restore relational test packages"
            )
            self.assertIn("dotnet restore ", relational_restore)
            self.assertIn("-noAutoResponse", relational_restore)

    def test_all_package_restores_use_locked_root_inputs(self) -> None:
        backend = self.job_body("backend-build")
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

        for job_name in self.RELATIONAL_EXECUTION_JOBS:
            relational_restore = self.step_body(
                self.job_body(job_name), "Restore relational test packages"
            )
            self.assertIn("--configfile NuGet.Config", relational_restore)
        verifier_source = (REPOSITORY_ROOT / "tools" / "verify_rcsi_relational_acceptance.py").read_text(
            encoding="utf-8"
        )
        self.assertEqual(verifier_source.count('"--no-restore"'), 2)
        vulnerable_step = self.step_body(backend, "Check vulnerable dependencies")
        self.assertIn("--no-restore", vulnerable_step)
        self.assertIn("--configfile NuGet.Config", vulnerable_step)

    def test_tool_restore_runs_immediately_after_fresh_preflight(self) -> None:
        backend = self.job_body("backend-build")
        restore_step = self.step_body(backend, "Restore tools and packages")
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        tool_restore_command = (
            "dotnet tool restore --tool-manifest dotnet-tools.json "
            "--configfile NuGet.Config"
        )
        solution_restore_command = (
            "dotnet restore GoymenERP.slnx -noAutoResponse "
            "--configfile NuGet.Config"
        )

        active_lines = self.read_active_step_lines(restore_step)
        self.assertEqual(
            active_lines,
            [
                validator_command,
                tool_restore_command,
                validator_command,
                solution_restore_command,
            ],
        )

    def test_unsupported_dotnet_boundaries_repeat_preflight_fail_closed(self) -> None:
        backend = self.job_body("backend-build")
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
        backend = self.job_body("backend-build")
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        backend_boundaries = {
            "Restore tools and packages": "dotnet restore GoymenERP.slnx",
            "Build": "dotnet build GoymenERP.slnx",
            "Architecture dependency guard": (
                "dotnet test tests/GoymenERP.ArchitectureTests/"
                "GoymenERP.ArchitectureTests.csproj"
            ),
            "Check vulnerable dependencies": "dotnet list GoymenERP.slnx",
        }
        for step_name, command in backend_boundaries.items():
            with self.subTest(step=step_name):
                step = self.step_body(backend, step_name)
                expected_preflight_count = (
                    2 if step_name == "Restore tools and packages" else 1
                )
                self.assertEqual(
                    step.count(validator_command),
                    expected_preflight_count,
                )
                self.assertLess(step.index(validator_command), step.index(command))
        worker = self.job_body("backend-test-group")
        for step_name, command in (
            ("Restore locked solution packages", "dotnet restore GoymenERP.slnx"),
            ("Run exact backend test group", "python3 -I -S -B tools/verify_backend_test_groups.py"),
        ):
            step = self.step_body(worker, step_name)
            self.assertEqual(step.count(validator_command), 1)
            self.assertLess(step.index(validator_command), step.index(command))

        for job_name, step_name, mode in (
            ("relational-shard", "Run exact SQL Server relational shard", "--shard"),
            ("relational-rcsi", "Run exact RCSI relational acceptance verifier", "--rcsi"),
        ):
            with self.subTest(job=job_name, step=step_name):
                relational = self.job_body(job_name)
                relational_restore_step = self.step_body(
                    relational, "Restore relational test packages"
                )
                self.assertEqual(relational_restore_step.count(validator_command), 1)
                self.assertLess(
                    relational_restore_step.index(validator_command),
                    relational_restore_step.index("dotnet restore "),
                )
                relational_step = self.step_body(relational, step_name)
                self.assertEqual(relational_step.count(validator_command), 1)
                self.assertLess(
                    relational_step.index(validator_command),
                    relational_step.index(
                        "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\\n"
                        f"          {mode}"
                    ),
                )

        verifier_source = (REPOSITORY_ROOT / "tools" / "verify_rcsi_relational_acceptance.py").read_text(
            encoding="utf-8"
        )
        self.assertEqual(verifier_source.count("run_bootstrap_preflight(repository_root)"), 2)

    def test_container_surface_depends_on_every_required_gate(self) -> None:
        body = self.job_body("containers")
        needs_block = re.search(r"(?ms)^    needs:\n(.*?)(?=^    [A-Za-z0-9_-]+:)", body)
        self.assertIsNotNone(needs_block, "Container job must have an explicit needs gate.")
        dependencies = set(re.findall(r"^      - ([A-Za-z0-9_-]+)$", needs_block.group(1), re.M))
        self.assertTrue(
            {"backend", "frontend", "relational", "contract-drift"}.issubset(
                dependencies
            )
        )

    def test_required_upstream_jobs_cannot_be_skipped_or_soft_failed(self) -> None:
        expected_job_keys = {
            "backend-build": ["    name:", "    runs-on:", "    steps:"],
            "backend-test-group": ["    name:", "    runs-on:", "    strategy:", "    steps:"],
            "backend": ["    name:", "    needs:", "    if:", "    runs-on:", "    steps:"],
            "frontend": [
                "    name:",
                "    runs-on:",
                "    defaults:",
                "    steps:",
            ],
            "relational": [
                "    name:",
                "    needs:",
                "    if:",
                "    runs-on:",
                "    steps:",
            ],
            "relational-shard": [
                "    name:",
                "    runs-on:",
                "    strategy:",
                "    env:",
                "    services:",
                "    steps:",
            ],
            "relational-rcsi": [
                "    name:",
                "    runs-on:",
                "    env:",
                "    services:",
                "    steps:",
            ],
            "contract-drift": ["    name:", "    runs-on:", "    steps:"],
        }
        for job_name, expected_keys in expected_job_keys.items():
            with self.subTest(job=job_name):
                body = self.job_body(job_name)
                keys = [
                    re.sub(r"^    ([^:]+):.*$", r"    \1:", line)
                    for line in re.findall(r"^    \S.*$", body, re.M)
                ]
                self.assertEqual(keys, expected_keys)

        frontend_defaults = re.search(
            r"(?ms)^    defaults:\n(.*?)(?=^    \S)",
            self.job_body("frontend"),
        )
        self.assertIsNotNone(frontend_defaults)
        self.assertEqual(
            frontend_defaults.group(1),
            "      run:\n"
            "        working-directory: src/frontend/goymen-erp-web\n",
        )

        for job_name in self.RELATIONAL_EXECUTION_JOBS:
            relational_env = re.search(
                r"(?ms)^    env:\n(.*?)(?=^    \S)",
                self.job_body(job_name),
            )
            self.assertIsNotNone(relational_env)
            self.assertEqual(
                relational_env.group(1),
                "      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n"
                "        Server=localhost,1433;Database=master;User Id=sa;"
                "Password=Ci_Relational_Only_2026;Encrypt=True;"
                "TrustServerCertificate=True\n",
            )
            self.assertIn(
                "    services:\n      sqlserver:\n"
                "        image: mcr.microsoft.com/mssql/server:2022-latest\n",
                self.job_body(job_name),
            )
        self.assertEqual(
            re.search(
                r"(?ms)^    strategy:\n(.*?)(?=^    \S)",
                self.job_body("relational-shard"),
            ).group(1),
            "      fail-fast: false\n"
            "      matrix:\n"
            "        shard: [A, B, C]\n",
        )

    def test_required_upstream_jobs_match_reviewed_exact_contracts(self) -> None:
        # #789/#794 replaced cloud transfer with local, run/attempt/head-bound
        # evidence. Keep exact job pins as well as the semantic/mutation guards.
        expected_job_fingerprints = {
            "backend-build": "f94065385869782118b7a0085581028daf7bd3289a0b1842d3b76db563173029",
            "backend-test-group": "4138b2c1aa82cb51ccbaf6cbe3a42a54416a93b61b598ca776a0a9207272be58",
            "backend": (
                "e2e37178376493f6a14224f36a3682680223ee9ce509742a7ef287e80bda78d3"
            ),
            "frontend": (
                "202453e576ee28ee342819117d1533b9b0aa3aacd7348bc893cb21672fa789df"
            ),
            "relational-shard": (
                "f8414c3df38ceeddf840369521891220587c981491d11ad11df60447dd77fa7b"
            ),
            "relational-rcsi": (
                "4b0f81dd06932cdce3ce52a87798361eb7004a38270f51438497952d3f78caea"
            ),
            "relational": (
                "0e13b8a66d7f02d8e5ea545d028ea37daffe5a5d5d31bbc4be6b4ff67dd47412"
            ),
            "contract-drift": (
                "067e1d19ce60a40f11e34e4dba7bae591e33c3ef06d271a8a557ca9226fffcea"
            ),
        }

        for job_name, expected_fingerprint in expected_job_fingerprints.items():
            with self.subTest(job=job_name):
                normalized_body = "\n".join(
                    line.rstrip() for line in self.job_body(job_name).splitlines()
                ).rstrip("\n") + "\n"
                actual_fingerprint = hashlib.sha256(
                    normalized_body.encode("utf-8")
                ).hexdigest()
                self.assertEqual(actual_fingerprint, expected_fingerprint)

    def test_required_upstream_preflights_and_frontend_commands_are_exact(self) -> None:
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        bootstrap_suite_command = (
            "python3 -I -S -B -m unittest discover "
            '-s tests/dotnet_bootstrap_integrity -p "test_*.py" -v'
        )
        safe_trx_summary_suite_command = (
            "python3 -I -S -B -m unittest discover "
            '-s tests/ci_safe_trx_summary -p "test_*.py" -v'
        )

        for job_name in ("backend-build", "backend-test-group", *self.RELATIONAL_EXECUTION_JOBS):
            with self.subTest(job=job_name):
                preflight = self.step_body(
                    self.job_body(job_name), ".NET bootstrap integrity preflight"
                )
                expected_lines = [
                    "run: |",
                    validator_command,
                    bootstrap_suite_command,
                    safe_trx_summary_suite_command,
                ]
                if job_name == "backend-test-group":
                    expected_lines.append(
                        "python3 -I -S -B -m unittest discover "
                        '-s tests/backend_test_group_verifier -p "test_*.py" -v'
                    )
                self.assertEqual(self.read_step_lines(preflight), expected_lines)

        contract_preflight = self.step_body(
            self.job_body("contract-drift"),
            ".NET bootstrap and drift validator preflight",
        )
        self.assertEqual(
            self.read_step_lines(contract_preflight),
            [
                "run: |",
                validator_command,
                bootstrap_suite_command,
                "python3 -I -S -B -m unittest discover "
                '-s tests/openapi_contract_drift -p "test_*.py" -v',
            ],
        )

        frontend_run_steps = re.findall(
            r"^      - run: (.+)$", self.job_body("frontend"), re.M
        )
        self.assertEqual(
            frontend_run_steps,
            [
                "npm ci",
                "npm audit --audit-level=high",
                "npm run lint",
                "npm test -- --maxWorkers=1 --no-file-parallelism",
                "npm run build",
            ],
        )

    def assert_backend_execution_and_aggregate_boundaries(self) -> None:
        head_ref = "${{ github.event.pull_request.head.sha || github.sha }}"
        group_ref = "${{ matrix.group }}"
        build = self.job_body("backend-build")
        worker = self.job_body("backend-test-group")
        aggregate = self.job_body("backend")
        self.assertEqual(
            re.findall(r"^    \S.*$", build, re.M),
            ["    name: Backend full build, vulnerability and migrations",
             "    runs-on: ubuntu-latest", "    steps:"],
        )
        self.assertIn("dotnet build GoymenERP.slnx -noAutoResponse", build)
        self.assertIn(
            "dotnet restore GoymenERP.slnx -noAutoResponse --configfile NuGet.Config",
            self.step_body(build, "Restore tools and packages"),
        )
        for step_name in (
            "Architecture dependency guard",
            "Check vulnerable dependencies",
            "Generate idempotent SQL migration script",
            "Generate identity idempotent SQL migration script",
            "Generate audit idempotent SQL migration script",
            "Generate idempotency idempotent SQL migration script",
        ):
            self.step_body(build, step_name)
        self.assertNotIn("dotnet test GoymenERP.slnx", build)
        self.assertEqual(
            re.findall(r"^    \S.*$", worker, re.M),
            ["    name: Backend test group (${{ matrix.group }})",
             "    runs-on: ubuntu-latest", "    strategy:", "    steps:"],
        )
        self.assertIn(
            "    strategy:\n      fail-fast: false\n      matrix:\n"
            "        group: [architecture, api, modules]\n",
            worker,
        )
        self.assertNotIn("continue-on-error:", worker)
        self.assertNotIn("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", worker)
        self.assertEqual(
            self.read_step_lines(
                self.step_body(worker, "Restore locked solution packages")
            ),
            [
                "run: |",
                "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
                "dotnet restore GoymenERP.slnx -noAutoResponse --configfile NuGet.Config",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(worker, "Run exact backend test group")
            ),
            [
                "run: |",
                "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
                "python3 -I -S -B tools/verify_backend_test_groups.py \\",
                f'--group "{group_ref}" \\',
                f'--results-directory "artifacts/ci-test-results/backend-groups/{group_ref}" \\',
                f'--proof-output "artifacts/ci-backend-group-proofs/proof-{group_ref}.json" \\',
                f'--source-head "{head_ref}" \\',
                '--tested-commit "$(git rev-parse HEAD)"',
            ],
        )
        self.assertEqual(
            self.read_step_lines(self.step_body(worker, "Upload exact backend group proof")),
            [
                "uses: actions/upload-artifact@v7",
                "with:",
                f"name: ci-backend-test-group-proof-{group_ref}",
                f"path: artifacts/ci-backend-group-proofs/proof-{group_ref}.json",
                "if-no-files-found: error",
                "retention-days: 7",
            ],
        )
        self.assertEqual(
            re.findall(r"^    \S.*$", aggregate, re.M),
            ["    name: Backend build, test and migration",
             "    needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]",
             "    if: ${{ always() }}",
             "    runs-on: ubuntu-latest", "    steps:"],
        )
        self.assertNotIn("continue-on-error:", aggregate)
        self.assertNotIn("dotnet ", aggregate)
        self.assertEqual(
            re.findall(r"^      - name: (.+)$", aggregate, re.M),
            [
                "Verify backend group proof parser",
                "Download exact backend group proofs",
                "Download same-run SQL proofs for excluded backend tests",
                "Verify complete backend build and test groups",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Verify backend group proof parser")
            ),
            [
                "run: |",
                "python3 -I -S -B -m unittest discover "
                '-s tests/backend_test_group_verifier -p "test_*.py" -v',
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Download exact backend group proofs")
            ),
            [
                "uses: actions/download-artifact@v8",
                "with:",
                "pattern: ci-backend-test-group-proof-*",
                "path: artifacts/ci-backend-group-proofs",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Download same-run SQL proofs for excluded backend tests")
            ),
            [
                "uses: actions/download-artifact@v8",
                "with:",
                "pattern: ci-relational-shard-proof-*",
                "path: artifacts/ci-relational-proofs",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Verify complete backend build and test groups")
            ),
            [
                "run: |",
                "python3 -I -S -B tools/verify_backend_test_groups.py \\",
                "--aggregate \\",
                "--proof-directory artifacts/ci-backend-group-proofs \\",
                f'--source-head "{head_ref}" \\',
                '--tested-commit "$(git rev-parse HEAD)" \\',
                '--build-result "${{ needs.backend-build.result }}" \\',
                '--worker-result "${{ needs.backend-test-group.result }}" \\',
                '--relational-proof-directory artifacts/ci-relational-proofs \\',
                '--relational-worker-result "${{ needs.relational-shard.result }}" \\',
                '--rcsi-result "${{ needs.relational-rcsi.result }}"',
            ],
        )

    def test_backend_execution_and_aggregate_boundaries_reject_mutants(self) -> None:
        self.assert_backend_execution_and_aggregate_boundaries()
        mutations = (
            ("missing group", "group: [architecture, api, modules]",
             "group: [architecture, api]"),
            ("duplicate group", "group: [architecture, api, modules]",
             "group: [architecture, api, modules, modules]"),
            ("conditional worker", "  backend-test-group:\n    name:",
             "  backend-test-group:\n    if: ${{ false }}\n    name:"),
            ("soft worker", "  backend-test-group:\n    name:",
             "  backend-test-group:\n    continue-on-error: true\n    name:"),
            ("missing build", "needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]",
             "needs: [backend-test-group]"),
            ("not always", "  backend:\n    name: Backend build, test and migration\n"
             "    needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]\n    if: ${{ always() }}",
             "  backend:\n    name: Backend build, test and migration\n"
             "    needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]\n    if: ${{ success() }}"),
            ("missing SQL dependency", "needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]",
             "needs: [backend-build, backend-test-group, relational-rcsi]"),
            ("missing SQL proof", "pattern: ci-relational-shard-proof-*\n          path: artifacts/ci-relational-proofs\n\n      - name: Verify complete backend",
             "pattern: ci-relational-shard-proof-A\n          path: artifacts/ci-relational-proofs\n\n      - name: Verify complete backend"),
            ("foreign SQL directory", "--relational-proof-directory artifacts/ci-relational-proofs",
             "--relational-proof-directory artifacts/other-proofs"),
            ("constant RCSI success", '--rcsi-result "${{ needs.relational-rcsi.result }}"',
             '--rcsi-result "success"'),
            ("untrusted restore", "dotnet restore GoymenERP.slnx -noAutoResponse --configfile NuGet.Config",
             "dotnet restore GoymenERP.slnx -noAutoResponse --configfile other.config"),
            ("summary substituted for proof", "pattern: ci-backend-test-group-proof-*",
             "pattern: ci-backend-test-group-summary-*"),
            ("aggregate no-op", '--worker-result "${{ needs.backend-test-group.result }}"',
             '--worker-result "${{ needs.backend-test-group.result }}" || true'),
        )
        for label, original, replacement in mutations:
            with self.subTest(mutant=label):
                changed = self.workflow.replace(original, replacement, 1)
                self.assertNotEqual(changed, self.workflow)
                with mock.patch.object(self, "workflow", changed):
                    with self.assertRaises(AssertionError):
                        self.assert_backend_execution_and_aggregate_boundaries()

    def assert_relational_execution_and_aggregate_boundaries(self) -> None:
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )
        bootstrap_suite = (
            "python3 -I -S -B -m unittest discover "
            '-s tests/dotnet_bootstrap_integrity -p "test_*.py" -v'
        )
        safe_summary_suite = (
            "python3 -I -S -B -m unittest discover "
            '-s tests/ci_safe_trx_summary -p "test_*.py" -v'
        )
        for job_name, verifier_step, verifier_lines in (
            (
                "relational-shard",
                "Run exact SQL Server relational shard",
                [
                    "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
                    '--shard "${{ matrix.shard }}" \\',
                    '--trx-output "artifacts/ci-test-results/relational/broad-${{ matrix.shard }}.trx" \\',
                    '--proof-output "artifacts/ci-relational-proofs/proof-${{ matrix.shard }}.json" \\',
                    '--source-head "${{ github.event.pull_request.head.sha || github.sha }}" \\',
                    '--tested-commit "$(git rev-parse HEAD)"',
                ],
            ),
            (
                "relational-rcsi",
                "Run exact RCSI relational acceptance verifier",
                [
                    "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
                    "--rcsi \\",
                    "--trx-output artifacts/ci-test-results/relational/rcsi.trx",
                ],
            ),
        ):
            body = self.job_body(job_name)
            header = body.split("    steps:\n", 1)[0]
            self.assertNotIn("    if:", header)
            self.assertNotIn("continue-on-error:", header)
            self.assertIn(
                "    env:\n      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n"
                "        Server=localhost,1433;Database=master;User Id=sa;"
                "Password=Ci_Relational_Only_2026;Encrypt=True;"
                "TrustServerCertificate=True\n",
                header,
            )
            self.assertIn(
                "    services:\n      sqlserver:\n"
                "        image: mcr.microsoft.com/mssql/server:2022-latest\n",
                header,
            )
            self.assertEqual(
                self.read_step_lines(
                    self.step_body(body, ".NET bootstrap integrity preflight")
                ),
                [
                    "run: |",
                    validator_command,
                    bootstrap_suite,
                    safe_summary_suite,
                ],
            )
            self.assertEqual(
                self.read_step_lines(
                    self.step_body(body, "Restore relational test packages")
                ),
                [
                    "run: |",
                    validator_command,
                    "dotnet restore tests/GoymenERP.Api.IntegrationTests/"
                    "GoymenERP.Api.IntegrationTests.csproj \\",
                    "-noAutoResponse \\",
                    "--configfile NuGet.Config",
                ],
            )
            self.assertEqual(
                self.read_step_lines(self.step_body(body, verifier_step)),
                ["run: |", validator_command, *verifier_lines],
            )

        shard = self.job_body("relational-shard")
        self.assertIn(
            "    strategy:\n"
            "      fail-fast: false\n"
            "      matrix:\n"
            "        shard: [A, B, C]\n",
            shard,
        )
        self.assertEqual(shard.count("        shard: [A, B, C]\n"), 1)
        self.assertNotIn("    strategy:", self.job_body("relational-rcsi"))

        aggregate = self.job_body("relational")
        self.assertEqual(
            re.findall(r"^    \S.*$", aggregate, re.M),
            [
                "    name: SQL Server relational acceptance",
                "    needs: [relational-shard, relational-rcsi]",
                "    if: ${{ always() }}",
                "    runs-on: ubuntu-latest",
                "    steps:",
            ],
        )
        self.assertNotIn("continue-on-error:", aggregate)
        self.assertNotIn("dotnet ", aggregate)
        self.assertEqual(aggregate.count("      - uses: actions/checkout@v7\n"), 1)
        self.assertEqual(
            re.findall(r"^      - name: (.+)$", aggregate, re.M),
            [
                "Verify relational shard proof parser",
                "Download exact SQL shard proofs",
                "Verify complete SQL Server relational acceptance",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Verify relational shard proof parser")
            ),
            [
                "run: |",
                "python3 -I -S -B -m unittest discover "
                '-s tests/rcsi_relational_acceptance_verifier -p "test_*.py" -v',
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(aggregate, "Download exact SQL shard proofs")
            ),
            [
                "uses: actions/download-artifact@v8",
                "with:",
                "pattern: ci-relational-shard-proof-*",
                "path: artifacts/ci-relational-proofs",
            ],
        )
        self.assertEqual(
            self.read_step_lines(
                self.step_body(
                    aggregate, "Verify complete SQL Server relational acceptance"
                )
            ),
            [
                "run: |",
                "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
                "--aggregate \\",
                "--proof-directory artifacts/ci-relational-proofs \\",
                '--source-head "${{ github.event.pull_request.head.sha || github.sha }}" \\',
                '--tested-commit "$(git rev-parse HEAD)" \\',
                '--worker-result "${{ needs.relational-shard.result }}" \\',
                '--rcsi-result "${{ needs.relational-rcsi.result }}"',
            ],
        )

    def test_relational_execution_and_aggregate_boundaries_reject_mutants(self) -> None:
        self.assert_relational_execution_and_aggregate_boundaries()
        mutations = (
            (
                "missing preflight",
                "      - name: .NET bootstrap integrity preflight\n",
                "      - name: Disabled bootstrap preflight\n",
            ),
            (
                "worker conditional",
                "  relational-shard:\n    name:",
                "  relational-shard:\n    if: ${{ false }}\n    name:",
            ),
            (
                "worker soft failure",
                "  relational-rcsi:\n    name:",
                "  relational-rcsi:\n    continue-on-error: true\n    name:",
            ),
            (
                "restore shell",
                "      - name: Restore relational test packages\n        run: |",
                "      - name: Restore relational test packages\n"
                "        shell: echo {0}\n        run: |",
            ),
            (
                "restore environment",
                "      - name: Restore relational test packages\n        run: |",
                "      - name: Restore relational test packages\n"
                "        env:\n          GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: ''\n"
                "        run: |",
            ),
            (
                "restore input",
                "          --configfile NuGet.Config\n",
                "          --configfile unpinned.config\n",
            ),
            (
                "missing shard",
                "        shard: [A, B, C]\n",
                "        shard: [A, B]\n",
            ),
            (
                "duplicate shard",
                "        shard: [A, B, C]\n",
                "        shard: [A, B, C, C]\n",
            ),
            (
                "missing aggregate dependency",
                "    needs: [relational-shard, relational-rcsi]\n",
                "    needs: [relational-shard]\n",
            ),
            (
                "aggregate not always",
                "    if: ${{ always() }}\n",
                "    if: ${{ success() }}\n",
            ),
            (
                "commented parser",
                "          python3 -I -S -B -m unittest discover "
                '-s tests/rcsi_relational_acceptance_verifier -p "test_*.py" -v',
                "          # python3 -I -S -B -m unittest discover "
                '-s tests/rcsi_relational_acceptance_verifier -p "test_*.py" -v',
            ),
            (
                "aggregate no-op",
                '          --rcsi-result "${{ needs.relational-rcsi.result }}"',
                '          --rcsi-result "${{ needs.relational-rcsi.result }}" || true',
            ),
        )
        for label, original, replacement in mutations:
            with self.subTest(mutant=label):
                changed = self.workflow.replace(original, replacement)
                self.assertNotEqual(changed, self.workflow)
                with mock.patch.object(self, "workflow", changed):
                    with self.assertRaises(AssertionError):
                        self.assert_relational_execution_and_aggregate_boundaries()

        aggregate = self.job_body("relational")
        download_marker = "      - name: Download exact SQL shard proofs\n"
        verify_marker = "      - name: Verify complete SQL Server relational acceptance\n"
        prefix, remainder = aggregate.split(download_marker, 1)
        download, verify = remainder.split(verify_marker, 1)
        reordered = prefix + verify_marker + verify + download_marker + download
        with mock.patch.object(self, "workflow", self.workflow.replace(aggregate, reordered)):
            with self.assertRaises(AssertionError):
                self.assert_relational_execution_and_aggregate_boundaries()

    def test_container_runner_validates_its_own_checkout_before_compose(self) -> None:
        body = self.job_body("containers")
        checkout_step = "      - uses: actions/checkout@v7"
        preflight_step = "      - name: .NET bootstrap integrity preflight"
        mirror_step = "      - name: Configure Docker Hub cache mirror"
        compose_step = "      - name: Validate compose model"
        build_step = "      - name: Build application images"
        validator_command = (
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"
        )

        self.assertEqual(
            re.findall(r"^\S.*$", self.workflow, re.M),
            ["name: CI", "on:", "permissions:", "concurrency:", "jobs:"],
        )
        self.assertEqual(
            body,
            (
                "    name: Container build\n"
                "    needs:\n"
                "      - backend\n"
                "      - frontend\n"
                "      - relational\n"
                "      - contract-drift\n"
                "    runs-on: ubuntu-latest\n"
                "    env:\n"
                "      MSSQL_SA_PASSWORD: Local_Only_Strong_Password_123!\n"
                "    steps:\n"
                "      - uses: actions/checkout@v7\n"
                "\n"
                "      - name: .NET bootstrap integrity preflight\n"
                "        run: |\n"
                f"          {validator_command}\n"
                "          python3 -I -S -B -m unittest discover "
                "-s tests/dockerhub_mirror_contract -p \"test_*.py\" -v\n"
                "\n"
                "      - name: Configure Docker Hub cache mirror\n"
                "        run: |\n"
                "          sudo python3 -I -S -B tools/configure_dockerhub_mirror.py configure \\\n"
                "            --daemon-config /etc/docker/daemon.json\n"
                "          sudo systemctl restart docker\n"
                "          docker info --format '{{json .RegistryConfig.Mirrors}}' | \\\n"
                "            python3 -I -S -B tools/configure_dockerhub_mirror.py verify\n"
                "\n"
                "      - name: Validate compose model\n"
                "        run: docker compose -f infra/docker-compose.yml config --quiet\n"
                "\n"
                "      - name: Build application images\n"
                "        run: docker compose -f infra/docker-compose.yml build api web\n"
            ),
        )
        self.assertEqual(
            re.findall(r"^    \S.*$", body, re.M),
            [
                "    name: Container build",
                "    needs:",
                "    runs-on: ubuntu-latest",
                "    env:",
                "    steps:",
            ],
        )
        self.assertNotIn("continue-on-error:", body)
        self.assertRegex(
            body,
            re.escape(checkout_step) + r"\n\n" + re.escape(preflight_step),
        )
        preflight = self.step_body(body, ".NET bootstrap integrity preflight")
        self.assertEqual(
            self.read_step_lines(preflight),
            [
                "run: |",
                validator_command,
                "python3 -I -S -B -m unittest discover "
                '-s tests/dockerhub_mirror_contract -p "test_*.py" -v',
            ],
        )
        self.assertEqual(
            self.read_step_lines(self.step_body(body, "Configure Docker Hub cache mirror")),
            [
                "run: |",
                "sudo python3 -I -S -B tools/configure_dockerhub_mirror.py configure \\",
                "--daemon-config /etc/docker/daemon.json",
                "sudo systemctl restart docker",
                "docker info --format '{{json .RegistryConfig.Mirrors}}' | \\",
                "python3 -I -S -B tools/configure_dockerhub_mirror.py verify",
            ],
        )
        self.assertEqual(
            self.read_step_lines(self.step_body(body, "Validate compose model")),
            ["run: docker compose -f infra/docker-compose.yml config --quiet"],
        )
        self.assertEqual(
            self.read_step_lines(self.step_body(body, "Build application images")),
            ["run: docker compose -f infra/docker-compose.yml build api web"],
        )

        preflight_index = body.index(preflight_step)
        mirror_index = body.index(mirror_step)
        compose_index = body.index(compose_step)
        build_index = body.index(build_step)
        boundary = body[preflight_index:mirror_index]
        self.assertEqual(
            re.findall(r"^      - (?:name:|uses:|run:).*$", boundary, re.M),
            [preflight_step],
        )
        self.assertLess(preflight_index, mirror_index)
        self.assertLess(mirror_index, compose_index)
        self.assertLess(compose_index, build_index)

    @staticmethod
    def read_step_lines(step: str) -> list[str]:
        return [
            line.strip()
            for line in step.splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]

    @classmethod
    def read_active_step_lines(cls, step: str) -> list[str]:
        return cls.read_step_lines(step)[1:]

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
