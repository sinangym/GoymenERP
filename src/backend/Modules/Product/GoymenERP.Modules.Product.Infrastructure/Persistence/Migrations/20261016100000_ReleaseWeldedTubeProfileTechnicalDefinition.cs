using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;

/// <summary>
/// P01.3A: publishes the source-stamped welded round-pipe, square-profile and canonical
/// rectangle-profile families. Existing families, dictionaries, Product cards and snapshots are
/// untouched. Down removes only the exact unreferenced additive footprint.
/// </summary>
[DbContext(typeof(ProductDbContext))]
[Migration(MigrationId)]
public sealed class ReleaseWeldedTubeProfileTechnicalDefinition : Migration
{
    public const string MigrationId = "20261016100000_ReleaseWeldedTubeProfileTechnicalDefinition";

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
                THROW 51045, 'The accepted Product technical definition prerequisite is unavailable; use a reviewed forward reconciliation.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Families]
                    WHERE [Id] IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99',
                                   'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54',
                                   'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df')
                       OR [Code] IN ('welded-round-pipe', 'welded-square-profile', 'welded-rectangle-profile')
                       OR [SourceRef] = 'issue66-6210091477-p013-a')
                OR EXISTS (SELECT 1 FROM [product].[Templates]
                    WHERE [Id] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1',
                                   '93772f5f-5be9-4dba-8c61-f9942f4e523d',
                                   'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                       OR [Code] IN ('gdc-welded-round-pipe-v1', 'gdc-welded-square-profile-v1',
                                     'gdc-welded-rectangle-profile-v1')
                       OR [SourceRef] = 'issue66-6210091477-p013-a')
                THROW 51045, 'The welded tube/profile identity is already occupied; use a reviewed forward reconciliation.', 1;

            INSERT INTO [product].[Families]
                ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef])
            VALUES
                ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'welded-round-pipe',
                 N'Kaynaklı Yuvarlak Boru', N'Boru', N'Kaynaklı', N'Yuvarlak',
                 'gdc-welded-round-pipe', 'issue66-6210091477-p013-a'),
                ('b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'welded-square-profile',
                 N'Kaynaklı Kare Profil', N'Profil', N'Kaynaklı', N'Kare',
                 'gdc-welded-square-profile', 'issue66-6210091477-p013-a'),
                ('f3ebc5b8-d55c-44dd-af7c-bfe2285f44df', 'welded-rectangle-profile',
                 N'Kaynaklı Dikdörtgen Profil', N'Profil', N'Kaynaklı', N'Dikdörtgen',
                 'gdc-welded-rectangle-profile', 'issue66-6210091477-p013-a');

            INSERT INTO [product].[Templates]
                ([Id], [Version], [Code], [Name], [SourceRef])
            VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'gdc-welded-round-pipe-v1',
                 N'GDC Kaynaklı Yuvarlak Boru Teknik Kartı', 'issue66-6210091477-p013-a'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'gdc-welded-square-profile-v1',
                 N'GDC Kaynaklı Kare Profil Teknik Kartı', 'issue66-6210091477-p013-a'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'gdc-welded-rectangle-profile-v1',
                 N'GDC Kaynaklı Dikdörtgen Profil Teknik Kartı', 'issue66-6210091477-p013-a');

            INSERT INTO [product].[TemplateFamilies] ([TemplateId], [TemplateVersion], [FamilyId])
            VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df');

            INSERT INTO [product].[TemplateFields]
                ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required],
                 [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField])
            VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'outside-diameter-mm', 1, 1, 'outside-diameter', 1, NULL, 0, NULL, NULL, NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'wall-thickness-mm', 2, 1, 'wall-thickness', 1, NULL, 0, NULL, NULL, NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'length-mm', 3, 1, 'length', 1, NULL, 0, NULL, NULL, NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'steel-grade', 4, 2, 'quality', 1, 'gdc-steel-grades', 0, NULL, NULL, NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'coating', 5, 2, 'coating', 1, 'gdc-coatings', 0, NULL, NULL, NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'producer-grade', 6, 2, NULL, 0, 'gdc-producer-grades', 1, NULL, NULL, 'steel-grade'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'section-width-mm', 1, 1, 'section-width', 1, NULL, 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'section-height-mm', 2, 1, 'section-height', 1, NULL, 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'wall-thickness-mm', 3, 1, 'wall-thickness', 1, NULL, 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'length-mm', 4, 1, 'length', 1, NULL, 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'steel-grade', 5, 2, 'quality', 1, 'gdc-steel-grades', 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'coating', 6, 2, 'coating', 1, 'gdc-coatings', 0, NULL, NULL, NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'producer-grade', 7, 2, NULL, 0, 'gdc-producer-grades', 1, NULL, NULL, 'steel-grade'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'section-width-mm', 1, 1, 'section-width', 1, NULL, 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'section-height-mm', 2, 1, 'section-height', 1, NULL, 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'wall-thickness-mm', 3, 1, 'wall-thickness', 1, NULL, 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'length-mm', 4, 1, 'length', 1, NULL, 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'steel-grade', 5, 2, 'quality', 1, 'gdc-steel-grades', 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'coating', 6, 2, 'coating', 1, 'gdc-coatings', 0, NULL, NULL, NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'producer-grade', 7, 2, NULL, 0, 'gdc-producer-grades', 1, NULL, NULL, 'steel-grade');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @ExpectedFamilies TABLE
                ([Id] uniqueidentifier, [Code] varchar(64) COLLATE Latin1_General_100_BIN2,
                 [Name] nvarchar(200) COLLATE Turkish_100_CI_AS,
                 [Cins] nvarchar(64) COLLATE Turkish_100_CI_AS,
                 [Tur] nvarchar(64) COLLATE Turkish_100_CI_AS,
                 [Tip] nvarchar(64) COLLATE Turkish_100_CI_AS,
                 [RuleSet] varchar(64) COLLATE Latin1_General_100_BIN2, [SourceRef] varchar(64));
            INSERT INTO @ExpectedFamilies VALUES
                ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'welded-round-pipe', N'Kaynaklı Yuvarlak Boru', N'Boru', N'Kaynaklı', N'Yuvarlak', 'gdc-welded-round-pipe', 'issue66-6210091477-p013-a'),
                ('b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'welded-square-profile', N'Kaynaklı Kare Profil', N'Profil', N'Kaynaklı', N'Kare', 'gdc-welded-square-profile', 'issue66-6210091477-p013-a'),
                ('f3ebc5b8-d55c-44dd-af7c-bfe2285f44df', 'welded-rectangle-profile', N'Kaynaklı Dikdörtgen Profil', N'Profil', N'Kaynaklı', N'Dikdörtgen', 'gdc-welded-rectangle-profile', 'issue66-6210091477-p013-a');

            IF EXISTS (SELECT * FROM @ExpectedFamilies EXCEPT
                       SELECT [Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef]
                         FROM [product].[Families] WHERE [SourceRef] = 'issue66-6210091477-p013-a')
                OR EXISTS (SELECT [Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef]
                         FROM [product].[Families]
                        WHERE [Id] IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df')
                           OR [Code] IN ('welded-round-pipe', 'welded-square-profile', 'welded-rectangle-profile')
                           OR [SourceRef] = 'issue66-6210091477-p013-a'
                       EXCEPT SELECT * FROM @ExpectedFamilies)
                THROW 51045, 'The welded tube/profile family release is partial or changed; use a reviewed forward fix.', 1;

            DECLARE @ExpectedTemplates TABLE
                ([Id] uniqueidentifier, [Version] bigint,
                 [Code] varchar(64) COLLATE Latin1_General_100_BIN2,
                 [Name] nvarchar(200) COLLATE Turkish_100_CI_AS, [SourceRef] varchar(64));
            INSERT INTO @ExpectedTemplates VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'gdc-welded-round-pipe-v1', N'GDC Kaynaklı Yuvarlak Boru Teknik Kartı', 'issue66-6210091477-p013-a'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'gdc-welded-square-profile-v1', N'GDC Kaynaklı Kare Profil Teknik Kartı', 'issue66-6210091477-p013-a'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'gdc-welded-rectangle-profile-v1', N'GDC Kaynaklı Dikdörtgen Profil Teknik Kartı', 'issue66-6210091477-p013-a');

            IF EXISTS (SELECT * FROM @ExpectedTemplates EXCEPT
                       SELECT [Id], [Version], [Code], [Name], [SourceRef]
                         FROM [product].[Templates] WHERE [SourceRef] = 'issue66-6210091477-p013-a')
                OR EXISTS (SELECT [Id], [Version], [Code], [Name], [SourceRef]
                         FROM [product].[Templates]
                        WHERE [Id] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                           OR [Code] IN ('gdc-welded-round-pipe-v1', 'gdc-welded-square-profile-v1', 'gdc-welded-rectangle-profile-v1')
                           OR [SourceRef] = 'issue66-6210091477-p013-a'
                       EXCEPT SELECT * FROM @ExpectedTemplates)
                THROW 51045, 'The welded tube/profile template release is partial or changed; use a reviewed forward fix.', 1;

            DECLARE @ExpectedLinks TABLE ([TemplateId] uniqueidentifier, [TemplateVersion] bigint, [FamilyId] uniqueidentifier);
            INSERT INTO @ExpectedLinks VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1', 1, 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d', 1, 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2', 1, 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df');

            IF EXISTS (SELECT * FROM @ExpectedLinks EXCEPT
                       SELECT [TemplateId], [TemplateVersion], [FamilyId] FROM [product].[TemplateFamilies]
                        WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2'))
                OR EXISTS (SELECT [TemplateId], [TemplateVersion], [FamilyId] FROM [product].[TemplateFamilies]
                        WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                       EXCEPT SELECT * FROM @ExpectedLinks)
                THROW 51045, 'The welded tube/profile family-template release is partial or changed; use a reviewed forward fix.', 1;

            DECLARE @ExpectedFields TABLE
                ([TemplateId] uniqueidentifier, [TemplateVersion] bigint,
                 [Key] varchar(64) COLLATE Latin1_General_100_BIN2, [Position] int,
                 [Kind] int, [Role] varchar(32) COLLATE Latin1_General_100_BIN2 NULL, [Required] bit,
                 [Dictionary] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
                 [AllowsCandidate] bit,
                 [AppliesWhenField] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
                 [AppliesWhenCode] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
                 [ParentField] varchar(64) COLLATE Latin1_General_100_BIN2 NULL);
            INSERT INTO @ExpectedFields VALUES
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'outside-diameter-mm',1,1,'outside-diameter',1,NULL,0,NULL,NULL,NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'wall-thickness-mm',2,1,'wall-thickness',1,NULL,0,NULL,NULL,NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'length-mm',3,1,'length',1,NULL,0,NULL,NULL,NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'steel-grade',4,2,'quality',1,'gdc-steel-grades',0,NULL,NULL,NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'coating',5,2,'coating',1,'gdc-coatings',0,NULL,NULL,NULL),
                ('f944d764-56f6-4a04-9a31-65074b2c06c1',1,'producer-grade',6,2,NULL,0,'gdc-producer-grades',1,NULL,NULL,'steel-grade'),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'section-width-mm',1,1,'section-width',1,NULL,0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'section-height-mm',2,1,'section-height',1,NULL,0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'wall-thickness-mm',3,1,'wall-thickness',1,NULL,0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'length-mm',4,1,'length',1,NULL,0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'steel-grade',5,2,'quality',1,'gdc-steel-grades',0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'coating',6,2,'coating',1,'gdc-coatings',0,NULL,NULL,NULL),
                ('93772f5f-5be9-4dba-8c61-f9942f4e523d',1,'producer-grade',7,2,NULL,0,'gdc-producer-grades',1,NULL,NULL,'steel-grade'),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'section-width-mm',1,1,'section-width',1,NULL,0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'section-height-mm',2,1,'section-height',1,NULL,0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'wall-thickness-mm',3,1,'wall-thickness',1,NULL,0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'length-mm',4,1,'length',1,NULL,0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'steel-grade',5,2,'quality',1,'gdc-steel-grades',0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'coating',6,2,'coating',1,'gdc-coatings',0,NULL,NULL,NULL),
                ('b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2',1,'producer-grade',7,2,NULL,0,'gdc-producer-grades',1,NULL,NULL,'steel-grade');

            IF EXISTS (SELECT * FROM @ExpectedFields EXCEPT
                       SELECT [TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required],
                              [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField]
                         FROM [product].[TemplateFields]
                        WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2'))
                OR EXISTS (SELECT [TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required],
                                  [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField]
                         FROM [product].[TemplateFields]
                        WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                       EXCEPT SELECT * FROM @ExpectedFields)
                THROW 51045, 'The welded tube/profile field release is partial or changed; use a reviewed forward fix.', 1;

            IF EXISTS (SELECT 1 FROM [product].[Products]
                    WHERE [FamilyId] IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df')
                       OR (JSON_VALUE([ReferencesJson], '$.templateId') IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                           AND JSON_VALUE([ReferencesJson], '$.templateVersion') = '1'))
                THROW 51045, 'Product cards reference the welded tube/profile release; rollback requires a reviewed forward fix.', 1;

            IF OBJECT_ID(N'[idempotency].[GroupSharedCommandTerminals]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals]
                    WHERE [Subject] = 'Product'
                      AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1',
                                            'product.card.activate.v1', 'product.card.deactivate.v1',
                                            'product.card.reactivate.v1')
                      AND (JSON_VALUE([ResponseJson], '$.card.product.familyId') IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df')
                           OR (JSON_VALUE([ResponseJson], '$.card.references.templateId') IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')
                               AND JSON_VALUE([ResponseJson], '$.card.references.templateVersion') = '1')))
                THROW 51045, 'Product replay history references the welded tube/profile release; rollback requires a reviewed forward fix.', 1;

            DELETE FROM [product].[TemplateFields]
                WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2');
            DELETE FROM [product].[TemplateFamilies]
                WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2');
            DELETE FROM [product].[Templates]
                WHERE [Id] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2') AND [Version] = 1;
            DELETE FROM [product].[Families]
                WHERE [Id] IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df');
            """);
    }
}
