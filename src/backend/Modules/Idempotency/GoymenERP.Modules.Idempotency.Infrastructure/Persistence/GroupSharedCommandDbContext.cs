using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

// Owner-only global terminal storage for the exact Product command key. Origin is
// evidence, not tenancy. No registration/migration is activated by this model.
public sealed class GroupSharedCommandDbContext(DbContextOptions<GroupSharedCommandDbContext> options)
    : DbContext(options)
{
    internal DbSet<GroupSharedCommandTerminalRecord> Terminals => Set<GroupSharedCommandTerminalRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GroupSharedCommandTerminalRecord>();
        entity.ToTable("GroupSharedCommandTerminals", "idempotency", table =>
        {
            table.HasCheckConstraint("CK_GroupSharedTerminals_ProductCommands",
                "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1')");
            table.HasCheckConstraint("CK_GroupSharedTerminals_ActorOrigin",
                "[LocalActorId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[OriginCompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_GroupSharedTerminals_Hashes",
                "DATALENGTH([IdempotencyKeyHash]) = 64 AND [IdempotencyKeyHash] NOT LIKE '%[^0-9A-F]%' "
                + "AND DATALENGTH([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9A-F]%'");
            table.HasCheckConstraint("CK_GroupSharedTerminals_Response",
                "ISJSON([ResponseJson], VALUE) = 1 AND [StatusCode] BETWEEN 200 AND 599 AND [SchemaVersion] >= 1");
            table.HasCheckConstraint("CK_GroupSharedTerminals_CompletedUtc", "DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
        });
        entity.HasKey(x => new { x.Subject, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.Subject).HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.CommandName).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.OriginalCorrelationId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
    }

    internal async Task InsertAsync(GroupSharedCommandTerminalRecord record, CancellationToken cancellationToken)
    {
        if (!Database.IsRelational() || Database.CurrentTransaction is null)
            throw new InvalidOperationException("A shared owner transaction must be enlisted before terminal insertion.");
        if (ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A terminal context is single-use and append-only.");
        Terminals.Add(record);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override int SaveChanges() => throw ReadOnly();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw ReadOnly();

    private static InvalidOperationException ReadOnly() =>
        new("Shared terminals can only be inserted by the enlisted Idempotency owner session.");
}

public sealed class GroupSharedCommandTerminalRecord
{
    private GroupSharedCommandTerminalRecord() { }

    public string Subject { get; private set; } = null!;
    public string CommandName { get; private set; } = null!;
    public string IdempotencyKeyHash { get; private set; } = null!;
    public Guid LocalActorId { get; private set; }
    public Guid OriginCompanyId { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public int StatusCode { get; private set; }
    public string ResponseJson { get; private set; } = null!;
    public string OriginalCorrelationId { get; private set; } = null!;
    public int SchemaVersion { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static GroupSharedCommandTerminalRecord FromTerminal(GroupSharedCommandTerminal terminal, DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (completedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Terminal completion must be UTC.", nameof(completedAtUtc));
        return new()
        {
            Subject = terminal.Binding.Key.Subject,
            CommandName = terminal.Binding.Key.CommandName,
            IdempotencyKeyHash = terminal.Binding.Key.IdempotencyKeyHash,
            LocalActorId = terminal.Binding.LocalActorId,
            OriginCompanyId = terminal.Binding.OriginCompanyId,
            RequestHash = terminal.Binding.RequestHash,
            StatusCode = terminal.StatusCode,
            ResponseJson = terminal.ResponseJson,
            OriginalCorrelationId = terminal.OriginalCorrelationId,
            SchemaVersion = terminal.SchemaVersion,
            CompletedAtUtc = completedAtUtc,
        };
    }

    public GroupSharedCommandTerminal RestoreTerminal() => GroupSharedCommandTerminal.Create(
        GroupSharedCommandBinding.RestoreStored(Subject, CommandName, IdempotencyKeyHash,
            LocalActorId, OriginCompanyId, RequestHash),
        StatusCode, ResponseJson, OriginalCorrelationId, SchemaVersion);
}
