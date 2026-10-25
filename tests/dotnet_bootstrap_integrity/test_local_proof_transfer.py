"""Execute the CI YAML's actual local transfer scripts in disposable Linux fixtures."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


@unittest.skipUnless(os.name == "posix" and shutil.which("bash"), "Linux bash is required")
class LocalProofTransferTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="goymen-local-proof-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.checkout = self.root / "checkout"
        self.checkout.mkdir()
        self.evidence = self.root / "evidence"
        self.env = {key: value for key, value in os.environ.items() if not key.startswith("GIT_")}
        self.env.update({
            "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": "/dev/null",
            "LOCAL_EVIDENCE_SOURCE_HEAD": "a" * 40,
            "GITHUB_RUN_ID": "12345", "GITHUB_RUN_ATTEMPT": "2",
            "RUNNER_NAME": "fixture-runner",
        })
        self.git("init", "--quiet")
        self.git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                 "-c", "core.hooksPath=/dev/null", "commit", "--quiet", "--allow-empty", "-m", "fixture")
        self.tested = self.git("rev-parse", "HEAD").strip()
        workflow = (REPOSITORY_ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.scripts = {}
        for anchor in ("publish-local-evidence", "restore-local-proofs"):
            match = re.search(
                rf"(?m)^        run: &{anchor} \|\n((?:^          .*\n|^\n)+)", workflow
            )
            self.assertIsNotNone(match, anchor)
            script = textwrap.dedent(match.group(1))
            # Only the absolute storage root is redirected. Execute every other
            # line verbatim, including git identity, hashing and fail-closed tests.
            self.assertEqual(script.count("root=/home/ci/local-ci-evidence\n"), 1)
            self.scripts[anchor] = script.replace(
                "root=/home/ci/local-ci-evidence\n", f"root='{self.evidence}'\n", 1
            )

    def git(self, *args: str) -> str:
        return subprocess.run(
            ["git", *args], cwd=self.checkout, env=self.env, check=True,
            text=True, capture_output=True, timeout=10,
        ).stdout

    def run_script(self, anchor: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["bash", "--noprofile", "--norc", "-c", self.scripts[anchor]],
            cwd=self.checkout, env=self.env, text=True, capture_output=True, timeout=10,
        )

    def publish(self, group: str, *, relational: bool = False) -> Path:
        kind = "relational" if relational else "backend"
        relative = f"artifacts/ci-{kind}-group-proofs/proof-{group}.json"
        if relational:
            relative = f"artifacts/ci-relational-proofs/proof-{group}.json"
        proof = self.checkout / relative
        proof.parent.mkdir(parents=True, exist_ok=True)
        proof.write_bytes(b'{"fixture":"transfer only; not SQL acceptance"}\n')
        self.env.update({
            "LOCAL_EVIDENCE_SCOPE": f"{kind}-proof-{group}",
            "LOCAL_EVIDENCE_FILE": relative,
        })
        result = self.run_script("publish-local-evidence")
        self.assertEqual(result.returncode, 0, result.stderr)
        return self.evidence / f"gh-ci-12345-2-{'a' * 7}" / f"{kind}-proof-{group}"

    def configure_restore(self, ids: str = "api", *, relational: bool = False) -> None:
        kind = "relational" if relational else "backend"
        output = f"artifacts/ci-{kind}-group-proofs"
        artifact_prefix = "ci-backend-test-group-proof-"
        if relational:
            output = "artifacts/ci-relational-proofs"
            artifact_prefix = "ci-relational-shard-proof-"
        self.env.update({
            "LOCAL_PROOF_IDS": ids, "LOCAL_PROOF_SCOPE_PREFIX": f"{kind}-proof-",
            "LOCAL_PROOF_ARTIFACT_PREFIX": artifact_prefix,
            "LOCAL_PROOF_RELATIVE_PREFIX": f"{output}/proof-",
            "LOCAL_PROOF_OUTPUT": output,
        })

    def test_round_trip_preserves_all_backend_and_relational_proofs(self) -> None:
        for relational, ids in ((False, ("api", "architecture", "modules")), (True, ("A", "B", "C"))):
            with self.subTest(relational=relational):
                sources = {group: self.publish(group, relational=relational) for group in ids}
                self.configure_restore(",".join(ids), relational=relational)
                result = self.run_script("restore-local-proofs")
                self.assertEqual(result.returncode, 0, result.stderr)
                output = self.checkout / self.env["LOCAL_PROOF_OUTPUT"]
                for group, source in sources.items():
                    restored = output / (self.env["LOCAL_PROOF_ARTIFACT_PREFIX"] + group) / f"proof-{group}.json"
                    original = source / "files" / (self.env["LOCAL_PROOF_RELATIVE_PREFIX"] + group + ".json")
                    self.assertEqual(restored.read_bytes(), original.read_bytes())
                self.assertEqual(len(list(output.glob("*/proof-*.json"))), 3)

    def test_restore_rejects_every_mismatched_manifest_identity(self) -> None:
        source = self.publish("api")
        self.configure_restore()
        manifest = source / "manifest.txt"
        original = manifest.read_text()
        for key in ("schemaVersion", "workflow", "scope", "runId", "runAttempt", "sourceHead", "testedCommit", "runnerName", "file"):
            with self.subTest(claim=key):
                manifest.write_text(re.sub(rf"(?m)^{key}=.*$", f"{key}=wrong", original))
                self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)
        manifest.write_text(original)

    def test_restore_rejects_same_size_corrupted_proof(self) -> None:
        source = self.publish("api")
        self.configure_restore()
        proof = source / "files" / self.env["LOCAL_EVIDENCE_FILE"]
        original = proof.read_bytes()
        proof.write_bytes(b"x" + original[1:])
        self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)

    def test_restore_rejects_truncated_proof(self) -> None:
        source = self.publish("api")
        self.configure_restore()
        (source / "files" / self.env["LOCAL_EVIDENCE_FILE"]).write_bytes(b"{}")
        self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)

    def test_restore_rejects_missing_group(self) -> None:
        self.publish("api")
        self.configure_restore("api,architecture,modules")
        self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)

    def test_restore_rejects_other_run_attempt_or_head(self) -> None:
        self.publish("api")
        self.configure_restore()
        for key, value in (("GITHUB_RUN_ID", "999"), ("GITHUB_RUN_ATTEMPT", "3"), ("LOCAL_EVIDENCE_SOURCE_HEAD", "b" * 40)):
            with self.subTest(identity=key):
                original = self.env[key]
                self.env[key] = value
                self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)
                self.env[key] = original

    def test_publish_rejects_overwrite_and_symlink_destination(self) -> None:
        source = self.publish("api")
        self.assertNotEqual(self.run_script("publish-local-evidence").returncode, 0)
        original = source.with_name("original-proof")
        source.rename(original)
        source.symlink_to(original, target_is_directory=True)
        self.assertNotEqual(self.run_script("publish-local-evidence").returncode, 0)

    def test_restore_rejects_symlinked_scope(self) -> None:
        source = self.publish("api")
        self.configure_restore()
        original = source.with_name("original-proof")
        source.rename(original)
        source.symlink_to(original, target_is_directory=True)
        self.assertNotEqual(self.run_script("restore-local-proofs").returncode, 0)
