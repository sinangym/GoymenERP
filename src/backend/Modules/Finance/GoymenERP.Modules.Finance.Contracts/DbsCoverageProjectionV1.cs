namespace GoymenERP.Modules.Finance.Contracts;

public sealed record DbsCoverageProjectionInputV1(
    Guid CompanyId,
    MoneyText TotalOpen,
    MoneyText SecuredBefore,
    MoneyText AvailableDbsBefore,
    MoneyText Assignment,
    ProfileReference Profile,
    int Scale);

public sealed record DbsCoverageProjectionV1(
    Guid CompanyId,
    MoneyText TotalOpen,
    MoneyText DbsSecuredAfter,
    MoneyText UnsecuredAfter,
    MoneyText AvailableDbsAfter,
    MoneyText CashCollected,
    ProfileReference Profile,
    bool CreatesMoneyEvent);

public sealed record DbsCoverageCalculationV1(
    DbsCoverageProjectionV1? Projection,
    IReadOnlyList<ValidationIssue> Issues);
