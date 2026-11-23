-- Göymen ERP SQL Server Agent job TEMPLATE — creates the jobs DISABLED.
-- Decision: KLN-D36 — daily FULL backup + transaction LOG backup every 15 minutes.
-- Runbook: docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md
--
-- Optional. Sites without SQL Server Agent (for example Express) schedule
-- 10_backup.sql with the operating system scheduler instead.
--
-- HUMAN GATE: this template never enables anything. Both jobs and both
-- schedules are created with @enabled = 0. Enabling them, choosing the backup
-- directory, the retention period and the offsite copy are site decisions
-- (OPEN-017 / OPEN-014 stay open). The script refuses to overwrite existing jobs.
--
-- Required sqlcmd variables (no defaults):
--   ServerName                SQL Server instance the job steps connect to
--   DatabaseName              database to back up
--   ScriptDirectory           directory holding 10_backup.sql on the SQL Server host,
--                             without a trailing separator
--   BackupDirectory           passed to 10_backup.sql, without a trailing separator
--   Compression               ON | OFF
--   EncryptionAlgorithm       passed to 10_backup.sql, or empty
--   EncryptionCertificate     passed to 10_backup.sql, or empty
--   FullBackupStartTime       daily start of the FULL job, HHMMSS (for example 013000)
--   LogBackupIntervalMinutes  interval of the LOG job; must not exceed the RPO in
--                             infra/backup/backup-restore-policy.v1.json
--
-- The job steps run sqlcmd as the SQL Server Agent service account (-E).

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ServerName nvarchar(256) = N'$(ServerName)';
DECLARE @DatabaseName sysname = N'$(DatabaseName)';
DECLARE @ScriptDirectory nvarchar(1000) = N'$(ScriptDirectory)';
DECLARE @BackupDirectory nvarchar(1000) = N'$(BackupDirectory)';
DECLARE @Compression nvarchar(8) = UPPER(N'$(Compression)');
DECLARE @EncryptionAlgorithm nvarchar(32) = N'$(EncryptionAlgorithm)';
DECLARE @EncryptionCertificate sysname = N'$(EncryptionCertificate)';
DECLARE @FullBackupStartText nvarchar(16) = N'$(FullBackupStartTime)';
DECLARE @LogIntervalText nvarchar(16) = N'$(LogBackupIntervalMinutes)';

-- SQL Server Agent schedule codes (msdb.dbo.sp_add_schedule).
DECLARE @FrequencyDaily int = 4;
DECLARE @EveryDay int = 1;
DECLARE @SubdayOnce int = 1;
DECLARE @SubdayMinutes int = 4;
DECLARE @MinutesPerHour int = 60;

IF DB_ID(@DatabaseName) IS NULL
    THROW 59161, N'Veritabanı bulunamadı; DatabaseName değişkenini kontrol edin.', 1;

IF LEN(@ServerName) = 0 OR LEN(@ScriptDirectory) = 0 OR LEN(@BackupDirectory) = 0
    THROW 59162, N'ServerName, ScriptDirectory ve BackupDirectory boş olamaz.', 1;

IF CHARINDEX(N'"', @ServerName + @DatabaseName + @ScriptDirectory + @BackupDirectory
                   + @Compression + @EncryptionAlgorithm + @EncryptionCertificate) > 0
    THROW 59163, N'Değişkenler çift tırnak içeremez.', 1;

