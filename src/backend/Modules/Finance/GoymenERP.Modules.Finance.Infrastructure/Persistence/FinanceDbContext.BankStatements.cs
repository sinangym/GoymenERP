using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

public sealed partial class FinanceDbContext
{
    internal DbSet<FinanceBankStatementImportRow> BankStatementImports => Set<FinanceBankStatementImportRow>();
    internal DbSet<FinanceBankStatementLineRow> BankStatementLines => Set<FinanceBankStatementLineRow>();
    internal DbSet<FinanceBankStatementCommandRow> BankStatementCommands => Set<FinanceBankStatementCommandRow>();

    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    internal const string BankStatementImportIdentityCheck = "[Id] <> " + EmptyGuid + " AND [CompanyId] <> " + EmptyGuid
        + " AND [BankAccountId] <> " + EmptyGuid + " AND [Version] >= 1 AND [Format] IN (1,2,3) AND [State] IN (1,2) "
        + "AND LEN([ReservedBy]) > 0 AND (([Format] = 1 AND [BankKey] IS NOT NULL AND LEN([BankKey]) > 0) "
        + "OR ([Format] <> 1 AND [BankKey] IS NULL))";

    internal const string BankStatementImportStateCheck = "([State] = 1 AND [DocumentId] IS NULL AND [DocumentVersion] IS NULL "
        + "AND [DocumentSha256] IS NULL AND [CsvMappingProfileId] IS NULL AND [CsvMappingProfileVersion] IS NULL "
        + "AND [LineCount] = 0 AND [DuplicateLineCount] = 0 AND [FirstTransactionOn] IS NULL AND [LastTransactionOn] IS NULL "
        + "AND [ParsedBy] IS NULL AND [ParsedAtUtc] IS NULL) "
        + "OR ([State] = 2 AND [DocumentId] IS NOT NULL AND [DocumentId] <> " + EmptyGuid + " AND [DocumentVersion] >= 1 "
        + "AND LEN([DocumentSha256]) = 64 AND [LineCount] >= 0 AND [DuplicateLineCount] >= 0 "
        + "AND [LineCount] + [DuplicateLineCount] >= 1 AND [FirstTransactionOn] IS NOT NULL "
        + "AND [LastTransactionOn] >= [FirstTransactionOn] AND LEN([ParsedBy]) > 0 AND [ParsedAtUtc] IS NOT NULL "
        + "AND (([Format] = 1 AND [CsvMappingProfileId] IS NOT NULL AND [CsvMappingProfileVersion] >= 1) "
        + "OR ([Format] <> 1 AND [CsvMappingProfileId] IS NULL AND [CsvMappingProfileVersion] IS NULL)))";

    internal const string BankStatementLineCheck = "[Id] <> " + EmptyGuid + " AND [ImportId] <> " + EmptyGuid
        + " AND [BankAccountId] <> " + EmptyGuid + " AND [LineNumber] >= 0 AND [Occurrence] >= 1 AND LEN([Fingerprint]) = 64 "
        + "AND [Direction] IN (1,2) AND LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' "
        + "AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' "
        + "AND LEN([Description]) > 0 AND ([CounterpartyIban] IS NULL OR LEN([CounterpartyIban]) = 26)";

    private void ConfigureBankStatements(ModelBuilder model)
    {
        var import = model.Entity<FinanceBankStatementImportRow>();
        import.ToTable("BankStatementImports", Schema, table =>
        {
            table.HasCheckConstraint("CK_BankStatementImports_Identity", BankStatementImportIdentityCheck);
            table.HasCheckConstraint("CK_BankStatementImports_State", BankStatementImportStateCheck);
        });
        import.HasKey(x => new { x.CompanyId, x.Id });
        import.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        import.Property(x => x.Version).IsConcurrencyToken();
        import.Property(x => x.BankKey).HasMaxLength(32).IsUnicode(false).UseCollation(IdentityCollation);
        import.Property(x => x.DocumentSha256).HasMaxLength(64).IsUnicode(false);
        import.Property(x => x.FirstTransactionOn).HasColumnType("date");
        import.Property(x => x.LastTransactionOn).HasColumnType("date");
        import.Property(x => x.ReservedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        import.Property(x => x.ParsedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        import.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.ReservedAtUtc });
        import.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.DocumentSha256 });
        import.HasOne<BankAccountRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var line = model.Entity<FinanceBankStatementLineRow>();
        line.ToTable("BankStatementLines", Schema, table =>
            table.HasCheckConstraint("CK_BankStatementLines_Content", BankStatementLineCheck));
        line.HasKey(x => new { x.CompanyId, x.Id });
        line.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        line.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        line.Property(x => x.TransactionOn).HasColumnType("date");
        line.Property(x => x.ValueOn).HasColumnType("date");
        line.Property(x => x.AmountText).HasColumnType("varchar(max)");
        line.Property(x => x.BalanceText).HasColumnType("varchar(max)");
        line.Property(x => x.CurrencyText).HasMaxLength(3).IsUnicode(false);
        line.Property(x => x.Reference).HasMaxLength(128);
        line.Property(x => x.Description).HasMaxLength(1000);
        line.Property(x => x.CounterpartyIban).HasMaxLength(26).IsUnicode(false).UseCollation(IdentityCollation);
        line.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.Fingerprint, x.Occurrence }).IsUnique();
        line.HasIndex(x => new { x.CompanyId, x.ImportId, x.LineNumber });
        line.HasOne<FinanceBankStatementImportRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ImportId })
            .OnDelete(DeleteBehavior.Restrict);

        var command = model.Entity<FinanceBankStatementCommandRow>();
        command.ToTable("BankStatementCommands", Schema, table =>
            table.HasCheckConstraint("CK_BankStatementCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 "
                + "AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND [ImportId] <> " + EmptyGuid + " AND ISJSON([ResultJson]) = 1"));
        command.HasKey(x => new { x.CompanyId, x.KeyHash });
        command.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        command.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        command.Property(x => x.CommandName).HasMaxLength(128).IsUnicode(false);
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        command.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
    }
}
