from __future__ import annotations

import ast
import contextlib
import csv
import importlib.util
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any, Callable


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "tools/validate_delivery_backlog.py"
RECOMPUTE_PATH = Path(__file__).with_name("recompute_delivery_backlog_provenance.py")


def _load_module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load module from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


VALIDATOR = _load_module("goymen_delivery_backlog_decision_round", VALIDATOR_PATH)
RECOMPUTE = _load_module("goymen_delivery_backlog_independent_recompute", RECOMPUTE_PATH)

REQUIRED_FILES = (
    "docs/backlog/DELIVERY_BACKLOG_v1.json",
    "docs/backlog/task_manifest.json",
    "docs/decisions/KARAR_SICILI.md",
    "docs/decisions/KARAR_SICILI.csv",
    "docs/decisions/ACIK_KARARLAR.md",
    "docs/acceptance/ACCEPTANCE_70.md",
    "docs/ui/CANONICAL_SCREEN_STATUS.csv",
)


class IndependentProvenanceRecomputeTests(unittest.TestCase):
    def test_parent_provenance_is_recomputed_without_validator_constants(self) -> None:
        source = RECOMPUTE_PATH.read_text(encoding="utf-8")
        self.assertNotIn("validate_delivery_backlog", source)
        self.assertNotIn("EXPECTED_", source)
        imports = []
        for node in ast.walk(ast.parse(source)):
            if isinstance(node, ast.Import):
                imports.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom):
                imports.append(node.module or "")
        self.assertFalse(
            any("validate_delivery_backlog" in imported for imported in imports), imports
        )

        result = RECOMPUTE.recompute(REPOSITORY_ROOT)

        self.assertEqual(117, result["decisionRegistry"]["count"])
        self.assertEqual(
            {"GECICI_VARSAYIM": 3, "KILITLI": 114},
            result["decisionRegistry"]["statusHistogram"],
        )
        self.assertEqual(
            "40d0ac9ff118c552e11b76612711246599bda0dced6ab694de638302c47d15d8",
            result["decisionRegistry"]["canonicalRowsSha256"],
        )
        self.assertEqual(
            "94c1b852e421288bb110f005bdf5e77a35046c3e9729f8eb6a044440d7da085f",
            result["decisionRegistry"]["orderedIdsSha256"],
        )
        self.assertTrue(result["decisionRegistryParity"]["isExact"])
        self.assertEqual([], result["decisionRoundParity"]["missingInMarkdown"])
        self.assertEqual([], result["decisionRoundParity"]["missingInCsv"])
        self.assertEqual([], result["decisionRoundParity"]["semanticMismatches"])
        self.assertEqual(
            "60f161e5af3f51f39d948ececbce1de82cbdf2aeb71d68bab6064f2c4fe8c09a",
            result["openDecisionCatalog"]["canonicalRowsSha256"],
        )
        self.assertTrue(result["openDecisionCatalog"]["isExpectedExact"])
        self.assertEqual([], result["openDecisionCatalog"]["missingExpectedIds"])
        self.assertEqual([], result["openDecisionCatalog"]["extraExpectedIds"])
        self.assertEqual(
            "a2e6926cabfe31a10b2fc5f3f1eb48212af0136b02bc55db6dc71ab359207497",
            result["deliveryBacklog"]["canonicalMappingSha256"],
        )
        self.assertEqual([], result["deliveryBacklog"]["missingDecisionIds"])
        self.assertEqual([], result["deliveryBacklog"]["extraDecisionIds"])
        self.assertEqual([], result["deliveryBacklog"]["missingOpenIds"])
        self.assertEqual([], result["deliveryBacklog"]["extraOpenIds"])


class DecisionRoundMutationTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        for relative_path in REQUIRED_FILES:
            source = REPOSITORY_ROOT / relative_path
            target = self.root / relative_path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        self.decision_md = self.root / "docs/decisions/KARAR_SICILI.md"
        self.decision_csv = self.root / "docs/decisions/KARAR_SICILI.csv"
        self.open_md = self.root / "docs/decisions/ACIK_KARARLAR.md"
        self.backlog = self.root / "docs/backlog/DELIVERY_BACKLOG_v1.json"

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def test_add_decision_in_both_sources_is_rejected(self) -> None:
        self.decision_md.write_text(
            self.decision_md.read_text(encoding="utf-8")
            + "| DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |\n",
            encoding="utf-8",
        )
        rows = self._read_csv_rows()
        rows.append(
            {
                "DecisionId": "DEC-TST-999",
                "Alan": "Test",
                "Karar": "Added",
                "Durum": "KILITLI",
                "Oncelik": "K1",
                "Kaynak": "Test",
                "Sahip": "Test",
                "KabulKaniti": "Test",
            }
        )
        self._write_csv_rows(rows)

        self.assert_error_contains("Decision registry baseline drift: expected 117, got 118")

    def test_remove_decision_from_both_sources_is_rejected(self) -> None:
        self._remove_markdown_decision("DEC-AI-009")
        self._write_csv_rows(
            [row for row in self._read_csv_rows() if row["DecisionId"] != "DEC-AI-009"]
        )

        self.assert_error_contains("Decision registry baseline drift: expected 117, got 116")

    def test_status_swap_with_same_histogram_is_rejected(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001", lambda row: row.replace("| KILITLI |", "| GECICI_VARSAYIM |")
        )
        self._mutate_markdown_decision(
            "DEC-PERF-001", lambda row: row.replace("| GECICI_VARSAYIM |", "| KILITLI |")
        )
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Durum"] = "GECICI_VARSAYIM"
            elif row["DecisionId"] == "DEC-PERF-001":
                row["Durum"] = "KILITLI"
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("locked baseline drift" in error for error in errors), errors)
        self.assertFalse(any("temporary baseline drift" in error for error in errors), errors)

    def test_decision_header_and_active_table_context_are_required(self) -> None:
        original = self.decision_md.read_text(encoding="utf-8")
        header = VALIDATOR.DECISION_MARKDOWN_HEADER
        cases = (
            (
                "header_swap",
                original.replace(
                    header,
                    "| ID | Karar | Alan | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |",
                    1,
                ),
                "must contain exactly one canonical Markdown header; found 0",
            ),
            (
                "fenced",
                original.replace(header, "```markdown\n" + header, 1) + "```\n",
                "inactive fence/comment/raw-HTML context",
            ),
        )
        for case, content, expected in cases:
            with self.subTest(case=case):
                self.decision_md.write_text(content, encoding="utf-8")
                self.assert_error_contains(expected)
                self.decision_md.write_text(original, encoding="utf-8")

    def test_open_header_and_active_table_context_are_required(self) -> None:
        original = self.open_md.read_text(encoding="utf-8")
        header = VALIDATOR.OPEN_MARKDOWN_HEADER
        cases = (
            (
                "header_swap",
                original.replace(
                    header,
                    header.replace("| ID | Durum |", "| Durum | ID |", 1),
                    1,
                ),
                "must contain exactly one canonical Markdown header; found 0",
            ),
            (
                "fenced",
                original.replace(header, "~~~markdown\n" + header, 1) + "~~~\n",
                "inactive fence/comment/raw-HTML context",
            ),
        )
        for case, content, expected in cases:
            with self.subTest(case=case):
                self.open_md.write_text(content, encoding="utf-8")
                self.assert_error_contains(expected)
                self.open_md.write_text(original, encoding="utf-8")

    def test_noncanonical_optional_pipe_rows_cannot_bypass_registry_pins(self) -> None:
        cases = (
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                " | DEC-TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "**DEC-TST-999** | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC-<!-- hidden -->TST-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (
                self.decision_md,
                "| DEC-AI-009 |",
                "DEC\\-TST\\-999 | Test | Added | KILITLI | K1 | Test | Test | Test |",
            ),
            (self.decision_md, "| DEC-AI-009 |", "DEC-TST-999"),
            (
                self.open_md,
                "| OPEN-021 |",
                "OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (
                self.open_md,
                "| OPEN-021 |",
                " | OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (
                self.open_md,
                "| OPEN-021 |",
                "> OPEN-999 | **KISMEN** | Closed | Open | Evidence | Code evidence |",
            ),
            (self.open_md, "| OPEN-021 |", "OPEN-999"),
        )
        originals = {
            self.decision_md: self.decision_md.read_text(encoding="utf-8"),
            self.open_md: self.open_md.read_text(encoding="utf-8"),
        }
        for path, after_prefix, added_row in cases:
            with self.subTest(path=path.name, row=added_row):
                lines = originals[path].splitlines()
                insertion = next(
                    index + 1
                    for index, line in enumerate(lines)
                    if line.startswith(after_prefix)
                )
                lines.insert(insertion, added_row)
                path.write_text("\n".join(lines) + "\n", encoding="utf-8")
                expected = (
                    "raw HTML/comment markup is not allowed"
                    if "<" in added_row
                    else "contains a non-canonical row"
                )
                self.assert_error_contains(expected)
                with self.assertRaisesRegex(
                    ValueError,
                    "raw HTML/comment markup is not allowed"
                    if "<" in added_row
                    else "non-canonical table row",
                ):
                    RECOMPUTE.recompute(self.root)
                path.write_text(originals[path], encoding="utf-8")

    def test_complex_fence_comment_and_raw_html_contexts_are_rejected(self) -> None:
        catalogs = (
            (self.decision_md, VALIDATOR.DECISION_MARKDOWN_HEADER),
            (self.open_md, VALIDATOR.OPEN_MARKDOWN_HEADER),
        )
        wrappers = (
            ("long_fence_short_pseudo_close", "````\n```\n", "\n````"),
            ("long_fence_mixed_pseudo_close", "````\n````~\n", "\n````"),
            ("comment_close_reopen", "<!--\n--> <!--\n", "\n-->"),
            ("pre", "<pre>\n", "\n</pre>"),
            ("processing_instruction", "<?test\n", "\n?>"),
            ("cdata", "<![CDATA[\n", "\n]]>"),
            ("block_tag", "<table>\n", "\n</table>"),
        )
        for path, header in catalogs:
            original = path.read_text(encoding="utf-8")
            for case, prefix, suffix in wrappers:
                with self.subTest(path=path.name, case=case):
                    mutated = original.replace(header, prefix + header, 1) + suffix + "\n"
                    path.write_text(mutated, encoding="utf-8")
                    self.assert_error_contains("inactive fence/comment/raw-HTML context")
                    with self.assertRaisesRegex(
                        ValueError,
                        "raw HTML/comment markup is not allowed"
                        if "<" in prefix
                        else "inactive fence/comment/raw-HTML context",
                    ):
                        RECOMPUTE.recompute(self.root)
                    path.write_text(original, encoding="utf-8")

    def test_semantic_column_change_in_both_sources_is_rejected(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001",
            lambda row: row.replace(
                "Göymen ERP bağımsız yeni üründür",
                "Göymen ERP değiştirilmiş üründür",
                1,
            ),
        )
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Karar"] = row["Karar"].replace(
                    "Göymen ERP bağımsız yeni üründür",
                    "Göymen ERP değiştirilmiş üründür",
                    1,
                )
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("Markdown/CSV semantic drift" in error for error in errors), errors)

    def test_open_decision_content_change_is_rejected(self) -> None:
        content = self.open_md.read_text(encoding="utf-8")
        content = content.replace("| OPEN-001 | **KISMEN** |", "| OPEN-001 | **AÇIK** |", 1)
        self.open_md.write_text(content, encoding="utf-8")

        self.assert_error_contains("Open decision catalog row baseline hash drift")

    def test_zero_open_rows_fail_closed_in_validator_and_recomputer(self) -> None:
        lines = self.open_md.read_text(encoding="utf-8").splitlines()
        self.open_md.write_text(
            "\n".join(line for line in lines if not line.startswith("| OPEN-")) + "\n",
            encoding="utf-8",
        )

        self.assert_error_contains("canonical Markdown table has no data rows")
        result = RECOMPUTE.recompute(self.root)
        self.assertEqual(0, result["openDecisionCatalog"]["count"])
        self.assertEqual(
            [f"OPEN-{number:03d}" for number in range(1, 22)],
            result["openDecisionCatalog"]["missingExpectedIds"],
        )
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, RECOMPUTE.main(["--root", str(self.root)]))

    def test_removing_new_decision_from_backlog_is_rejected(self) -> None:
        backlog = json.loads(self.backlog.read_text(encoding="utf-8"))
        backlog["global_decisions"].remove("DEC-AI-009")
        self.backlog.write_text(
            json.dumps(backlog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

        self.assert_error_contains("missing decision ids: DEC-AI-009")

    def test_duplicate_decision_row_is_rejected(self) -> None:
        line = self._markdown_decision_line("DEC-AI-009")
        self.decision_md.write_text(
            self.decision_md.read_text(encoding="utf-8") + line + "\n", encoding="utf-8"
        )
        rows = self._read_csv_rows()
        rows.append(dict(next(row for row in rows if row["DecisionId"] == "DEC-AI-009")))
        self._write_csv_rows(rows)

        self.assert_error_contains("Duplicate decision registry id: 'DEC-AI-009'")
        self.assert_error_contains("Duplicate decision registry CSV id: 'DEC-AI-009'")

    def test_reorder_only_in_both_sources_is_rejected_by_order_pin(self) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        first = next(i for i, line in enumerate(lines) if line.startswith("| DEC-DOC-010 |"))
        second = next(i for i, line in enumerate(lines) if line.startswith("| DEC-AI-009 |"))
        lines[first], lines[second] = lines[second], lines[first]
        self.decision_md.write_text("\n".join(lines) + "\n", encoding="utf-8")
        rows = self._read_csv_rows()
        first = next(i for i, row in enumerate(rows) if row["DecisionId"] == "DEC-DOC-010")
        second = next(i for i, row in enumerate(rows) if row["DecisionId"] == "DEC-AI-009")
        rows[first], rows[second] = rows[second], rows[first]
        self._write_csv_rows(rows)

        errors = self._errors()
        self.assertTrue(any("order baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("row baseline hash drift" in error for error in errors), errors)
        self.assertFalse(any("Markdown/CSV row order drift" in error for error in errors), errors)

    def test_markdown_only_drift_is_rejected_by_parity(self) -> None:
        self._mutate_markdown_decision(
            "DEC-GOV-001", lambda row: row.replace("| Yönetişim |", "| MD-only |", 1)
        )

        self.assert_error_contains(
            "Decision registry Markdown/CSV semantic drift for 'DEC-GOV-001': area"
        )

    def test_csv_only_drift_is_rejected_by_parity(self) -> None:
        rows = self._read_csv_rows()
        for row in rows:
            if row["DecisionId"] == "DEC-GOV-001":
                row["Alan"] = "CSV-only"
        self._write_csv_rows(rows)

        self.assert_error_contains(
            "Decision registry Markdown/CSV semantic drift for 'DEC-GOV-001': area"
        )

    def test_csv_bom_and_crlf_do_not_create_false_parity_drift(self) -> None:
        csv_text = self.decision_csv.read_text(encoding="utf-8-sig")
        csv_payload = csv_text.replace("\r\n", "\n").replace("\n", "\r\n")
        self.decision_csv.write_bytes(b"\xef\xbb\xbf" + csv_payload.encode("utf-8"))
        md_text = self.decision_md.read_text(encoding="utf-8")
        self.decision_md.write_bytes(
            md_text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")
        )

        errors = self._errors()
        self.assertEqual([], errors)

    def assert_error_contains(self, expected: str) -> None:
        errors = self._errors()
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected {expected!r} in {errors!r}",
        )

    def _errors(self) -> list[str]:
        errors, _ = VALIDATOR.validate_repository_with_statistics(self.root)
        return errors

    def _read_csv_rows(self) -> list[dict[str, str]]:
        with self.decision_csv.open(encoding="utf-8-sig", newline="") as stream:
            return [dict(row) for row in csv.DictReader(stream)]

    def _write_csv_rows(self, rows: list[dict[str, str]]) -> None:
        fieldnames = list(VALIDATOR.DECISION_CSV_HEADERS)
        with self.decision_csv.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=fieldnames, lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)

    def _markdown_decision_line(self, decision_id: str) -> str:
        return next(
            line
            for line in self.decision_md.read_text(encoding="utf-8").splitlines()
            if line.startswith(f"| {decision_id} |")
        )

    def _mutate_markdown_decision(
        self, decision_id: str, transform: Callable[[str], str]
    ) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if line.startswith(f"| {decision_id} |"):
                lines[index] = transform(line)
                break
        else:
            self.fail(f"Missing Markdown decision {decision_id}")
        self.decision_md.write_text("\n".join(lines) + "\n", encoding="utf-8")

    def _remove_markdown_decision(self, decision_id: str) -> None:
        lines = self.decision_md.read_text(encoding="utf-8").splitlines()
        filtered = [line for line in lines if not line.startswith(f"| {decision_id} |")]
        self.assertEqual(len(lines) - 1, len(filtered))
        self.decision_md.write_text("\n".join(filtered) + "\n", encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
