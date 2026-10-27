from __future__ import annotations

import ast
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import py_compile
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPOSITORY_ROOT / "tools" / "generate_release_manifest.py"
SCHEMA_PATH = REPOSITORY_ROOT / "contracts" / "release" / "release-manifest-v1.schema.json"


def _load_generator():
    spec = importlib.util.spec_from_file_location("release_manifest_generator", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("release manifest generator cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


generator = _load_generator()


def _git(*arguments: str) -> str:
    environment = os.environ.copy()
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"
    return subprocess.check_output(
        ["git", "-C", str(REPOSITORY_ROOT), *arguments],
        env=environment,
        text=True,
        encoding="utf-8",
    ).strip()


SOURCE_COMMIT = _git("rev-parse", "HEAD")


class ReleaseManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.snapshot = generator._load_snapshot(REPOSITORY_ROOT, SOURCE_COMMIT)
        cls.recovery_module = generator._load_recovery_module(cls.snapshot)
        cls.schema = generator._load_schema(cls.snapshot)
        cls.manifest = generator._build_manifest_from_snapshot(
            cls.snapshot, cls.recovery_module
        )
        generator._validate_schema(cls.recovery_module, cls.manifest, cls.schema)

    def test_current_source_manifest_is_exact_and_schema_valid(self) -> None:
        built = generator.build_manifest(REPOSITORY_ROOT, SOURCE_COMMIT)
        self.assertEqual(self.manifest, built)
        generator.validate_manifest(REPOSITORY_ROOT, built)
        self.assertEqual("goymenerp.release-manifest.v1", built["schemaVersion"])
        self.assertEqual("SOURCE_ONLY", built["manifestMode"])
        self.assertEqual(f"source:{SOURCE_COMMIT}", built["releaseId"])
        self.assertEqual(SOURCE_COMMIT, built["source"]["commit"])
        self.assertEqual(_git("rev-parse", f"{SOURCE_COMMIT}^{{tree}}"), built["source"]["tree"])
        self.assertTrue(built["source"]["cleanCheckout"])

    def test_components_are_source_bound_and_artifacts_are_not_built(self) -> None:
        self.assertEqual(["backend-api", "frontend-web"], [item["id"] for item in self.manifest["components"]])
        for component in self.manifest["components"]:
            self.assertEqual("NOT_BUILT", component["artifactStatus"])
            self.assertGreater(component["sourceFileCount"], 0)
            self.assertRegex(component["sourceSha256"], r"^[0-9a-f]{64}$")
            self.assertNotIn("artifactDigest", component)
            self.assertNotIn("artifactReference", component)

    def test_recovery_inventory_is_reproduced_and_bound_to_exact_blobs(self) -> None:
        recovery = self.manifest["recoveryInventory"]
        self.assertEqual(
            {"dbContexts": 18, "migrations": 97, "durableStores": 21},
            recovery["counts"],
        )
        self.assertEqual(
            [
                "contracts/recovery/recovery-surface-inventory-v1.schema.json",
                "tools/generate_recovery_surface_inventory.py",
                "tests/recovery_surface_inventory/test_generate_recovery_surface_inventory.py",
            ],
            [item["path"] for item in recovery["sources"]],
        )
        for record in recovery["sources"]:
            entry = self.snapshot.entries[record["path"]]
            self.assertEqual(entry.object_id, record["gitBlob"])
            self.assertEqual(hashlib.sha256(entry.content).hexdigest(), record["sha256"])

    def test_recovery_generator_executes_exact_blob_not_ignored_valid_pyc(self) -> None:
        recovery_path = REPOSITORY_ROOT / generator.RECOVERY_GENERATOR_PATH
        cache_path = Path(importlib.util.cache_from_source(str(recovery_path)))
        cache_directory_existed = cache_path.parent.exists()
        previous_cache = cache_path.read_bytes() if cache_path.exists() else None
        source = recovery_path.read_bytes()
        canary_name = "_R02_IGNORED_BYTECODE_EXECUTED"
        malicious_source = source + f"\n{canary_name} = True\n".encode("ascii")

        try:
            cache_path.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory() as directory:
                malicious_path = Path(directory) / recovery_path.name
                malicious_path.write_bytes(malicious_source)
                py_compile.compile(
                    str(malicious_path),
                    cfile=str(cache_path),
                    dfile=str(recovery_path),
                    doraise=True,
                    invalidation_mode=py_compile.PycInvalidationMode.TIMESTAMP,
                )

            payload = bytearray(cache_path.read_bytes())
            self.assertEqual(0, int.from_bytes(payload[4:8], "little"))
            source_stat = recovery_path.stat()
            payload[8:12] = (int(source_stat.st_mtime) & 0xFFFFFFFF).to_bytes(
                4, "little"
            )
            payload[12:16] = (source_stat.st_size & 0xFFFFFFFF).to_bytes(
                4, "little"
            )
            cache_path.write_bytes(payload)

            legacy_name = "_r02_legacy_source_file_loader_probe"
            legacy_spec = importlib.util.spec_from_file_location(legacy_name, recovery_path)
            self.assertIsNotNone(legacy_spec)
            assert legacy_spec is not None and legacy_spec.loader is not None
            legacy_module = importlib.util.module_from_spec(legacy_spec)
            sys.modules[legacy_name] = legacy_module
            try:
                legacy_spec.loader.exec_module(legacy_module)
                self.assertIs(getattr(legacy_module, canary_name, False), True)
            finally:
                sys.modules.pop(legacy_name, None)

            self.assertEqual(
                "",
                _git("status", "--porcelain=v1", "--untracked-files=all"),
                "Ignored bytecode must not alter the tracked clean-tree proof",
            )
            exact_module = generator._load_recovery_module(self.snapshot)
            self.assertFalse(hasattr(exact_module, canary_name))
            rebuilt = generator._build_manifest_from_snapshot(
                self.snapshot, exact_module
            )
            self.assertEqual(self.manifest, rebuilt)
        finally:
            if previous_cache is None:
                cache_path.unlink(missing_ok=True)
                if not cache_directory_existed:
                    try:
                        cache_path.parent.rmdir()
                    except OSError:
                        pass
            else:
                cache_path.write_bytes(previous_cache)

        self.assertEqual("", _git("status", "--porcelain=v1", "--untracked-files=all"))

    def test_configuration_lists_key_names_without_values(self) -> None:
        configuration = self.manifest["configuration"]
        self.assertEqual("KEY_NAMES_ONLY", configuration["classification"])
        self.assertTrue(
            {
                "Authentication",
                "BootstrapIdentity",
                "ConnectionStrings:GoymenErp",
                "DatabaseStartup:Mode",
                "DB_CONNECTION_STRING",
                "Documents:ObjectStore:RootPath",
                "IdentitySeed",
                "OrganizationSeed",
            }.issubset(configuration["backendKeys"])
        )
        self.assertEqual(
            ["VITE_API_BASE_URL", "VITE_BOOTSTRAP_IDENTITY_ENABLED", "VITE_DEV_USER"],
            configuration["frontendKeys"],
        )
        encoded = generator.canonical_json(self.manifest)
        for value in (
            "company-a-user",
            "http://localhost:5173",
            "DevelopmentBootstrap",
            "goymen-erp-api",
            "https://openapi.invalid",
            "Şirket A Geliştirme Kullanıcısı",
        ):
            self.assertNotIn(value, encoded)

    def test_configuration_constant_recognizer_stays_key_name_only(self) -> None:
        source = """
            internal const string ConfigurationKey = "DatabaseStartup:Mode";
            public const string SectionName = "IdentitySeed";
            private const string EvidenceConfigurationKey = "Evidence:Enabled";
            private const string ArbitraryValue = "DO_NOT_INVENTORY";
        """
        self.assertEqual(
            ["DatabaseStartup:Mode", "IdentitySeed", "Evidence:Enabled"],
            generator.CONFIG_CONSTANT_PATTERN.findall(source),
        )

    def test_environment_secret_canary_is_never_read_or_emitted(self) -> None:
        canary = "R02_SECRET_CANARY_9f0ca61a"
        with mock.patch.dict(os.environ, {"R02_RELEASE_SECRET": canary}):
            rebuilt = generator._build_manifest_from_snapshot(
                self.snapshot, self.recovery_module
            )
        self.assertNotIn(canary, generator.canonical_json(rebuilt))

    def test_feature_list_is_the_exact_backend_module_source_surface(self) -> None:
        expected = [
            "Accounting", "Audit", "Counterparty", "Documents", "Finance",
            "Idempotency", "Identity", "Inventory", "Logistics", "Manufacturing",
            "Organization", "Product", "Purchasing", "Sales", "Settings", "Work",
        ]
        self.assertEqual(expected, [item["id"] for item in self.manifest["features"]])
        for feature in self.manifest["features"]:
            self.assertEqual("SOURCE_PRESENT", feature["status"])
            self.assertEqual(
                f"src/backend/Modules/{feature['id']}", feature["sourcePath"]
            )
            self.assertGreater(feature["sourceFileCount"], 0)

    def test_unverified_evidence_and_negative_assertions_are_fail_closed(self) -> None:
        self.assertEqual("UNVERIFIED", self.manifest["result"])
        self.assertFalse(self.manifest["deployable"])
        self.assertEqual(
            {
                "applicationBuild": "NOT_RUN", "frontendBuild": "NOT_RUN",
                "tests": "NOT_RUN", "realSql": "NOT_RUN", "securityScan": "NOT_RUN",
                "sbom": "MISSING", "provenance": "UNVERIFIED",
                "attestation": "MISSING", "restore": "NOT_RUN", "uat": "NOT_RUN",
            },
            self.manifest["evidence"],
        )
        self.assertTrue(self.manifest["assertions"]["sourceOnly"])
        for key, value in self.manifest["assertions"].items():
            if key != "sourceOnly":
                self.assertIs(value, False, key)

    def test_schema_rejects_extra_fields_wrong_types_and_boolean_integer_aliases(self) -> None:
        changed = copy.deepcopy(self.manifest)
        changed["unexpected"] = True
        with self.assertRaises(generator.ReleaseManifestError):
            generator._validate_schema(self.recovery_module, changed, self.schema)

        changed = copy.deepcopy(self.manifest)
        changed["source"]["cleanCheckout"] = 1
        with self.assertRaises(generator.ReleaseManifestError):
            generator._validate_schema(self.recovery_module, changed, self.schema)

        changed = copy.deepcopy(self.manifest)
        changed["assertions"]["deploys"] = 0
        with self.assertRaises(generator.ReleaseManifestError):
            generator._validate_schema(self.recovery_module, changed, self.schema)

    def test_schema_is_closed_at_every_object_boundary(self) -> None:
        def visit(node: object) -> None:
            if isinstance(node, dict):
                if node.get("type") == "object":
                    self.assertIs(node.get("additionalProperties"), False)
                for value in node.values():
                    visit(value)
            elif isinstance(node, list):
                for value in node:
                    visit(value)

        visit(self.schema)

    def test_wrong_source_nested_root_and_dirty_tree_fail_closed(self) -> None:
        with self.assertRaisesRegex(generator.ReleaseManifestError, "current Git HEAD"):
            generator.build_manifest(REPOSITORY_ROOT, "0" * 40)
        with self.assertRaisesRegex(generator.ReleaseManifestError, "Git top-level"):
            generator.build_manifest(REPOSITORY_ROOT / "src", SOURCE_COMMIT)

        probe = REPOSITORY_ROOT / ".r021-release-manifest-dirty-probe"
        self.assertFalse(probe.exists())
        try:
            probe.write_text("dirty\n", encoding="utf-8")
            with self.assertRaisesRegex(generator.ReleaseManifestError, "must be clean"):
                generator.build_manifest(REPOSITORY_ROOT, SOURCE_COMMIT)
        finally:
            probe.unlink(missing_ok=True)

    def test_exact_source_records_use_commit_blobs_not_worktree_reads(self) -> None:
        records = [
            *self.manifest["recoveryInventory"]["sources"],
            *self.manifest["configuration"]["sourceFiles"],
        ]
        for record in records:
            entry = self.snapshot.entries[record["path"]]
            self.assertEqual(entry.object_id, record["gitBlob"])
            self.assertEqual(hashlib.sha256(entry.content).hexdigest(), record["sha256"])

    def test_cli_is_two_run_deterministic_utf8_lf_and_exact_head_bound(self) -> None:
        command = [
            sys.executable, "-I", "-S", "-B", str(GENERATOR_PATH),
            "--repository-root", str(REPOSITORY_ROOT),
            "--source-commit", SOURCE_COMMIT,
        ]
        first = subprocess.run(command, cwd=REPOSITORY_ROOT, check=False, capture_output=True)
        second = subprocess.run(command, cwd=REPOSITORY_ROOT, check=False, capture_output=True)
        self.assertEqual(0, first.returncode, first.stderr.decode("utf-8", errors="replace"))
        self.assertEqual(0, second.returncode, second.stderr.decode("utf-8", errors="replace"))
        self.assertEqual(first.stdout, second.stdout)
        self.assertEqual(generator.canonical_bytes(self.manifest), first.stdout)
        self.assertNotIn(b"\r\n", first.stdout)
        self.assertTrue(first.stdout.endswith(b"\n"))
        self.assertFalse(first.stdout.endswith(b"\n\n"))
        decoded = json.loads(first.stdout.decode("utf-8"))
        self.assertEqual(SOURCE_COMMIT, decoded["source"]["commit"])

    def test_canonical_bytes_have_a_fixed_portable_hash(self) -> None:
        fixture = {"z": "Göymen", "a": [True, False, 0, 1]}
        encoded = generator.canonical_bytes(fixture)
        self.assertEqual(
            "84568c2defce8688a7900c23f8a59a23bb82f4f8fd83e9887b577bba38d139f6",
            hashlib.sha256(encoded).hexdigest(),
        )
        self.assertNotIn(b"\r\n", encoded)
        self.assertEqual(encoded, encoded.decode("utf-8").encode("utf-8"))

    def test_generator_uses_only_python_standard_library(self) -> None:
        tree = ast.parse(GENERATOR_PATH.read_text(encoding="utf-8"))
        allowed = {
            "__future__", "argparse", "dataclasses", "hashlib", "importlib", "json",
            "os", "pathlib", "re", "subprocess", "sys", "types", "typing",
        }
        imported: set[str] = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported.update(alias.name.split(".", 1)[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imported.add(node.module.split(".", 1)[0])
        self.assertEqual(set(), imported - allowed)
        self.assertNotIn("importlib", imported)


if __name__ == "__main__":
    unittest.main()
