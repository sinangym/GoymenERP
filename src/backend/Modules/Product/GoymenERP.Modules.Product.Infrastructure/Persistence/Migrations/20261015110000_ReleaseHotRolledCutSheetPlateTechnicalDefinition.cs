using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;

/// <summary>
/// PRODUCT-TECH-FAMILY-02: publishes the source-stamped hot-rolled cut-sheet/plate family and its own
/// thickness/width/length template. Existing coil/slit-strip rows and dictionaries are reused without
/// rewriting them. Down removes only the exact unreferenced additive footprint; referenced cards or
/// immutable command replay history require a reviewed forward fix.
/// </summary>
[DbContext(typeof(ProductDbContext))]
[Migration(MigrationId)]
public sealed class ReleaseHotRolledCutSheetPlateTechnicalDefinition : Migration
{
    public const string MigrationId = "20261015110000_ReleaseHotRolledCutSheetPlateTechnicalDefinition";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries]
                    WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR'
                      AND [Status] = 1 AND [SourceRef] = 'product-source-40eb0b82')
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries]
                    WHERE [Dictionary] = 'gdc-coatings' AND [Code] = N'uncoated'
                      AND [Status] = 1 AND [SourceRef] = 'product-source-40eb0b82' AND [IsExplicitNone] = 1)
                OR NOT EXISTS (SELECT 1 FROM [product].[DictionaryEntries]
                    WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237')
                THROW 51044, 'The accepted Product technical definition prerequisite is unavailable; use a reviewed forward reconciliation.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Families]
                    WHERE [Id] = '075d01d5-c57c-44b9-903a-89c00e4db029' OR [Code] = 'hr-cut-sheet-plate')
                OR EXISTS (SELECT 1 FROM [product].[Templates]
                    WHERE [Id] = '57937348-393e-4982-a5a4-63eff8be40c9'
                       OR [Code] = 'gdc-hot-rolled-cut-sheet-plate-v1')
                THROW 51044, 'The hot-rolled cut-sheet/plate identity is already occupied; use a reviewed forward reconciliation.', 1;

            INSERT INTO [product].[Families]
                ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef])
            VALUES
                ('075d01d5-c57c-44b9-903a-89c00e4db029', 'hr-cut-sheet-plate',
                 N'Sıcak Haddelenmiş Kesilmiş Sac/Levha', N'Sac', N'Sıcak Haddelenmiş',
                 N'Kesilmiş Sac/Levha', 'gdc-cut-sheet-plate',
                 'issue66-6180299892-product-tech-family-02');

            INSERT INTO [product].[Templates]
                ([Id], [Version], [Code], [Name], [SourceRef])
            VALUES
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1,
                 'gdc-hot-rolled-cut-sheet-plate-v1',
                 N'GDC Sıcak Haddelenmiş Kesilmiş Sac/Levha Teknik Kartı',
                 'issue66-6180299892-product-tech-family-02');

            INSERT INTO [product].[TemplateFamilies]
                ([TemplateId], [TemplateVersion], [FamilyId])
            VALUES
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1,
                 '075d01d5-c57c-44b9-903a-89c00e4db029');

            INSERT INTO [product].[TemplateFields]
                ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required],
                 [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField])
            VALUES
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'thickness-mm', 1, 1, 'thickness', 1, NULL, 0, NULL, NULL, NULL),
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'width-mm', 2, 1, 'width', 1, NULL, 0, NULL, NULL, NULL),
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'length-mm', 3, 1, 'length', 1, NULL, 0, NULL, NULL, NULL),
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'steel-grade', 4, 2, 'quality', 1, 'gdc-steel-grades', 0, NULL, NULL, NULL),
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'coating', 5, 2, 'coating', 1, 'gdc-coatings', 0, NULL, NULL, NULL),
                ('57937348-393e-4982-a5a4-63eff8be40c9', 1, 'producer-grade', 6, 2, NULL, 0, 'gdc-producer-grades', 1, NULL, NULL, 'steel-grade');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF (SELECT COUNT(*) FROM [product].[Families]
                    WHERE [Id] = '075d01d5-c57c-44b9-903a-89c00e4db029'
                       OR [Code] = 'hr-cut-sheet-plate'
                       OR [SourceRef] = 'issue66-6180299892-product-tech-family-02') <> 1
                OR NOT EXISTS (SELECT 1 FROM [product].[Families]
                    WHERE [Id] = '075d01d5-c57c-44b9-903a-89c00e4db029'
                      AND [Code] = 'hr-cut-sheet-plate'
                      AND [Name] = N'Sıcak Haddelenmiş Kesilmiş Sac/Levha'
                      AND [Cins] = N'Sac' AND [Tur] = N'Sıcak Haddelenmiş'
                      AND [Tip] = N'Kesilmiş Sac/Levha'
                      AND [RuleSet] = 'gdc-cut-sheet-plate'
                      AND [SourceRef] = 'issue66-6180299892-product-tech-family-02')
                OR (SELECT COUNT(*) FROM [product].[Templates]
                    WHERE [Id] = '57937348-393e-4982-a5a4-63eff8be40c9'
                       OR [Code] = 'gdc-hot-rolled-cut-sheet-plate-v1'
                       OR [SourceRef] = 'issue66-6180299892-product-tech-family-02') <> 1
                OR NOT EXISTS (SELECT 1 FROM [product].[Templates]
                    WHERE [Id] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [Version] = 1
                      AND [Code] = 'gdc-hot-rolled-cut-sheet-plate-v1'
                      AND [Name] = N'GDC Sıcak Haddelenmiş Kesilmiş Sac/Levha Teknik Kartı'
                      AND [SourceRef] = 'issue66-6180299892-product-tech-family-02')
                OR (SELECT COUNT(*) FROM [product].[TemplateFamilies]
                    WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1) <> 1
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFamilies]
                    WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1
                      AND [FamilyId] = '075d01d5-c57c-44b9-903a-89c00e4db029')
                OR (SELECT COUNT(*) FROM [product].[TemplateFields]
                    WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1) <> 6
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm' AND [Position] = 1 AND [Kind] = 1 AND [Role] = 'thickness' AND [Required] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'width-mm' AND [Position] = 2 AND [Kind] = 1 AND [Role] = 'width' AND [Required] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'length-mm' AND [Position] = 3 AND [Kind] = 1 AND [Role] = 'length' AND [Required] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'steel-grade' AND [Position] = 4 AND [Kind] = 2 AND [Role] = 'quality' AND [Required] = 1 AND [Dictionary] = 'gdc-steel-grades' AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'coating' AND [Position] = 5 AND [Kind] = 2 AND [Role] = 'coating' AND [Required] = 1 AND [Dictionary] = 'gdc-coatings' AND [AllowsCandidate] = 0 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] IS NULL)
                OR NOT EXISTS (SELECT 1 FROM [product].[TemplateFields] WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade' AND [Position] = 6 AND [Kind] = 2 AND [Role] IS NULL AND [Required] = 0 AND [Dictionary] = 'gdc-producer-grades' AND [AllowsCandidate] = 1 AND [AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL AND [ParentField] = 'steel-grade')
                THROW 51044, 'The hot-rolled cut-sheet/plate release is partial or changed; use a reviewed forward fix.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Products]
                    WHERE [FamilyId] = '075d01d5-c57c-44b9-903a-89c00e4db029'
                       OR (JSON_VALUE([ReferencesJson], '$.templateId') = '57937348-393e-4982-a5a4-63eff8be40c9'
                           AND JSON_VALUE([ReferencesJson], '$.templateVersion') = '1'))
                THROW 51044, 'Product cards reference the hot-rolled cut-sheet/plate release; rollback requires a reviewed forward fix.', 1;

            IF OBJECT_ID(N'[idempotency].[GroupSharedCommandTerminals]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals]
                    WHERE [Subject] = 'Product'
                      AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1',
                                            'product.card.activate.v1', 'product.card.deactivate.v1',
                                            'product.card.reactivate.v1')
                      AND (JSON_VALUE([ResponseJson], '$.card.product.familyId') = '075d01d5-c57c-44b9-903a-89c00e4db029'
                           OR (JSON_VALUE([ResponseJson], '$.card.references.templateId') = '57937348-393e-4982-a5a4-63eff8be40c9'
                               AND JSON_VALUE([ResponseJson], '$.card.references.templateVersion') = '1')))
                THROW 51044, 'Product replay history references the hot-rolled cut-sheet/plate release; rollback requires a reviewed forward fix.', 1;

            DELETE FROM [product].[TemplateFields]
                WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1;
            DELETE FROM [product].[TemplateFamilies]
                WHERE [TemplateId] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [TemplateVersion] = 1;
            DELETE FROM [product].[Templates]
                WHERE [Id] = '57937348-393e-4982-a5a4-63eff8be40c9' AND [Version] = 1;
            DELETE FROM [product].[Families]
                WHERE [Id] = '075d01d5-c57c-44b9-903a-89c00e4db029';
            """);
    }
}
