using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// FNS-D52: the persistent per-bank CSV column mapping versions of a company. The table carries the company inside its key
/// and the company query filter; the rows are insert-only and the primary key on (company, bank key, version) is the
/// concurrency fence (two writers of the same bank's next version: one wins, the other is a version conflict). The checks
/// hold the published closed code lists, the layout's required columns and the technical bounds; the "no column used twice"
/// rule is the shared code rule, re-checked by the provider on every read. No value is defaulted by the database and no row
/// is seeded for any real bank.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string BankCsvMappingKeyCollation = "Latin1_General_100_BIN2";

    internal const string BankCsvMappingIdentityCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [Version] > 0 AND LEN([BankKey]) BETWEEN 1 AND 32 AND [BankKey] LIKE '[A-Z0-9]%' "
        + "AND [BankKey] NOT LIKE '%[^A-Z0-9_-]%' AND LEN(LTRIM(RTRIM([BankDisplayName]))) > 0";

    internal const string BankCsvMappingCodesCheck =
        "[State] IN ('Active','Inactive') AND [Delimiter] IN ('Semicolon','Comma','Tab') "
        + "AND [DecimalSeparator] IN ('Comma','Period') AND [ThousandsSeparator] IN ('None','Period','Comma','Space') "
        + "AND [ThousandsSeparator] <> [DecimalSeparator] AND [SkipLeadingRows] BETWEEN 0 AND 50";

    internal const string BankCsvMappingDateFormatCheck =
        "[DateFormat] IN ('dd.MM.yyyy','dd/MM/yyyy','dd-MM-yyyy','yyyy-MM-dd','yyyyMMdd','MM/dd/yyyy',"
        + "'dd.MM.yyyy HH:mm','dd.MM.yyyy HH:mm:ss','yyyy-MM-dd HH:mm:ss')";

    internal const string BankCsvMappingLayoutCheck =
        "([AmountLayout] = 'SignedAmount' AND [AmountColumn] IS NOT NULL AND [InflowColumn] IS NULL "
        + "AND [OutflowColumn] IS NULL AND [SignedAmountPositiveMeans] IN ('Inflow','Outflow')) "
        + "OR ([AmountLayout] = 'InflowOutflowColumns' AND [AmountColumn] IS NULL AND [InflowColumn] IS NOT NULL "
        + "AND [OutflowColumn] IS NOT NULL AND [SignedAmountPositiveMeans] IS NULL)";

    internal const string BankCsvMappingColumnsCheck =
        "[TransactionDateColumn] BETWEEN 1 AND 256 AND [DescriptionColumn] BETWEEN 1 AND 256 "
        + "AND ([ValueDateColumn] IS NULL OR [ValueDateColumn] BETWEEN 1 AND 256) "
        + "AND ([AmountColumn] IS NULL OR [AmountColumn] BETWEEN 1 AND 256) "
        + "AND ([InflowColumn] IS NULL OR [InflowColumn] BETWEEN 1 AND 256) "
        + "AND ([OutflowColumn] IS NULL OR [OutflowColumn] BETWEEN 1 AND 256) "
        + "AND ([BalanceColumn] IS NULL OR [BalanceColumn] BETWEEN 1 AND 256) "
        + "AND ([ReferenceColumn] IS NULL OR [ReferenceColumn] BETWEEN 1 AND 256) "
        + "AND ([CurrencyColumn] IS NULL OR [CurrencyColumn] BETWEEN 1 AND 256)";

    internal DbSet<BankCsvMappingVersionRow> BankCsvMappingVersions => Set<BankCsvMappingVersionRow>();

    private void ConfigureBankCsvMappings(ModelBuilder modelBuilder)
    {
        var mapping = modelBuilder.Entity<BankCsvMappingVersionRow>();
        mapping.ToTable("BankCsvMappingVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_Identity", BankCsvMappingIdentityCheck);
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_Codes", BankCsvMappingCodesCheck);
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_DateFormat", BankCsvMappingDateFormatCheck);
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_Layout", BankCsvMappingLayoutCheck);
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_Columns", BankCsvMappingColumnsCheck);
            table.HasCheckConstraint("CK_SettingsBankCsvMappingVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        mapping.HasKey(x => new { x.CompanyId, x.BankKey, x.Version });
        mapping.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        mapping.Property(x => x.BankKey).HasMaxLength(BankCsvMappingInputLimitsV1.BankKeyMaximumLength).IsUnicode(false)
            .UseCollation(BankCsvMappingKeyCollation);
        mapping.Property(x => x.BankDisplayName).HasMaxLength(BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength);
        mapping.Property(x => x.State).HasMaxLength(16).IsUnicode(false);
        mapping.Property(x => x.Delimiter).HasMaxLength(16).IsUnicode(false);
        mapping.Property(x => x.DecimalSeparator).HasMaxLength(16).IsUnicode(false);
        mapping.Property(x => x.ThousandsSeparator).HasMaxLength(16).IsUnicode(false);
        mapping.Property(x => x.DateFormat).HasMaxLength(32).IsUnicode(false);
        mapping.Property(x => x.AmountLayout).HasMaxLength(32).IsUnicode(false);
        mapping.Property(x => x.SignedAmountPositiveMeans).HasMaxLength(16).IsUnicode(false);
        mapping.Property(x => x.RecordedBy).HasMaxLength(128);
        mapping.Property(x => x.Reason).HasMaxLength(BankCsvMappingInputLimitsV1.ReasonMaximumLength);
        mapping.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
