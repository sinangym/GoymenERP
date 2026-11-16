using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.Companies;

public enum CompanyTaxIdCorrectionStatus
{
    Pending = 1,
    Applied = 2,
}

/// <summary>
/// KIM-D41 + KIM-D48: a staged correction of a company's VKN. A data fix (for example the migration that finds the
/// fiscal seed's stand-in "VKN" on the placeholder company) never rewrites the company directly: it stages the old value
/// (<see cref="StagedVkn"/>), the corrected value (<see cref="CorrectedVkn"/>, null means "VKN girilmemiş") and its
/// <see cref="Source"/>. The company's VKN moves only when a person holding the approve permission applies the row; the
/// applied row then carries the approver and stays as the trail next to its single audit event. A correction is never
/// deleted and is applied only while the company still carries exactly the staged value.
/// </summary>
public sealed class CompanyTaxIdCorrection : ICompanyOwnedRecord
{
    public const int SourceMaximumLength = 128;
    public const int ReasonMaximumLength = 1000;
    public const int ActorMaximumLength = 128;

    public CompanyTaxIdCorrection(
        Guid id,
        Guid companyId,
        string source,
        string stagedVkn,
        string? correctedVkn,
        long companyVersionAtStaging,
        string reason,
        DateTimeOffset stagedAtUtc)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", nameof(id)) : id;
        CompanyId = companyId == Guid.Empty
            ? throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId))
            : companyId;
        if (!Company.IsCanonicalVkn(stagedVkn))
        {
            throw new ArgumentException("Düzeltilecek VKN kanonik biçimde değil.", nameof(stagedVkn));
        }

        if (correctedVkn is not null)
        {
            if (!Company.IsCanonicalVkn(correctedVkn) || !CompanyVknCheckDigit.IsSatisfiedBy(correctedVkn))
            {
                throw new ArgumentException(
                    "Düzeltilmiş VKN 10 ASCII rakamdan oluşmalı ve kontrol hanesi kuralını sağlamalıdır.",
                    nameof(correctedVkn));
            }

            if (string.Equals(stagedVkn, correctedVkn, StringComparison.Ordinal))
            {
                throw new ArgumentException("Düzeltilmiş VKN eski değerle aynı olamaz.", nameof(correctedVkn));
            }
        }

        if (companyVersionAtStaging < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(companyVersionAtStaging));
        }

        Source = RequireText(source, nameof(source), SourceMaximumLength);
        StagedVkn = stagedVkn;
        CorrectedVkn = correctedVkn;
        CompanyVersionAtStaging = companyVersionAtStaging;
        Reason = RequireText(reason, nameof(reason), ReasonMaximumLength);
        StagedAtUtc = stagedAtUtc;
        Status = CompanyTaxIdCorrectionStatus.Pending;
    }

    private CompanyTaxIdCorrection()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>What staged the row (the data-fix migration's name); the audit event's system actor.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>The VKN the company carried when the row was staged (the old value).</summary>
    public string StagedVkn { get; private set; } = string.Empty;

    /// <summary>The new value; null clears the VKN ("VKN girilmemiş"), allowed only on a placeholder company.</summary>
    public string? CorrectedVkn { get; private set; }

    public long CompanyVersionAtStaging { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public CompanyTaxIdCorrectionStatus Status { get; private set; }

    public DateTimeOffset StagedAtUtc { get; private set; }

    public string? AppliedBy { get; private set; }

    public DateTimeOffset? AppliedAtUtc { get; private set; }

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public bool IsPending => Status == CompanyTaxIdCorrectionStatus.Pending;

    /// <summary>
    /// True when <paramref name="company"/> is this row's company and still carries exactly the staged VKN. A row that is
    /// not current is never applied: the VKN moved through another path after staging.
    /// </summary>
    public bool IsCurrentFor(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Id == CompanyId && string.Equals(company.Vkn, StagedVkn, StringComparison.Ordinal);
    }

    /// <summary>Applies the correction to <paramref name="company"/> and closes the row in the same unit of work.</summary>
    public void Apply(Company company, string appliedBy, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(company);
        var actor = RequireText(appliedBy, nameof(appliedBy), ActorMaximumLength);
        if (!IsPending)
        {
            throw new InvalidOperationException("Uygulanmış VKN düzeltmesi yeniden uygulanamaz.");
        }

        if (!IsCurrentFor(company))
        {
            throw new InvalidOperationException(
                "Şirketin VKN'si düzeltme hazırlandıktan sonra değişti; düzeltme uygulanamaz.");
        }

        company.ApplyTaxIdCorrection(StagedVkn, CorrectedVkn, utcNow);
        Status = CompanyTaxIdCorrectionStatus.Applied;
        AppliedBy = actor;
        AppliedAtUtc = utcNow;
        Version++;
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(parameterName)
            : normalized;
    }
}
