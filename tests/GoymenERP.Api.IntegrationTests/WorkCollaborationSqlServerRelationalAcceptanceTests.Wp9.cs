using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

// WP9 real-SQL evidence: the widened record kinds, the note read receipts and the WidenWorkRecordTypesAndNoteReads forward fix.
// SENTETIK test doubles answer for the owners (see the W1-C file).
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Collaboration_WP9_Down_passes_with_only_first_five_kind_notes_and_refuses_with_52170_while_a_new_kind_note_or_a_read_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new WidenWorkRecordTypesAndNoteReads().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            var old = await CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-order-draft", CollaborationRecord, new("SENTETIK eski tur", null, null), new("wp9-1", "corr-wp9-1", null));
            await ExecAsync(connectionString, guard);

            await ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNoteReads] ([CompanyId],[NoteId],[ReaderUserId],[ReadAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','{old.Note.Id}','{CollaborationReader}',SYSUTCDATETIME());
                """);
            Assert.Equal(52170, (await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard))).Number);
            var duplicate = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNoteReads] ([CompanyId],[NoteId],[ReaderUserId],[ReadAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','{old.Note.Id}','{CollaborationReader}',SYSUTCDATETIME());
                """)));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            var orphan = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNoteReads] ([CompanyId],[NoteId],[ReaderUserId],[ReadAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','{Guid.NewGuid()}','{CollaborationReader}',SYSUTCDATETIME());
                """)));
            Assert.Equal(547, orphan.Number);
            Assert.Equal(1, await CountAsync(connectionString, "RecordNoteReads", TestCompanyIds.CompanyA));

            var invoiceNote = await CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-invoice", CollaborationRecord, new("SENTETIK fatura notu", null, null), new("wp9-2", "corr-wp9-2", null));
            Assert.Equal("sales-invoice", invoiceNote.Note.RecordType);
            var outOfRange = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNotes] ([CompanyId],[Id],[RecordType],[RecordId],[AuthorUserId],[Body],[CorrectsNoteId],[CreatedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{TestCompanyIds.CompanyA}','{Guid.NewGuid()}',14,'{CollaborationRecord}','{CollaborationWriter}','x',NULL,SYSUTCDATETIME(),'c',NULL);
                """)));
            Assert.Equal(547, outOfRange.Number);
            Assert.Equal(52170, (await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard))).Number);
            Assert.Equal(2, await CountAsync(connectionString, "RecordNotes", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task Collaboration_YOL_D42_Down_passes_with_other_kind_notes_and_refuses_with_52171_while_a_goods_receipt_note_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkGoodsReceiptRecordType().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            await CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-quote", CollaborationRecord, new("SENTETIK teklif notu", null, null), new("d42-1", "corr-d42-1", null));
            await ExecAsync(connectionString, guard);
            var receiptNote = await CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("purchase-goods-receipt", CollaborationRecord, new("SENTETIK mal kabul notu", null, null), new("d42-2", "corr-d42-2", null));
            Assert.Equal("purchase-goods-receipt", receiptNote.Note.RecordType);
            Assert.Equal(52171, (await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard))).Number);
            var outOfRange = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNotes] ([CompanyId],[Id],[RecordType],[RecordId],[AuthorUserId],[Body],[CorrectsNoteId],[CreatedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{TestCompanyIds.CompanyA}','{Guid.NewGuid()}',14,'{CollaborationRecord}','{CollaborationWriter}','x',NULL,SYSUTCDATETIME(),'c',NULL);
                """)));
            Assert.Equal(547, outOfRange.Number);
            Assert.Equal(2, await CountAsync(connectionString, "RecordNotes", TestCompanyIds.CompanyA));
        });
    }
}
