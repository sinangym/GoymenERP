#!/usr/bin/env python3
"""Fail-closed static validation for the ERP-000I metadata inventory SQL."""

from __future__ import annotations

import argparse
import hashlib
import re
import sys
from pathlib import Path


REQUIRED_METADATA_SOURCES = {
    "sys.fulltext_catalogs",
    "sys.fulltext_languages",
    "sys.fulltext_stoplists",
}
EXPECTED_NORMALIZED_SQL_SHA256 = (
    "9344df23a2188d5fd20289291fc726fcd72bf51b5d96dc5af2a33ed6c2a7e540"
)
REQUIRED_SERVER_PROPERTIES = {
    "edition",
    "engineedition",
    "isfulltextinstalled",
    "productlevel",
    "productversion",
}
REQUIRED_RESULT_ALIASES = {
    "catalog_id",
    "catalog_name",
    "database_collation",
    "edition",
    "engine_edition",
    "is_accent_sensitive",
    "is_default_catalog",
    "is_full_text_installed",
    "language_lcid",
    "language_name",
    "product_level",
    "product_version",
    "stoplist_id",
    "stoplist_name",
}
ALLOWED_FUNCTIONS = {
    "convert",
    "databasepropertyex",
    "db_name",
    "nvarchar",
    "serverproperty",
}
FORBIDDEN_SQL_KEYWORDS = {
    "alter",
    "backup",
    "begin",
    "bulk",
    "commit",
    "create",
    "dbcc",
    "delete",
    "deny",
    "drop",
    "execute",
    "exec",
    "grant",
    "insert",
    "merge",
    "raiserror",
    "restore",
    "revoke",
    "rollback",
    "set",
    "throw",
    "truncate",
    "update",
    "use",
    "waitfor",
}
FORBIDDEN_DOMAIN_IDENTIFIERS = {"cari", "counterparty"}
SENSITIVE_TEXT_PATTERNS = (
    re.compile(
        r"\b(?:server|data\s+source|initial\s+catalog|user\s+id|uid|"
        r"password|pwd|integrated\s+security|trusted_connection|"
        r"trustservercertificate)\s*=",
        flags=re.IGNORECASE,
    ),
    re.compile(
        r"\b(?:api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]",
        flags=re.IGNORECASE,
    ),
    re.compile(r"\b(?:mssql|sqlserver)://", flags=re.IGNORECASE),
)


def _strip_sql_comments(sql: str) -> str:
    """Remove line and nested block comments while preserving quoted text."""

    output: list[str] = []
    index = 0
    block_depth = 0
    in_line_comment = False
    in_string = False

    while index < len(sql):
        current = sql[index]
        following = sql[index + 1] if index + 1 < len(sql) else ""

        if in_line_comment:
            if current in "\r\n":
                in_line_comment = False
                output.append(current)
            else:
                output.append(" ")
            index += 1
            continue

        if block_depth:
            if current == "/" and following == "*":
                block_depth += 1
                output.extend((" ", " "))
                index += 2
            elif current == "*" and following == "/":
                block_depth -= 1
                output.extend((" ", " "))
                index += 2
            else:
                output.append(current if current in "\r\n" else " ")
                index += 1
            continue

        if in_string:
            output.append(current)
            if current == "'":
                if following == "'":
                    output.append(following)
                    index += 2
                    continue
                in_string = False
            index += 1
            continue

        if current == "-" and following == "-":
            in_line_comment = True
            output.extend((" ", " "))
            index += 2
        elif current == "/" and following == "*":
            block_depth = 1
            output.extend((" ", " "))
            index += 2
        else:
            output.append(current)
            if current == "'":
                in_string = True
            index += 1

    return "".join(output)


def _mask_sql_strings(sql: str) -> str:
    """Mask string literal contents so values cannot be parsed as SQL tokens."""

    output: list[str] = []
    index = 0
    in_string = False
    while index < len(sql):
        current = sql[index]
        following = sql[index + 1] if index + 1 < len(sql) else ""
        if in_string:
            output.append(" ")
            if current == "'":
                if following == "'":
                    output.append(" ")
                    index += 2
                    continue
                in_string = False
            index += 1
            continue
        output.append(current)
        if current == "'":
            in_string = True
        index += 1
    return "".join(output)


def _normalise_relation(raw_relation: str) -> str:
    return re.sub(r"[\[\]\s]", "", raw_relation).lower()


