using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductReaderTests
{
    [Theory]
    [InlineData("page")]
    [InlineData("card")]
    [InlineData("groups")]
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
            _ => (Task)reader.GetCodeGroupsAsync(default),
        });
    }
}
