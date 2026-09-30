using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Api.IntegrationTests;

// Model/controlled owner tests, not SQL acceptance. Public persistence remains unregistered.
public sealed class GroupSharedCommandPersistenceTests
{
    private static readonly Guid Actor = Guid.Parse("53000000-0000-0000-0000-000000000001");
    private static readonly Guid Origin = Guid.Parse("54000000-0000-0000-0000-000000000001");
    private static readonly string Key = new('A', 64);
    private static readonly string Payload = new('B', 64);

    [Fact]
    public void StoredBindingRestoresDataWithoutManufacturingAuthority()
    {
        var binding = Binding();
        var restored = GroupSharedCommandBinding.RestoreStored("Product", binding.Key.CommandName,
            Key, Actor, Origin, Payload);
        Assert.Equal(binding, restored);
        Assert.False(restored.MatchesReauthorizedIntent(Context(anotherOrigin: true), Key, Payload));
    }

    // #329: each owner command's stored terminal identity restores; the SQL check admits the same set.
    [Theory]
    [InlineData(GroupSharedMasterOperation.DraftCreate, "product.draft.create.v1")]
    [InlineData(GroupSharedMasterOperation.DraftBasicEdit, "product.draft.basic-edit.v1")]
    [InlineData(GroupSharedMasterOperation.Activate, "product.card.activate.v1")]
    [InlineData(GroupSharedMasterOperation.Deactivate, "product.card.deactivate.v1")]
    [InlineData(GroupSharedMasterOperation.Reactivate, "product.card.reactivate.v1")]
    public void EachOwnerCommandRestoresFromItsStoredIdentity(GroupSharedMasterOperation operation, string commandName)
    {
        var binding = GroupSharedCommandBinding.Create(Context(operation), Key, Payload);
        Assert.Equal(commandName, binding.Key.CommandName);
        Assert.Equal(binding, GroupSharedCommandBinding.RestoreStored("Product", commandName, Key, Actor, Origin, Payload));
        using var context = new GroupSharedCommandDbContext(new DbContextOptionsBuilder<GroupSharedCommandDbContext>()
            .UseSqlServer("Server=localhost;Database=MODEL_ONLY;Integrated Security=true;TrustServerCertificate=true").Options);
        var check = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GroupSharedCommandTerminalRecord))!
            .GetCheckConstraints().Single(x => x.Name == "CK_GroupSharedTerminals_ProductCommands").Sql;
        Assert.Contains($"'{commandName}'", check, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("command")]
    [InlineData("key")]
    [InlineData("payload")]
    [InlineData("actor")]
    [InlineData("origin")]
    public void CorruptStoredBindingsFailClosed(string change)
    {
        Assert.Throws<ArgumentException>(() => GroupSharedCommandBinding.RestoreStored(
            change == "subject" ? "Counterparty" : "Product",
            change == "command" ? "product.delete.v1" : "product.draft.create.v1",
            change == "key" ? Key.ToLowerInvariant() : Key,
            change == "actor" ? Guid.Empty : Actor, change == "origin" ? Guid.Empty : Origin,
            change == "payload" ? "missing" : Payload));
    }

    [Fact]
    public void TerminalRoundTripPreservesExactResponseAndOriginalMetadata()
    {
        var terminal = GroupSharedCommandTerminal.Create(Binding(), 201, " \n{\"name\":\"draft\"} \n", "original-correlation", 1);
        var record = GroupSharedCommandTerminalRecord.FromTerminal(terminal, DateTimeOffset.UtcNow);
        Assert.Equal(terminal, record.RestoreTerminal());
        Assert.Throws<ArgumentException>(() => GroupSharedCommandTerminalRecord.FromTerminal(terminal,
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3))));
    }

    [Fact]
    public void GlobalUniqueKeyDoesNotIncludeActorOrOrigin()
    {
        using var context = new GroupSharedCommandDbContext(new DbContextOptionsBuilder<GroupSharedCommandDbContext>()
            .UseSqlServer("Server=localhost;Database=MODEL_ONLY;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GroupSharedCommandTerminalRecord))!;
        Assert.Equal("idempotency", entity.GetSchema());
        Assert.Equal("GroupSharedCommandTerminals", entity.GetTableName());
        Assert.Equal(new[] { "Subject", "CommandName", "IdempotencyKeyHash" }, entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Null(entity.FindProperty("CompanyId"));
        Assert.Equal(5, entity.GetCheckConstraints().Count());
        Assert.Contains("ISJSON([ResponseJson], VALUE)", entity.GetCheckConstraints().Single(x => x.Name == "CK_GroupSharedTerminals_Response").Sql);
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty("IdempotencyKeyHash")!.GetCollation());
    }

    [Fact]
    public async Task AllPublicSavePathsStayClosedAndMissingReadDoesNotInsert()
    {
        var options = new DbContextOptionsBuilder<GroupSharedCommandDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new GroupSharedCommandDbContext(options);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(false));
        Assert.Null(await new SqlGroupSharedCommandTerminalStore(options).ReadAsync(Binding().Key));
        Assert.Empty(await context.Set<GroupSharedCommandTerminalRecord>().ToListAsync());
    }

    private static GroupSharedCommandBinding Binding() => GroupSharedCommandBinding.Create(Context(), Key, Payload);
    private static GroupSharedMasterCommandContext Context(bool anotherOrigin = false) =>
        GroupSharedMasterCommandContext.FromServerDecision(new(true, GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.DraftCreate, Actor, anotherOrigin ? Guid.NewGuid() : Origin, "rev-1"),
            GroupSharedMasterOperation.DraftCreate);
    private static GroupSharedMasterCommandContext Context(GroupSharedMasterOperation operation) =>
        GroupSharedMasterCommandContext.FromServerDecision(new(true, GroupSharedMasterKind.Product,
            operation, Actor, Origin, "rev-1"), operation);
}
