using System.Globalization;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>
/// Retention of idempotency records, bound from <c>Idempotency:Retention</c> (OPEN-021, NS-12: the period starts at the
/// configured value and is changed from settings). The period is never a code constant. The disposal job is coded but
/// stays OFF unless <c>Idempotency:Retention:Purge:Enabled</c> is true; enabling it is a human activation gate, and when
/// it is enabled every value below must be configured explicitly or start-up is refused (fail-closed).
/// </summary>
public sealed class IdempotencyRetentionOptions
{
    public const string SectionName = "Idempotency:Retention";
    public const string PurgeSectionName = SectionName + ":Purge";

    private IdempotencyRetentionOptions(
        IdempotencyRetentionPeriod? period,
        string? policyRef,
        bool purgeEnabled,
        int? batchSize,
        TimeSpan? pollInterval,
        TimeSpan? commandTimeout)
    {
        Period = period;
        PolicyRef = policyRef;
        PurgeEnabled = purgeEnabled;
        BatchSize = batchSize;
        PollInterval = pollInterval;
        CommandTimeout = commandTimeout;
    }

    public IdempotencyRetentionPeriod? Period { get; }

    public string? PolicyRef { get; }

    public bool PurgeEnabled { get; }

    public int? BatchSize { get; }

    public TimeSpan? PollInterval { get; }

    public TimeSpan? CommandTimeout { get; }

    /// <summary>
    /// Reads the section. A value that is present but malformed is always refused; a missing value is refused only when
    /// the purge is enabled (see <see cref="ResolvePurge"/>).
    /// </summary>
    public static IdempotencyRetentionOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var purge = configuration.GetSection(PurgeSectionName);

        IdempotencyRetentionPeriod? period = null;
        var periodText = section["Period"];
        if (!string.IsNullOrWhiteSpace(periodText))
        {
            if (!IdempotencyRetentionPeriod.TryParse(periodText, out var parsed)
                || parsed.ToString().Length > IdempotencyRetentionPeriod.MaxTextLength)
            {
                throw new InvalidOperationException(
                    $"Idempotency saklama süresi geçersiz: {SectionName}:Period (ISO 8601, ör. P90D).");
            }

            period = parsed;
        }

        string? policyRef = null;
        var policyText = section["PolicyRef"];
        if (!string.IsNullOrWhiteSpace(policyText))
        {
            if (policyText.Trim().Length > IdempotencyRetentionPurgeRecord.PolicyRefMaxLength)
            {
                throw new InvalidOperationException(
                    $"Idempotency saklama politika referansı çok uzun: {SectionName}:PolicyRef.");
            }

            policyRef = policyText.Trim();
        }

        var enabledText = purge["Enabled"];
        var enabled = false;
        if (!string.IsNullOrWhiteSpace(enabledText) && !bool.TryParse(enabledText, out enabled))
        {
            throw new InvalidOperationException(
                $"Idempotency saklama imha işi açma ayarı geçersiz: {PurgeSectionName}:Enabled.");
        }

        return new IdempotencyRetentionOptions(
            period,
            policyRef,
            enabled,
            ReadPositiveInt(purge, "BatchSize"),
            ReadPositiveTimeSpan(purge, "PollInterval"),
            ReadPositiveTimeSpan(purge, "CommandTimeout"));
    }

    /// <summary>The validated purge settings; refuses start-up when any value the enabled job needs is missing.</summary>
    public IdempotencyRetentionPurgeSettings ResolvePurge()
    {
        if (!PurgeEnabled)
        {
            throw new InvalidOperationException(
                $"Idempotency saklama imha işi kapalı: {PurgeSectionName}:Enabled açıkça true olmalıdır.");
        }

        var missing = new List<string>();
        if (Period is null) missing.Add(SectionName + ":Period");
        if (PolicyRef is null) missing.Add(SectionName + ":PolicyRef");
        if (BatchSize is null) missing.Add(PurgeSectionName + ":BatchSize");
        if (PollInterval is null) missing.Add(PurgeSectionName + ":PollInterval");
        if (CommandTimeout is null) missing.Add(PurgeSectionName + ":CommandTimeout");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Idempotency saklama imha işi açık ama şu ayarlar tanımlı değil: " + string.Join(", ", missing) + ".");
        }

        return new IdempotencyRetentionPurgeSettings(
            Period!.Value,
            PolicyRef!,
            BatchSize!.Value,
            PollInterval!.Value,
            CommandTimeout!.Value);
    }

    private static int? ReadPositiveInt(IConfiguration section, string key)
    {
        var text = section[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException(
                $"Idempotency saklama imha ayarı geçersiz: {PurgeSectionName}:{key} pozitif bir tam sayı olmalıdır.");
    }

    private static TimeSpan? ReadPositiveTimeSpan(IConfiguration section, string key)
    {
        var text = section[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value)
               && value > TimeSpan.Zero
               && value.TotalMilliseconds <= int.MaxValue
            ? value
            : throw new InvalidOperationException(
                $"Idempotency saklama imha ayarı geçersiz: {PurgeSectionName}:{key} pozitif bir süre olmalıdır.");
    }
}

/// <summary>The resolved settings of the enabled purge job.</summary>
public sealed record IdempotencyRetentionPurgeSettings(
    IdempotencyRetentionPeriod Period,
    string PolicyRef,
    int BatchSize,
    TimeSpan PollInterval,
    TimeSpan CommandTimeout);
