using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S21 (URT-D62) through the composed output host on a REAL SQL Server: an output declared without an entered code
/// takes the next number of the company's Ayarlar output label format as its own label code — the Inventory label opens
/// with it, never with the input label's number, which stays only the lineage edge; a replay answers the same code and
/// writes nothing more; a generated candidate already taken is skipped to the next number while a taken entered code is
/// refused and both owners roll back; without an entered code or an Ayarlar format nothing is written; and the
/// migration's Down passes while no code is stored and refuses (51731) once one is.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string SyntheticOutputLabelFormat = "SYN-CK-{n}";

    [SqlServerFact]
    public async Task An_output_without_an_entered_code_takes_the_next_ayarlar_number_and_never_the_input_label_code()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LABEL-CODE", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission,
            labelCodePolicy: new FixedOutputLabelCodePolicy(SyntheticOutputLabelFormat));
        var output = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m) with { LabelCode = null };
        var metadata = ManufacturingHostMetadata(run, "label-code-generated");
        var inputLabelCode = await run.ScalarAsync<string>(
            "SELECT Code FROM inventory.Labels WHERE Id = @id", ("@id", seed.ParentLabelId));

        var result = await scope.Host.DeclareOutputAsync(output, metadata);

        Assert.False(result.IsReplay);
        Assert.Equal("SYN-CK-1", result.LabelCode);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLabelCodes WHERE OutputId = @output AND OrderId = @order "
            + "AND OutputKind = 3 AND LabelId = @label AND LabelCode = N'SYN-CK-1' AND Source = 2 "
            + "AND GeneratedNumber = 1 AND Format = @format AND CompanyId = @company AND CorrelationId = @correlation",
            ("@output", output.OutputId), ("@order", seed.OrderId), ("@label", output.LabelId),
            ("@format", SyntheticOutputLabelFormat), ("@company", CompanyA),
            ("@correlation", metadata.CorrelationId)));

        // The Inventory label opens with the output's own code; the input label's number is not in it.
        var outputLabelCode = await run.ScalarAsync<string>(
            "SELECT Code FROM inventory.Labels WHERE Id = @id", ("@id", output.LabelId));
        Assert.Equal("SYN-CK-1", outputLabelCode);
        Assert.DoesNotContain(inputLabelCode, outputLabelCode, StringComparison.OrdinalIgnoreCase);

        // The input label stays the output's lineage edge.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputId = @output AND InputLabelId = @input "
            + "AND OutputLabelId = @label",
            ("@output", output.OutputId), ("@input", seed.ParentLabelId), ("@label", output.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation "
            + "AND EventType = N'manufacturing.output.label-code.recorded.v1'",
            ("@correlation", metadata.CorrelationId)));

        var replayMetadata = ManufacturingHostMetadata(run, "label-code-generated-replay");
        var replay = await scope.Host.DeclareOutputAsync(output, replayMetadata);

        Assert.True(replay.IsReplay);
        Assert.Equal("SYN-CK-1", replay.LabelCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLabelCodes"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", replayMetadata.CorrelationId)));

        var stored = await scope.Store.FindOutputLabelCodeAsync(output.OutputId);
        Assert.Equal(
            new ManufacturingOutputLabelCodeV1(output.OutputId, output.LabelId, "SYN-CK-1",
                ProductionOutputLabelCodeSource.Generated, 1, SyntheticOutputLabelFormat),
            stored);
    }

    [SqlServerFact]
    public async Task A_generated_output_label_number_skips_a_taken_code_and_a_taken_entered_code_is_refused()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LABEL-CODE-TAKEN", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission,
            labelCodePolicy: new FixedOutputLabelCodePolicy(SyntheticOutputLabelFormat));

        // An entered code wins and is recorded as entered, without a number.
        var entered = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 10m) with
        {
            LabelCode = "SYN-CK-1",
            OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(2),
        };
        var enteredResult = await scope.Host.DeclareOutputAsync(entered,
            ManufacturingHostMetadata(run, "label-code-entered"));
        Assert.Equal("SYN-CK-1", enteredResult.LabelCode);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLabelCodes WHERE OutputId = @output AND Source = 1 "
            + "AND GeneratedNumber IS NULL AND Format IS NULL",
            ("@output", entered.OutputId)));

        // The first candidate of the format is already the entered code: it is skipped, never renamed.
        var generated = OutputCommand(seed, enteredResult.OrderVersion, enteredResult.ParentLabelVersion, 10m) with
        {
            LabelCode = null,
            OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(3),
        };
        var generatedResult = await scope.Host.DeclareOutputAsync(generated,
            ManufacturingHostMetadata(run, "label-code-skipped"));
        Assert.Equal("SYN-CK-2", generatedResult.LabelCode);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLabelCodes WHERE OutputId = @output AND Source = 2 "
            + "AND GeneratedNumber = 2 AND LabelCode = N'SYN-CK-2'",
            ("@output", generated.OutputId)));
        Assert.Equal("SYN-CK-2", await run.ScalarAsync<string>(
            "SELECT Code FROM inventory.Labels WHERE Id = @id", ("@id", generated.LabelId)));

        // A taken entered code is refused; the output fact, its edges and the Inventory label all roll back.
        var taken = OutputCommand(seed, generatedResult.OrderVersion, generatedResult.ParentLabelVersion, 10m) with
        {
            LabelCode = "SYN-CK-2",
            OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(4),
        };
        var takenMetadata = ManufacturingHostMetadata(run, "label-code-taken");
        var refused = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
            scope.Host.DeclareOutputAsync(taken, takenMetadata));

        Assert.Equal(ManufacturingStoreCodes.OutputLabelCodeConflict, refused.Code);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", taken.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputId = @id", ("@id", taken.OutputId)));
        Assert.Equal(generatedResult.OrderVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", taken.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", taken.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", takenMetadata.CorrelationId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLabelCodes"));
    }

    [SqlServerFact]
    public async Task An_output_without_a_code_or_an_ayarlar_format_is_refused_and_writes_nothing()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LABEL-CODE-NONE", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var command = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 40m) with { LabelCode = null };
        var metadata = ManufacturingHostMetadata(run, "label-code-required");

        var refused = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
            scope.Host.DeclareOutputAsync(command, metadata));

        Assert.Equal(ManufacturingStoreCodes.OutputLabelCodeRequired, refused.Code);
        await AssertNoOutputEffectAsync(run, seed, command, metadata.CorrelationId);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLabelCodes"));
    }

    [SqlServerFact]
    public async Task The_output_label_code_migrations_Down_passes_with_no_code_and_refuses_while_one_exists()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOutputLabelCodes().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LABEL-CODE-DOWN", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using (var scope = ReturnOutputHostScope.Create(run, authority, admission,
                         labelCodePolicy: new FixedOutputLabelCodePolicy(SyntheticOutputLabelFormat)))
        {
            await scope.Host.DeclareOutputAsync(
                OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m) with { LabelCode = null },
                ManufacturingHostMetadata(run, "label-code-down-output"));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51731, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLabelCodes"));
    }

    /// <summary>A synthetic Ayarlar answer: the same output label format for the expected company.</summary>
    private sealed class FixedOutputLabelCodePolicy(string format) : IManufacturingOutputLabelCodePolicyReadPortV1
    {
        public Task<ManufacturingOutputLabelCodePolicyV1> ReadAsync(Guid companyId, CancellationToken cancellationToken)
        {
            Assert.Equal(CompanyA, companyId);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ManufacturingOutputLabelCodePolicyV1(format));
        }
    }
}
