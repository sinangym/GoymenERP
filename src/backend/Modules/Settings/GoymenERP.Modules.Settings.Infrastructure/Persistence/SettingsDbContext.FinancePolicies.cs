using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal DbSet<FinanceApprovalPolicyRow> FinanceApprovalPolicies => Set<FinanceApprovalPolicyRow>();
    internal DbSet<FinanceApprovalPolicyRuleRow> FinanceApprovalPolicyRules => Set<FinanceApprovalPolicyRuleRow>();
    internal DbSet<CashCountDifferencePolicyRow> CashCountDifferencePolicies => Set<CashCountDifferencePolicyRow>();

    private void ConfigureFinancePolicies(ModelBuilder modelBuilder)
    {
        var approval = modelBuilder.Entity<FinanceApprovalPolicyRow>();
        ConfigureFinancePolicyHeader(approval, "FinanceApprovalPolicies");
        approval.HasIndex(x => new { x.CompanyId, x.Version }).IsUnique();
        approval.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var rule = modelBuilder.Entity<FinanceApprovalPolicyRuleRow>();
        rule.ToTable("FinanceApprovalPolicyRules", Schema, table =>
        {
            table.HasCheckConstraint("CK_FinanceApprovalPolicyRules_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [RuleId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ApproverRoleId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FinanceApprovalPolicyRules_Step", "[ApprovalStep] IN (1,2)");
            table.HasCheckConstraint("CK_FinanceApprovalPolicyRules_Text", "LEN([OperationPermission]) > 0 AND LEN([ApproverRoleCode]) > 0 AND LEN([RoleSourceVersion]) > 0");
            table.HasCheckConstraint("CK_FinanceApprovalPolicyRules_Amount", FinancePolicyAmountCheck);
        });
        rule.HasKey(x => new { x.CompanyId, x.PolicyId, x.RuleId });
        rule.HasIndex(x => new { x.CompanyId, x.PolicyId, x.OperationPermission, x.CurrencyId, x.ApproverRoleId, x.ApprovalStep }).IsUnique();
        rule.Property(x => x.OperationPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        rule.Property(x => x.ApproverRoleCode).HasMaxLength(128);
        rule.Property(x => x.RoleSourceVersion).HasMaxLength(128).IsUnicode(false);
        rule.Property(x => x.AmountThreshold).HasMaxLength(FinancePolicyRulesV1.AmountMaximumLength).IsUnicode(false);
        rule.HasOne<FinanceApprovalPolicyRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PolicyId }).OnDelete(DeleteBehavior.Restrict);
        rule.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var cash = modelBuilder.Entity<CashCountDifferencePolicyRow>();
        ConfigureFinancePolicyHeader(cash, "CashCountDifferencePolicies", table =>
        {
            table.HasCheckConstraint("CK_CashCountDifferencePolicies_Scope", "[CashAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CashAccountVersion]) > 0");
            table.HasCheckConstraint("CK_CashCountDifferencePolicies_Amount", FinancePolicyAmountCheck);
        });
        cash.HasIndex(x => new { x.CompanyId, x.CashAccountId, x.CurrencyId, x.Version }).IsUnique();
        cash.Property(x => x.CashAccountVersion).HasMaxLength(128).IsUnicode(false);
        cash.Property(x => x.AmountThreshold).HasMaxLength(FinancePolicyRulesV1.AmountMaximumLength).IsUnicode(false);
        cash.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }

    internal const string FinancePolicyAmountCheck = "LEN([AmountThreshold]) BETWEEN 1 AND 64 AND [AmountThreshold] NOT LIKE '%[^0-9.]%' AND LEFT([AmountThreshold],1) <> '.' AND RIGHT([AmountThreshold],1) <> '.' AND LEN([AmountThreshold])-LEN(REPLACE([AmountThreshold],'.','')) <= 1";

    private static void ConfigureFinancePolicyHeader<T>(EntityTypeBuilder<T> entity, string name,
        Action<Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder<T>>? extra = null) where T : class, IFinancePolicyVersionRow
    {
        entity.ToTable(name, Schema, table =>
        {
            table.HasCheckConstraint($"CK_{name}_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [StateVersion] > 0");
            table.HasCheckConstraint($"CK_{name}_Window", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
            table.HasCheckConstraint($"CK_{name}_State", "([State]='Draft' AND [StateVersion]=1 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [RetiredFrom] IS NULL) OR ([State]='Published' AND [StateVersion]=2 AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NULL) OR ([State]='Retired' AND [StateVersion]=3 AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NOT NULL)");
            table.HasCheckConstraint($"CK_{name}_Text", "LEN([PreparedBy])>0 AND LEN([Reason])>0 AND LEN([LastReason])>0");
            extra?.Invoke(table);
        });
        entity.HasKey(x => new { x.CompanyId, x.PolicyId });
        entity.Property(x => x.Version).ValueGeneratedNever();
        entity.Property(x => x.StateVersion).IsConcurrencyToken();
        entity.Property(x => x.State).HasMaxLength(16).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.PreparedBy).HasMaxLength(128);
        entity.Property(x => x.PublishedBy).HasMaxLength(128);
        entity.Property(x => x.Reason).HasMaxLength(FinancePolicyRulesV1.ReasonMaximumLength);
        entity.Property(x => x.LastReason).HasMaxLength(FinancePolicyRulesV1.ReasonMaximumLength);
    }

    private void ApplyFinancePolicyWriteGuard(EntityEntry entry)
    {
        if (entry.Entity is FinanceApprovalPolicyRuleRow && entry.State != EntityState.Added)
            throw new InvalidOperationException("Published finance policy rule values are immutable.");
        if (entry.Entity is not IFinancePolicyVersionRow row) return;
        if (row.CompanyId == Guid.Empty || !companyContext.IsResolved || row.CompanyId != companyContext.CompanyId)
            throw new InvalidOperationException("Finance policy company differs from the resolved owner.");
        if (entry.State == EntityState.Added)
        {
            if (row.State != "Draft" || row.StateVersion != 1 || row.PublishedBy is not null || row.PublishedAtUtc is not null || row.RetiredFrom is not null)
                throw new InvalidOperationException("A finance policy begins as an explicit draft.");
            return;
        }
        if (entry.State != EntityState.Modified) throw new InvalidOperationException("Finance policies are not deleted.");
        var before = (string)entry.Property(nameof(IFinancePolicyVersionRow.State)).OriginalValue!;
        var beforeVersion = (long)entry.Property(nameof(IFinancePolicyVersionRow.StateVersion)).OriginalValue!;
        if (row.StateVersion != beforeVersion + 1 || !(before == "Draft" && row.State == "Published" || before == "Published" && row.State == "Retired"))
            throw new InvalidOperationException("Invalid finance policy lifecycle transition.");
        string[] allowed = before == "Draft"
            ? [nameof(row.State), nameof(row.StateVersion), nameof(row.PublishedBy), nameof(row.PublishedAtUtc), nameof(row.LastReason)]
            : [nameof(row.State), nameof(row.StateVersion), nameof(row.RetiredFrom), nameof(row.LastReason)];
        if (entry.Properties.Any(property => property.IsModified && !allowed.Contains(property.Metadata.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Finance policy values cannot change after preparation.");
    }
}
