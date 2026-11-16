using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Domain.Common;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed class OrganizationDbContext(
    DbContextOptions<OrganizationDbContext> options,
    ICompanyContext companyContext,
    OrganizationBootstrapWriteScope bootstrapWriteScope)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();

    public DbSet<WorkCenterCalendar> WorkCenterCalendars => Set<WorkCenterCalendar>();

    public DbSet<WorkCenterCalendarShiftRecord> WorkCenterCalendarShifts => Set<WorkCenterCalendarShiftRecord>();

    public DbSet<WorkCenterCalendarClosedDateRecord> WorkCenterCalendarClosedDates =>
        Set<WorkCenterCalendarClosedDateRecord>();

    public DbSet<CompanyTaxIdChangeRequest> CompanyTaxIdChangeRequests => Set<CompanyTaxIdChangeRequest>();

    public DbSet<CompanyTaxIdCorrection> CompanyTaxIdCorrections => Set<CompanyTaxIdCorrection>();

    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    internal const string WorkCenterCalendarVersionCheck = "[Version] >= 1";
    internal const string WorkCenterCalendarShiftRevisionCheck = "[Revision] >= 1";
    internal const string WorkCenterCalendarShiftDayCheck = "[Day] BETWEEN 0 AND 6";
    /// <summary>KIM-D33: an earlier end spills into the next day, so only an empty window (start = end) is refused.</summary>
    internal const string WorkCenterCalendarShiftWindowCheck = "[StartTime] <> [EndTime]";
    internal const string WorkCenterCalendarClosedDateRevisionCheck = "[Revision] >= 1";

    // KIM-S23 (KIM-D28, DEC-ORG-005): a non-placeholder company always carries a VKN and no two companies share one.
    internal const string CompanyVknRequiredCheck = "[IsPlaceholder] = 1 OR [Vkn] IS NOT NULL";
    internal const string CompanyVknUniqueFilter = "[Vkn] IS NOT NULL";

    // KIM-S23: the VKN change request table (maker-checker, at most one pending request per company).
    internal const string CompanyTaxIdChangePendingFilter = "[Status] = N'Pending'";
    internal const string CompanyTaxIdChangeStatusCheck = "[Status] IN (N'Pending', N'Approved', N'Rejected')";
    internal const string CompanyTaxIdChangeProposedVknCheck =
        "LEN([ProposedVkn]) = 10 AND [ProposedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'";
    internal const string CompanyTaxIdChangeCurrentVknCheck =
        "[CurrentVkn] IS NULL OR (LEN([CurrentVkn]) = 10 AND [CurrentVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')";
    internal const string CompanyTaxIdChangeDistinctVknCheck =
        "[CurrentVkn] IS NULL OR [CurrentVkn] <> [ProposedVkn]";
    internal const string CompanyTaxIdChangeDecisionCheck =
        "([Status] = N'Pending' AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionNote] IS NULL) "
        + "OR ([Status] <> N'Pending' AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)";
    internal const string CompanyTaxIdChangeMakerCheckerCheck =
        "[DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2";
    internal const string CompanyTaxIdChangeVersionCheck = "[Version] >= 1 AND [CompanyVersionAtRequest] >= 1";

    // KIM-D41/KIM-D48: the staged VKN correction table (old/new value and source; applied only after approval, at most
    // one pending correction per company).
    internal const string CompanyTaxIdCorrectionPendingFilter = "[Status] = N'Pending'";
    internal const string CompanyTaxIdCorrectionStatusCheck = "[Status] IN (N'Pending', N'Applied')";
    internal const string CompanyTaxIdCorrectionStagedVknCheck =
        "LEN([StagedVkn]) = 10 AND [StagedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'";
    internal const string CompanyTaxIdCorrectionCorrectedVknCheck =
        "[CorrectedVkn] IS NULL OR (LEN([CorrectedVkn]) = 10 AND [CorrectedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' "
        + "AND [CorrectedVkn] <> [StagedVkn])";
    internal const string CompanyTaxIdCorrectionAppliedCheck =
        "([Status] = N'Pending' AND [AppliedBy] IS NULL AND [AppliedAtUtc] IS NULL) "
        + "OR ([Status] = N'Applied' AND [AppliedBy] IS NOT NULL AND [AppliedAtUtc] IS NOT NULL)";
    internal const string CompanyTaxIdCorrectionVersionCheck = "[Version] >= 1 AND [CompanyVersionAtStaging] >= 1";
    internal const string CompanyTaxIdCorrectionPendingIndexName = "IX_CompanyTaxIdCorrections_CompanyId_Pending";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureCompany(modelBuilder);
        ConfigureSite(modelBuilder);
        ConfigureLocation(modelBuilder);
        ConfigureWorkCenter(modelBuilder);
        ConfigureWorkCenterCalendar(modelBuilder);
        ConfigureCompanyTaxIdChangeRequest(modelBuilder);
        ConfigureCompanyTaxIdCorrection(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureCompany(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Company>();
        entity.ToTable("Companies", "organization", table =>
        {
            table.HasCheckConstraint(
                "CK_Companies_Vkn_Format",
                "[Vkn] IS NULL OR (LEN([Vkn]) = 10 AND [Vkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
            table.HasCheckConstraint(
                "CK_Companies_Placeholder_InvoiceDisabled",
                "[InvoiceCapable] = 0 OR ([IsPlaceholder] = 0 AND [Vkn] IS NOT NULL)");
            table.HasCheckConstraint("CK_Companies_Vkn_RequiredUnlessPlaceholder", CompanyVknRequiredCheck);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Vkn).HasMaxLength(10);
        entity.Property(x => x.InvoiceCapable).HasDefaultValue(false);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.Vkn)
            .IsUnique()
            .HasFilter(CompanyVknUniqueFilter)
            .HasDatabaseName(CompanyVknUniqueIndexName);
    }

    internal const string CompanyVknUniqueIndexName = "IX_Companies_Vkn";
    internal const string CompanyTaxIdChangePendingIndexName = "IX_CompanyTaxIdChangeRequests_CompanyId_Pending";

    /// <summary>
    /// KIM-D41/KIM-D48: staged VKN corrections. The company owns its rows (query filter + write guard), at most one
    /// correction per company is pending (filtered unique index), an applied row names its approver and a row is never
    /// deleted.
    /// </summary>
    private void ConfigureCompanyTaxIdCorrection(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyTaxIdCorrection>();
        entity.ToTable("CompanyTaxIdCorrections", "organization", table =>
        {
            table.HasCheckConstraint("CK_CompanyTaxIdCorrections_Status", CompanyTaxIdCorrectionStatusCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdCorrections_StagedVkn", CompanyTaxIdCorrectionStagedVknCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdCorrections_CorrectedVkn", CompanyTaxIdCorrectionCorrectedVknCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdCorrections_Applied", CompanyTaxIdCorrectionAppliedCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdCorrections_Version", CompanyTaxIdCorrectionVersionCheck);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Source).HasMaxLength(CompanyTaxIdCorrection.SourceMaximumLength).IsRequired();
        entity.Property(x => x.StagedVkn).HasMaxLength(Company.VknLength).IsRequired();
        entity.Property(x => x.CorrectedVkn).HasMaxLength(Company.VknLength);
        entity.Property(x => x.Reason).HasMaxLength(CompanyTaxIdCorrection.ReasonMaximumLength).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.AppliedBy).HasMaxLength(CompanyTaxIdCorrection.ActorMaximumLength);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Ignore(x => x.IsPending);
        entity.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter(CompanyTaxIdCorrectionPendingFilter)
            .HasDatabaseName(CompanyTaxIdCorrectionPendingIndexName);
        entity.HasIndex(x => new { x.CompanyId, x.StagedAtUtc });
        entity.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// KIM-S23: the company VKN change requests. The company owns its rows (query filter + write guard), at most one
    /// request per company is pending (filtered unique index), the decider is never the preparer (check constraint
    /// next to the service and domain rules) and a request is never deleted.
    /// </summary>
    private void ConfigureCompanyTaxIdChangeRequest(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyTaxIdChangeRequest>();
        entity.ToTable("CompanyTaxIdChangeRequests", "organization", table =>
        {
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_Status", CompanyTaxIdChangeStatusCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_ProposedVkn", CompanyTaxIdChangeProposedVknCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_CurrentVkn", CompanyTaxIdChangeCurrentVknCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_DistinctVkn", CompanyTaxIdChangeDistinctVknCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_Decision", CompanyTaxIdChangeDecisionCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_MakerChecker", CompanyTaxIdChangeMakerCheckerCheck);
            table.HasCheckConstraint("CK_CompanyTaxIdChangeRequests_Version", CompanyTaxIdChangeVersionCheck);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.CurrentVkn).HasMaxLength(Company.VknLength);
        entity.Property(x => x.ProposedVkn).HasMaxLength(Company.VknLength).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(CompanyTaxIdChangeRequest.ReasonMaximumLength).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.RequestedBy).HasMaxLength(CompanyTaxIdChangeRequest.ActorMaximumLength).IsRequired();
        entity.Property(x => x.DecidedBy).HasMaxLength(CompanyTaxIdChangeRequest.ActorMaximumLength);
        entity.Property(x => x.DecisionNote).HasMaxLength(CompanyTaxIdChangeRequest.NoteMaximumLength);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Ignore(x => x.IsPending);
        entity.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter(CompanyTaxIdChangePendingFilter)
            .HasDatabaseName(CompanyTaxIdChangePendingIndexName);
        entity.HasIndex(x => new { x.CompanyId, x.RequestedAtUtc });
        entity.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureSite(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Site>();
        ConfigureTenantEntity(entity, "Sites");
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        entity.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLocation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Location>();
        ConfigureTenantEntity(entity, "Locations");
        entity.ToTable("Locations", "organization", table =>
        {
            table.HasCheckConstraint(
                "CK_Locations_InternalOperatorParty",
                "[IsExternal] = 1 OR [OperatorPartyId] IS NULL");
            table.HasCheckConstraint(
                "CK_Locations_OperatorParty_NotEmpty",
                "[OperatorPartyId] IS NULL OR [OperatorPartyId] <> '00000000-0000-0000-0000-000000000000'");
        });
        entity.HasOne<Site>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SiteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureWorkCenter(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkCenter>();
        ConfigureTenantEntity(entity, "WorkCenters");
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        entity.HasOne<Location>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// B-15: one calendar head per work centre (unique on the company and work centre) and append-only revision rows.
    /// Every table carries the company, its composite foreign keys stay inside the company, and the query filter fences
    /// reads like every other Organization table.
    /// </summary>
    private void ConfigureWorkCenterCalendar(ModelBuilder modelBuilder)
    {
        var calendar = modelBuilder.Entity<WorkCenterCalendar>();
        calendar.ToTable("WorkCenterCalendars", "organization", table =>
            table.HasCheckConstraint("CK_WorkCenterCalendars_Version", WorkCenterCalendarVersionCheck));
        calendar.HasKey(x => x.Id);
        calendar.Property(x => x.Id).ValueGeneratedNever();
        calendar.HasAlternateKey(x => new { x.CompanyId, x.Id });
        calendar.Property(x => x.Version).IsConcurrencyToken();
        calendar.Property(x => x.RowVersion).IsRowVersion();
        calendar.HasIndex(x => new { x.CompanyId, x.WorkCenterId }).IsUnique();
        calendar.HasOne<WorkCenter>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.WorkCenterId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        calendar.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var shift = modelBuilder.Entity<WorkCenterCalendarShiftRecord>();
        shift.ToTable("WorkCenterCalendarShifts", "organization", table =>
        {
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Revision", WorkCenterCalendarShiftRevisionCheck);
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Day", WorkCenterCalendarShiftDayCheck);
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Window", WorkCenterCalendarShiftWindowCheck);
        });
        shift.HasKey(x => x.Id);
        shift.Property(x => x.Id).ValueGeneratedNever();
        shift.Property(x => x.StartTime).HasColumnType("time(0)");
        shift.Property(x => x.EndTime).HasColumnType("time(0)");
        shift.HasIndex(x => new { x.CompanyId, x.CalendarId, x.Revision, x.Day, x.StartTime }).IsUnique();
        shift.HasOne<WorkCenterCalendar>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CalendarId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        shift.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var closedDate = modelBuilder.Entity<WorkCenterCalendarClosedDateRecord>();
        closedDate.ToTable("WorkCenterCalendarClosedDates", "organization", table =>
            table.HasCheckConstraint(
                "CK_WorkCenterCalendarClosedDates_Revision",
                WorkCenterCalendarClosedDateRevisionCheck));
        closedDate.HasKey(x => x.Id);
        closedDate.Property(x => x.Id).ValueGeneratedNever();
        closedDate.HasIndex(x => new { x.CompanyId, x.CalendarId, x.Revision, x.Date }).IsUnique();
        closedDate.HasOne<WorkCenterCalendar>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CalendarId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        closedDate.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void ConfigureTenantEntity<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        string tableName)
        where TEntity : TenantEntity
    {
        entity.ToTable(tableName, "organization");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.IsActive });
    }

    private void ApplyTenantWriteGuards()
    {
        OrganizationAuditEventTenantGuard.Validate(this, companyContext);

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Kritik organizasyon kaydı CompanyId olmadan yazılamaz.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(x => x.CompanyId).IsModified)
            {
                throw new InvalidOperationException("Bir organizasyon kaydının CompanyId değeri değiştirilemez.");
            }

            if (!bootstrapWriteScope.IsActive)
            {
                if (!companyContext.IsResolved)
                {
                    throw new InvalidOperationException(
                        "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.");
                }

                if (entry.Entity.CompanyId != companyContext.CompanyId)
                {
                    throw new InvalidOperationException(
                        "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.");
                }
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Touch(now);
            }
        }

        foreach (var entry in ChangeTracker.Entries<ICompanyOwnedRecord>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted
                || (entry.State == EntityState.Modified
                    && entry.Entity is not (WorkCenterCalendar or CompanyTaxIdChangeRequest or CompanyTaxIdCorrection)))
            {
                // A calendar head is never deleted and a revision row is append-only: a change is a new revision.
                // A VKN change request is never deleted either; only its decision closes it (KIM-S23). A staged VKN
                // correction is never deleted; only its application closes it (KIM-D48).
                throw new InvalidOperationException(
                    "İş merkezi takvimi kayıtları, VKN değişiklik talepleri ve VKN düzeltmeleri silinemez; takvim satırları değiştirilemez.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Kritik organizasyon kaydı CompanyId olmadan yazılamaz.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(nameof(ICompanyOwnedRecord.CompanyId)).IsModified)
            {
                throw new InvalidOperationException("Bir organizasyon kaydının CompanyId değeri değiştirilemez.");
            }

            if (!bootstrapWriteScope.IsActive)
            {
                if (!companyContext.IsResolved)
                {
                    throw new InvalidOperationException(
                        "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.");
                }

                if (entry.Entity.CompanyId != companyContext.CompanyId)
                {
                    throw new InvalidOperationException(
                        "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.");
                }
            }
        }

        ApplyCompanyWriteGuard();

        foreach (var entry in ChangeTracker.Entries<CommandExecution>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Idempotency kayıtları değiştirilemez veya silinemez.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Idempotency kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }

    /// <summary>
    /// KIM-S23: outside the bootstrap seed a company row is never created or deleted through this context, and the only
    /// company a request may change is the resolved active company (the approved VKN change). Companies carry no query
    /// filter (the context directory reads them), so this guard is what fences a write to another company.
    /// </summary>
    private void ApplyCompanyWriteGuard()
    {
        if (bootstrapWriteScope.IsActive)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<Company>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Şirket kaydı yalnız kurulum tohumlamasında oluşturulabilir ve silinemez.");
            }

            if (entry.State == EntityState.Modified
                && (!companyContext.IsResolved || entry.Entity.Id != companyContext.CompanyId))
            {
                throw new InvalidOperationException(
                    "Yalnız aktif şirket bağlamındaki şirket kaydı değiştirilebilir.");
            }
        }
    }
}
