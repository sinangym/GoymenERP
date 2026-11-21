-- Göymen ERP document object manifest export (read-only).
-- Output schema: goymen-erp-document-blob-manifest/v1, consumed by
--   python3 -I -S -B tools/verify_backup_restore.py objects --manifest ... --root ...
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- The Belge Kasası bytes live outside SQL Server, in the object store directory
-- (keys companies/{company}/sha256/{hash}). A database restore is only complete
-- when every blob row of the restored database has its object in the restored
-- object directory with the same size and SHA-256. Run this script against the
-- RESTORED database (drill target), not against production.
--
-- Required sqlcmd variable:
--   DatabaseName   the database this script must be connected to (guard)
--
-- Example (operator):
--   sqlcmd -S <server> -E -b -y 0 -h -1 -f 65001 -d GoymenErp_Drill ^
--     -v DatabaseName="GoymenErp_Drill" -i 50_export_document_blob_manifest.sql -o blob-manifest.json

SET NOCOUNT ON;

IF DB_NAME() <> N'$(DatabaseName)'
    THROW 59141, N'Bağlı veritabanı DatabaseName ile aynı değil; -d ve DatabaseName değerlerini kontrol edin.', 1;

IF OBJECT_ID(N'documents.ContentBlobs', N'U') IS NULL
    THROW 59142, N'documents.ContentBlobs tablosu bulunamadı; Belge Kasası şeması bu veritabanında yok.', 1;

DECLARE @Json nvarchar(max) = (
    SELECT
        N'goymen-erp-document-blob-manifest/v1' AS [schema],
        DB_NAME() AS databaseName,
        CONVERT(nvarchar(19), CAST(SYSUTCDATETIME() AS datetime2(0)), 126) + N'Z' AS exportedAtUtc,
        JSON_QUERY(ISNULL((
            SELECT
                LOWER(CONVERT(char(36), cb.CompanyId)) AS companyId,
                cb.Sha256 AS sha256,
                cb.SizeBytes AS sizeBytes,
                cb.ObjectKey AS objectKey
            FROM documents.ContentBlobs AS cb
            ORDER BY cb.CompanyId, cb.Sha256
            FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS blobs
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);

SELECT @Json;