IF RIGHT(@ScriptDirectory, 1) IN (N'\', N'/') OR RIGHT(@BackupDirectory, 1) IN (N'\', N'/')
    THROW 59164, N'ScriptDirectory ve BackupDirectory dizin ayıracıyla bitmemelidir.', 1;

IF @Compression NOT IN (N'ON', N'OFF')
    THROW 59165, N'Compression yalnız ON veya OFF olabilir.', 1;

IF LEN(@FullBackupStartText) <> 6 OR @FullBackupStartText LIKE N'%[^0-9]%'
   OR TRY_CONVERT(int, LEFT(@FullBackupStartText, 2)) > 23
   OR TRY_CONVERT(int, SUBSTRING(@FullBackupStartText, 3, 2)) > 59
   OR TRY_CONVERT(int, RIGHT(@FullBackupStartText, 2)) > 59
    THROW 59166, N'FullBackupStartTime HHMMSS biçiminde geçerli bir saat olmalıdır.', 1;

IF LEN(@LogIntervalText) = 0 OR @LogIntervalText LIKE N'%[^0-9]%'
   OR TRY_CONVERT(int, @LogIntervalText) IS NULL
   OR TRY_CONVERT(int, @LogIntervalText) < 1
   OR TRY_CONVERT(int, @LogIntervalText) >= @MinutesPerHour
    THROW 59167, N'LogBackupIntervalMinutes 1 ile 59 arasında bir tam sayı olmalıdır.', 1;

DECLARE @FullBackupStart int = CONVERT(int, @FullBackupStartText);
DECLARE @LogInterval int = CONVERT(int, @LogIntervalText);
DECLARE @Separator nchar(1) = CASE WHEN CHARINDEX(N'\', @ScriptDirectory) > 0 THEN N'\' ELSE N'/' END;
DECLARE @FullJobName sysname = N'GoymenERP backup FULL ' + @DatabaseName;
DECLARE @LogJobName sysname = N'GoymenERP backup LOG ' + @DatabaseName;

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs AS j WHERE j.name IN (@FullJobName, @LogJobName))
    THROW 59168, N'Yedek işleri zaten var; şablon mevcut işin üzerine yazmaz.', 1;

DECLARE @CommandPrefix nvarchar(4000) =
    N'sqlcmd -S "' + @ServerName + N'" -E -b -i "' + @ScriptDirectory + @Separator + N'10_backup.sql"'
    + N' -v DatabaseName="' + @DatabaseName + N'"'
    + N' BackupDirectory="' + @BackupDirectory + N'"'
    + N' Compression="' + @Compression + N'"'
    + N' EncryptionAlgorithm="' + @EncryptionAlgorithm + N'"'
    + N' EncryptionCertificate="' + @EncryptionCertificate + N'"';
DECLARE @FullCommand nvarchar(4000) = @CommandPrefix + N' BackupKind="FULL"';
DECLARE @LogCommand nvarchar(4000) = @CommandPrefix + N' BackupKind="LOG"';

BEGIN TRANSACTION;

EXEC msdb.dbo.sp_add_job
    @job_name = @FullJobName,
    @enabled = 0,
    @description = N'KLN-D36 günlük tam yedek. Etkinleştirme insan kapısıdır.';
EXEC msdb.dbo.sp_add_jobstep
    @job_name = @FullJobName,
    @step_name = N'FULL backup and verify',
    @subsystem = N'CmdExec',
    @command = @FullCommand;
EXEC msdb.dbo.sp_add_schedule
    @schedule_name = @FullJobName,
    @enabled = 0,
    @freq_type = @FrequencyDaily,
    @freq_interval = @EveryDay,
    @freq_subday_type = @SubdayOnce,
    @active_start_time = @FullBackupStart;
EXEC msdb.dbo.sp_attach_schedule @job_name = @FullJobName, @schedule_name = @FullJobName;
EXEC msdb.dbo.sp_add_jobserver @job_name = @FullJobName;

EXEC msdb.dbo.sp_add_job
    @job_name = @LogJobName,
    @enabled = 0,
    @description = N'KLN-D36 işlem günlüğü yedeği. Etkinleştirme insan kapısıdır.';
EXEC msdb.dbo.sp_add_jobstep
    @job_name = @LogJobName,
    @step_name = N'LOG backup and verify',
    @subsystem = N'CmdExec',
    @command = @LogCommand;
EXEC msdb.dbo.sp_add_schedule
    @schedule_name = @LogJobName,
    @enabled = 0,
    @freq_type = @FrequencyDaily,
    @freq_interval = @EveryDay,
    @freq_subday_type = @SubdayMinutes,
    @freq_subday_interval = @LogInterval;
EXEC msdb.dbo.sp_attach_schedule @job_name = @LogJobName, @schedule_name = @LogJobName;
EXEC msdb.dbo.sp_add_jobserver @job_name = @LogJobName;

COMMIT TRANSACTION;

SELECT j.name AS JobName, j.enabled AS JobEnabled, s.name AS ScheduleName, s.enabled AS ScheduleEnabled
FROM msdb.dbo.sysjobs AS j
JOIN msdb.dbo.sysjobschedules AS js ON js.job_id = j.job_id
JOIN msdb.dbo.sysschedules AS s ON s.schedule_id = js.schedule_id
WHERE j.name IN (@FullJobName, @LogJobName)
ORDER BY j.name;

PRINT N'İşler ve zamanlamalar KAPALI oluşturuldu; etkinleştirme insan kapısıdır.';
