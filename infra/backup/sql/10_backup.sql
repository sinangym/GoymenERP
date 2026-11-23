-- Göymen ERP backup (FULL or LOG) followed by RESTORE VERIFYONLY.
-- Decision: KLN-D36 — daily FULL + transaction LOG every 15 minutes.
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- The repository never runs this script and never schedules it. Scheduling it
-- (SQL Agent, Windows Task Scheduler, cron) and choosing the target directory,
-- retention and offsite copy are site decisions behind the human gate
-- (OPEN-017 / OPEN-014 stay open).
--
-- Required sqlcmd variables (no defaults; sqlcmd stops when one is missing):
--   DatabaseName           database to back up
--   BackupKind             FULL | LOG
--   BackupDirectory        existing directory on the SQL Server host, WITHOUT a trailing
--                          separator (a trailing \ before a closing quote breaks Windows
--                          command-line quoting); the separator is taken from the path
--   Compression            ON | OFF (OFF on editions without backup compression)
--   EncryptionAlgorithm    AES_128 | AES_192 | AES_256 | TRIPLE_DES_3KEY, or empty
--   EncryptionCertificate  server certificate name in master, or empty
-- EncryptionAlgorithm and EncryptionCertificate are given together or both left
-- empty. Whether native encryption is mandatory is an open site decision.
--
-- Example (operator):
--   sqlcmd -S <server> -E -b -v DatabaseName="GoymenErp" BackupKind="LOG" ^
--     BackupDirectory="D:\Backup\GoymenErp" Compression="ON" ^
--     EncryptionAlgorithm="" EncryptionCertificate="" -i 10_backup.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName sysname = N'$(DatabaseName)';
DECLARE @BackupKind nvarchar(16) = UPPER(N'$(BackupKind)');
DECLARE @BackupDirectory nvarchar(3000) = N'$(BackupDirectory)';
DECLARE @Compression nvarchar(8) = UPPER(N'$(Compression)');
DECLARE @EncryptionAlgorithm nvarchar(32) = UPPER(N'$(EncryptionAlgorithm)');
DECLARE @EncryptionCertificate sysname = N'$(EncryptionCertificate)';

IF DB_ID(@DatabaseName) IS NULL
    THROW 59111, N'Veritabanı bulunamadı; DatabaseName değişkenini kontrol edin.', 1;

IF @BackupKind NOT IN (N'FULL', N'LOG')
    THROW 59112, N'BackupKind yalnız FULL veya LOG olabilir.', 1;

IF LEN(@BackupDirectory) = 0 OR RIGHT(@BackupDirectory, 1) IN (N'\', N'/')
    THROW 59113, N'BackupDirectory boş olamaz ve dizin ayıracıyla (\ veya /) bitmemelidir.', 1;

DECLARE @Separator nchar(1) = CASE WHEN CHARINDEX(N'\', @BackupDirectory) > 0 THEN N'\' ELSE N'/' END;

IF @Compression NOT IN (N'ON', N'OFF')
    THROW 59114, N'Compression yalnız ON veya OFF olabilir.', 1;

IF (LEN(@EncryptionAlgorithm) = 0 AND LEN(@EncryptionCertificate) > 0)
   OR (LEN(@EncryptionAlgorithm) > 0 AND LEN(@EncryptionCertificate) = 0)
    THROW 59115, N'EncryptionAlgorithm ve EncryptionCertificate birlikte verilmeli veya ikisi de boş bırakılmalıdır.', 1;

IF LEN(@EncryptionAlgorithm) > 0
   AND @EncryptionAlgorithm NOT IN (N'AES_128', N'AES_192', N'AES_256', N'TRIPLE_DES_3KEY')
    THROW 59116, N'EncryptionAlgorithm desteklenen listede değil.', 1;

IF LEN(@EncryptionCertificate) > 0
   AND NOT EXISTS (SELECT 1 FROM master.sys.certificates AS c WHERE c.name = @EncryptionCertificate)
    THROW 59117, N'EncryptionCertificate master veritabanında bulunamadı.', 1;

IF @BackupKind = N'LOG'
   AND NOT EXISTS (
       SELECT 1 FROM sys.databases AS d
       WHERE d.name = @DatabaseName AND d.recovery_model_desc = N'FULL')
    THROW 59118, N'İşlem günlüğü yedeği için kurtarma modeli FULL olmalıdır.', 1;

DECLARE @NowUtc datetime2(0) = SYSUTCDATETIME();
DECLARE @Stamp nvarchar(32) =
    CONVERT(nvarchar(8), @NowUtc, 112) + N'T' + REPLACE(CONVERT(nvarchar(8), @NowUtc, 108), N':', N'') + N'Z';
DECLARE @Extension nvarchar(8) = CASE @BackupKind WHEN N'FULL' THEN N'.bak' ELSE N'.trn' END;
DECLARE @BackupFile nvarchar(4000) =
    @BackupDirectory + @Separator + @DatabaseName + N'_' + @BackupKind + N'_' + @Stamp + @Extension;
DECLARE @BackupSetName nvarchar(128) = LEFT(@DatabaseName + N' ' + @BackupKind + N' ' + @Stamp, 128);

DECLARE @Options nvarchar(max) =
    N'CHECKSUM, FORMAT, INIT, NAME = @BackupSetName, '
    + CASE @Compression WHEN N'ON' THEN N'COMPRESSION' ELSE N'NO_COMPRESSION' END
    + CASE WHEN LEN(@EncryptionAlgorithm) > 0
           THEN N', ENCRYPTION (ALGORITHM = ' + @EncryptionAlgorithm
                + N', SERVER CERTIFICATE = ' + QUOTENAME(@EncryptionCertificate) + N')'
           ELSE N'' END;

DECLARE @Statement nvarchar(max) =
    CASE @BackupKind
        WHEN N'FULL' THEN N'BACKUP DATABASE ' + QUOTENAME(@DatabaseName)
        ELSE N'BACKUP LOG ' + QUOTENAME(@DatabaseName)
    END
    + N' TO DISK = @BackupFile WITH ' + @Options + N';';

EXEC sys.sp_executesql
    @Statement,
    N'@BackupFile nvarchar(4000), @BackupSetName nvarchar(128)',
    @BackupFile = @BackupFile,
    @BackupSetName = @BackupSetName;

RESTORE VERIFYONLY FROM DISK = @BackupFile WITH CHECKSUM;

SELECT
    @DatabaseName AS DatabaseName,
    @BackupKind AS BackupKind,
    @BackupFile AS BackupFile,
    @NowUtc AS StartedUtc;
