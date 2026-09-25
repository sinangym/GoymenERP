using System.Security.Cryptography;
using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductReaderTests
{
    private static readonly Guid GroupId = Guid.Parse("b9265f2f-a18b-451f-b49a-b8e7bcae325f");

    [Theory]
    [InlineData("page")]
    [InlineData("card")]
    [InlineData("groups")]
    [InlineData("references")]
    public async Task Missing_global_authority_denies_before_cursor_parsing_or_any_database_connection(string operation)
    {
        await using var context = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false").Options);
        using var cursors = new ProductCursorCodec(RandomNumberGenerator.GetBytes(32), TimeProvider.System, TimeSpan.FromMinutes(5));
        var reader = new ProductReader(new(context), cursors, new UnconfiguredGroupSharedMasterAuthorizationService());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation switch
        {
            "page" => reader.GetPageAsync(new(Cursor: "invalid"), default),
            "card" => reader.GetAsync(Guid.NewGuid(), default),
            "references" => reader.GetReferenceOptionsAsync(new(Guid.NewGuid()), default),
            _ => (Task)reader.GetCodeGroupsAsync(default),
        });
    }

    [Theory]
    [InlineData(true, "revoked")]
    [InlineData(false, "revoked")]
    [InlineData(true, "actor")]
    [InlineData(false, "actor")]
    [InlineData(true, "origin")]
    [InlineData(false, "origin")]
    [InlineData(true, "revision")]
    [InlineData(false, "revision")]
    public async Task Authority_change_after_catalogue_read_hides_both_existing_and_missing_group(bool exists, string change)
    {
        var authority = new ChangingAuthority();
        var catalog = new ControlledCatalog(exists, () => authority.Snapshot = change switch
        {
            "revoked" => authority.Snapshot with { IsGranted = false },
            "actor" => authority.Snapshot with { LocalActorId = Guid.NewGuid() },
            "origin" => authority.Snapshot with { OriginCompanyId = Guid.NewGuid() },
            _ => authority.Snapshot with { AuthorityRevision = "after-read-revision" },
        });
        await using var context = Context(catalog);
        using var cursors = Cursors();
        var reader = new ProductReader(new(context), cursors, authority);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetReferenceOptionsAsync(new(GroupId), default));
        Assert.Equal(1, catalog.Reads);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Stable_authority_preserves_the_original_missing_reference_error_after_the_final_check()
    {
        var authority = new ChangingAuthority();
        var catalog = new ControlledCatalog(false, () => { });
        await using var context = Context(catalog);
        using var cursors = Cursors();
        var reader = new ProductReader(new(context), cursors, authority);

        var error = await Assert.ThrowsAsync<ProductValidationException>(() => reader.GetReferenceOptionsAsync(new(GroupId), default));
        Assert.Equal("groupId", Assert.Single(error.Errors).Field);
        Assert.Equal("invalid_reference", Assert.Single(error.Errors).Code);
        Assert.Equal(1, catalog.Reads);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Stable_authority_still_returns_reference_options_after_the_final_check()
    {
        var authority = new ChangingAuthority();
        var catalog = new ControlledCatalog(true, () => { });
        await using var context = Context(catalog);
        using var cursors = Cursors();
        var reader = new ProductReader(new(context), cursors, authority);

        var options = await reader.GetReferenceOptionsAsync(new(GroupId), default);
        Assert.Equal(GroupId, options.GroupId);
        Assert.Equal(1, catalog.Reads);
        Assert.Equal(2, authority.Calls);
        // PRD-FAM-1: no released family definition, so families and templates stay unavailable.
        Assert.Equal(1, catalog.RegistryReads);
        Assert.Empty(options.Families);
        Assert.Equal(["classes", "families", "units", "trackingPolicies", "templates"], options.UnavailableCatalogs);
    }

    [Fact]
    public async Task Canceled_catalogue_read_preserves_cancellation_without_an_authority_error_or_reference_disclosure()
    {
        using var cancellation = new CancellationTokenSource();
        var authority = new ChangingAuthority();
        var catalog = new ControlledCatalog(false, () =>
        {
            authority.Snapshot = authority.Snapshot with { IsGranted = false };
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });
        await using var context = Context(catalog);
        using var cursors = Cursors();
        var reader = new ProductReader(new(context), cursors, authority);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetReferenceOptionsAsync(new(GroupId), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, catalog.Reads);
        Assert.Equal(1, authority.Calls);
    }

    private static ProductDbContext Context(ControlledCatalog catalog) => new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ControlledProductReader;Integrated Security=true;Encrypt=false")
        .AddInterceptors(new PreventConnectionOpen(), catalog).Options);

    private static ProductCursorCodec Cursors() => new(RandomNumberGenerator.GetBytes(32), TimeProvider.System, TimeSpan.FromMinutes(5));

    private sealed class ChangingAuthority : IGroupSharedMasterAuthorizationService
    {
        public int Calls { get; private set; }
        public GroupSharedMasterAuthorizationSnapshot Snapshot { get; set; } = new(true, GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.Read, Guid.NewGuid(), Guid.NewGuid(), "before-read-revision");

        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    // Controlled EF response, not SQL evidence. Both opening paths are suppressed;
    // the query's reader is supplied locally so no database connection is opened.
    private sealed class PreventConnectionOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class ControlledCatalog(bool exists, Action afterRead) : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public int RegistryReads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[product].[Families]", StringComparison.Ordinal))
            {
                // The family registry is empty: no released definition (PRD-FAM-1).
                RegistryReads++;
                var families = new DataTable();
                foreach (var column in new[] { "Id", "Cins", "Code", "Name", "RuleSet", "SourceRef", "Tip", "Tur" })
                    families.Columns.Add(column, column == "Id" ? typeof(Guid) : typeof(string));
                return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(families.CreateDataReader()));
            }

            Assert.Contains("[product].[CodeGroups]", command.CommandText);
            Reads++;
            var table = new DataTable();
            table.Columns.Add("Id", typeof(Guid));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Prefix", typeof(string));
            if (exists) table.Rows.Add(GroupId, "Kontrollü grup", "TEST");
            afterRead();
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
