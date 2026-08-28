from __future__ import annotations

import unittest
from pathlib import Path

from tools.erp000i.validate_sql_server_search_capability_inventory import (
    validate_inventory_sql,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SQL_PATH = (
    REPOSITORY_ROOT
    / "tools"
    / "erp000i"
    / "sql_server_search_capability_inventory.sql"
)
EVIDENCE_TEMPLATE_PATH = (
    REPOSITORY_ROOT
    / "docs"
    / "evidence"
    / "erp-000i"
    / "SQL_SERVER_SEARCH_CAPABILITY_INVENTORY_EVIDENCE_TEMPLATE.md"
)


class SqlServerSearchCapabilityInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.sql = SQL_PATH.read_text(encoding="utf-8")

    def test_repository_inventory_is_valid(self) -> None:
        self.assertEqual([], validate_inventory_sql(self.sql))

    def test_mutation_and_ddl_verbs_are_rejected(self) -> None:
        forbidden_statements = (
            "INSERT INTO dbo.InventoryFixture (Id) VALUES (1);",
            "UPDATE dbo.InventoryFixture SET Id = 2;",
            "DELETE FROM dbo.InventoryFixture;",
            "MERGE dbo.InventoryFixture AS target "
            "USING dbo.SourceFixture AS source ON 1 = 0 "
            "WHEN NOT MATCHED THEN INSERT (Id) VALUES (1);",
            "CREATE TABLE dbo.InventoryFixture (Id int);",
            "ALTER TABLE dbo.InventoryFixture ADD Value int;",
            "DROP TABLE dbo.InventoryFixture;",
            "TRUNCATE TABLE dbo.InventoryFixture;",
        )
        for statement in forbidden_statements:
            with self.subTest(statement=statement.split()[0]):
                errors = validate_inventory_sql(self.sql + "\n" + statement)
                self.assert_error_contains(errors, "Forbidden non-query SQL keyword")

    def test_exec_and_dynamic_external_access_are_rejected(self) -> None:
        for statement in (
            "EXEC sys.sp_executesql N'SELECT 1';",
            "EXECUTE sys.sp_help;",
            "SELECT * FROM OPENROWSET(BULK N'payload.bin', SINGLE_BLOB) AS payload;",
        ):
            with self.subTest(statement=statement.split()[0]):
                errors = validate_inventory_sql(self.sql + "\n" + statement)
                self.assertTrue(
                    any(
                        marker in error
                        for marker in (
                            "Forbidden non-query SQL keyword",
                            "External or dynamic data access is forbidden",
                        )
                        for error in errors
                    ),
                    errors,
                )

    def test_connection_string_and_secret_shapes_are_rejected(self) -> None:
        sensitive_fragments = (
            "Server=sql.example.invalid;Initial Catalog=erp",
            "Password=not-a-real-secret",
            "Client_Secret:not-a-real-secret",
            "mssql://example.invalid/database",
        )
        for fragment in sensitive_fragments:
            with self.subTest(fragment=fragment.split("=")[0]):
                errors = validate_inventory_sql(self.sql + f"\n-- {fragment}\n")
                self.assert_error_contains(
                    errors, "Connection-string or secret-shaped text is forbidden"
                )

    def test_counterparty_cari_and_other_user_tables_are_rejected(self) -> None:
        user_sources = (
            "dbo.Counterparty",
            "dbo.Cari",
            "dbo.AnyBusinessTable",
        )
        for source in user_sources:
            with self.subTest(source=source):
                errors = validate_inventory_sql(
                    self.sql + f"\nSELECT Id FROM {source};\n"
                )
                self.assertTrue(
                    any(
                        marker in error
                        for marker in (
                            "Business-domain identifier is forbidden",
                            "Non-system or unapproved relation source",
                        )
                        for error in errors
                    ),
                    errors,
                )

    def test_comma_join_and_quoted_scalar_subquery_cannot_bypass_canonical_digest(self) -> None:
        variants = (
            self.sql.replace(
                "FROM sys.fulltext_languages",
                "FROM sys.fulltext_languages, dbo.AnyBusinessTable",
            ),
            self.sql.replace(
                "CONVERT(int, SERVERPROPERTY(N'IsFullTextInstalled')) "
                "AS is_full_text_installed",
                "CONVERT(int, SERVERPROPERTY(N'IsFullTextInstalled')) "
                "AS is_full_text_installed, "
                '(SELECT TOP (1) Id FROM "dbo"."AnyBusinessTable")',
            ),
        )
        for variant in variants:
            with self.subTest(variant=variant[-120:]):
                errors = validate_inventory_sql(variant)
                self.assert_error_contains(
                    errors, "Canonical SQL content digest mismatch"
                )

    def test_semicolonless_state_change_cannot_bypass_canonical_digest(self) -> None:
        changed_sql = self.sql.replace(
            "ORDER BY stoplist_id;",
            "ORDER BY stoplist_id DISABLE TRIGGER ALL ON DATABASE;",
        )

        errors = validate_inventory_sql(changed_sql)

        self.assert_error_contains(errors, "Canonical SQL content digest mismatch")

    def test_sqlcmd_preprocessing_is_rejected(self) -> None:
        directives = (
            ":r external.sql",
            "!! whoami",
            "SELECT N'$(InjectedValue)' AS injected_value;",
        )
        for directive in directives:
            with self.subTest(directive=directive):
                errors = validate_inventory_sql(self.sql + "\n" + directive + "\n")
                self.assert_error_contains(
                    errors,
                    "SQLCMD preprocessing directives or substitutions are forbidden",
                )

    def test_required_metadata_sources_are_structural_not_result_values(self) -> None:
        changed_sql = self.sql.replace(
            "sys.fulltext_catalogs", "sys.fulltext_languages"
        )
        errors = validate_inventory_sql(changed_sql)
        self.assert_error_contains(
            errors, "Missing metadata source: sys.fulltext_catalogs"
        )

    def test_required_aliases_are_structural_not_result_values(self) -> None:
        changed_sql = self.sql.replace("database_collation", "collation_observation")
        errors = validate_inventory_sql(changed_sql)
        self.assert_error_contains(errors, "Missing result alias: database_collation")

    def test_unrequested_server_property_is_rejected(self) -> None:
        changed_sql = self.sql.replace(
            "SERVERPROPERTY(N'ProductLevel')",
            "SERVERPROPERTY(N'MachineName')",
        )
        errors = validate_inventory_sql(changed_sql)
        self.assert_error_contains(errors, "Missing SERVERPROPERTY field: productlevel")
        self.assert_error_contains(
            errors, "Unexpected SERVERPROPERTY field: machinename"
        )

    def test_evidence_template_is_explicitly_decision_neutral(self) -> None:
        evidence = EVIDENCE_TEMPLATE_PATH.read_text(encoding="utf-8").casefold()
        required_boundaries = (
            "global search poc kabulü değildir",
            "veritabanı sağlayıcısı kabulü değildir",
            "collation kararı veya kabulü değildir",
            "dil/lcid",
            "performans/operasyon eşiği kabulü değildir",
            "bu şablon karar kapatmaz",
        )
        for boundary in required_boundaries:
            with self.subTest(boundary=boundary):
                self.assertIn(boundary, evidence)

    def assert_error_contains(self, errors: list[str], expected: str) -> None:
        self.assertTrue(
            any(expected in error for error in errors),
            f"Expected an error containing {expected!r}, got: {errors!r}",
        )


if __name__ == "__main__":
    unittest.main()
