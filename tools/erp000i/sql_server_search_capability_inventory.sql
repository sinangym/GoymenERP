/*
ERP-000I decision-neutral SQL Server search capability inventory.
The result is capability evidence input; it does not make an acceptance decision.
*/

SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')) AS product_version,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductLevel')) AS product_level,
    CONVERT(nvarchar(256), SERVERPROPERTY(N'Edition')) AS edition,
    CONVERT(int, SERVERPROPERTY(N'EngineEdition')) AS engine_edition,
    CONVERT(int, SERVERPROPERTY(N'IsFullTextInstalled')) AS is_full_text_installed;

SELECT
    CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), N'Collation')) AS database_collation;

SELECT
    lcid AS language_lcid,
    name AS language_name
FROM sys.fulltext_languages
ORDER BY lcid;

SELECT
    fulltext_catalog_id AS catalog_id,
    name AS catalog_name,
    is_default AS is_default_catalog,
    is_accent_sensitivity_on AS is_accent_sensitive
FROM sys.fulltext_catalogs
ORDER BY fulltext_catalog_id;

SELECT
    stoplist_id AS stoplist_id,
    name AS stoplist_name
FROM sys.fulltext_stoplists
ORDER BY stoplist_id;
