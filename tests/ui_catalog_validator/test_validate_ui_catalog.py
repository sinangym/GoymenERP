from __future__ import annotations

import csv
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from tools.validate_ui_catalog import (
    ASSET_MANIFEST_FIELDS,
    CANONICAL_STATUS_FIELDS,
    EXPECTED_CANONICAL_SCREENS,
    EXPECTED_ORIGINAL_ASSETS,
    EXPECTED_PROTOTYPES,
    EXPECTED_SELECTED_SOURCES,
    EXPECTED_SOURCE_RECORDS,
    SCREEN_CATALOG_FIELDS,
    validate_repository,
)


class UiCatalogValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        self._build_valid_fixture()

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_valid_golden_fixture_passes(self) -> None:
        self.assertEqual([], validate_repository(self.root))

    def test_broken_source_count_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/SCREEN_CATALOG.csv")
        self._write_csv(
            "docs/ui/SCREEN_CATALOG.csv",
            SCREEN_CATALOG_FIELDS,
            rows[:-1],
        )

        self.assert_error_contains("Golden source record count mismatch")

    def test_duplicate_record_id_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/SCREEN_CATALOG.csv")
        rows[1]["record_id"] = rows[0]["record_id"]
        self._write_csv("docs/ui/SCREEN_CATALOG.csv", SCREEN_CATALOG_FIELDS, rows)

        self.assert_error_contains("Duplicate record_id values")

    def test_missing_asset_path_is_rejected(self) -> None:
        (self.root / "docs/ui/assets/original/test/asset-001.bin").unlink()

        self.assert_error_contains("Missing or unsafe manifest asset")

    def test_wrong_asset_hash_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/ASSET_MANIFEST.csv")
        rows[0]["sha256"] = "0" * 64
        self._write_csv("docs/ui/ASSET_MANIFEST.csv", ASSET_MANIFEST_FIELDS, rows)

        self.assert_error_contains("SHA-256 mismatch")

    def test_wrong_asset_byte_size_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/ASSET_MANIFEST.csv")
        rows[0]["bytes"] = str(int(rows[0]["bytes"]) + 1)
        self._write_csv("docs/ui/ASSET_MANIFEST.csv", ASSET_MANIFEST_FIELDS, rows)

        self.assert_error_contains("Byte size mismatch")

    def test_unresolved_canonical_reference_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/SCREEN_CATALOG.csv")
        rows[0]["canonical_ids"] = json.dumps(["MISSING-CANONICAL"])
        self._write_csv("docs/ui/SCREEN_CATALOG.csv", SCREEN_CATALOG_FIELDS, rows)
        card = self.root / rows[0]["screen_card"]
        card.write_text(
            card.read_text(encoding="utf-8").replace(
                'canonical_ids: ["C-001"]',
                'canonical_ids: ["MISSING-CANONICAL"]',
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("Unresolved canonical reference")

    def test_unresolved_decision_reference_is_rejected(self) -> None:
        rows = self._read_csv("docs/ui/SCREEN_CATALOG.csv")
        rows[0]["decision_refs"] = json.dumps(["UI-MAP-999"])
        self._write_csv("docs/ui/SCREEN_CATALOG.csv", SCREEN_CATALOG_FIELDS, rows)
        card = self.root / rows[0]["screen_card"]
        card.write_text(
            card.read_text(encoding="utf-8").replace(
                'decision_refs: ["UI-MAP-001"]',
                'decision_refs: ["UI-MAP-999"]',
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("Unresolved decision reference")

    def test_narrative_decision_mention_does_not_resolve_reference(self) -> None:
        reconciliation = self.root / "docs/ui/SOURCE_RECONCILIATION.md"
        reconciliation.write_text(
            reconciliation.read_text(encoding="utf-8")
            + "Narrative-only candidate UI-MAP-999 is not accepted.\n",
            encoding="utf-8",
        )
        rows = self._read_csv("docs/ui/SCREEN_CATALOG.csv")
        rows[0]["decision_refs"] = json.dumps(["UI-MAP-999"])
        self._write_csv("docs/ui/SCREEN_CATALOG.csv", SCREEN_CATALOG_FIELDS, rows)
        card = self.root / rows[0]["screen_card"]
        card.write_text(
            card.read_text(encoding="utf-8").replace(
                'decision_refs: ["UI-MAP-001"]',
                'decision_refs: ["UI-MAP-999"]',
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("Unresolved decision reference 'UI-MAP-999'")

    def test_unexpected_accepted_decision_is_rejected(self) -> None:
        reconciliation = self.root / "docs/ui/SOURCE_RECONCILIATION.md"
        reconciliation.write_text(
            reconciliation.read_text(encoding="utf-8")
            + "**UI-MAP-007:** unexpected accepted fixture decision\n",
            encoding="utf-8",
        )

        self.assert_error_contains("Unexpected accepted UI mapping decisions")

    def test_unmanifested_selected_source_is_rejected(self) -> None:
        source = self.root / "docs/ui/sources/test/unmanifested.md"
        source.write_text("unmanifested source\n", encoding="utf-8")

        self.assert_error_contains("SELECTED_SOURCE filesystem/manifest mismatch")

    def test_frontmatter_catalog_parity_mismatch_is_rejected(self) -> None:
        card = self.root / "docs/ui/screens/test/REC-001.md"
        card.write_text(
            card.read_text(encoding="utf-8").replace(
                "mapping_status: EXACT",
                "mapping_status: SPLIT",
            ),
            encoding="utf-8",
        )

        self.assert_error_contains("Frontmatter/catalog parity mismatch")

    def assert_error_contains(self, expected: str) -> None:
        errors = validate_repository(self.root)
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected an error containing {expected!r}, got: {errors!r}",
        )

    def _build_valid_fixture(self) -> None:
        ui_root = self.root / "docs/ui"
        cards_root = ui_root / "screens/test"
        originals_root = ui_root / "assets/original/test"
        prototypes_root = ui_root / "prototypes/test"
        sources_root = ui_root / "sources/test"
        cards_root.mkdir(parents=True)
        originals_root.mkdir(parents=True)
        prototypes_root.mkdir(parents=True)
        sources_root.mkdir(parents=True)

        decisions = "\n".join(
            f"**UI-MAP-{number:03d}:** fixture decision"
            for number in range(1, 7)
        )
        (ui_root / "SOURCE_RECONCILIATION.md").write_text(
            decisions + "\n",
            encoding="utf-8",
        )

        screen_rows: list[dict[str, str]] = []
        canonical_rows: list[dict[str, str]] = []
        asset_rows: list[dict[str, str]] = []

        for index in range(1, EXPECTED_SOURCE_RECORDS + 1):
            record_id = f"REC-{index:03d}"
            canonical_id = f"C-{index:03d}"
            asset_relative = f"docs/ui/assets/original/test/asset-{index:03d}.bin"
            asset_path = self.root / asset_relative
            asset_path.write_bytes(f"asset-{index:03d}".encode("ascii"))
            asset_rows.append(self._asset_row(asset_relative, "ORIGINAL_ASSET"))
            card_relative = f"docs/ui/screens/test/{record_id}.md"
            (self.root / card_relative).write_text(
                "\n".join(
                    [
                        "---",
                        f'record_id: "{record_id}"',
                        'source_namespace: "FIXTURE"',
                        f'source_id: "SRC-{index:03d}"',
                        f'canonical_ids: ["{canonical_id}"]',
                        "related_reference_ids: []",
                        "conditional_workspace_ids: []",
                        "mapping_status: EXACT",
                        'decision_refs: ["UI-MAP-001"]',
                        "source_status: LOCKED_VISUAL",
                        "visual_status: LOCKED_VISUAL",
                        'source_date: "2026-08-28"',
                        'source_version: "v1"',
                        "---",
                        "",
                        f"# {record_id}",
                        "",
                    ]
                ),
                encoding="utf-8",
            )
            screen_rows.append(
                {
                    "record_id": record_id,
                    "source_namespace": "FIXTURE",
                    "source_id": f"SRC-{index:03d}",
                    "title": record_id,
                    "domain": "fixture",
                    "canonical_ids": json.dumps([canonical_id]),
                    "related_reference_ids": "[]",
                    "conditional_workspace_ids": "[]",
                    "mapping_status": "EXACT",
                    "decision_refs": json.dumps(["UI-MAP-001"]),
                    "source_status": "LOCKED_VISUAL",
                    "visual_status": "LOCKED_VISUAL",
                    "source_date": "2026-08-28",
                    "source_version": "v1",
                    "primary_reference": (
                        f"../../assets/original/test/asset-{index:03d}.bin"
                    ),
                    "screen_card": card_relative,
                }
            )

        for index in range(
            EXPECTED_SOURCE_RECORDS + 1,
            EXPECTED_ORIGINAL_ASSETS + 1,
        ):
            asset_relative = f"docs/ui/assets/original/test/asset-{index:03d}.bin"
            (self.root / asset_relative).write_bytes(
                f"asset-{index:03d}".encode("ascii")
            )
            asset_rows.append(self._asset_row(asset_relative, "ORIGINAL_ASSET"))

        for index in range(1, EXPECTED_PROTOTYPES + 1):
            prototype_relative = f"docs/ui/prototypes/test/prototype-{index:03d}.html"
            (self.root / prototype_relative).write_text(
                f"<!doctype html><title>prototype {index}</title>\n",
                encoding="utf-8",
            )
            asset_rows.append(
                self._asset_row(prototype_relative, "REFERENCE_PROTOTYPE")
            )

        for index in range(1, EXPECTED_SELECTED_SOURCES + 1):
            source_relative = f"docs/ui/sources/test/source-{index:03d}.md"
            (self.root / source_relative).write_text(
                f"# selected source {index}\n",
                encoding="utf-8",
            )
            asset_rows.append(self._asset_row(source_relative, "SELECTED_SOURCE"))

        for index in range(1, EXPECTED_CANONICAL_SCREENS + 1):
            record_id = (
                f"REC-{index:03d}" if index <= EXPECTED_SOURCE_RECORDS else ""
            )
            canonical_rows.append(
                {
                    "canonical_id": f"C-{index:03d}",
                    "domain": "fixture",
                    "name": f"Canonical {index:03d}",
                    "scope": "fixture",
                    "reference_coverage": (
                        "CONFIRMED_EXACT_REFERENCE"
                        if record_id
                        else "DECISION_ONLY_REFERENCE_MISSING"
                    ),
                    "reference_records": record_id,
                }
            )

        self._write_csv(
            "docs/ui/SCREEN_CATALOG.csv",
            SCREEN_CATALOG_FIELDS,
            screen_rows,
        )
        self._write_csv(
            "docs/ui/CANONICAL_SCREEN_STATUS.csv",
            CANONICAL_STATUS_FIELDS,
            canonical_rows,
        )
        self._write_csv(
            "docs/ui/ASSET_MANIFEST.csv",
            ASSET_MANIFEST_FIELDS,
            asset_rows,
        )

    def _asset_row(self, relative_path: str, artifact_class: str) -> dict[str, str]:
        payload = (self.root / relative_path).read_bytes()
        digest = hashlib.sha256(payload).hexdigest().upper()
        return {
            "repo_path": relative_path,
            "artifact_class": artifact_class,
            "source_screen_or_set": "fixture",
            "bytes": str(len(payload)),
            "sha256": digest,
            "source_container_sha256": digest,
            "classification": "TEST_FIXTURE",
            "notes": "generated test fixture",
        }

    def _read_csv(self, relative_path: str) -> list[dict[str, str]]:
        with (self.root / relative_path).open(
            "r",
            encoding="utf-8",
            newline="",
        ) as handle:
            return list(csv.DictReader(handle))

    def _write_csv(
        self,
        relative_path: str,
        fieldnames: tuple[str, ...],
        rows: list[dict[str, str]],
    ) -> None:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=fieldnames, lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)


if __name__ == "__main__":
    unittest.main()
