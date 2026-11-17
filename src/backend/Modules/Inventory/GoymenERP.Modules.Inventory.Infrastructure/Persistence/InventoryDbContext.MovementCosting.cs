using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Globalization;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryDbContext
{
    internal DbSet<MovementCostStampRow> MovementCostStamps => Set<MovementCostStampRow>();
    internal DbSet<CurrencyAverageCostLedgerRow> CurrencyAverageCostLedger => Set<CurrencyAverageCostLedgerRow>();
    internal DbSet<PendingMovementCostRow> PendingMovementCosts => Set<PendingMovementCostRow>();
    internal DbSet<MovementCostRecognitionRow> MovementCostRecognitions => Set<MovementCostRecognitionRow>();
    internal DbSet<ManufacturingCostGenerationRow> ManufacturingCostGenerations => Set<ManufacturingCostGenerationRow>();
    internal DbSet<ManufacturingCostGenerationHistoryRow> ManufacturingCostGenerationHistory => Set<ManufacturingCostGenerationHistoryRow>();
    internal DbSet<ExactCostPoolRow> ExactCostPools => Set<ExactCostPoolRow>();

    private void ConfigureMovementCosting(ModelBuilder modelBuilder)
    {
        var stamp = modelBuilder.Entity<MovementCostStampRow>();
        stamp.ToTable("MovementCostStamps", Schema, table =>
        {
            table.HasCheckConstraint("CK_MovementCostStamps_Identity", "[LineNumber] >= 1 AND [StampVersion] >= 1 "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND ([SourceIssueMovementId] IS NULL OR [SourceIssueMovementId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_MovementCostStamps_Value", "([Status] = 1 "
                + "AND (([UnitCostPerKg] IS NOT NULL AND [Amount] IS NOT NULL) OR [UnitCostRatioJson] IS NOT NULL) AND [LedgerVersion] >= 1 AND [LedgerVersion] IS NOT NULL "
                + "AND [Reason] IS NULL) OR ([Status] = 2 AND [UnitCostPerKg] IS NULL AND [Amount] IS NULL "
                + "AND [UnitCostRatioJson] IS NULL AND LEN([Reason]) > 0 AND [Reason] IS NOT NULL AND ([LedgerVersion] IS NULL OR [LedgerVersion] >= 1))");
            table.HasCheckConstraint("CK_MovementCostStamps_Currency", "LEN([CurrencySourceVersion]) > 0 AND "
                + "(([EntryCurrencyStatus] = 1 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL "
                + "AND [EntryExchangeRateRatioJson] IS NULL AND [ForeignUnitCostRatioJson] IS NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 2 AND [OriginalCurrencyId] IS NOT NULL "
                + "AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND (([ForeignUnitCostPerKg] <> '0' AND [ForeignUnitCostPerKg] IS NOT NULL) OR [ForeignUnitCostRatioJson] IS NOT NULL) "
                + "AND (([EntryExchangeRate] > 0 AND [EntryExchangeRate] IS NOT NULL) OR [EntryExchangeRateRatioJson] IS NOT NULL) AND [CurrencyReason] IS NULL) "
                + "OR ([EntryCurrencyStatus] = 3 AND [OriginalCurrencyId] IS NOT NULL "
                + "AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryExchangeRate] IS NULL "
                + "AND [EntryExchangeRateRatioJson] IS NULL AND ([ForeignUnitCostPerKg] IS NULL OR [ForeignUnitCostPerKg] <> '0') AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL) "
                + "OR ([EntryCurrencyStatus] = 4 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL "
                + "AND [EntryExchangeRate] IS NULL AND [EntryExchangeRateRatioJson] IS NULL AND [ForeignUnitCostRatioJson] IS NULL AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL))");
            table.HasCheckConstraint("CK_MovementCostStamps_Envelope", "LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 "
                + "AND ISJSON([EvidenceJson], OBJECT) = 1");
            table.HasCheckConstraint("CK_MovementCostStamps_DecimalText", CanonicalCostDecimal("UnitCostPerKg")
                + " AND " + CanonicalCostDecimal("Amount") + " AND " + CanonicalCostDecimal("ForeignUnitCostPerKg"));
            table.HasCheckConstraint("CK_MovementCostStamps_ExactProof", "([UnitCostRatioJson] IS NULL OR ISJSON([UnitCostRatioJson], OBJECT) = 1) "
                + "AND ([ForeignUnitCostRatioJson] IS NULL OR ISJSON([ForeignUnitCostRatioJson], OBJECT) = 1) "
                + "AND ([PoolEffectJson] IS NULL OR ISJSON([PoolEffectJson], OBJECT) = 1) "
                + "AND ([EntryExchangeRateRatioJson] IS NULL OR ISJSON([EntryExchangeRateRatioJson], OBJECT) = 1) "
                + "AND ([FirstEntryRateJson] IS NULL OR ISJSON([FirstEntryRateJson], OBJECT) = 1)");
        });
        stamp.HasKey(row => new { row.CompanyId, row.MovementId, row.LineNumber, row.CurrencyId });
        var decimalText = new ValueConverter<decimal, string>(value => CostDecimalText(value), value => CostDecimalValue(value));
        stamp.Property(row => row.UnitCostPerKg).HasConversion(decimalText).HasColumnType("varchar(64)");
        stamp.Property(row => row.Amount).HasConversion(decimalText).HasColumnType("varchar(64)");
        stamp.Property(row => row.ForeignUnitCostPerKg).HasConversion(decimalText).HasColumnType("varchar(64)");
        stamp.Property(row => row.UnitCostRatioJson).HasColumnType("nvarchar(max)");
        stamp.Property(row => row.ForeignUnitCostRatioJson).HasColumnType("nvarchar(max)");
        stamp.Property(row => row.PoolEffectJson).HasColumnType("nvarchar(max)");
        stamp.Property(row => row.EntryExchangeRateRatioJson).HasColumnType("nvarchar(max)");
        stamp.Property(row => row.FirstEntryRateJson).HasColumnType("nvarchar(max)");
        stamp.Property(row => row.EntryExchangeRate).HasPrecision(38, 18);
        stamp.Property(row => row.Reason).HasMaxLength(160).IsUnicode(false);
        stamp.Property(row => row.CurrencyReason).HasMaxLength(160).IsUnicode(false);
        stamp.Property(row => row.CurrencySourceVersion).HasMaxLength(160).IsUnicode(false).IsRequired();
        stamp.Property(row => row.CorrelationId).HasMaxLength(100).IsRequired();
        stamp.Property(row => row.EvidenceJson).HasColumnType("nvarchar(max)").IsRequired();
        stamp.HasOne<MovementLineRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.MovementId, row.LineNumber })
            .OnDelete(DeleteBehavior.Restrict);
        stamp.HasOne<MovementRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.SourceIssueMovementId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        stamp.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var ledger = modelBuilder.Entity<CurrencyAverageCostLedgerRow>();
        ledger.ToTable("CurrencyAverageCostLedger", Schema, table =>
        {
            table.HasCheckConstraint("CK_CurrencyAverageCostLedger_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CurrencyAverageCostLedger_State", AverageCostLedgerStateCheck);
        });
        ledger.HasKey(row => row.Id);
        ledger.Property(row => row.Id).ValueGeneratedNever();
        ledger.Property(row => row.QuantityKg).HasPrecision(18, 3);
        ledger.Property(row => row.AverageCostPerKg).HasPrecision(18, 4);
        ledger.Property(row => row.RowVersion).IsRowVersion();
        ledger.HasIndex(row => new { row.CompanyId, row.LocationId, row.ProductId, row.CurrencyId }).IsUnique();
        ledger.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        LocationOf(ledger);
        ledger.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        var exactPool = modelBuilder.Entity<ExactCostPoolRow>();
        exactPool.ToTable("ExactCostPools", Schema, table => table.HasCheckConstraint("CK_ExactCostPools_State",
            "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [Version] >= 1 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND (([Status] = 1 AND [QuantityRatioJson] IS NOT NULL "
            + "AND ISJSON([QuantityRatioJson], OBJECT) = 1 AND [AverageCostPerKg] >= 0 AND [AverageCostPerKg] IS NOT NULL "
            + "AND [UnreadableReason] IS NULL) OR ([Status] = 2 AND [QuantityRatioJson] IS NULL AND [AverageCostPerKg] IS NULL "
            + "AND [UnreadableReason] BETWEEN 1 AND 9 AND [UnreadableReason] IS NOT NULL))"));
        exactPool.HasKey(row => new { row.CompanyId, row.LocationId, row.ProductId, row.CurrencyId });
        exactPool.Property(row => row.QuantityRatioJson).HasColumnType("nvarchar(max)");
        exactPool.Property(row => row.AverageCostPerKg).HasPrecision(18, 4);
        exactPool.Property(row => row.RowVersion).IsRowVersion();
        exactPool.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        LocationOf(exactPool);
        exactPool.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        ConfigurePendingCostRecognition(modelBuilder);
    }

    internal static string CostDecimalText(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    internal static decimal CostDecimalValue(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            || result < 0m || CostDecimalText(result) != value) throw new InvalidOperationException("Maliyet decimal metni canonical değil.");
        return result;
    }
    private static string CanonicalCostDecimal(string name) => $"([{name}] IS NULL OR (LEN([{name}]) BETWEEN 1 AND 50 "
        + $"AND [{name}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [{name}] NOT LIKE '%.%.%' "
        + $"AND [{name}] NOT LIKE '.%' AND [{name}] NOT LIKE '%.' AND [{name}] NOT LIKE '0[0-9]%' "
        + $"AND (CHARINDEX('.',[{name}]) = 0 OR [{name}] NOT LIKE '%0') AND LEN([{name}]) = DATALENGTH([{name}]) "
        + $"AND LEN(REPLACE([{name}],'.','')) <= 29 AND (CHARINDEX('.',[{name}]) = 0 OR LEN([{name}]) - CHARINDEX('.',[{name}]) <= 28) "
        + $"AND (LEN(REPLACE([{name}],'.','')) < 29 OR REPLACE([{name}],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') "
        + $"AND TRY_CONVERT(decimal(38,18),[{name}]) IS NOT NULL))";

    private void ConfigurePendingCostRecognition(ModelBuilder modelBuilder)
    {
        var pending = modelBuilder.Entity<PendingMovementCostRow>();
        pending.ToTable("PendingMovementCosts", Schema, table => table.HasCheckConstraint("CK_PendingMovementCosts_State",
            "[LineNumber] >= 1 AND [QuantityKg] > 0 AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND LEN([Reason]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0"));
        pending.HasKey(row => new { row.CompanyId, row.MovementId, row.LineNumber, row.CurrencyId });
        pending.Property(row => row.QuantityKg).HasPrecision(18, 3);
        pending.Property(row => row.Reason).HasMaxLength(160).IsUnicode(false).IsRequired();
        pending.HasOne<MovementLineRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.MovementId, row.LineNumber }).OnDelete(DeleteBehavior.Restrict);
        LabelOf(pending); LocationOf(pending);
        pending.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var recognition = modelBuilder.Entity<MovementCostRecognitionRow>();
        recognition.ToTable("MovementCostRecognitions", Schema, table => table.HasCheckConstraint("CK_MovementCostRecognitions_State",
            "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UnitCostPerKg] > 0 "
            + "AND (([RemainingQuantityKg] >= 0 AND [RemainingQuantityKg] IS NOT NULL) OR ([RemainingQuantityRatioJson] IS NOT NULL AND ISJSON([RemainingQuantityRatioJson], OBJECT) = 1)) "
            + "AND (([ConsumedQuantityKg] >= 0 AND [ConsumedQuantityKg] IS NOT NULL) OR ([ConsumedQuantityRatioJson] IS NOT NULL AND ISJSON([ConsumedQuantityRatioJson], OBJECT) = 1)) "
            + "AND ([RemainingQuantityRatioJson] IS NULL OR ISJSON([RemainingQuantityRatioJson], OBJECT) = 1) "
            + "AND ([ConsumedQuantityRatioJson] IS NULL OR ISJSON([ConsumedQuantityRatioJson], OBJECT) = 1) "
            + "AND LEN([SourceVersion]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 "
            + "AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0"));
        recognition.HasKey(row => row.Id);
        recognition.Property(row => row.Id).ValueGeneratedNever();
        recognition.HasIndex(row => new { row.CompanyId, row.MovementId, row.LineNumber, row.CurrencyId })
            .IsUnique().HasFilter("[GenerationId] IS NULL");
        recognition.HasIndex(row => new { row.CompanyId, row.MovementId, row.LineNumber, row.CurrencyId, row.GenerationId })
            .IsUnique().HasFilter("[GenerationId] IS NOT NULL");
        recognition.Property(row => row.UnitCostPerKg).HasPrecision(38, 18);
        recognition.Property(row => row.RemainingQuantityKg).HasPrecision(18, 3);
        recognition.Property(row => row.ConsumedQuantityKg).HasPrecision(18, 3);
        recognition.Property(row => row.RemainingQuantityRatioJson).HasColumnType("nvarchar(max)");
        recognition.Property(row => row.ConsumedQuantityRatioJson).HasColumnType("nvarchar(max)");
        recognition.Property(row => row.SourceVersion).HasMaxLength(160).IsUnicode(false).IsRequired();
        recognition.Property(row => row.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        recognition.Property(row => row.CorrelationId).HasMaxLength(100).IsRequired();
        recognition.HasOne<PendingMovementCostRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.MovementId, row.LineNumber, row.CurrencyId }).OnDelete(DeleteBehavior.Restrict);
        recognition.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var generation = modelBuilder.Entity<ManufacturingCostGenerationRow>();
        generation.ToTable("ManufacturingCostGenerations", Schema, table => table.HasCheckConstraint("CK_ManufacturingCostGenerations_State",
            "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [Version] >= 1 AND [State] IN (1,2,3,4) AND LEN([ClosedOrderVersion]) > 0 "
            + "AND ISJSON([OutputFactsJson], ARRAY) = 1 AND ISJSON([LastResultJson], OBJECT) = 1 "
            + "AND [UpdatedAtUtc] >= [CreatedAtUtc] AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0"));
        generation.HasKey(row => row.Id);
        generation.Property(row => row.Id).ValueGeneratedNever();
        generation.HasAlternateKey(row => new { row.CompanyId, row.Id });
        generation.HasIndex(row => new { row.CompanyId, row.ManufacturingOrderId }).IsUnique().HasFilter("[State] <> 4");
        generation.Property(row => row.ClosedOrderVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        generation.Property(row => row.OutputFactsJson).HasColumnType("nvarchar(max)").IsRequired();
        generation.Property(row => row.LastResultJson).HasColumnType("nvarchar(max)").IsRequired();
        generation.Property(row => row.RowVersion).IsRowVersion();
        generation.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        generation.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var history = modelBuilder.Entity<ManufacturingCostGenerationHistoryRow>();
        history.ToTable("ManufacturingCostGenerationHistory", Schema, table => table.HasCheckConstraint("CK_ManufacturingCostGenerationHistory_State",
            "[Version] >= 1 AND LEN([InputFingerprint]) > 0 AND ISJSON([InputEvidenceJson], OBJECT) = 1 "
            + "AND ISJSON([ResultJson], OBJECT) = 1 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 "
            + "AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0"));
        history.HasKey(row => new { row.CompanyId, row.GenerationId, row.Version });
        history.Property(row => row.InputFingerprint).HasMaxLength(160).IsUnicode(false).IsRequired();
        history.Property(row => row.InputEvidenceJson).HasColumnType("nvarchar(max)").IsRequired();
        history.Property(row => row.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        history.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        history.Property(row => row.CorrelationId).HasMaxLength(100).IsRequired();
        history.HasOne<ManufacturingCostGenerationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.GenerationId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        history.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
