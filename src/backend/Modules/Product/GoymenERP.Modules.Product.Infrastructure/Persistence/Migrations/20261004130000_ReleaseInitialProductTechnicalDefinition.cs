using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decision 4A: publishes the first source-stamped Product family/template/dictionary release. This
/// migration changes data only. It refuses a non-empty registry rather than merging or rewriting an
/// unknown release. Down removes only this exact footprint and refuses while a stored Product card
/// references one of its families or its template.
/// </summary>
[DbContext(typeof(ProductDbContext))]
[Migration(MigrationId)]
public sealed class ReleaseInitialProductTechnicalDefinition : Migration
{
    public const string MigrationId = "20261004130000_ReleaseInitialProductTechnicalDefinition";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [product].[Families])
                OR EXISTS (SELECT 1 FROM [product].[Templates])
                OR EXISTS (SELECT 1 FROM [product].[TemplateFamilies])
                OR EXISTS (SELECT 1 FROM [product].[TemplateFields])
                OR EXISTS (SELECT 1 FROM [product].[DictionaryEntries])
                THROW 51044, 'Product technical definition release requires an empty registry; use a reviewed forward reconciliation.', 1;

            INSERT INTO [product].[Families]
                ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef])
            VALUES
                ('b2b7079a-14ad-41ee-8d3a-35d55d1a21e9', 'hr-coil', N'Sıcak Haddelenmiş Rulo Sac', N'Sac', N'Sıcak Haddelenmiş', N'Rulo', 'gdc-band-coil', 'issue66-5973142585-4A'),
                ('75e15f39-bfcb-4fea-9065-a602e2539439', 'cr-coil', N'Soğuk Haddelenmiş Rulo Sac', N'Sac', N'Soğuk Haddelenmiş', N'Rulo', 'gdc-band-coil', 'issue66-5973142585-4A'),
                ('2ffdf417-c7b0-4cc5-8c58-40b5cd906c55', 'hr-slit-coil', N'Sıcak Haddelenmiş Dilinmiş Bant', N'Sac', N'Sıcak Haddelenmiş', N'Dilinmiş Bant', 'gdc-band-coil', 'issue66-5973142585-4A'),
                ('1917d6df-f2de-4717-80fc-56255161b0d0', 'cr-slit-coil', N'Soğuk Haddelenmiş Dilinmiş Bant', N'Sac', N'Soğuk Haddelenmiş', N'Dilinmiş Bant', 'gdc-band-coil', 'issue66-5973142585-4A');

            INSERT INTO [product].[Templates]
                ([Id], [Version], [Code], [Name], [SourceRef])
            VALUES
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'gdc-flat-coil-v1', N'GDC Rulo ve Dilinmiş Bant Teknik Kartı', 'issue66-5973142585-4A');

            INSERT INTO [product].[TemplateFamilies]
                ([TemplateId], [TemplateVersion], [FamilyId])
            VALUES
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'b2b7079a-14ad-41ee-8d3a-35d55d1a21e9'),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, '75e15f39-bfcb-4fea-9065-a602e2539439'),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55'),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, '1917d6df-f2de-4717-80fc-56255161b0d0');

            INSERT INTO [product].[TemplateFields]
                ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required],
                 [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField])
            VALUES
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'thickness-mm', 1, 1, 'thickness', 1, NULL, 0, NULL, NULL, NULL),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'width-mm', 2, 1, 'width', 1, NULL, 0, NULL, NULL, NULL),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'steel-grade', 3, 2, 'quality', 1, 'gdc-steel-grades', 0, NULL, NULL, NULL),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'coating', 4, 2, 'coating', 1, 'gdc-coatings', 0, NULL, NULL, NULL),
                ('ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2', 1, 'producer-grade', 5, 2, NULL, 0, 'gdc-producer-grades', 1, NULL, NULL, 'steel-grade');

            INSERT INTO [product].[DictionaryEntries]
                ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [ParentCode], [MassGm2], [IsExplicitNone])
            VALUES
                ('gdc-steel-grades', N'S235JR', N'S235JR', 1, 'product-source-40eb0b82', NULL, NULL, 0),
                ('gdc-producer-grades', N'erdemir:3237', N'Erdemir 3237 — S235JR+AR', 1, 'erdemir-flat-2021-p16', N'S235JR', NULL, 0),
                ('gdc-producer-grades', N'erdemir:4237', N'Erdemir 4237 — S235JR+AR CTA', 1, 'erdemir-flat-2021-p16', N'S235JR', NULL, 0),
                ('gdc-producer-grades', N'tosyali:TOS3780', N'Tosyalı TOS3780 — S235JR Düşük Si', 1, 'tosyali-flat-undated-p14', N'S235JR', NULL, 0),
                ('gdc-coatings', N'uncoated', N'Kaplamasız', 1, 'product-source-40eb0b82', NULL, NULL, 1);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF (SELECT COUNT(*) FROM [product].[Families]
                    WHERE [Id] IN ('b2b7079a-14ad-41ee-8d3a-35d55d1a21e9', '75e15f39-bfcb-4fea-9065-a602e2539439',
                        '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55', '1917d6df-f2de-4717-80fc-56255161b0d0')
                       OR [SourceRef] = 'issue66-5973142585-4A') <> 4
                OR NOT EXISTS (SELECT 1 FROM [product].[Families] WHERE [Id] = 'b2b7079a-14ad-41ee-8d3a-35d55d1a21e9' AND [Code] = 'hr-coil' AND [Name] = N'Sıcak Haddelenmiş Rulo Sac' AND [Cins] = N'Sac' AND [Tur] = N'Sıcak Haddelenmiş' AND [Tip] = N'Rulo' AND [RuleSet] = 'gdc-band-coil' AND [SourceRef] = 'issue66-5973142585-4A')
                OR NOT EXISTS (SELECT 1 FROM [product].[Families] WHERE [Id] = '75e15f39-bfcb-4fea-9065-a602e2539439' AND [Code] = 'cr-coil' AND [Name] = N'Soğuk Haddelenmiş Rulo Sac' AND [Cins] = N'Sac' AND [Tur] = N'Soğuk Haddelenmiş' AND [Tip] = N'Rulo' AND [RuleSet] = 'gdc-band-coil' AND [SourceRef] = 'issue66-5973142585-4A')
                OR NOT EXISTS (SELECT 1 FROM [product].[Families] WHERE [Id] = '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55' AND [Code] = 'hr-slit-coil' AND [Name] = N'Sıcak Haddelenmiş Dilinmiş Bant' AND [Cins] = N'Sac' AND [Tur] = N'Sıcak Haddelenmiş' AND [Tip] = N'Dilinmiş Bant' AND [RuleSet] = 'gdc-band-coil' AND [SourceRef] = 'issue66-5973142585-4A')
                OR NOT EXISTS (SELECT 1 FROM [product].[Families] WHERE [Id] = '1917d6df-f2de-4717-80fc-56255161b0d0' AND [Code] = 'cr-slit-coil' AND [Name] = N'Soğuk Haddelenmiş Dilinmiş Bant' AND [Cins] = N'Sac' AND [Tur] = N'Soğuk Haddelenmiş' AND [Tip] = N'Dilinmiş Bant' AND [RuleSet] = 'gdc-band-coil' AND [SourceRef] = 'issue66-5973142585-4A')
                OR (SELECT COUNT(*) FROM [product].[Templates]
                    WHERE ([Id] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [Version] = 1)
                       OR [SourceRef] = 'issue66-5973142585-4A') <> 1
                OR NOT EXISTS (SELECT 1 FROM [product].[Templates] WHERE [Id] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [Version] = 1 AND [Code] = 'gdc-flat-coil-v1' AND [Name] = N'GDC Rulo ve Dilinmiş Bant Teknik Kartı' AND [SourceRef] = 'issue66-5973142585-4A')
                OR (SELECT COUNT(*) FROM [product].[TemplateFamilies]
                    WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1) <> 4
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFamilies] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [FamilyId] = 'b2b7079a-14ad-41ee-8d3a-35d55d1a21e9')
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFamilies] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [FamilyId] = '75e15f39-bfcb-4fea-9065-a602e2539439')
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFamilies] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [FamilyId] = '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55')
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFamilies] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [FamilyId] = '1917d6df-f2de-4717-80fc-56255161b0d0')
                OR (SELECT COUNT(*) FROM [product].[TemplateFields]
                    WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1) <> 5
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm' AND [Position] = 1 AND [Kind] = 1 AND [Role] = 'thickness' AND [Required] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'width-mm' AND [Position] = 2 AND [Kind] = 1 AND [Role] = 'width' AND [Required] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'steel-grade' AND [Position] = 3 AND [Kind] = 2 AND [Role] = 'quality' AND [Required] = 1 AND [Dictionary] = 'gdc-steel-grades' AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'coating' AND [Position] = 4 AND [Kind] = 2 AND [Role] = 'coating' AND [Required] = 1 AND [Dictionary] = 'gdc-coatings' AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade' AND [Position] = 5 AND [Kind] = 2 AND [Role] IS NULL AND [Required] = 0 AND [Dictionary] = 'gdc-producer-grades' AND [AllowsCandidate] = 1 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] = 'steel-grade')
                OR (SELECT COUNT(*) FROM [product].[DictionaryEntries]
                    WHERE ([Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR')
                       OR ([Dictionary] = 'gdc-producer-grades' AND [Code] IN (N'erdemir:3237', N'erdemir:4237', N'tosyali:TOS3780'))
                       OR ([Dictionary] = 'gdc-coatings' AND [Code] = N'uncoated')) <> 5
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries] WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR' AND [DisplayName] = N'S235JR' AND [Status] = 1 AND [SourceRef] = 'product-source-40eb0b82' AND [ParentCode] IS NULL AND [MassGm2] IS NULL AND [IsExplicitNone] = 0)
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries] WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237' AND [DisplayName] = N'Erdemir 3237 — S235JR+AR' AND [Status] = 1 AND [SourceRef] = 'erdemir-flat-2021-p16' AND [ParentCode] = N'S235JR' AND [MassGm2] IS NULL AND [IsExplicitNone] = 0)
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries] WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:4237' AND [DisplayName] = N'Erdemir 4237 — S235JR+AR CTA' AND [Status] = 1 AND [SourceRef] = 'erdemir-flat-2021-p16' AND [ParentCode] = N'S235JR' AND [MassGm2] IS NULL AND [IsExplicitNone] = 0)
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries] WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'tosyali:TOS3780' AND [DisplayName] = N'Tosyalı TOS3780 — S235JR Düşük Si' AND [Status] = 1 AND [SourceRef] = 'tosyali-flat-undated-p14' AND [ParentCode] = N'S235JR' AND [MassGm2] IS NULL AND [IsExplicitNone] = 0)
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries] WHERE [Dictionary] = 'gdc-coatings' AND [Code] = N'uncoated' AND [DisplayName] = N'Kaplamasız' AND [Status] = 1 AND [SourceRef] = 'product-source-40eb0b82' AND [ParentCode] IS NULL AND [MassGm2] IS NULL AND [IsExplicitNone] = 1)
                THROW 51044, 'Product technical definition release is partial or changed; use a reviewed forward fix.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Products]
                WHERE [FamilyId] IN ('b2b7079a-14ad-41ee-8d3a-35d55d1a21e9', '75e15f39-bfcb-4fea-9065-a602e2539439',
                    '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55', '1917d6df-f2de-4717-80fc-56255161b0d0')
                   OR (JSON_VALUE([ReferencesJson], '$.templateId') = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2'
                       AND JSON_VALUE([ReferencesJson], '$.templateVersion') = '1'))
                THROW 51044, 'Product cards reference the technical definition release; rollback requires a reviewed forward fix.', 1;

            IF OBJECT_ID(N'[idempotency].[GroupSharedCommandTerminals]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals]
                    WHERE [Subject] = 'Product'
                      AND ([CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1',
                                            'product.card.activate.v1', 'product.card.deactivate.v1', 'product.card.reactivate.v1'))
                      AND (JSON_VALUE([ResponseJson], '$.card.product.familyId') IN
                            ('b2b7079a-14ad-41ee-8d3a-35d55d1a21e9', '75e15f39-bfcb-4fea-9065-a602e2539439',
                             '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55', '1917d6df-f2de-4717-80fc-56255161b0d0')
                           OR (JSON_VALUE([ResponseJson], '$.card.references.templateId') = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2'
                               AND JSON_VALUE([ResponseJson], '$.card.references.templateVersion') = '1')))
                    THROW 51044, 'Product replay history references the technical definition release; rollback requires a reviewed forward fix.', 1;
            END;

            DELETE FROM [product].[TemplateFields]
                WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1;
            DELETE FROM [product].[TemplateFamilies]
                WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1;
            DELETE FROM [product].[Templates]
                WHERE [Id] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [Version] = 1;
            DELETE FROM [product].[Families]
                WHERE [Id] IN ('b2b7079a-14ad-41ee-8d3a-35d55d1a21e9', '75e15f39-bfcb-4fea-9065-a602e2539439',
                    '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55', '1917d6df-f2de-4717-80fc-56255161b0d0');
            DELETE FROM [product].[DictionaryEntries]
                WHERE ([Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR')
                   OR ([Dictionary] = 'gdc-producer-grades' AND [Code] IN (N'erdemir:3237', N'erdemir:4237', N'tosyali:TOS3780'))
                   OR ([Dictionary] = 'gdc-coatings' AND [Code] = N'uncoated');
            """);
    }
}
