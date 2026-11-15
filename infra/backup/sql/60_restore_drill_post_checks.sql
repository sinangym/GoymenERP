-- Göymen ERP restore drill post-checks (run against the RESTORED drill database).
-- Decision: KLN-D36 — one restore drill per month.
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- Guards: the connected database must be the drill target and must differ from
-- the source (production) database name. The script reads only; DBCC CHECKDB
-- does not repair anything (no REPAIR option is ever used).
--
-- Required sqlcmd variables:
--   DrillDatabase    restored database name (connected database)
--   SourceDatabase   database the backups were taken from
--
-- Example (operator):
--   sqlcmd -S <server> -E -b -d GoymenErp_Drill -v DrillDatabase="GoymenErp_Drill" ^
--     SourceDatabase="GoymenErp" -i 60_restore_drill_post_checks.sql

SET NOCOUNT ON;

DECLARE @DrillDatabase sysname = N'$(DrillDatabase)';
DECLARE @SourceDatabase sysname = N'$(SourceDatabase)';

IF DB_NAME() <> @DrillDatabase
    THROW 59151, N'Bağlı veritabanı DrillDatabase değil; deneme kontrolleri yalnız geri yüklenen kopyada çalışır.', 1;

IF @DrillDatabase = @SourceDatabase
    THROW 59152, N'DrillDatabase kaynak veritabanıyla aynı olamaz.', 1;

-- 1) Physical and logical consistency of the restored copy.
DBCC CHECKDB WITH NO_INFOMSGS, ALL_ERRORMSGS;

-- 2) Every module migration history table and its latest applied migration.
--    The drill evidence records that each module history is present.
DECLARE @Histories TABLE (SchemaName sysname NOT NULL, TableName sysname NOT NULL, LastMigrationId nvarchar(150) NULL, MigrationCount int NOT NULL);
DECLARE @Schema sysname, @Table sysname, @Sql nvarchar(max);

DECLARE history_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name
    FROM sys.tables AS t
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE t.name IN (N'__EFMigrationsHistory', N'__GroupSharedCommandMigrationsHistory')
    ORDER BY s.name, t.name;

OPEN history_cursor;
FETCH NEXT FROM history_cursor INTO @Schema, @Table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'SELECT @SchemaName, @TableName, MAX(MigrationId), COUNT(*) FROM '
        + QUOTENAME(@Schema) + N'.' + QUOTENAME(@Table) + N';';
    INSERT INTO @Histories (SchemaName, TableName, LastMigrationId, MigrationCount)
    EXEC sys.sp_executesql @Sql, N'@SchemaName sysname, @TableName sysname', @SchemaName = @Schema, @TableName = @Table;
    FETCH NEXT FROM history_cursor INTO @Schema, @Table;
END;
CLOSE history_cursor;
DEALLOCATE history_cursor;

SELECT SchemaName, TableName, LastMigrationId, MigrationCount
FROM @Histories
ORDER BY SchemaName, TableName;

IF NOT EXISTS (SELECT 1 FROM @Histories WHERE MigrationCount > 0)
    THROW 59153, N'Geri yüklenen kopyada uygulanmış migration geçmişi bulunamadı.', 1;

PRINT N'Deneme kontrolleri geçti: CHECKDB temiz, migration geçmişi mevcut.';
