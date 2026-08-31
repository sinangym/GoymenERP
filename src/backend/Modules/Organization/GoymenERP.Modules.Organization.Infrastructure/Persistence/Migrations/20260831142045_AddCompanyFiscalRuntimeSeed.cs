using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyFiscalRuntimeSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InvoiceCapable",
                schema: "organization",
                table: "Companies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Vkn",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.Sql(
                """
                DECLARE @GdcId uniqueidentifier = '10000000-0000-0000-0000-000000000001';
                DECLARE @GspId uniqueidentifier = '10000000-0000-0000-0000-000000000002';
                DECLARE @GinId uniqueidentifier = '10000000-0000-0000-0000-000000000003';

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Code] = N'GDC' AND [Id] <> @GdcId)
                    THROW 51020, 'GDC şirket kodu beklenen kalıcı şirket kimliğinden farklı bir kayda ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Code] = N'GSP' AND [Id] <> @GspId)
                    THROW 51021, 'GSP şirket kodu beklenen kalıcı şirket kimliğinden farklı bir kayda ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Code] = N'TBD-GOY-01' AND [Id] <> @GdcId)
                    THROW 51024, 'Eski GDC şirket kodu beklenen kalıcı şirket kimliğinden farklı bir kayda ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Code] = N'TBD-GOY-02' AND [Id] <> @GspId)
                    THROW 51025, 'Eski GSP şirket kodu beklenen kalıcı şirket kimliğinden farklı bir kayda ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Code] = N'GIN' AND [Id] <> @GinId)
                    THROW 51026, 'GIN şirket kodu beklenen kalıcı şirket kimliğinden farklı bir kayda ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Id] = @GinId AND [Code] <> N'GIN')
                    THROW 51027, 'GIN kalıcı şirket kimliği beklenmeyen bir şirket koduna ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Id] = @GdcId AND [Code] NOT IN (N'TBD-GOY-01', N'GDC'))
                    THROW 51022, 'GDC kalıcı şirket kimliği beklenmeyen bir şirket koduna ait.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [organization].[Companies]
                    WHERE [Id] = @GspId AND [Code] NOT IN (N'TBD-GOY-02', N'GSP'))
                    THROW 51023, 'GSP kalıcı şirket kimliği beklenmeyen bir şirket koduna ait.', 1;

                UPDATE [organization].[Companies]
                SET [Code] = N'GDC',
                    [LegalName] = N'Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti.',
                    [Vkn] = N'4100046586',
                    [IsPlaceholder] = 0,
                    [InvoiceCapable] = 1,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = @GdcId;

                UPDATE [organization].[Companies]
                SET [Code] = N'GSP',
                    [LegalName] = N'Göymen Sac Profil San. ve Tic. Ltd. Şti.',
                    [Vkn] = N'4101507252',
                    [IsPlaceholder] = 0,
                    [InvoiceCapable] = 1,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = @GspId;

                UPDATE [organization].[Companies]
                SET [Code] = N'GIN',
                    [LegalName] = N'Göymen İnşaat',
                    [Vkn] = N'1111111111',
                    [IsPlaceholder] = 1,
                    [InvoiceCapable] = 0,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = @GinId;

                -- Eski runtime seed aynı fiziksel topolojiyi iki placeholder şirkete de
                -- yazıyordu. GSP artık hukuki şirket olarak korunur ancak başlangıç
                -- topolojisinin sahibi yalnız GDC'dir. Yalnız eski seeder'ın deterministik
                -- kimlikleri pasifleştirilir; kayıtlar ve olası FK ilişkileri silinmez.
                UPDATE [organization].[WorkCenters]
                SET [IsActive] = 0,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [CompanyId] = @GspId
                  AND [IsActive] = 1
                  AND [Id] IN (
                      '81122f47-389b-e671-7134-8c7ceda1a775',
                      '4a9e89ce-74e7-fd35-af14-ceeca8680eac',
                      '215afe2b-7f09-8d46-1c05-fceba350667a',
                      '71be52bb-cc4c-5044-e329-4383b7a4e60e',
                      '9466ee81-9787-9f05-abcf-c1cdad88685c');

                UPDATE [organization].[Locations]
                SET [IsActive] = 0,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [CompanyId] = @GspId
                  AND [IsActive] = 1
                  AND [Id] IN (
                      'eadfa1e8-ba24-d0ab-8a9d-95673e805de4',
                      '70322b00-cdaf-750f-481c-6045759acfe1',
                      'c4fdb0dc-5ac3-f32d-01ac-f3d89d296a78',
                      'b8db432d-6a91-1659-4307-2da1b6fef84c',
                      'ab7c0ab3-78bd-8d2b-7f39-ecb1210688fd',
                      '33aed69a-29f1-6368-91d6-9852251b4169',
                      '28aaaa1e-a913-24f0-e74c-36c645f04447',
                      '8b3168e5-1f39-3fd6-c1bf-405fcaf8869a',
                      'fcda9540-b66a-56e8-55d5-942d202c5bb6',
                      '7a380edd-c04d-5c2e-c383-45904fdc43c1',
                      'cf05d752-7704-c582-2469-4161aada0550',
                      '5e7a4292-5046-fe43-53d9-e5e35488fc94',
                      'c8552c71-0874-2547-e541-16c7a859f3b4',
                      '06e51e93-4a8b-e24b-0cf4-e0f093b168f3',
                      'cc2e6f2a-6ade-6197-8871-403d41748d0e');

                UPDATE [organization].[Sites]
                SET [IsActive] = 0,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [CompanyId] = @GspId
                  AND [IsActive] = 1
                  AND [Id] IN (
                      '0655829a-27ee-8582-901c-0961d85263d7',
                      '1aa24258-9c68-200e-de9c-f956ecb010ed',
                      'd736ea68-393a-8ae5-adbe-b5708f85b9f6');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_Placeholder_InvoiceDisabled",
                schema: "organization",
                table: "Companies",
                sql: "[InvoiceCapable] = 0 OR ([IsPlaceholder] = 0 AND [Vkn] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_Vkn_Format",
                schema: "organization",
                table: "Companies",
                sql: "[Vkn] IS NULL OR (LEN([Vkn]) = 10 AND [Vkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_Placeholder_InvoiceDisabled",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_Vkn_Format",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "InvoiceCapable",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Vkn",
                schema: "organization",
                table: "Companies");
        }
    }
}