def validate_inventory_sql(sql: str) -> list[str]:
    """Return every static contract violation found in ``sql``."""

    errors: list[str] = []
    normalized_sql = sql.replace("\r\n", "\n").replace("\r", "\n")
    actual_digest = hashlib.sha256(normalized_sql.encode("utf-8")).hexdigest()
    if actual_digest != EXPECTED_NORMALIZED_SQL_SHA256:
        errors.append(
            "Canonical SQL content digest mismatch: "
            f"expected {EXPECTED_NORMALIZED_SQL_SHA256}, got {actual_digest}"
        )

    if re.search(r"(?m)^\s*(?::[A-Za-z]+|!!)", sql) or "$(" in sql:
        errors.append("SQLCMD preprocessing directives or substitutions are forbidden")

    for pattern in SENSITIVE_TEXT_PATTERNS:
        if pattern.search(sql):
            errors.append("Connection-string or secret-shaped text is forbidden")
            break

    without_comments = _strip_sql_comments(sql)
    executable = _mask_sql_strings(without_comments)

    for keyword in sorted(FORBIDDEN_SQL_KEYWORDS):
        if re.search(rf"\b{re.escape(keyword)}\b", executable, flags=re.IGNORECASE):
            errors.append(f"Forbidden non-query SQL keyword: {keyword.upper()}")

    if re.search(r"\bSELECT\b[\s\S]*?\bINTO\b", executable, flags=re.IGNORECASE):
        errors.append("SELECT INTO is forbidden")
    if re.search(
        r"\b(?:OPENROWSET|OPENQUERY|OPENDATASOURCE)\b",
        executable,
        flags=re.IGNORECASE,
    ):
        errors.append("External or dynamic data access is forbidden")
    if re.search(r"(?:^|\s)GO(?:\s|$)", executable, flags=re.IGNORECASE):
        errors.append("Batch separators are forbidden")
    if re.search(r"\bSELECT\s+\*", executable, flags=re.IGNORECASE):
        errors.append("Wildcard selection is forbidden")

    for identifier in sorted(FORBIDDEN_DOMAIN_IDENTIFIERS):
        if re.search(rf"\b{identifier}\b", executable, flags=re.IGNORECASE):
            errors.append(f"Business-domain identifier is forbidden: {identifier}")

    statements = [
        statement.strip()
        for statement in executable.split(";")
        if statement.strip()
    ]
    if len(statements) != 5:
        errors.append(
            f"Expected exactly five metadata SELECT statements, got {len(statements)}"
        )
    for index, statement in enumerate(statements, start=1):
        if not re.match(r"^SELECT\b", statement, flags=re.IGNORECASE):
            errors.append(f"Statement {index} is not a SELECT query")

    relation_pattern = re.compile(
        r"\b(?:FROM|JOIN)\s+"
        r"((?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$#]*)"
        r"(?:\s*\.\s*(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$#]*)){0,3})",
        flags=re.IGNORECASE,
    )
    actual_sources = {
        _normalise_relation(match.group(1))
        for match in relation_pattern.finditer(executable)
    }
    unexpected_sources = actual_sources - REQUIRED_METADATA_SOURCES
    missing_sources = REQUIRED_METADATA_SOURCES - actual_sources
    if unexpected_sources:
        errors.append(
            "Non-system or unapproved relation source: "
            + ", ".join(sorted(unexpected_sources))
        )
    if missing_sources:
        errors.append("Missing metadata source: " + ", ".join(sorted(missing_sources)))

    actual_properties = {
        value.lower()
        for value in re.findall(
            r"\bSERVERPROPERTY\s*\(\s*N?'([^']+)'\s*\)",
            without_comments,
            flags=re.IGNORECASE,
        )
    }
    missing_properties = REQUIRED_SERVER_PROPERTIES - actual_properties
    unexpected_properties = actual_properties - REQUIRED_SERVER_PROPERTIES
    if missing_properties:
        errors.append(
            "Missing SERVERPROPERTY field: "
            + ", ".join(sorted(missing_properties))
        )
    if unexpected_properties:
        errors.append(
            "Unexpected SERVERPROPERTY field: "
            + ", ".join(sorted(unexpected_properties))
        )

    if not re.search(
        r"\bDATABASEPROPERTYEX\s*\(\s*DB_NAME\s*\(\s*\)\s*,\s*N?'Collation'\s*\)",
        without_comments,
        flags=re.IGNORECASE,
    ):
        errors.append("Current-database collation metadata query is missing")

    actual_aliases = {
        alias.lower()
        for alias in re.findall(
            r"\bAS\s+([A-Za-z_][A-Za-z0-9_]*)",
            executable,
            flags=re.IGNORECASE,
        )
    }
    missing_aliases = REQUIRED_RESULT_ALIASES - actual_aliases
    unexpected_aliases = actual_aliases - REQUIRED_RESULT_ALIASES
    if missing_aliases:
        errors.append("Missing result alias: " + ", ".join(sorted(missing_aliases)))
    if unexpected_aliases:
        errors.append(
            "Unexpected result alias: " + ", ".join(sorted(unexpected_aliases))
        )

    actual_functions = {
        function.lower()
        for function in re.findall(
            r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", executable, flags=re.IGNORECASE
        )
    }
    unexpected_functions = actual_functions - ALLOWED_FUNCTIONS
    if unexpected_functions:
        errors.append(
            "Unexpected SQL function: " + ", ".join(sorted(unexpected_functions))
        )

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate the ERP-000I SQL Server metadata inventory SQL."
    )
    parser.add_argument(
        "sql_file",
        nargs="?",
        type=Path,
        default=Path(__file__).with_name("sql_server_search_capability_inventory.sql"),
    )
    arguments = parser.parse_args(argv)

    try:
        sql = arguments.sql_file.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        print(f"Cannot read SQL inventory: {exc}", file=sys.stderr)
        return 1

    errors = validate_inventory_sql(sql)
    if errors:
        print("ERP-000I SQL capability inventory validation FAILED", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("ERP-000I SQL capability inventory validation PASSED")
    print("- five read-only metadata queries")
    print("- required sources and result aliases present")
    print("- mutation, external access and sensitive text absent")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
