using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;

/// <summary>
/// Publishes the approved <c>Sac</c>/<c>SAC</c> Product code group. This migration changes data
/// only. It refuses an existing identity, name or prefix collision instead of adopting an
/// unverified row. Down removes only the exact unused footprint and preserves product/replay/code
/// history fail closed.
/// </summary>
[DbContext(typeof(ProductDbContext))]
[Migration(MigrationId)]
public sealed class ReleaseInitialProductCodeGroup : Migration
{
    public const string MigrationId = "20261004213000_ReleaseInitialProductCodeGroup";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [product].[CodeGroups]
                WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0'
                   OR [Name] = N'Sac'
                   OR [Prefix] = 'SAC')
                THROW 51045, 'Product Sac code-group release conflicts with an existing group; use a reviewed forward reconciliation.', 1;

            INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix])
            VALUES ('829e576d-c635-502a-97b8-9d5074c9d6a0', N'Sac', 'SAC');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM [product].[CodeGroups]
                WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0'
                  AND [Name] = N'Sac' AND [Prefix] = 'SAC')
                OR (SELECT COUNT(*) FROM [product].[CodeGroups]
                    WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0'
                       OR [Name] = N'Sac' OR [Prefix] = 'SAC') <> 1
                THROW 51045, 'Product Sac code-group release is missing or changed; use a reviewed forward fix.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Products]
                    WHERE [GroupId] = '829e576d-c635-502a-97b8-9d5074c9d6a0')
                OR EXISTS (SELECT 1 FROM [product].[CodeCounters] WHERE [Prefix] = 'SAC')
                OR EXISTS (SELECT 1 FROM [product].[CodeRegistrations] WHERE [Code] LIKE 'SAC-%')
                THROW 51045, 'Product cards or code history reference the Sac code-group release; rollback requires a reviewed forward fix.', 1;

            IF OBJECT_ID(N'[idempotency].[GroupSharedCommandTerminals]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals]
                    WHERE [Subject] = 'Product'
                      AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1',
                                            'product.card.activate.v1', 'product.card.deactivate.v1',
                                            'product.card.reactivate.v1')
                      AND JSON_VALUE([ResponseJson], '$.card.product.groupId') =
                          '829e576d-c635-502a-97b8-9d5074c9d6a0')
                THROW 51045, 'Product replay history references the Sac code-group release; rollback requires a reviewed forward fix.', 1;

            DELETE FROM [product].[CodeGroups]
            WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0';
            """);
    }
}
