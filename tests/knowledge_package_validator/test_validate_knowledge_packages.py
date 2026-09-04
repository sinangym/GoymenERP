from __future__ import annotations

import hashlib
import importlib.util
import json
import stat
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_knowledge_packages.py"
MODULE_NAME = "goymen_knowledge_package_validator"
MODULE_SPEC = importlib.util.spec_from_file_location(MODULE_NAME, VALIDATOR_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load knowledge package validator from {VALIDATOR_PATH}")
VALIDATOR_MODULE = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_NAME] = VALIDATOR_MODULE
MODULE_SPEC.loader.exec_module(VALIDATOR_MODULE)

validate_repository = VALIDATOR_MODULE.validate_repository
validate_repository_with_statistics = (
    VALIDATOR_MODULE.validate_repository_with_statistics
)

RUN_ID = "GOYMENERP-KNOWLEDGE-V4-01"
MANIFESTED_FILES = (
    "ATTACHMENT_INDEX.md",
    "CONFLICT_CANDIDATES.md",
    "EXPORT_REPORT.md",
    "KNOWLEDGE.md",
    "NORMALIZED_ITEMS.ndjson",
)
SCHEMA_ROOT = REPOSITORY_ROOT / "docs/knowledge/schema"


class KnowledgePackageValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        self.knowledge_root = self.root / "docs/knowledge"
        self.knowledge_root.mkdir(parents=True)
        self.registry_path = self.knowledge_root / "SOURCE_REGISTRY.json"
        self.registry = self._base_registry()
        self._write_json(self.registry_path, self.registry)

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_current_repository_contract_passes(self) -> None:
        errors, statistics = validate_repository_with_statistics(REPOSITORY_ROOT)

        self.assertEqual([], errors)
        self.assertIn(statistics.registry_state, {"collecting", "ready"})

    def test_collecting_registry_accepts_an_empty_inbox(self) -> None:
        (self.knowledge_root / "inbox").mkdir()
        (self.knowledge_root / "inbox/.gitkeep").write_bytes(b"")

        errors, statistics = validate_repository_with_statistics(self.root)

        self.assertEqual([], errors)
        self.assertEqual(0, statistics.package_count)
        self.assertEqual(0, statistics.item_count)

    def test_source_manifest_schema_matches_validator_contract(self) -> None:
        schema = self._read_json(SCHEMA_ROOT / "source-manifest-v1.schema.json")
        properties = schema["properties"]

        self.assertEqual(set(VALIDATOR_MODULE.MANIFEST_FIELDS), set(schema["required"]))
        self.assertEqual(set(VALIDATOR_MODULE.MANIFEST_FIELDS), set(properties))
        self.assertEqual(set(VALIDATOR_MODULE.PROVIDERS), set(properties["provider"]["enum"]))
        self.assertEqual(
            set(VALIDATOR_MODULE.COVERAGE_VALUES), set(properties["coverage"]["enum"])
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.MANIFEST_STATUS_VALUES),
            set(properties["status"]["enum"]),
        )
        self.assertEqual(
            VALIDATOR_MODULE.RUN_COMPONENT_PATTERN.pattern,
            properties["run_id"]["pattern"],
        )
        self.assertEqual(
            VALIDATOR_MODULE.SOURCE_ID_PATTERN.pattern,
            properties["source_id"]["pattern"],
        )
        self.assertEqual(
            VALIDATOR_MODULE.SHA256_PATTERN.pattern,
            properties["source_snapshot_sha256"]["pattern"],
        )
        self.assertEqual(
            VALIDATOR_MODULE.SHA256_PATTERN.pattern,
            properties["content_fingerprint_sha256"]["pattern"],
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.MANIFEST_FILE_FIELDS),
            set(properties["files"]["items"]["required"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.MANIFEST_FILE_FIELDS),
            set(properties["files"]["items"]["properties"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.MANIFEST_COUNT_FIELDS),
            set(properties["counts"]["required"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.MANIFEST_COUNT_FIELDS),
            set(properties["counts"]["properties"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.PRIVACY_REVIEW_FIELDS),
            set(properties["privacy_review"]["required"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.PRIVACY_REVIEW_FIELDS),
            set(properties["privacy_review"]["properties"]),
        )

    def test_normalized_item_schema_matches_validator_contract(self) -> None:
        schema = self._read_json(SCHEMA_ROOT / "normalized-item-v1.schema.json")
        properties = schema["properties"]

        self.assertEqual(set(VALIDATOR_MODULE.ITEM_FIELDS), set(schema["required"]))
        self.assertEqual(set(VALIDATOR_MODULE.ITEM_FIELDS), set(properties))
        enum_pairs = (
            ("type", VALIDATOR_MODULE.ITEM_TYPE_VALUES),
            ("current_or_target", VALIDATOR_MODULE.CURRENT_OR_TARGET_VALUES),
            ("status", VALIDATOR_MODULE.ITEM_STATUS_VALUES),
            ("priority", VALIDATOR_MODULE.PRIORITY_VALUES),
            ("source_provider", VALIDATOR_MODULE.PROVIDERS),
            ("confidence", VALIDATOR_MODULE.CONFIDENCE_VALUES),
        )
        for field, expected in enum_pairs:
            with self.subTest(field=field):
                self.assertEqual(set(expected), set(properties[field]["enum"]))
        self.assertEqual(
            VALIDATOR_MODULE.ITEM_ID_PATTERN.pattern, properties["id"]["pattern"]
        )
        self.assertEqual(
            VALIDATOR_MODULE.SOURCE_ID_PATTERN.pattern,
            properties["source_id"]["pattern"],
        )
        source_date_variants = properties["source_date"]["oneOf"]
        self.assertEqual({"string", "null"}, {entry["type"] for entry in source_date_variants})
        source_date_string = next(
            entry for entry in source_date_variants if entry["type"] == "string"
        )
        self.assertEqual(
            VALIDATOR_MODULE.SOURCE_DATE_PATTERN.pattern,
            source_date_string["pattern"],
        )
        relations = schema["$defs"]["relations"]
        self.assertEqual(VALIDATOR_MODULE.ITEM_ID_PATTERN.pattern, relations["items"]["pattern"])
        for field in ("supersedes", "conflicts_with", "related_items"):
            self.assertEqual("#/$defs/relations", properties[field]["$ref"])

    def test_source_registry_schema_matches_validator_contract(self) -> None:
        schema = self._read_json(SCHEMA_ROOT / "source-registry-v1.schema.json")
        properties = schema["properties"]
        expected_source = schema["$defs"]["expected_source"]
        source = schema["$defs"]["source"]
        attestation = schema["$defs"]["source_inventory_attestation"]

        self.assertEqual(set(VALIDATOR_MODULE.REGISTRY_FIELDS), set(schema["required"]))
        self.assertEqual(set(VALIDATOR_MODULE.REGISTRY_FIELDS), set(properties))
        self.assertEqual(VALIDATOR_MODULE.SCHEMA_VERSION, properties["schema_version"]["const"])
        self.assertEqual(VALIDATOR_MODULE.RUN_ID, properties["run_id"]["const"])
        self.assertEqual(
            VALIDATOR_MODULE.CANONICAL_REPOSITORY,
            properties["canonical_repository"]["const"],
        )
        self.assertEqual(VALIDATOR_MODULE.DEFAULT_BRANCH, properties["default_branch"]["const"])
        self.assertEqual({"collecting", "ready"}, set(properties["state"]["enum"]))
        self.assertEqual(
            list(VALIDATOR_MODULE.PROVIDERS),
            [entry["const"] for entry in properties["required_providers"]["prefixItems"]],
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.EXPECTED_SOURCE_FIELDS),
            set(expected_source["required"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.EXPECTED_SOURCE_FIELDS),
            set(expected_source["properties"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.REGISTRY_SOURCE_FIELDS), set(source["required"])
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.REGISTRY_SOURCE_FIELDS), set(source["properties"])
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.PROVIDERS),
            set(expected_source["properties"]["provider"]["enum"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.EXPECTED_SOURCE_STATUS_VALUES),
            set(expected_source["properties"]["status"]["enum"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.PROVIDERS),
            set(source["properties"]["provider"]["enum"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.REGISTRY_SOURCE_STATUS_VALUES),
            set(source["properties"]["status"]["enum"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.SOURCE_INVENTORY_ATTESTATION_FIELDS),
            set(attestation["required"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.SOURCE_INVENTORY_ATTESTATION_FIELDS),
            set(attestation["properties"]),
        )
        self.assertEqual(
            set(VALIDATOR_MODULE.INVENTORY_ATTESTATION_STATUS_VALUES),
            set(attestation["properties"]["status"]["enum"]),
        )
        attested_at_string = next(
            entry
            for entry in attestation["properties"]["attested_at"]["oneOf"]
            if entry["type"] == "string"
        )
        self.assertEqual(
            VALIDATOR_MODULE.ATTESTED_AT_PATTERN.pattern,
            attested_at_string["pattern"],
        )

    def test_valid_source_package_passes_and_reports_statistics(self) -> None:
        self._create_package("codex")

        errors, statistics = validate_repository_with_statistics(self.root)

        self.assertEqual([], errors)
        self.assertEqual("collecting", statistics.registry_state)
        self.assertEqual(1, statistics.package_count)
        self.assertEqual(1, statistics.item_count)

    def test_missing_and_unexpected_package_files_are_rejected(self) -> None:
        package = self._create_package("codex")
        (package / "KNOWLEDGE.md").unlink()
        (package / "raw-transcript.txt").write_text("raw\n", encoding="utf-8")

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "is missing required files: KNOWLEDGE.md")
        self.assert_error_contains(errors, "has unexpected files: raw-transcript.txt")

    def test_manifest_rejects_duplicate_json_keys(self) -> None:
        package = self._create_package("codex")
        manifest_path = package / "SOURCE_MANIFEST.json"
        text = manifest_path.read_text(encoding="utf-8")
        manifest_path.write_text(
            text.replace(
                '"schema_version": "1.0",',
                '"schema_version": "1.0",\n  "schema_version": "1.0",',
                1,
            ),
            encoding="utf-8",
        )

        self.assert_error_contains(
            validate_repository(self.root), "duplicate JSON object key 'schema_version'"
        )

    def test_ndjson_rejects_duplicate_keys_and_non_finite_numbers(self) -> None:
        cases = (
            (
                '{"schema_version":"1.0","schema_version":"1.0"}\n',
                "duplicate JSON object key 'schema_version'",
            ),
            ('{"value":NaN}\n', "non-standard JSON numeric constant 'NaN'"),
        )
        for content, expected in cases:
            with self.subTest(expected=expected):
                package = self._create_package("codex")
                (package / "NORMALIZED_ITEMS.ndjson").write_text(
                    content, encoding="utf-8"
                )
                self._refresh_manifest(package)

                self.assert_error_contains(validate_repository(self.root), expected)

                self._remove_package(package)

    def test_invalid_utf8_and_bom_are_rejected(self) -> None:
        cases = (
            (b"\xff\n", "is not valid UTF-8"),
            (b"\xef\xbb\xbf# Knowledge\n", "must not contain a UTF-8 BOM"),
        )
        for content, expected in cases:
            with self.subTest(expected=expected):
                package = self._create_package("codex")
                (package / "KNOWLEDGE.md").write_bytes(content)
                self._refresh_manifest(package)

                self.assert_error_contains(validate_repository(self.root), expected)

                self._remove_package(package)

    def test_manifest_identity_must_match_path(self) -> None:
        cases = (
            ("run_id", "OTHER-RUN-001", "run_id does not match its directory"),
            ("provider", "claude", "provider does not match its directory"),
            ("source_id", "codex-other-abcdef123456", "source_id does not match its directory"),
        )
        for field, value, expected in cases:
            with self.subTest(field=field):
                package = self._create_package("codex")
                manifest = self._read_json(package / "SOURCE_MANIFEST.json")
                manifest[field] = value
                self._write_json(package / "SOURCE_MANIFEST.json", manifest)

                self.assert_error_contains(validate_repository(self.root), expected)

                self._remove_package(package)

    def test_manifest_requires_exact_fields_and_file_allowlist(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        del manifest["coverage"]
        manifest["extra"] = True
        manifest["files"][0]["path"] = "SOURCE_MANIFEST.json"
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "is missing fields: coverage")
        self.assert_error_contains(errors, "has unexpected fields: extra")
        self.assert_error_contains(errors, "is not an allowed package filename")

    def test_manifest_hash_byte_count_and_fingerprint_drift_are_rejected(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        for file_entry in manifest["files"]:
            if file_entry["path"] == "KNOWLEDGE.md":
                file_entry["bytes"] += 1
                file_entry["sha256"] = "0" * 64
        manifest["content_fingerprint_sha256"] = "1" * 64
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "byte count does not match manifest")
        self.assert_error_contains(errors, "SHA-256 does not match manifest")
        self.assert_error_contains(errors, "content fingerprint does not match")

    def test_source_snapshot_hash_is_required_lowercase_and_independent(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")

        self.assertNotEqual(
            manifest["source_snapshot_sha256"],
            manifest["content_fingerprint_sha256"],
        )

        del manifest["source_snapshot_sha256"]
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)
        self.assert_error_contains(
            validate_repository(self.root), "is missing fields: source_snapshot_sha256"
        )

        manifest["source_snapshot_sha256"] = "A" * 64
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)
        self.assert_error_contains(
            validate_repository(self.root),
            "source_snapshot_sha256 must be a lowercase SHA-256 digest",
        )

    def test_source_id_suffix_must_derive_from_source_snapshot(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        manifest["source_snapshot_sha256"] = "1" * 64
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

        self.assert_error_contains(
            validate_repository(self.root),
            "source_id must end with the first 12 characters of "
            "source_snapshot_sha256",
        )

    def test_source_id_must_use_its_provider_prefix(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        source_id = "conversation-" + manifest["source_snapshot_sha256"][:12]
        renamed_package = package.with_name(source_id)
        package.rename(renamed_package)
        manifest["source_id"] = source_id
        self._write_json(renamed_package / "SOURCE_MANIFEST.json", manifest)
        item = self._valid_item("codex")
        item["source_id"] = source_id
        item["id"] = self._deterministic_item_id(item)
        self._write_items(renamed_package, [item])

        self.assert_error_contains(
            validate_repository(self.root),
            "source_id must start with provider prefix 'codex-'",
        )

    def test_item_requires_exact_fields_and_supported_enums(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        del item["module"]
        item["extra"] = "drift"
        item["type"] = "unknown_type"
        item["status"] = "DONE"
        item["priority"] = "urgent"
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "is missing fields: module")
        self.assert_error_contains(errors, "has unexpected fields: extra")
        self.assert_error_contains(errors, "type has unsupported value")
        self.assert_error_contains(errors, "status has unsupported value")
        self.assert_error_contains(errors, "priority has unsupported value")

    def test_item_identity_must_match_manifest(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        item["source_id"] = "codex-other-abcdef123456"
        item["source_provider"] = "claude"
        item["conversation_id"] = "different-conversation"
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "source_id does not match its manifest")
        self.assert_error_contains(errors, "source_provider does not match its manifest")
        self.assert_error_contains(errors, "conversation_id does not match its manifest")

    def test_item_id_must_derive_from_exact_identity_fields(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        original_id = item["id"]
        item["statement"] = item["statement"] + " Değiştirildi."
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assertEqual(original_id, item["id"])
        self.assert_error_contains(
            errors, ".id does not match its deterministic content identity"
        )

    def test_non_identity_item_fields_do_not_change_deterministic_id(self) -> None:
        item = self._valid_item("codex")
        original_id = item["id"]
        item["priority"] = "P3"
        item["confidence"] = "MEDIUM"
        item["status"] = "PROPOSED"

        self.assertEqual(original_id, self._deterministic_item_id(item))

    def test_invalid_item_id_and_source_date_are_rejected(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        item["id"] = "bad-id"
        item["source_date"] = "31/08/2026"
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, ".id has an invalid format")
        self.assert_error_contains(errors, "must be an ISO-8601 date/date-time or null")

    def test_item_id_must_be_exactly_24_uppercase_hex_characters(self) -> None:
        pattern = VALIDATOR_MODULE.ITEM_ID_PATTERN

        self.assertIsNone(pattern.fullmatch("KN-" + "A" * 23))
        self.assertIsNotNone(pattern.fullmatch("KN-" + "A" * 24))
        self.assertIsNone(pattern.fullmatch("KN-" + "A" * 25))
        self.assertIsNone(pattern.fullmatch("KN-" + "a" * 24))
        self.assertIsNone(pattern.fullmatch("KN-" + "G" * 24))
        self.assertIsNone(pattern.fullmatch("KN-" + "A" * 12 + "-" + "B" * 11))

    def test_duplicate_item_ids_across_packages_are_rejected(self) -> None:
        duplicate_id = "KN-" + "A" * 24
        self._create_package("codex", item_id=duplicate_id)
        self._create_package("claude", item_id=duplicate_id)

        self.assert_error_contains(
            validate_repository(self.root),
            f"Duplicate knowledge item id {duplicate_id!r}",
        )

    def test_duplicate_content_fingerprints_are_rejected(self) -> None:
        first = self._create_package("codex")
        second = self._create_package("claude")
        first_manifest = self._read_json(first / "SOURCE_MANIFEST.json")
        second_manifest = self._read_json(second / "SOURCE_MANIFEST.json")
        second_manifest["content_fingerprint_sha256"] = first_manifest[
            "content_fingerprint_sha256"
        ]
        self._write_json(second / "SOURCE_MANIFEST.json", second_manifest)

        self.assert_error_contains(
            validate_repository(self.root), "duplicate content fingerprints"
        )

    def test_unknown_and_self_relationships_are_rejected(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        unknown_id = "KN-" + "B" * 24
        item["supersedes"] = [item["id"]]
        item["related_items"] = [unknown_id]
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "has a self-reference")
        self.assert_error_contains(errors, f"references unknown item {unknown_id!r}")

    def test_conflict_requires_a_real_conflict_reference(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        item["status"] = "CONFLICT"
        self._write_items(package, [item])

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "with CONFLICT status must name conflicts_with")

    def test_inferred_item_cannot_claim_high_confidence(self) -> None:
        package = self._create_package("codex")
        item = self._valid_item("codex")
        item["status"] = "INFERRED"
        item["confidence"] = "HIGH"
        self._write_items(package, [item])

        self.assert_error_contains(
            validate_repository(self.root),
            "with INFERRED status cannot have HIGH confidence",
        )

    def test_manifest_item_and_conflict_counts_must_match(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        manifest["counts"]["items"] = 2
        manifest["counts"]["conflicts"] = 1
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "item count does not match manifest")
        self.assert_error_contains(errors, "conflict count does not match manifest")

    def test_attachment_index_zero_count_has_no_entry_headings(self) -> None:
        package = self._create_package("codex")

        self.assertEqual([], validate_repository(self.root))

        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\n"
            "Attachment-Count: 0\n\n"
            "## ATTACHMENT 1\n"
            "Unexpected entry.\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assert_error_contains(
            validate_repository(self.root),
            "attachment headings must be exactly contiguous from 1 through 0",
        )

    def test_attachment_index_requires_exact_top_count_metadata(self) -> None:
        package = self._create_package("codex")
        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\nNo declared count.\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assert_error_contains(
            validate_repository(self.root),
            "must contain exactly one Attachment-Count metadata line within the first 20 lines",
        )

        for content in (
            "# Attachment index\nAttachment-Count: 0\nAttachment-Count: 0\n",
            "# Attachment index\n" + "filler\n" * 20 + "Attachment-Count: 0\n",
        ):
            with self.subTest(content=content):
                (package / "ATTACHMENT_INDEX.md").write_text(
                    content, encoding="utf-8"
                )
                self._refresh_manifest(package)
                self.assert_error_contains(
                    validate_repository(self.root),
                    "must contain exactly one Attachment-Count metadata line within the first 20 lines",
                )

        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\nAttachment-Count: 02\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)
        self.assert_error_contains(
            validate_repository(self.root),
            "Attachment-Count must be an exact non-negative integer",
        )

    def test_attachment_index_count_matches_manifest_and_contiguous_headings(self) -> None:
        package = self._create_package("codex")
        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\n"
            "Attachment-Count: 2\n\n"
            "## ATTACHMENT 1\nFirst attachment.\n\n"
            "## ATTACHMENT 2\nSecond attachment.\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assert_error_contains(
            validate_repository(self.root), "attachment count does not match manifest"
        )

        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        manifest["counts"]["attachments"] = 2
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)
        self.assertEqual([], validate_repository(self.root))

        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\n"
            "Attachment-Count: 2\n\n"
            "## ATTACHMENT 1\nFirst attachment.\n\n"
            "## ATTACHMENT 1\nDuplicate attachment.\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)
        errors = validate_repository(self.root)
        self.assert_error_contains(errors, "contains duplicate attachment headings: 1")
        self.assert_error_contains(
            errors, "attachment headings must be exactly contiguous from 1 through 2"
        )

        for headings in (
            (1, 3),
            (1, 2, 3),
        ):
            with self.subTest(headings=headings):
                heading_text = "\n".join(
                    f"## ATTACHMENT {number}\nAttachment {number}."
                    for number in headings
                )
                (package / "ATTACHMENT_INDEX.md").write_text(
                    "# Attachment index\n\nAttachment-Count: 2\n\n"
                    + heading_text
                    + "\n",
                    encoding="utf-8",
                )
                self._refresh_manifest(package)
                self.assert_error_contains(
                    validate_repository(self.root),
                    "attachment headings must be exactly contiguous from 1 through 2",
                )

        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\nAttachment-Count: 1\n\n"
            "## ATTACHMENT 0\nInvalid attachment.\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)
        self.assert_error_contains(
            validate_repository(self.root),
            "attachment heading must have exact form",
        )

    def test_manifest_requires_passing_privacy_review(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        manifest["privacy_review"] = {
            "secret_scan": "failed",
            "pii_review": "failed",
            "redactions": -1,
        }
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "privacy_review.secret_scan must be 'passed'")
        self.assert_error_contains(errors, "privacy_review.pii_review must be 'passed'")
        self.assert_error_contains(
            errors, "privacy_review.redactions must be a non-negative integer"
        )

    def test_high_confidence_secret_patterns_are_rejected(self) -> None:
        cases = (
            ("-----BEGIN " + "PRIVATE KEY-----\n", "possible private key"),
            ("sk-" + "abcdefghijklmnopqrstuvwxyz123456\n", "possible OpenAI token"),
            ("ghp_" + "abcdefghijklmnopqrstuvwxyz123456\n", "possible GitHub token"),
            ("AKIA" + "ABCDEFGHIJKLMNOP\n", "possible AWS access key"),
            (
                "Authorization: " + "Bearer " + "abcdefghijklmnopqrstuvwxyz123456\n",
                "possible Bearer credential",
            ),
            ("xoxb-" + "1234567890-abcdefghij\n", "possible Slack token"),
            (
                "Server=db;Pass" + "word=ActualSecretValue123;\n",
                "possible password credential",
            ),
        )
        for content, expected in cases:
            with self.subTest(expected=expected):
                package = self._create_package("codex")
                (package / "KNOWLEDGE.md").write_text(content, encoding="utf-8")
                self._refresh_manifest(package)

                self.assert_error_contains(validate_repository(self.root), expected)

                self._remove_package(package)

    def test_redacted_and_placeholder_credentials_are_allowed(self) -> None:
        package = self._create_package("codex")
        (package / "KNOWLEDGE.md").write_text(
            "Password=[REDACTED]\n"
            "Password=[REDACTED:database:1234abcd]\n"
            "Password=${GOYMEN_TEST_PASSWORD}\n"
            "api_key={{ GOYMEN_TEST_API_KEY }}\n"
            "client_secret=<GOYMEN_TEST_SECRET>\n"
            "access_token=YOUR_ACCESS_TOKEN\n"
            "pwd=placeholder\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assertEqual([], validate_repository(self.root))

    def test_short_explicit_password_is_rejected(self) -> None:
        package = self._create_package("codex")
        (package / "KNOWLEDGE.md").write_text(
            "Password=" + "abc123\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assert_error_contains(
            validate_repository(self.root), "possible password credential"
        )

    def test_placeholder_exemptions_are_anchored_to_the_entire_value(self) -> None:
        package = self._create_package("codex")
        (package / "KNOWLEDGE.md").write_text(
            "Password=prefix${GOYMEN_TEST_PASSWORD}\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assert_error_contains(
            validate_repository(self.root), "possible password credential"
        )

    def test_high_confidence_pii_patterns_are_rejected(self) -> None:
        bban = "00061005197" + "86457841326"
        rearranged = bban + "2927" + "00"
        check_digits = 98 - (int(rearranged) % 97)
        iban = "TR" + f"{check_digits:02d}" + bban
        cases = (
            (
                "Contact: " + "sinan" + "@" + "private-domain.test\n",
                "possible personal email address",
            ),
            (
                "Mobile: " + "+90 555" + " 123 45 67\n",
                "possible Turkish mobile phone number",
            ),
            ("Identity: " + "100000" + "00146\n", "possible valid TCKN"),
            ("Bank: " + iban + "\n", "possible valid TR IBAN"),
        )
        for content, expected in cases:
            with self.subTest(expected=expected):
                package = self._create_package("codex")
                (package / "KNOWLEDGE.md").write_text(content, encoding="utf-8")
                self._refresh_manifest(package)

                self.assert_error_contains(validate_repository(self.root), expected)

                self._remove_package(package)

    def test_documentation_examples_and_hashes_are_not_reported_as_pii(self) -> None:
        package = self._create_package("codex")
        (package / "KNOWLEDGE.md").write_text(
            "Contact: " + "user" + "@" + "example.com\n"
            "Digest: " + "0" * 64 + "\n",
            encoding="utf-8",
        )
        self._refresh_manifest(package)

        self.assertEqual([], validate_repository(self.root))

    def test_clean_consolidated_outputs_are_scanned_and_allowed(self) -> None:
        output = self.knowledge_root / "consolidated" / RUN_ID / "nested"
        output.mkdir(parents=True)
        (output / "REPORT.md").write_text(
            "# Verified consolidated report\n", encoding="utf-8"
        )

        self.assertEqual([], validate_repository(self.root))

    def test_consolidated_output_secret_is_rejected(self) -> None:
        output = self.knowledge_root / "consolidated" / RUN_ID
        output.mkdir(parents=True)
        (output / "REPORT.md").write_text(
            "Authorization: " + "Bearer " + "abcdefghijklmnopqrstuvwxyz123456\n",
            encoding="utf-8",
        )

        self.assert_error_contains(
            validate_repository(self.root),
            "consolidated output",
        )
        self.assert_error_contains(
            validate_repository(self.root),
            "possible Bearer credential",
        )

    def test_consolidated_output_symlink_is_rejected(self) -> None:
        output = self.knowledge_root / "consolidated" / RUN_ID
        output.mkdir(parents=True)
        target = self.root / "outside.md"
        target.write_text("outside\n", encoding="utf-8")
        link = output / "LINK.md"
        try:
            link.symlink_to(target)
        except OSError as exc:
            self.skipTest(f"File symlinks are unavailable: {exc}")

        self.assert_error_contains(
            validate_repository(self.root),
            "Consolidated output must not be a symbolic link",
        )

    def test_windows_reparse_attribute_is_rejected_even_without_symlink_mode(self) -> None:
        class WindowsReparseMetadata:
            st_mode = stat.S_IFREG
            st_file_attributes = 0x400

        self.assertTrue(
            VALIDATOR_MODULE._is_link_or_reparse(WindowsReparseMetadata())
        )

    def test_registry_import_must_resolve_and_match_package(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        self.registry["expected_sources"] = [
            {
                "provider": "codex",
                "conversation_id": manifest["conversation_id"],
                "required": True,
                "status": "imported",
                "source_id": manifest["source_id"],
                "reason": None,
            }
        ]
        self.registry["sources"] = [
            self._registry_source(package, manifest, status="validated")
        ]
        self._write_json(self.registry_path, self.registry)

        self.assertEqual([], validate_repository(self.root))

        self.registry["sources"][0]["conversation_id"] = "wrong-conversation"
        self._write_json(self.registry_path, self.registry)
        self.assert_error_contains(
            validate_repository(self.root), "conversation_id does not match"
        )

    def test_expected_source_status_payloads_are_exact(self) -> None:
        invalid_payloads = (
            (
                {
                    "provider": "codex",
                    "conversation_id": "codex-conversation-1",
                    "required": True,
                    "status": "pending",
                    "source_id": "codex-conversation-abcdef123456",
                    "reason": "still waiting",
                },
                "source_id must be null when status is 'pending'",
            ),
            (
                {
                    "provider": "codex",
                    "conversation_id": "codex-conversation-1",
                    "required": True,
                    "status": "imported",
                    "source_id": None,
                    "reason": "already imported",
                },
                "source_id is required when status is 'imported'",
            ),
            (
                {
                    "provider": "codex",
                    "conversation_id": "codex-conversation-1",
                    "required": False,
                    "status": "unavailable",
                    "source_id": "codex-conversation-abcdef123456",
                    "reason": None,
                },
                "source_id must be null when status is 'unavailable'",
            ),
        )
        for payload, expected in invalid_payloads:
            with self.subTest(status=payload["status"]):
                self.registry["expected_sources"] = [payload]
                self._write_json(self.registry_path, self.registry)

                errors = validate_repository(self.root)

                self.assert_error_contains(errors, expected)
                if payload["status"] == "imported":
                    self.assert_error_contains(
                        errors, "reason must be null when status is 'imported'"
                    )
                if payload["status"] == "unavailable":
                    self.assert_error_contains(
                        errors, "reason is required when status is 'unavailable'"
                    )

    def test_inventory_attestation_payload_and_ready_gate_are_enforced(self) -> None:
        self.registry["source_inventory_attestation"] = {
            "status": "pending",
            "attested_by": "Sinan",
            "attested_at": "2026-09-03",
            "evidence_locator": "message",
            "scope_note": "claimed complete",
        }
        self.registry["state"] = "ready"
        self._write_json(self.registry_path, self.registry)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "attested_by must be null while status is 'pending'")
        self.assert_error_contains(
            errors,
            "source_inventory_attestation status must be 'user_confirmed_complete'",
        )

    def test_ready_registry_requires_every_provider_and_required_source(self) -> None:
        package = self._create_package("codex")
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        self.registry["state"] = "ready"
        self.registry["expected_sources"] = [
            {
                "provider": "codex",
                "conversation_id": manifest["conversation_id"],
                "required": True,
                "status": "pending",
                "source_id": None,
                "reason": None,
            }
        ]
        self.registry["sources"] = [
            self._registry_source(package, manifest, status="validated")
        ]
        self._write_json(self.registry_path, self.registry)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "no integrated source for provider 'codex'")
        self.assert_error_contains(errors, "no integrated source for provider 'claude'")
        self.assert_error_contains(errors, "no integrated source for provider 'chatgpt'")
        self.assert_error_contains(errors, "required expected_sources[0] must be imported")
        self.assert_error_contains(
            errors,
            "required expected source for provider 'claude'",
        )
        self.assert_error_contains(
            errors,
            "required expected source for provider 'chatgpt'",
        )

    def test_ready_registry_rejects_validated_source_state(self) -> None:
        self._configure_ready_registry()
        self.registry["sources"][0]["status"] = "validated"
        self._write_json(self.registry_path, self.registry)
        self._create_ready_consolidated()

        self.assert_error_contains(
            validate_repository(self.root),
            "must be integrated or superseded",
        )

    def test_ready_registry_rejects_an_empty_expected_source_inventory(self) -> None:
        packages = [
            self._create_package(provider)
            for provider in ("codex", "claude", "chatgpt")
        ]
        manifests = [
            self._read_json(path / "SOURCE_MANIFEST.json") for path in packages
        ]
        self.registry["state"] = "ready"
        self.registry["sources"] = [
            self._registry_source(path, manifest, status="integrated")
            for path, manifest in zip(packages, manifests, strict=True)
        ]
        self._write_json(self.registry_path, self.registry)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "expected_sources must not be empty")
        for provider in ("codex", "claude", "chatgpt"):
            self.assert_error_contains(
                errors, f"required expected source for provider {provider!r}"
            )

    def test_ready_registry_requires_complete_accessible_source_manifests(self) -> None:
        packages = [
            self._create_package(provider)
            for provider in ("codex", "claude", "chatgpt")
        ]
        manifests = [
            self._read_json(path / "SOURCE_MANIFEST.json") for path in packages
        ]
        self.registry["state"] = "ready"
        self.registry["expected_sources"] = [
            {
                "provider": manifest["provider"],
                "conversation_id": manifest["conversation_id"],
                "required": True,
                "status": "imported",
                "source_id": manifest["source_id"],
                "reason": None,
            }
            for manifest in manifests
        ]
        self.registry["sources"] = [
            self._registry_source(path, manifest, status="integrated")
            for path, manifest in zip(packages, manifests, strict=True)
        ]
        self._write_json(self.registry_path, self.registry)

        incomplete = manifests[0]
        incomplete["status"] = "partial"
        incomplete["coverage"] = "partial"
        incomplete["inaccessible"] = ["older messages are unavailable"]
        self._write_json(packages[0] / "SOURCE_MANIFEST.json", incomplete)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "manifest status must be 'complete'")
        self.assert_error_contains(errors, "manifest coverage must be 'complete'")
        self.assert_error_contains(errors, "manifest inaccessible must be empty")

    def test_ready_registry_with_three_integrated_sources_passes(self) -> None:
        self._configure_ready_registry()
        self._create_ready_consolidated()

        self.assertEqual([], validate_repository(self.root))

    def test_ready_registry_requires_consolidated_run_directory(self) -> None:
        self._configure_ready_registry()

        self.assert_error_contains(
            validate_repository(self.root),
            "Ready registry requires consolidated directory",
        )

    def test_ready_registry_requires_exact_consolidated_file_set(self) -> None:
        self._configure_ready_registry()
        output = self._create_ready_consolidated()
        (output / "00_SOURCE_COVERAGE.md").unlink()
        (output / "EXTRA.md").write_text("extra\n", encoding="utf-8")

        errors = validate_repository(self.root)

        self.assert_error_contains(
            errors, "Ready consolidated outputs are missing: 00_SOURCE_COVERAGE.md"
        )
        self.assert_error_contains(
            errors, "Ready consolidated outputs are unexpected: EXTRA.md"
        )

    def test_ready_markdown_requires_exact_metadata_and_common_sha(self) -> None:
        self._configure_ready_registry()
        output = self._create_ready_consolidated()
        first = output / "00_SOURCE_COVERAGE.md"
        first.write_text(
            "# Coverage\n"
            "Status: PARTIAL_NON_NORMATIVE_CANDIDATE\n"
            f"Run-ID: {RUN_ID}\n"
            f"Base-Main-SHA: {'b' * 40}\n",
            encoding="utf-8",
        )

        errors = validate_repository(self.root)

        self.assert_error_contains(
            errors, "must contain exactly one 'Status: NON_NORMATIVE_CANDIDATE'"
        )
        self.assert_error_contains(
            errors, "must use exactly one common Base-Main-SHA"
        )

    def test_ready_markdown_requires_headings_substance_and_privacy_metadata(self) -> None:
        self._configure_ready_registry()
        output = self._create_ready_consolidated()
        base_sha = "a" * 40
        (output / "00_SOURCE_COVERAGE.md").write_text(
            "# Coverage\n"
            f"Status: {VALIDATOR_MODULE.NON_NORMATIVE_STATUS}\n"
            f"Run-ID: {RUN_ID}\n"
            f"Base-Main-SHA: {base_sha}\n"
            "## Kaynak Envanteri\n"
            "Short.\n",
            encoding="utf-8",
        )
        integration = output / "INTEGRATION_REPORT.md"
        integration.write_text(
            integration.read_text(encoding="utf-8")
            .replace("Secret-Scan: PASS\n", "")
            .replace("PII-Review: PASS\n", "")
            .replace("Redactions: 0\n", ""),
            encoding="utf-8",
        )

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "is missing required headings: ## Kapsam Açıkları")
        self.assert_error_contains(errors, "must contain at least 200 substantive body characters")
        self.assert_error_contains(errors, "must contain exactly one 'Secret-Scan: PASS'")
        self.assert_error_contains(errors, "must contain exactly one 'PII-Review: PASS'")
        self.assert_error_contains(errors, "non-negative Redactions line")

    def test_ready_trace_and_catalog_must_cover_every_integrated_item(self) -> None:
        self._configure_ready_registry()
        output = self._create_ready_consolidated()
        trace_path = output / "04_TRACEABILITY_MATRIX.csv"
        trace_lines = trace_path.read_text(encoding="utf-8").splitlines()
        trace_path.write_text(
            "\n".join([trace_lines[0], *trace_lines[2:]]) + "\n",
            encoding="utf-8",
        )
        catalog_path = output / "catalog.ndjson"
        catalog_lines = catalog_path.read_text(encoding="utf-8").splitlines()
        catalog_path.write_text(
            "\n".join(catalog_lines[1:]) + "\n",
            encoding="utf-8",
        )

        errors = validate_repository(self.root)

        self.assert_error_contains(
            errors, "Ready traceability CSV is missing integrated source/item references"
        )
        self.assert_error_contains(
            errors, "Ready catalog NDJSON is missing integrated source/item references"
        )

    def test_verify_git_head_accepts_match_and_rejects_mismatch_or_unavailable(self) -> None:
        self._configure_ready_registry()
        base_sha = "a" * 40
        self._create_ready_consolidated(base_sha)

        matching = validate_repository(
            self.root,
            verify_git_head=True,
            git_head_resolver=lambda _root: (base_sha, None),
        )
        mismatching = validate_repository(
            self.root,
            verify_git_head=True,
            git_head_resolver=lambda _root: ("b" * 40, None),
        )
        unavailable = validate_repository(
            self.root,
            verify_git_head=True,
            git_head_resolver=lambda _root: (None, "offline"),
        )

        self.assertEqual([], matching)
        self.assert_error_contains(mismatching, "does not match live refs/remotes/origin/main")
        self.assert_error_contains(unavailable, "Cannot resolve live refs/remotes/origin/main")

    def test_ready_csv_and_catalog_require_exact_metadata(self) -> None:
        self._configure_ready_registry()
        output = self._create_ready_consolidated()
        base_sha = "a" * 40
        (output / "04_TRACEABILITY_MATRIX.csv").write_text(
            "record_id,normative_status,run_id,base_main_sha\n"
            f"ROW-1,PARTIAL_NON_NORMATIVE_CANDIDATE,{RUN_ID},{base_sha}\n",
            encoding="utf-8",
        )
        (output / "catalog.ndjson").write_text(
            json.dumps(
                {
                    "id": "CAT-1",
                    "normative_status": "NON_NORMATIVE_CANDIDATE",
                    "run_id": "OTHER-RUN",
                    "base_main_sha": base_sha,
                },
                separators=(",", ":"),
            )
            + "\n",
            encoding="utf-8",
        )

        errors = validate_repository(self.root)

        self.assert_error_contains(
            errors,
            "normative_status must be 'NON_NORMATIVE_CANDIDATE'",
        )
        self.assert_error_contains(errors, f"run_id must be {RUN_ID!r}")

    def test_package_symlink_is_rejected(self) -> None:
        package = self._create_package("codex")
        knowledge_path = package / "KNOWLEDGE.md"
        target = self.root / "outside.md"
        target.write_text("outside\n", encoding="utf-8")
        knowledge_path.unlink()
        try:
            knowledge_path.symlink_to(target)
        except OSError as exc:
            self.skipTest(f"File symlinks are unavailable: {exc}")

        self.assert_error_contains(
            validate_repository(self.root), "KNOWLEDGE.md must not be a symbolic link"
        )

    def test_unsupported_provider_and_unsafe_source_directory_are_rejected(self) -> None:
        source = self.knowledge_root / "inbox" / RUN_ID / "other" / "Bad_Source"
        source.mkdir(parents=True)

        errors = validate_repository(self.root)

        self.assert_error_contains(errors, "Unsupported provider directory 'other'")
        self.assert_error_contains(errors, "unsafe directory name 'Bad_Source'")

    @staticmethod
    def _base_registry() -> dict[str, Any]:
        return {
            "schema_version": "1.0",
            "run_id": RUN_ID,
            "canonical_repository": "https://github.com/sinangym/GoymenERP.git",
            "default_branch": "main",
            "state": "collecting",
            "source_inventory_attestation": {
                "status": "pending",
                "attested_by": None,
                "attested_at": None,
                "evidence_locator": None,
                "scope_note": None,
            },
            "required_providers": ["codex", "claude", "chatgpt"],
            "expected_sources": [],
            "sources": [],
        }

    @staticmethod
    def _source_snapshot(provider: str) -> str:
        return hashlib.sha256(
            f"visible source snapshot:{provider}".encode("utf-8")
        ).hexdigest()

    @classmethod
    def _source_id(cls, provider: str) -> str:
        return f"{provider}-conversation-{cls._source_snapshot(provider)[:12]}"

    def _configure_ready_registry(self) -> list[Path]:
        packages = [
            self._create_package(provider)
            for provider in ("codex", "claude", "chatgpt")
        ]
        manifests = [
            self._read_json(path / "SOURCE_MANIFEST.json") for path in packages
        ]
        self.registry["state"] = "ready"
        self.registry["source_inventory_attestation"] = {
            "status": "user_confirmed_complete",
            "attested_by": "Sinan",
            "attested_at": "2026-09-03T18:00:00+03:00",
            "evidence_locator": "user-message:source-inventory-confirmation",
            "scope_note": "Bu run için ilan edilen konuşma envanteri tamamdır.",
        }
        self.registry["expected_sources"] = [
            {
                "provider": manifest["provider"],
                "conversation_id": manifest["conversation_id"],
                "required": True,
                "status": "imported",
                "source_id": manifest["source_id"],
                "reason": None,
            }
            for manifest in manifests
        ]
        self.registry["sources"] = [
            self._registry_source(path, manifest, status="integrated")
            for path, manifest in zip(packages, manifests, strict=True)
        ]
        self._write_json(self.registry_path, self.registry)
        return packages

    def _create_ready_consolidated(self, base_sha: str = "a" * 40) -> Path:
        output = self.knowledge_root / "consolidated" / RUN_ID
        output.mkdir(parents=True, exist_ok=True)
        substantive_body = (
            "Bu çıktı, yalnızca içe aktarılan ve doğrulanan kaynak paketlerinden "
            "üretilmiş normatif olmayan bir adaydır. Her bulgu kaynak kimliği ve "
            "öğe kimliği üzerinden izlenebilir; doğrulanmamış varsayımlar mevcut "
            "durum olarak sunulmaz. Çelişkiler ile açık kararlar ayrıca işaretlenir "
            "ve insan onayı olmadan ana rapora ya da uygulama kapsamına terfi etmez."
        )
        for filename in VALIDATOR_MODULE.READY_MARKDOWN_FILES:
            title = filename.removesuffix(".md").replace("_", " ")
            privacy_metadata = ""
            if filename == "INTEGRATION_REPORT.md":
                privacy_metadata = (
                    "Secret-Scan: PASS\n"
                    "PII-Review: PASS\n"
                    "Redactions: 0\n"
                )
            headings = "\n".join(
                sorted(
                    VALIDATOR_MODULE.READY_MARKDOWN_REQUIRED_HEADINGS.get(
                        filename, frozenset()
                    )
                )
            )
            (output / filename).write_text(
                f"# {title}\n"
                f"Status: {VALIDATOR_MODULE.NON_NORMATIVE_STATUS}\n"
                f"Run-ID: {RUN_ID}\n"
                f"Base-Main-SHA: {base_sha}\n"
                f"{privacy_metadata}\n"
                f"{headings}\n\n"
                f"{substantive_body}\n",
                encoding="utf-8",
            )

        references: list[tuple[str, str]] = []
        for source in self.registry["sources"]:
            if source["status"] != "integrated":
                continue
            manifest_path = self.root / source["manifest_path"]
            package = manifest_path.parent
            for line in (package / "NORMALIZED_ITEMS.ndjson").read_text(
                encoding="utf-8"
            ).splitlines():
                if line.strip():
                    references.append((source["source_id"], json.loads(line)["id"]))

        csv_lines = [
            "trace_id,source_id,item_id,target_ref,disposition,exclusion_reason,"
            "normative_status,run_id,base_main_sha"
        ]
        catalog_lines: list[str] = []
        for index, (source_id, item_id) in enumerate(references, start=1):
            canonical_id = f"CAN-{index:04d}"
            csv_lines.append(
                f"TRACE-{index:04d},{source_id},{item_id},{canonical_id},included,,"
                f"{VALIDATOR_MODULE.NON_NORMATIVE_STATUS},{RUN_ID},{base_sha}"
            )
            catalog_lines.append(
                json.dumps(
                    {
                        "canonical_id": canonical_id,
                        "source_refs": [
                            {"source_id": source_id, "item_id": item_id}
                        ],
                        "disposition": "included",
                        "exclusion_reason": None,
                        "normative_status": VALIDATOR_MODULE.NON_NORMATIVE_STATUS,
                        "run_id": RUN_ID,
                        "base_main_sha": base_sha,
                    },
                    ensure_ascii=False,
                    separators=(",", ":"),
                )
            )
        (output / "04_TRACEABILITY_MATRIX.csv").write_text(
            "\n".join(csv_lines) + "\n",
            encoding="utf-8",
        )
        (output / "catalog.ndjson").write_text(
            "\n".join(catalog_lines) + "\n",
            encoding="utf-8",
        )
        return output

    def _valid_item(
        self, provider: str, *, item_id: str | None = None
    ) -> dict[str, Any]:
        item = {
            "schema_version": "1.0",
            "id": "KN-" + "0" * 24,
            "source_id": self._source_id(provider),
            "type": "requirement",
            "module": "Genel",
            "statement": f"{provider} kaynağından doğrulanabilir gereksinim.",
            "current_or_target": "TARGET",
            "status": "USER_REQUIREMENT",
            "priority": "P1",
            "source_provider": provider,
            "conversation_id": f"{provider}-conversation-1",
            "source_locator": "visible-message-1",
            "source_date": "2026-09-03",
            "confidence": "HIGH",
            "supersedes": [],
            "conflicts_with": [],
            "related_items": [],
        }
        item["id"] = item_id or self._deterministic_item_id(item)
        return item

    def _create_package(
        self, provider: str, *, item_id: str | None = None
    ) -> Path:
        source_id = self._source_id(provider)
        package = self.knowledge_root / "inbox" / RUN_ID / provider / source_id
        package.mkdir(parents=True)
        (package / "KNOWLEDGE.md").write_text("# Knowledge\n", encoding="utf-8")
        (package / "CONFLICT_CANDIDATES.md").write_text(
            "# Conflict candidates\n\nNone.\n", encoding="utf-8"
        )
        (package / "ATTACHMENT_INDEX.md").write_text(
            "# Attachment index\n\nAttachment-Count: 0\n\nNone.\n",
            encoding="utf-8",
        )
        (package / "EXPORT_REPORT.md").write_text(
            "# Export report\n\nComplete.\n", encoding="utf-8"
        )
        self._write_ndjson(
            package / "NORMALIZED_ITEMS.ndjson",
            [self._valid_item(provider, item_id=item_id)],
        )
        manifest = {
            "schema_version": "1.0",
            "run_id": RUN_ID,
            "provider": provider,
            "source_id": source_id,
            "conversation_id": f"{provider}-conversation-1",
            "conversation_title": f"{provider.title()} ERP conversation",
            "coverage": "complete",
            "inaccessible": [],
            "status": "complete",
            "privacy_review": {
                "secret_scan": "passed",
                "pii_review": "passed",
                "redactions": 0,
            },
            "source_snapshot_sha256": self._source_snapshot(provider),
            "content_fingerprint_sha256": "0" * 64,
            "files": [],
            "counts": {"items": 1, "attachments": 0, "conflicts": 0},
        }
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)
        self._refresh_manifest(package)
        return package

    def _write_items(self, package: Path, items: list[dict[str, Any]]) -> None:
        self._write_ndjson(package / "NORMALIZED_ITEMS.ndjson", items)
        self._refresh_manifest(package)

    def _refresh_manifest(self, package: Path) -> None:
        manifest = self._read_json(package / "SOURCE_MANIFEST.json")
        files: list[dict[str, Any]] = []
        for filename in MANIFESTED_FILES:
            raw = (package / filename).read_bytes()
            files.append(
                {
                    "path": filename,
                    "bytes": len(raw),
                    "sha256": hashlib.sha256(raw).hexdigest(),
                }
            )
        ndjson_raw = (package / "NORMALIZED_ITEMS.ndjson").read_bytes()
        manifest["content_fingerprint_sha256"] = hashlib.sha256(ndjson_raw).hexdigest()
        manifest["files"] = files
        items = [
            json.loads(line)
            for line in ndjson_raw.decode("utf-8").splitlines()
            if line.strip()
        ]
        manifest["counts"]["items"] = len(items)
        manifest["counts"]["conflicts"] = sum(
            item.get("status") == "CONFLICT" for item in items
        )
        self._write_json(package / "SOURCE_MANIFEST.json", manifest)

    def _registry_source(
        self, package: Path, manifest: dict[str, Any], *, status: str
    ) -> dict[str, Any]:
        return {
            "provider": manifest["provider"],
            "source_id": manifest["source_id"],
            "conversation_id": manifest["conversation_id"],
            "manifest_path": (package / "SOURCE_MANIFEST.json")
            .relative_to(self.root)
            .as_posix(),
            "content_fingerprint_sha256": manifest["content_fingerprint_sha256"],
            "status": status,
        }

    @staticmethod
    def _write_json(path: Path, value: dict[str, Any]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

    @staticmethod
    def _write_ndjson(path: Path, values: list[dict[str, Any]]) -> None:
        text = "".join(
            json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"
            for value in values
        )
        path.write_text(text, encoding="utf-8")

    @staticmethod
    def _deterministic_item_id(item: dict[str, Any]) -> str:
        identity = "\n".join(
            str(item[field])
            for field in (
                "source_id",
                "source_locator",
                "type",
                "module",
                "current_or_target",
                "statement",
            )
        )
        return "KN-" + hashlib.sha256(identity.encode("utf-8")).hexdigest()[:24].upper()

    @staticmethod
    def _read_json(path: Path) -> dict[str, Any]:
        value = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(value, dict):
            raise AssertionError(f"Expected object in {path}")
        return value

    @staticmethod
    def _remove_package(package: Path) -> None:
        for child in package.iterdir():
            child.unlink()
        package.rmdir()

    def assert_error_contains(self, errors: list[str], expected: str) -> None:
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected {expected!r} in errors:\n" + "\n".join(errors),
        )


if __name__ == "__main__":
    unittest.main()
