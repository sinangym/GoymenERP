from __future__ import annotations

import hashlib
import io
import json
import os
import re
import shutil
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from typing import Any
from unittest import mock


MODULE_DIRECTORY = Path(__file__).resolve().parent
if str(MODULE_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(MODULE_DIRECTORY))

import validate_erp000m_evidence as validator_module  # noqa: E402
from validate_erp000m_evidence import (  # noqa: E402
    DOCS_RELATIVE_ROOT,
    EVIDENCE_TEMPLATE,
    ENVIRONMENT_SELF_HASH_SPEC,
    ENVIRONMENT_TEMPLATE,
    MANIFEST_SELF_HASH_SPEC,
    README_TEMPLATE,
    SELF_HASH_SPECS,
    SelfHashSpec,
    WORKLOAD_SELF_HASH_SPEC,
    WORKLOAD_TEMPLATE,
    compute_self_hash_sha256,
    main,
    validate_persisted_hash_pack,
    validate_persisted_self_hash,
    validate_repository,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class Erp000mEvidenceTemplateValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        destination = self.root / DOCS_RELATIVE_ROOT
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(REPOSITORY_ROOT / DOCS_RELATIVE_ROOT, destination)

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_repository_templates_are_valid(self) -> None:
        self.assertEqual([], validate_repository(self.root))

    def test_filled_pack_hashes_recompute_from_persisted_markdown_and_json(self) -> None:
        environment, workload, manifest, expected_hashes = self._prepare_filled_pack()

        self.assertEqual(
            [],
            validate_persisted_hash_pack(environment, workload, manifest),
        )
        for path, spec in zip(
            (environment, workload, manifest),
            SELF_HASH_SPECS,
        ):
            with self.subTest(artifact=spec.artifact_name):
                computation, errors = compute_self_hash_sha256(path, spec)
                self.assertEqual([], errors)
                self.assertIsNotNone(computation)
                assert computation is not None
                self.assertEqual(
                    expected_hashes[spec.artifact_name],
                    computation.computed_sha256,
                )
                self.assertEqual(
                    computation.computed_sha256,
                    computation.stored_value,
                )

        stdout = io.StringIO()
        with redirect_stdout(stdout):
            exit_code = main(
                [
                    "--verify-filled-pack",
                    str(environment),
                    str(workload),
                    str(manifest),
                ]
            )
        self.assertEqual(0, exit_code)
        self.assertIn(
            "Captured ERP-000M evidence bytes and hash links are valid",
            stdout.getvalue(),
        )
        self.assertIn(
            "Revalidate before every consumption boundary",
            stdout.getvalue(),
        )

    def test_filled_pack_uses_one_captured_snapshot_per_artifact(self) -> None:
        environment, workload, manifest, _ = self._prepare_filled_pack()
        original_capture = validator_module._capture_exact_utf8
        original_open = validator_module._open_capture_descriptor
        capture_counts: dict[Path, int] = {}
        open_counts: dict[Path, int] = {}

        def capture_once(path: Path) -> Any:
            canonical_path = Path(path)
            capture_counts[canonical_path] = capture_counts.get(canonical_path, 0) + 1
            if capture_counts[canonical_path] > 1:
                raise AssertionError(f"second artifact snapshot attempted: {path}")
            return original_capture(path)

        def open_once(path: Path) -> int:
            canonical_path = Path(path)
            open_counts[canonical_path] = open_counts.get(canonical_path, 0) + 1
            if open_counts[canonical_path] > 1:
                raise AssertionError(f"second artifact descriptor attempted: {path}")
            return original_open(path)

        with mock.patch.object(
            validator_module,
            "_capture_exact_utf8",
            side_effect=capture_once,
        ), mock.patch.object(
            validator_module,
            "_open_capture_descriptor",
            side_effect=open_once,
        ), mock.patch.object(
            Path,
            "read_text",
            side_effect=AssertionError("mixed snapshot path read attempted"),
        ):
            errors = validate_persisted_hash_pack(environment, workload, manifest)

        self.assertEqual([], errors)
        self.assertEqual(
            {environment: 1, workload: 1, manifest: 1},
            capture_counts,
        )
        self.assertEqual(
            {environment: 1, workload: 1, manifest: 1},
            open_counts,
        )

    def test_filled_mode_rejects_non_regular_artifact_before_open(self) -> None:
        directory = self.root / "not-a-regular-artifact"
        directory.mkdir()

        errors = validate_persisted_self_hash(directory, WORKLOAD_SELF_HASH_SPEC)

        self.assertTrue(any("must be a regular file" in error for error in errors))

    def test_filled_mode_rejects_symbolic_link_or_reparse_point(self) -> None:
        target = self.root / WORKLOAD_TEMPLATE
        link = self.root / "workload-link.json"
        try:
            os.symlink(target, link)
        except (NotImplementedError, OSError) as exc:
            self.skipTest(f"platform cannot create a symbolic link: {exc}")

        errors = validate_persisted_self_hash(link, WORKLOAD_SELF_HASH_SPEC)

        self.assertTrue(
            any("symbolic link or reparse point is forbidden" in error for error in errors)
        )

    def test_filled_mode_rejects_directory_symlink_or_junction_ancestor(self) -> None:
        real_directory = self.root / "real-artifact-directory"
        real_directory.mkdir()
        artifact_name = "workload.json"
        shutil.copy2(
            self.root / WORKLOAD_TEMPLATE,
            real_directory / artifact_name,
        )
        linked_directory = self.root / "linked-artifact-directory"
        try:
            os.symlink(
                real_directory,
                linked_directory,
                target_is_directory=True,
            )
        except (NotImplementedError, OSError) as exc:
            self.skipTest(
                f"platform cannot create a directory symbolic link or junction: {exc}"
            )

        errors = validate_persisted_self_hash(
            linked_directory / artifact_name,
            WORKLOAD_SELF_HASH_SPEC,
        )

        self.assertTrue(
            any(
                "ancestor symbolic link or reparse point is forbidden" in error
                for error in errors
            )
        )

    @unittest.skipUnless(
        os.name == "nt",
        "NTFS alternate data stream enforcement is Windows-specific",
    )
    def test_filled_mode_rejects_ntfs_alternate_data_stream(self) -> None:
        _, workload, _, _ = self._prepare_filled_pack()
        carrier = self.root / "carrier.txt"
        carrier.write_text("carrier", encoding="utf-8")
        stream = Path(f"{carrier}:workload.json")
        try:
            stream.write_bytes(workload.read_bytes())
        except OSError as exc:
            self.skipTest(f"filesystem does not support NTFS alternate streams: {exc}")

        errors = validate_persisted_self_hash(stream, WORKLOAD_SELF_HASH_SPEC)

        self.assertTrue(
            any("alternate data streams or named-stream" in error for error in errors)
        )

    @unittest.skipUnless(
        os.name == "nt",
        "Windows share-mode enforcement is platform-specific",
    )
    def test_windows_capture_handle_denies_write_and_delete_until_read_finishes(
        self,
    ) -> None:
        _, workload, _, _ = self._prepare_filled_pack()
        original_read = validator_module.os.read
        attempted = False
        write_blocked = False
        delete_blocked = False

        def read_with_concurrent_mutation_attempt(
            descriptor: int,
            byte_count: int,
        ) -> bytes:
            nonlocal attempted, write_blocked, delete_blocked
            if not attempted:
                attempted = True
                try:
                    with workload.open("r+b"):
                        pass
                except OSError:
                    write_blocked = True
                try:
                    workload.unlink()
                except OSError:
                    delete_blocked = True
            return original_read(descriptor, byte_count)

        with mock.patch.object(
            validator_module.os,
            "read",
            side_effect=read_with_concurrent_mutation_attempt,
        ):
            errors = validate_persisted_self_hash(
                workload,
                WORKLOAD_SELF_HASH_SPEC,
            )

        self.assertTrue(attempted)
        self.assertTrue(write_blocked, "Windows capture allowed a concurrent writer")
        self.assertTrue(delete_blocked, "Windows capture allowed a concurrent delete")
        self.assertEqual([], errors)

    def test_default_template_mode_still_rejects_concrete_self_hashes(self) -> None:
        cases = (
            (ENVIRONMENT_TEMPLATE, ENVIRONMENT_SELF_HASH_SPEC),
            (WORKLOAD_TEMPLATE, WORKLOAD_SELF_HASH_SPEC),
            (EVIDENCE_TEMPLATE, MANIFEST_SELF_HASH_SPEC),
        )
        for relative_path, spec in cases:
            with self.subTest(artifact=spec.artifact_name):
                self._restore_templates()
                path = self.root / relative_path
                self._replace_self_hash_value(
                    path,
                    spec,
                    spec.sentinel,
                    "A" * 64,
                )

                errors = validate_repository(self.root)
                self.assertTrue(
                    any(
                        "expected exact placeholder" in error
                        or "canonical environment hash drift" in error
                        for error in errors
                    )
                )

    def test_non_hash_byte_mutation_is_detected_for_every_artifact(self) -> None:
        mutations = (
            (ENVIRONMENT_SELF_HASH_SPEC, ENVIRONMENT_TEMPLATE),
            (WORKLOAD_SELF_HASH_SPEC, WORKLOAD_TEMPLATE),
            (MANIFEST_SELF_HASH_SPEC, EVIDENCE_TEMPLATE),
        )
        for spec, relative_path in mutations:
            with self.subTest(artifact=spec.artifact_name):
                self._restore_templates()
                self._prepare_filled_pack()
                path = self.root / relative_path
                path.write_bytes(path.read_bytes() + b"\n")

                self.assertTrue(
                    any(
                        "persisted self-hash mismatch" in error
                        for error in validate_persisted_self_hash(path, spec)
                    )
                )

    def test_stored_hash_mutation_is_detected(self) -> None:
        environment, _, _, expected_hashes = self._prepare_filled_pack()
        original = expected_hashes[ENVIRONMENT_SELF_HASH_SPEC.artifact_name]
        replacement = "0" * 64
        self._replace_self_hash_value(
            environment,
            ENVIRONMENT_SELF_HASH_SPEC,
            original,
            replacement,
        )

        self.assertTrue(
            any(
                "persisted self-hash mismatch" in error
                for error in validate_persisted_self_hash(
                    environment,
                    ENVIRONMENT_SELF_HASH_SPEC,
                )
            )
        )

    def test_unsealed_lowercase_short_and_non_hex_hashes_are_rejected(self) -> None:
        cases = (
            (
                "unsealed",
                WORKLOAD_SELF_HASH_SPEC.sentinel,
                "is unsealed",
            ),
            ("lowercase", "a" * 64, "64 uppercase hexadecimal"),
            ("short", "A" * 63, "64 uppercase hexadecimal"),
            ("non-hex", "G" * 64, "64 uppercase hexadecimal"),
        )
        for name, value, expected_error in cases:
            with self.subTest(case=name):
                self._restore_templates()
                path = self.root / WORKLOAD_TEMPLATE
                if value != WORKLOAD_SELF_HASH_SPEC.sentinel:
                    self._replace_self_hash_value(
                        path,
                        WORKLOAD_SELF_HASH_SPEC,
                        WORKLOAD_SELF_HASH_SPEC.sentinel,
                        value,
                    )

                self.assertTrue(
                    any(
                        expected_error in error
                        for error in validate_persisted_self_hash(
                            path,
                            WORKLOAD_SELF_HASH_SPEC,
                        )
                    )
                )

    def test_missing_and_duplicate_self_hash_fields_fail_closed(self) -> None:
        mutations = (
            (
                "missing-json",
                WORKLOAD_TEMPLATE,
                '    "workload_profile_hash_sha256": '
                '"__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__",\n',
                "",
                WORKLOAD_SELF_HASH_SPEC,
                "must contain a string",
            ),
            (
                "duplicate-json",
                WORKLOAD_TEMPLATE,
                '    "workload_profile_hash_sha256": '
                '"__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__",\n',
                '    "workload_profile_hash_sha256": '
                '"__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__",\n'
                '    "workload_profile_hash_sha256": '
                '"__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__",\n',
                WORKLOAD_SELF_HASH_SPEC,
                "duplicate key",
            ),
            (
                "missing-markdown",
                ENVIRONMENT_TEMPLATE,
                "| inventory_hash_sha256 | "
                "__REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_SHA256__ | "
                "__REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_EVIDENCE_REF__ |\n",
                "",
                ENVIRONMENT_SELF_HASH_SPEC,
                "missing canonical self-hash row",
            ),
        )
        for name, relative_path, original, replacement, spec, expected_error in mutations:
            with self.subTest(case=name):
                self._restore_templates()
                path = self.root / relative_path
                raw = path.read_text(encoding="utf-8")
                self.assertIn(original, raw)
                path.write_text(
                    raw.replace(original, replacement, 1),
                    encoding="utf-8",
                )

                self.assertTrue(
                    any(
                        expected_error in error
                        for error in validate_persisted_self_hash(path, spec)
                    )
                )

    def test_escaped_json_self_hash_value_is_rejected(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        escaped = WORKLOAD_SELF_HASH_SPEC.sentinel.replace(
            "WORKLOAD",
            "\\u0057ORKLOAD",
            1,
        )
        self.assertNotEqual(WORKLOAD_SELF_HASH_SPEC.sentinel, escaped)
        path.write_text(
            raw.replace(WORKLOAD_SELF_HASH_SPEC.sentinel, escaped, 1),
            encoding="utf-8",
        )

        self.assertTrue(
            any(
                "literal canonical self-hash field" in error
                for error in validate_persisted_self_hash(
                    path,
                    WORKLOAD_SELF_HASH_SPEC,
                )
            )
        )

    def test_escaped_nested_self_hash_key_is_rejected_as_global_duplicate(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        anchor = '    "owner": "__REQUIRED_INPUT__:OWNER__",\n'
        escaped_key = "workload_profile_hash_sha\\u0032\\u0035\\u0036"
        injected = (
            anchor
            + f'    "{escaped_key}": "__REQUIRED_INPUT__:UNRELATED__",\n'
        )
        self.assertIn(anchor, raw)
        path.write_text(raw.replace(anchor, injected, 1), encoding="utf-8")

        self.assertTrue(
            any(
                "must appear exactly once globally" in error
                for error in validate_persisted_self_hash(
                    path,
                    WORKLOAD_SELF_HASH_SPEC,
                )
            )
        )

    def test_deep_json_recursion_fails_closed_without_escaping_validator(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        depth = 1500
        path.write_text(
            '{"nested":' * depth + "0" + "}" * depth,
            encoding="utf-8",
        )

        errors = validate_persisted_self_hash(path, WORKLOAD_SELF_HASH_SPEC)

        self.assertTrue(errors)
        self.assertTrue(
            any("self-hash field" in error for error in errors),
            "deep JSON must be rejected without recursive-walker failure",
        )

        with mock.patch.object(
            validator_module.json,
            "loads",
            side_effect=RecursionError("adversarial JSON nesting"),
        ):
            recursion_errors = validate_persisted_self_hash(
                path,
                WORKLOAD_SELF_HASH_SPEC,
            )
        self.assertTrue(
            any("strict JSON parse failed" in error for error in recursion_errors)
        )

    def test_only_canonical_json_slot_is_neutralized(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        raw = path.read_text(encoding="utf-8").replace(
            "__REQUIRED_INPUT__:OWNER__",
            WORKLOAD_SELF_HASH_SPEC.sentinel,
            1,
        )
        path.write_text(raw, encoding="utf-8")

        expected = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        computation, errors = compute_self_hash_sha256(
            path,
            WORKLOAD_SELF_HASH_SPEC,
        )

        self.assertEqual([], errors)
        self.assertIsNotNone(computation)
        assert computation is not None
        self.assertEqual(expected, computation.computed_sha256)
        self._replace_self_hash_value(
            path,
            WORKLOAD_SELF_HASH_SPEC,
            WORKLOAD_SELF_HASH_SPEC.sentinel,
            expected,
        )
        self.assertEqual(
            [],
            validate_persisted_self_hash(path, WORKLOAD_SELF_HASH_SPEC),
        )

    def test_json_format_markdown_eol_and_utf8_bom_mutations_are_rejected(self) -> None:
        environment, workload, _, _ = self._prepare_filled_pack()
        workload_payload = json.loads(workload.read_text(encoding="utf-8"))
        workload.write_text(
            json.dumps(
                workload_payload,
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "persisted self-hash mismatch" in error
                for error in validate_persisted_self_hash(
                    workload,
                    WORKLOAD_SELF_HASH_SPEC,
                )
            )
        )

        environment_bytes = environment.read_bytes()
        if b"\r\n" in environment_bytes:
            environment.write_bytes(environment_bytes.replace(b"\r\n", b"\n"))
        else:
            environment.write_bytes(environment_bytes.replace(b"\n", b"\r\n"))
        self.assertTrue(
            any(
                "persisted self-hash mismatch" in error
                for error in validate_persisted_self_hash(
                    environment,
                    ENVIRONMENT_SELF_HASH_SPEC,
                )
            )
        )

        self._restore_templates()
        workload = self.root / WORKLOAD_TEMPLATE
        workload.write_bytes(b"\xef\xbb\xbf" + workload.read_bytes())
        self.assertTrue(
            any(
                "UTF-8 BOM is forbidden" in error
                for error in validate_persisted_self_hash(
                    workload,
                    WORKLOAD_SELF_HASH_SPEC,
                )
            )
        )

    def test_resealed_manifest_with_wrong_source_hash_fails_cross_hash_check(self) -> None:
        environment, workload, manifest, expected_hashes = self._prepare_filled_pack()
        manifest_hash = expected_hashes[MANIFEST_SELF_HASH_SPEC.artifact_name]
        environment_hash = expected_hashes[ENVIRONMENT_SELF_HASH_SPEC.artifact_name]
        wrong_environment_hash = "0" * 64
        self._replace_self_hash_value(
            manifest,
            MANIFEST_SELF_HASH_SPEC,
            manifest_hash,
            MANIFEST_SELF_HASH_SPEC.sentinel,
        )
        raw = manifest.read_bytes()
        self.assertIn(environment_hash.encode("ascii"), raw)
        manifest.write_bytes(
            raw.replace(
                environment_hash.encode("ascii"),
                wrong_environment_hash.encode("ascii"),
                1,
            )
        )
        self._seal_with_independent_preimage(manifest, MANIFEST_SELF_HASH_SPEC)

        self.assertEqual(
            [],
            validate_persisted_self_hash(manifest, MANIFEST_SELF_HASH_SPEC),
        )
        errors = validate_persisted_hash_pack(environment, workload, manifest)

        self.assertTrue(
            any("environment.inventory_hash_sha256" in error for error in errors)
        )

    def test_non_ascii_manifest_cross_hash_fails_shape_check_without_exception(self) -> None:
        environment, workload, manifest, expected_hashes = self._prepare_filled_pack()
        manifest_hash = expected_hashes[MANIFEST_SELF_HASH_SPEC.artifact_name]
        workload_hash = expected_hashes[WORKLOAD_SELF_HASH_SPEC.artifact_name]
        self._replace_self_hash_value(
            manifest,
            MANIFEST_SELF_HASH_SPEC,
            manifest_hash,
            MANIFEST_SELF_HASH_SPEC.sentinel,
        )
        raw = manifest.read_bytes()
        self.assertEqual(1, raw.count(workload_hash.encode("ascii")))
        manifest.write_bytes(
            raw.replace(
                workload_hash.encode("ascii"),
                ("Ğ" * 64).encode("utf-8"),
                1,
            )
        )
        self._seal_with_independent_preimage(manifest, MANIFEST_SELF_HASH_SPEC)

        errors = validate_persisted_hash_pack(environment, workload, manifest)

        self.assertTrue(
            any(
                "workload.profile_hash_sha256 must contain 64 uppercase hexadecimal"
                in error
                for error in errors
            )
        )

    def test_duplicate_json_key_is_rejected(self) -> None:
        path = self.root / WORKLOAD_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        duplicate = (
            '"owner": "__REQUIRED_INPUT__:OWNER__",\n'
            '    "owner": "__REQUIRED_INPUT__:OWNER__",'
        )
        path.write_text(
            raw.replace('"owner": "__REQUIRED_INPUT__:OWNER__",', duplicate, 1),
            encoding="utf-8",
        )

        self.assert_error_contains("duplicate key 'owner'")

    def test_non_finite_json_numbers_are_rejected(self) -> None:
        for non_finite in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(non_finite=non_finite):
                self._restore_templates()
                path = self.root / WORKLOAD_TEMPLATE
                raw = path.read_text(encoding="utf-8")
                path.write_text(
                    raw.replace('"__REQUIRED_INPUT__:OWNER__"', non_finite, 1),
                    encoding="utf-8",
                )
                self.assert_error_contains("non-finite JSON number")

    def test_candidate_decision_fields_reject_concrete_defaults(self) -> None:
        cases = {
            "provider": "fixture-provider",
            "edition": "fixture-edition",
            "collation": "fixture-collation",
            "isolation_level": "fixture-isolation",
            "topology": "fixture-topology",
        }
        for field, value in cases.items():
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(WORKLOAD_TEMPLATE)
                payload["candidate_inputs"][field] = value
                self._write_json(WORKLOAD_TEMPLATE, payload)
                self.assert_error_contains(
                    f"candidate_inputs.{field}: forbidden concrete default"
                )

    def test_workload_numeric_values_are_rejected(self) -> None:
        cases: list[tuple[tuple[Any, ...], Any, str]] = [
            (("dataset", "volume_classes", 0, "record_count"), 100, "record_count"),
            (
                ("workload", "concurrency", "concurrent_actor_count"),
                10,
                "concurrent_actor_count",
            ),
            (
                ("workload", "operation_classes", 0, "mix_share"),
                "50%",
                "mix_share",
            ),
            (
                ("performance_acceptance_inputs", 0, "threshold"),
                "300 ms",
                "threshold",
            ),
        ]
        for path_parts, value, field in cases:
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(WORKLOAD_TEMPLATE)
                self._set_nested(payload, path_parts, value)
                self._write_json(WORKLOAD_TEMPLATE, payload)
                self.assert_error_contains("forbidden concrete default")

    def test_measurement_values_and_thresholds_have_no_defaults(self) -> None:
        for field, value in (("measured_value", 1), ("threshold", "1 second")):
            with self.subTest(field=field):
                self._restore_templates()
                payload = self._read_json(EVIDENCE_TEMPLATE)
                payload["measurements"][0][field] = value
                self._write_json(EVIDENCE_TEMPLATE, payload)
                self.assert_error_contains(
                    f"measurements.[0].{field}: forbidden concrete default"
                )

    def test_required_metadata_cannot_be_removed(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        del payload["metadata"]["source_commit"]
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains("metadata: missing required key 'source_commit'")

    def test_tool_version_rows_cannot_be_empty(self) -> None:
        payload = self._read_json(EVIDENCE_TEMPLATE)
        payload["metadata"]["tool_versions"] = []
        self._write_json(EVIDENCE_TEMPLATE, payload)

        self.assert_error_contains(
            "metadata.tool_versions: template array must contain exactly 1 row"
        )

    def test_seed_hash_and_reproduction_fields_are_required(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        del payload["dataset"]["generator"]["seed"]
        del payload["dataset"]["generator"]["dataset_hash_sha256"]
        del payload["dataset"]["generator"]["reproduction_instructions_ref"]
        self._write_json(WORKLOAD_TEMPLATE, payload)

        errors = validate_repository(self.root)
        self.assertTrue(any("missing required key 'seed'" in error for error in errors))
        self.assertTrue(
            any("missing required key 'dataset_hash_sha256'" in error for error in errors)
        )
        self.assertTrue(
            any(
                "missing required key 'reproduction_instructions_ref'" in error
                for error in errors
            )
        )

    def test_unknown_sensitive_shaped_key_is_rejected(self) -> None:
        payload = self._read_json(EVIDENCE_TEMPLATE)
        sensitive_key = "pass" + "word"
        payload["metadata"][sensitive_key] = "__REQUIRED_INPUT__:FORBIDDEN__"
        self._write_json(EVIDENCE_TEMPLATE, payload)

        self.assert_error_contains("sensitive-looking key is forbidden")

    def test_sensitive_shaped_values_are_rejected(self) -> None:
        shaped_values = (
            "gh" + "p_" + ("A" * 32),
            "Server=fixture;" + "Pass" + "word=not-a-real-value;",
            "-----BEGIN " + "PRIVATE KEY-----",
        )
        for shaped_value in shaped_values:
            with self.subTest(value_prefix=shaped_value[:8]):
                self._restore_templates()
                payload = self._read_json(EVIDENCE_TEMPLATE)
                payload["metadata"]["owner"] = shaped_value
                self._write_json(EVIDENCE_TEMPLATE, payload)
                self.assert_error_contains("sensitive-looking value is forbidden")

    def test_invalid_placeholder_marker_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["metadata"]["owner"] = "<OWNER>"
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains("metadata.owner: expected exact placeholder")

    def test_provider_placeholder_swap_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["candidate_inputs"]["provider"] = "__REQUIRED_INPUT__:ADR-003:EDITION__"
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains(
            "candidate_inputs.provider: forbidden concrete default; value must remain "
            "an exact placeholder"
        )

    def test_threshold_placeholder_swap_is_rejected(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["performance_acceptance_inputs"][0]["threshold"] = (
            "__REQUIRED_INPUT__:OPEN-015:THRESHOLD_UNIT__"
        )
        self._write_json(WORKLOAD_TEMPLATE, payload)

        self.assert_error_contains(
            "performance_acceptance_inputs.[0].threshold: forbidden concrete default"
        )

    def test_hash_placeholder_swap_breaks_exact_and_cross_template_invariants(self) -> None:
        payload = self._read_json(WORKLOAD_TEMPLATE)
        payload["evidence"]["workload_profile_hash_sha256"] = (
            "__REQUIRED_INPUT__:DATASET_HASH_SHA256__"
        )
        self._write_json(WORKLOAD_TEMPLATE, payload)

        errors = validate_repository(self.root)
        self.assertTrue(
            any(
                "evidence.workload_profile_hash_sha256: expected exact placeholder"
                in error
                for error in errors
            )
        )
        self.assertTrue(
            any("cross-template invariant 'workload_profile_hash' mismatch" in error for error in errors)
        )

    def test_environment_inventory_requires_every_field(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        line = (
            "| source_commit | __REQUIRED_INPUT__:SOURCE_COMMIT__ | "
            "__REQUIRED_INPUT__:COMMIT_EVIDENCE_REF__ |\n"
        )
        path.write_text(raw.replace(line, ""), encoding="utf-8")

        self.assert_error_contains("missing required field 'source_commit'")

    def test_environment_row_hidden_in_html_comment_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        row = (
            "| owner | __REQUIRED_INPUT__:OWNER__ | "
            "__REQUIRED_INPUT__:OWNER_EVIDENCE_REF__ |"
        )
        path.write_text(raw.replace(row, f"<!--\n{row}\n-->", 1), encoding="utf-8")

        self.assert_error_contains("table rows hidden in HTML comments are forbidden")

    def test_environment_row_in_wrong_table_context_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        row = (
            "| provider_candidate | __REQUIRED_INPUT__:ADR-003:PROVIDER__ | "
            "__REQUIRED_INPUT__:PROVIDER_EVIDENCE_REF__ |\n"
        )
        raw = raw.replace(row, "", 1)
        insertion = "## Kimlik ve güven sınırları\n\n"
        path.write_text(raw.replace(insertion, insertion + row, 1), encoding="utf-8")

        self.assert_error_contains("canonical table structure mismatch")

    def test_environment_tables_hidden_in_markdown_fences_are_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        table_pattern = re.compile(
            r"(^\| Field ID \| Value \| Evidence reference \|\n"
            r"^\|---\|---\|---\|\n(?:^\|.*\|\n)+)",
            flags=re.MULTILINE,
        )
        fenced, replacement_count = table_pattern.subn(
            lambda match: f"```text\n{match.group(1)}```\n",
            raw,
        )
        self.assertEqual(5, replacement_count)
        path.write_text(fenced, encoding="utf-8")

        self.assert_error_contains("table rows hidden in Markdown fences are forbidden")

    def test_environment_table_header_removal_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace("| Field ID | Value | Evidence reference |\n", "", 1),
            encoding="utf-8",
        )

        self.assert_error_contains("canonical table structure mismatch")

    def test_duplicate_environment_headers_and_separators_are_rejected(self) -> None:
        mutations = (
            (
                "| Field ID | Value | Evidence reference |\n",
                "| Field ID | Value | Evidence reference |\n"
                "| Field ID | Value | Evidence reference |\n",
            ),
            (
                "|---|---|---|\n",
                "|---|---|---|\n|---|---|---|\n",
            ),
        )
        for original, replacement in mutations:
            with self.subTest(original=original.strip()):
                self._restore_templates()
                path = self.root / ENVIRONMENT_TEMPLATE
                raw = path.read_text(encoding="utf-8")
                path.write_text(
                    raw.replace(original, replacement, 1),
                    encoding="utf-8",
                )
                self.assert_error_contains("canonical table structure mismatch")

    def test_environment_canonical_hash_rejects_benign_drift(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical environment hash drift")

    def test_environment_decision_fields_reject_concrete_values(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        raw = raw.replace(
            "| topology | __REQUIRED_INPUT__:OPEN-017:TOPOLOGY__ |",
            "| topology | fixture-topology |",
        )
        path.write_text(raw, encoding="utf-8")

        self.assert_error_contains("topology: forbidden concrete default")

    def test_environment_evidence_reference_must_be_placeholder(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        raw = raw.replace(
            "__REQUIRED_INPUT__:OWNER_EVIDENCE_REF__",
            "docs/local-owner-note.txt",
            1,
        )
        path.write_text(raw, encoding="utf-8")

        self.assert_error_contains(
            "owner: evidence reference must remain exact placeholder"
        )

    def test_sensitive_value_in_environment_prose_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        shaped_value = "\nproof: " + "pass" + "word=not-a-real-value\n"
        path.write_text(
            path.read_text(encoding="utf-8") + shaped_value,
            encoding="utf-8",
        )

        self.assert_error_contains("sensitive-looking raw content is forbidden")

    def test_sensitive_key_names_in_prose_are_rejected(self) -> None:
        shaped_names = (
            "sec" + "ret",
            "to" + "ken",
            "creden" + "tial",
            "connection" + "_string",
        )
        for shaped_name in shaped_names:
            with self.subTest(shaped_name=shaped_name):
                self._restore_templates()
                path = self.root / ENVIRONMENT_TEMPLATE
                path.write_text(
                    path.read_text(encoding="utf-8")
                    + f"\n{shaped_name}: REDACTED\n",
                    encoding="utf-8",
                )
                self.assert_error_contains(
                    "sensitive-looking raw key/value name is forbidden"
                )

    def test_sas_like_signature_is_rejected(self) -> None:
        path = self.root / ENVIRONMENT_TEMPLATE
        shaped_query = "?sv=future&" + "sig=" + ("A" * 32)
        path.write_text(
            path.read_text(encoding="utf-8") + f"\n{shaped_query}\n",
            encoding="utf-8",
        )

        self.assert_error_contains("sensitive-looking raw content is forbidden")

    def test_decision_assignment_drift_shapes_are_rejected(self) -> None:
        fields = (
            "provider",
            "edition",
            "collation",
            "isolation_level",
            "topology",
            "threshold",
        )
        for field in fields:
            with self.subTest(field=field):
                self._restore_templates()
                path = self.root / README_TEMPLATE
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{field}=fixture-value\n",
                    encoding="utf-8",
                )
                self.assert_error_contains(
                    "decision/default assignment drift is forbidden"
                )

    def test_closed_gate_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\ngates=CLOSED\n",
            encoding="utf-8",
        )

        self.assert_error_contains("closed-gate drift is forbidden")

    def test_implementation_go_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace(
                "Implementation state: `NO-GO / BLOCKED`",
                "Implementation state: `GO`",
                1,
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("implementation GO drift is forbidden")

    def test_readme_no_go_gate_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        raw = path.read_text(encoding="utf-8")
        path.write_text(
            raw.replace(
                "- Implementation state: `NO-GO / BLOCKED`",
                "- Implementation state: `GO`",
                1,
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("required decision-boundary line drifted")

    def test_readme_provider_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        provider_name = "SQL" + " Server"
        path.write_text(
            path.read_text(encoding="utf-8") + f"\nChosen provider: {provider_name}\n",
            encoding="utf-8",
        )

        self.assert_error_contains("forbidden concrete default detected")

    def test_readme_benign_hash_drift_is_rejected(self) -> None:
        path = self.root / README_TEMPLATE
        path.write_text(
            path.read_text(encoding="utf-8") + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical README hash drift")

    def test_extra_sensitive_file_is_rejected_and_scanned(self) -> None:
        path = self.root / DOCS_RELATIVE_ROOT / "unreviewed-input.txt"
        shaped_value = "gh" + "p_" + ("A" * 32)
        path.write_text(shaped_value, encoding="utf-8")

        errors = validate_repository(self.root)
        self.assertTrue(any("unreviewed file is forbidden" in error for error in errors))
        self.assertTrue(
            any("sensitive-looking raw content is forbidden" in error for error in errors)
        )

    def assert_error_contains(self, expected: str) -> None:
        errors = validate_repository(self.root)
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected an error containing {expected!r}, got: {errors!r}",
        )

    def _prepare_filled_pack(
        self,
    ) -> tuple[Path, Path, Path, dict[str, str]]:
        environment = self.root / ENVIRONMENT_TEMPLATE
        workload = self.root / WORKLOAD_TEMPLATE
        manifest = self.root / EVIDENCE_TEMPLATE
        for path in (environment, workload, manifest):
            raw = path.read_text(encoding="utf-8")
            self.assertIn("__REQUIRED_INPUT__:OWNER__", raw)
            path.write_text(
                raw.replace("__REQUIRED_INPUT__:OWNER__", "fixture-owner", 1),
                encoding="utf-8",
            )

        environment_hash = self._seal_with_independent_preimage(
            environment,
            ENVIRONMENT_SELF_HASH_SPEC,
        )
        workload_hash = self._seal_with_independent_preimage(
            workload,
            WORKLOAD_SELF_HASH_SPEC,
        )

        manifest_raw = manifest.read_bytes()
        self.assertEqual(
            1,
            manifest_raw.count(ENVIRONMENT_SELF_HASH_SPEC.sentinel.encode("ascii")),
        )
        self.assertEqual(
            1,
            manifest_raw.count(WORKLOAD_SELF_HASH_SPEC.sentinel.encode("ascii")),
        )
        manifest.write_bytes(
            manifest_raw.replace(
                ENVIRONMENT_SELF_HASH_SPEC.sentinel.encode("ascii"),
                environment_hash.encode("ascii"),
                1,
            ).replace(
                WORKLOAD_SELF_HASH_SPEC.sentinel.encode("ascii"),
                workload_hash.encode("ascii"),
                1,
            )
        )
        manifest_hash = self._seal_with_independent_preimage(
            manifest,
            MANIFEST_SELF_HASH_SPEC,
        )
        return (
            environment,
            workload,
            manifest,
            {
                ENVIRONMENT_SELF_HASH_SPEC.artifact_name: environment_hash,
                WORKLOAD_SELF_HASH_SPEC.artifact_name: workload_hash,
                MANIFEST_SELF_HASH_SPEC.artifact_name: manifest_hash,
            },
        )

    def _seal_with_independent_preimage(self, path: Path, spec: SelfHashSpec) -> str:
        computation, errors = compute_self_hash_sha256(path, spec)
        self.assertEqual([], errors)
        self.assertIsNotNone(computation)
        assert computation is not None
        self.assertEqual(spec.sentinel, computation.stored_value)

        expected = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        self.assertEqual(expected, computation.computed_sha256)
        self._replace_self_hash_value(path, spec, spec.sentinel, expected)
        return expected

    def _replace_self_hash_value(
        self,
        path: Path,
        spec: SelfHashSpec,
        old_value: str,
        new_value: str,
    ) -> None:
        raw = path.read_bytes()
        if spec.json_path is None:
            old = f"| {spec.field_name} | {old_value} |".encode("utf-8")
            new = f"| {spec.field_name} | {new_value} |".encode("utf-8")
        else:
            old = f'"{spec.field_name}": "{old_value}"'.encode("utf-8")
            new = f'"{spec.field_name}": "{new_value}"'.encode("utf-8")
        self.assertEqual(1, raw.count(old))
        path.write_bytes(raw.replace(old, new, 1))

    def _restore_templates(self) -> None:
        destination = self.root / DOCS_RELATIVE_ROOT
        shutil.rmtree(destination)
        shutil.copytree(REPOSITORY_ROOT / DOCS_RELATIVE_ROOT, destination)

    def _read_json(self, relative_path: Path) -> dict[str, Any]:
        return json.loads((self.root / relative_path).read_text(encoding="utf-8"))

    def _write_json(self, relative_path: Path, payload: dict[str, Any]) -> None:
        (self.root / relative_path).write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    @staticmethod
    def _set_nested(payload: Any, path_parts: tuple[Any, ...], value: Any) -> None:
        target = payload
        for part in path_parts[:-1]:
            target = target[part]
        target[path_parts[-1]] = value


if __name__ == "__main__":
    unittest.main()
