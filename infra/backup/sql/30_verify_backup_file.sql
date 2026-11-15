-- Göymen ERP backup file verification (no restore, no write to any database).
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- RESTORE VERIFYONLY reads the whole backup and checks the page checksums; it
-- does not prove that the database is consistent after a restore. Only the
-- monthly restore drill (KLN-D36) proves that.
--
-- Required sqlcmd variable:
--   BackupFile   full path of a .bak or .trn file on the SQL Server host
--
-- Example (operator):
--   sqlcmd -S <server> -E -b -v BackupFile="D:\Backup\GoymenErp\GoymenErp_FULL_20261115T010000Z.bak" ^
--     -i 30_verify_backup_file.sql

SET NOCOUNT ON;

DECLARE @BackupFile nvarchar(4000) = N'$(BackupFile)';

IF LEN(@BackupFile) = 0
    THROW 59121, N'BackupFile boş olamaz.', 1;

RESTORE VERIFYONLY FROM DISK = @BackupFile WITH CHECKSUM;

-- Header: backup type, LSN range, checksum and encryption state of the file.
RESTORE HEADERONLY FROM DISK = @BackupFile;

-- Logical files: needed for the MOVE clauses of a restore to another location.
RESTORE FILELISTONLY FROM DISK = @BackupFile;

PRINT N'Yedek dosyası okunabilir ve sağlama toplamları geçerli.';
