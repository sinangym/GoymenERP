-- Göymen ERP backup preflight (read-only).
-- Decision: KLN-D36 (RPO 15 min / RTO 4 h). Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- This script only reads catalog views. It never changes the recovery model,
-- never creates a backup and never enables a job. Switching a database to FULL
-- recovery and starting the log chain is an operator step behind the human gate.
--
-- Usage (operator, never CI):
--   sqlcmd -S <server> -E -b -v DatabaseName="GoymenErp" -i 00_preflight_readonly.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName sysname = N'$(DatabaseName)';

IF DB_ID(@DatabaseName) IS NULL
    THROW 59101, N'Veritabanı bulunamadı; DatabaseName değişkenini kontrol edin.', 1;

SELECT
    d.name AS DatabaseName,
    d.recovery_model_desc AS RecoveryModel,
    d.page_verify_option_desc AS PageVerify,
    d.state_desc AS DatabaseState,
    d.is_read_committed_snapshot_on AS ReadCommittedSnapshot,
    d.log_reuse_wait_desc AS LogReuseWait,
    d.is_encrypted AS TransparentDataEncryption,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) AS ServerEdition,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')) AS ProductVersion
FROM sys.databases AS d
WHERE d.name = @DatabaseName;

-- Certificates usable for native backup encryption. A certificate whose private
-- key was never backed up makes every encrypted backup unrestorable elsewhere.
SELECT
    c.name AS CertificateName,
    c.subject AS Subject,
    c.expiry_date AS ExpiryDateUtc,
    c.pvt_key_last_backup_date AS PrivateKeyLastBackupDate
FROM master.sys.certificates AS c
WHERE c.name NOT LIKE N'##%'
ORDER BY c.name;

-- Last finished backup of each kind for this database.
SELECT
    b.type AS BackupType,
    MAX(b.backup_finish_date) AS LastFinishLocal
FROM msdb.dbo.backupset AS b
WHERE b.database_name = @DatabaseName
  AND b.is_copy_only = 0
GROUP BY b.type
ORDER BY b.type;

IF NOT EXISTS (
    SELECT 1
    FROM sys.databases AS d
    WHERE d.name = @DatabaseName
      AND d.recovery_model_desc = N'FULL')
    THROW 59102, N'Kurtarma modeli FULL değil; 15 dakikalık işlem günlüğü yedeği (KLN-D36) bu modelde alınamaz.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.databases AS d
    WHERE d.name = @DatabaseName
      AND d.page_verify_option_desc = N'CHECKSUM')
    THROW 59103, N'PAGE_VERIFY CHECKSUM değil; bozuk sayfalar yedekte fark edilmeyebilir.', 1;

PRINT N'Ön kontrol geçti: kurtarma modeli FULL, PAGE_VERIFY CHECKSUM.';
