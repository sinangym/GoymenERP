using System.Globalization;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Api.Contracts;

/// <summary>
/// Safe wire projection returned by Cari create and company-link mutations.
/// Int64 concurrency tokens are decimal strings so JavaScript clients cannot
/// silently round them, and integration-owner fields are deliberately absent.
/// </summary>
public sealed record CounterpartyMutationAccountView(
    Guid Id,
    Guid CompanyId,
    PartyRole Roles,
    bool IsActive,
    string Version);

public sealed record CounterpartyMutationView(
    Guid Id,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    bool IsActive,
    string Version,
    IReadOnlyList<CounterpartyMutationAccountView> CompanyAccounts);

internal static class CounterpartyMutationViewProjector
{
    private const PartyRole KnownRoles =
        PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;

    public static CounterpartyMutationView ProjectPartyCreate(
        PartyCoreView source,
        Guid activeCompanyId,
        CreateCounterparty submittedCommand)
    {
        ArgumentNullException.ThrowIfNull(submittedCommand);
        var normalizedTradeName = string.IsNullOrWhiteSpace(submittedCommand.TradeName)
            ? null
            : submittedCommand.TradeName.Trim();
        var result = ProjectCore(
            source,
            activeCompanyId,
            expectedPartyId: null,
            submittedCommand.Roles,
            requirePartyVersionOne: true);
        if (result.Kind != submittedCommand.Kind
            || !string.Equals(
                result.LegalName,
                submittedCommand.LegalName.Trim(),
                StringComparison.Ordinal)
            || !string.Equals(
                result.TradeName,
                normalizedTradeName,
                StringComparison.Ordinal))
        {
            throw InvalidProjection();
        }

        return result;
    }

    public static CounterpartyMutationView ProjectCompanyAccountCreate(
        PartyCoreView source,
        Guid activeCompanyId,
        Guid expectedPartyId,
        CreateCompanyPartyAccount submittedCommand)
    {
        ArgumentNullException.ThrowIfNull(submittedCommand);
        if (expectedPartyId == Guid.Empty)
        {
            throw InvalidProjection();
        }

        return ProjectCore(
            source,
            activeCompanyId,
            expectedPartyId,
            submittedCommand.Roles,
            requirePartyVersionOne: false);
    }

    private static CounterpartyMutationView ProjectCore(
        PartyCoreView source,
        Guid activeCompanyId,
        Guid? expectedPartyId,
        PartyRole expectedRoles,
        bool requirePartyVersionOne)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (activeCompanyId == Guid.Empty
            || source.Id == Guid.Empty
            || (expectedPartyId.HasValue && source.Id != expectedPartyId.Value)
            || !Enum.IsDefined(source.Kind)
            || !IsCanonicalRequiredText(source.LegalName, 250)
            || !IsCanonicalOptionalText(source.TradeName, 200)
            || source.Version <= 0
            || (requirePartyVersionOne && source.Version != 1)
            || !source.IsActive
            || source.CompanyAccounts is null
            || source.CompanyAccounts.Count != 1)
        {
            throw InvalidProjection();
        }

        string code;
        try
        {
            code = PartyCode.Parse(source.Code).Value;
            if (!string.Equals(code, source.Code, StringComparison.Ordinal))
            {
                throw InvalidProjection();
            }
        }
        catch (ArgumentException exception)
        {
            throw InvalidProjection(exception);
        }

        var account = source.CompanyAccounts[0];
        if (account.Id == Guid.Empty
            || account.CompanyId == Guid.Empty
            || account.CompanyId != activeCompanyId
            || (account.Roles & ~KnownRoles) != 0
            || account.Roles != expectedRoles
            || !account.IsActive
            || account.Version != 1)
        {
            throw InvalidProjection();
        }

        return new CounterpartyMutationView(
            source.Id,
            code,
            source.Kind,
            source.LegalName,
            source.TradeName,
            source.IsActive,
            source.Version.ToString(CultureInfo.InvariantCulture),
            [
                new CounterpartyMutationAccountView(
                    account.Id,
                    account.CompanyId,
                    account.Roles,
                    account.IsActive,
                    account.Version.ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    private static bool IsCanonicalRequiredText(string? value, int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static bool IsCanonicalOptionalText(string? value, int maximumLength)
    {
        return value is null || IsCanonicalRequiredText(value, maximumLength);
    }

    private static InvalidOperationException InvalidProjection(Exception? inner = null)
    {
        return new InvalidOperationException(
            "Cari işlem sonucu güvenli API projeksiyonuyla eşleşmiyor.",
            inner);
    }
}
