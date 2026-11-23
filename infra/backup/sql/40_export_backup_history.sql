-- Göymen ERP backup history export (read-only, msdb).
-- Output schema: goymen-erp-backup-history/v1, consumed by
--   python3 -I -S -B tools/verify_backup_restore.py history ...
--   python3 -I -S -B tools/verify_backup_restore.py restore-plan ...
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- Times are written in UTC. msdb stores local server time plus the offset in
-- 15-minute units (backupset.time_zone); 127 means "unknown", exported as null
-- so the verifier refuses the row instead of guessing.
--
-- Required sqlcmd variables:
--   DatabaseName   database whose backups are exported
--   SinceUtc       first backup start to export, e.g. 2026-11-01T00:00:00Z
--
-- Example (operator; -y 0 keeps the JSON on one unlimited line):
--   sqlcmd -S <server> -E -b -y 0 -h -1 -f 65001 -v DatabaseName="GoymenErp" ^
--     SinceUtc="2026-11-01T00:00:00Z" -i 40_export_backup_history.sql -o backup-history.json

SET NOCOUNT ON;

DECLARE @DatabaseName sysname = N'$(DatabaseName)';
DECLARE @SinceText nvarchar(32) = N'$(SinceUtc)';

IF DB_ID(@DatabaseName) IS NULL
    THROW 59131, N'Veritabanı bulunamadı; DatabaseName değişkenini kontrol edin.', 1;

IF LEN(@SinceText) <> 20 OR RIGHT(@SinceText, 1) <> N'Z'
   OR TRY_CONVERT(datetime2(0), LEFT(@SinceText, 19), 126) IS NULL
    THROW 59132, N'SinceUtc YYYY-MM-DDTHH:MM:SSZ biçiminde olmalıdır.', 1;

DECLARE @SinceUtc datetime2(0) = CONVERT(datetime2(0), LEFT(@SinceText, 19), 126);
DECLARE @UnknownTimeZone tinyint = 127;
DECLARE @MinutesPerTimeZoneUnit int = 15;

DECLARE @Json nvarchar(max) = (
    SELECT
        N'goymen-erp-backup-history/v1' AS [schema],
        d.name AS databaseName,
        d.recovery_model_desc AS recoveryModel,
        CONVERT(nvarchar(19), CAST(SYSUTCDATETIME() AS datetime2(0)), 126) + N'Z' AS exportedAtUtc,
        JSON_QUERY(ISNULL((
            SELECT
                b.backup_set_id AS backupSetId,
                b.type AS [type],
                CAST(b.is_copy_only AS bit) AS isCopyOnly,
                CASE WHEN b.time_zone = @UnknownTimeZone THEN NULL ELSE
                    CONVERT(nvarchar(19), CAST(DATEADD(minute, -b.time_zone * @MinutesPerTimeZoneUnit, b.backup_start_date) AS datetime2(0)), 126) + N'Z'
                END AS startUtc,
                CASE WHEN b.time_zone = @UnknownTimeZone THEN NULL ELSE
                    CONVERT(nvarchar(19), CAST(DATEADD(minute, -b.time_zone * @MinutesPerTimeZoneUnit, b.backup_finish_date) AS datetime2(0)), 126) + N'Z'
                END AS finishUtc,
                CASE WHEN b.time_zone = @UnknownTimeZone THEN NULL ELSE b.time_zone * @MinutesPerTimeZoneUnit END AS timeZoneOffsetMinutes,
                CONVERT(varchar(40), b.first_lsn) AS firstLsn,
                CONVERT(varchar(40), b.last_lsn) AS lastLsn,
                CONVERT(varchar(40), b.checkpoint_lsn) AS checkpointLsn,
                CONVERT(varchar(40), b.database_backup_lsn) AS databaseBackupLsn,
                CAST(b.has_backup_checksums AS bit) AS hasBackupChecksums,
                CAST(b.is_damaged AS bit) AS isDamaged,
                b.key_algorithm AS keyAlgorithm,
                b.position AS position,
                JSON_QUERY(ISNULL((
                    SELECT m.physical_device_name AS physicalDeviceName
                    FROM msdb.dbo.backupmediafamily AS m
                    WHERE m.media_set_id = b.media_set_id
                    ORDER BY m.family_sequence_number
                    FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS mediaFamilies,
                JSON_QUERY(ISNULL((
                    SELECT f.logical_name AS logicalName, f.file_type AS fileType
                    FROM msdb.dbo.backupfile AS f
                    WHERE f.backup_set_id = b.backup_set_id
                    ORDER BY f.file_type, f.logical_name
                    FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS files
            FROM msdb.dbo.backupset AS b
            WHERE b.database_name = d.name
              AND (b.time_zone = @UnknownTimeZone
                   OR DATEADD(minute, -b.time_zone * @MinutesPerTimeZoneUnit, b.backup_start_date) >= @SinceUtc)
            ORDER BY b.backup_start_date, b.backup_set_id
            FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS backups
    FROM sys.databases AS d
    WHERE d.name = @DatabaseName
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);

SELECT @Json;
