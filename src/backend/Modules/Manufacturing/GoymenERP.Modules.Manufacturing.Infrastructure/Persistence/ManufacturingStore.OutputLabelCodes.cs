using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-S21 (URT-D62): the output label's own, independent code. The output declaration gives its new label a code in its
/// own transaction, after the output fact and before Inventory opens the label: an entered code wins, otherwise the next
/// number of the company's Ayarlar format. The input labels the output came from stay its lineage edges
/// (<see cref="RecordOutputLineageAsync"/>) and never enter the code. A code is never renamed silently: a taken entered
/// code is refused, and a generated candidate already taken is skipped to the next number.
/// </summary>
public sealed partial class ManufacturingStore
{
    private const string OutputLabelCodeEventType = "manufacturing.output.label-code.recorded.v1";

    /// <summary>
    /// Gives the output <paramref name="outputId"/> of <paramref name="orderId"/> its label code and records it. It runs
    /// inside the caller's output transaction and writes no command record and no order version.
    /// <list type="bullet">
    /// <item>A code already stored for the output is the answer again (a replay); a different entered code, label or
    /// order for it is refused (<see cref="ManufacturingStoreCodes.FactIdConflict"/>).</item>
    /// <item>A replayed output with no code stored (declared before codes were recorded) takes the entered code and
    /// records nothing; without one it is refused (<see cref="ManufacturingStoreCodes.OutputLabelCodeRequired"/>): a
    /// label already opened is never given a new number afterwards.</item>
    /// <item>A new output takes the entered code when one is given, refused when it is taken in the company
    /// (<see cref="ManufacturingStoreCodes.OutputLabelCodeConflict"/>). Without one it takes the next number of
    /// <paramref name="format"/>; no format is <see cref="ManufacturingStoreCodes.OutputLabelCodeRequired"/>, an unusable
    /// one <see cref="ManufacturingStoreCodes.OutputLabelCodeFormatInvalid"/>.</item>
    /// </list>
    /// <paramref name="registered"/> answers which candidate codes are already registered elsewhere (Inventory's labels);
    /// null checks only this store's output codes and leaves Inventory's staging as the final guard.
    /// </summary>
    public async Task<ManufacturingOutputLabelCodeV1> AssignOutputLabelCodeAsync(
        Guid orderId,
        Guid outputId,
        Guid labelId,
        string? enteredCode,
        string? format,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlySet<string>>>? registered,
        bool outputReplayed,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        if (orderId == Guid.Empty || outputId == Guid.Empty || labelId == Guid.Empty)
        {
            throw Refused(ManufacturingStoreCodes.LineageInvalid, "Çıktı etiket kodunun kimliği geçersiz; hiçbir şey kaydedilmedi.");
        }

        if (enteredCode is not null && (!ProductionOutputLabelCodes.IsValidCode(enteredCode)
                                        || !string.Equals(enteredCode, enteredCode.Trim(), StringComparison.Ordinal)))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Çıktı etiket kodu saklanan alana sığmıyor; kesilmez.");
        }

        var outputKind = (int)FactKind.OutputDeclaration;
        var declared = await dbContext.Facts.AsNoTracking()
            .AnyAsync(fact => fact.Kind == outputKind && fact.Id == outputId && fact.OrderId == orderId, cancellationToken);
        if (!declared)
        {
            throw Refused(ManufacturingStoreCodes.LineageInvalid,
                "Etiket kodu bu emrin beyan edilmiş bir çıktısına ait değil; hiçbir şey kaydedilmedi.");
        }

        var stored = await dbContext.OutputLabelCodes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OutputId == outputId, cancellationToken);
        if (stored is not null)
        {
            return stored.OrderId == orderId && stored.LabelId == labelId
                   && (enteredCode is null || string.Equals(enteredCode, stored.LabelCode, StringComparison.Ordinal))
                ? OutputLabelCodeView(stored)
                : throw Refused(ManufacturingStoreCodes.FactIdConflict,
                    "Aynı çıktının etiket kodu farklı içerikle verildi.");
        }

        if (outputReplayed)
        {
            return enteredCode is not null
                ? new ManufacturingOutputLabelCodeV1(outputId, labelId, enteredCode,
                    ProductionOutputLabelCodeSource.Entered, null, null)
                : throw Refused(ManufacturingStoreCodes.OutputLabelCodeRequired,
                    "Bu çıktının etiketi kod kaydı tutulmadan açıldı; etiket kodu girilmelidir.");
        }

        ManufacturingOutputLabelCodeV1 assigned;
        if (enteredCode is not null)
        {
            if (await IsLabelCodeTakenAsync(enteredCode, registered, cancellationToken))
            {
                throw Refused(ManufacturingStoreCodes.OutputLabelCodeConflict,
                    "Bu çıktı etiket kodu şirkette kullanılıyor; kod kendiliğinden değiştirilmez.");
            }

            assigned = new ManufacturingOutputLabelCodeV1(outputId, labelId, enteredCode,
                ProductionOutputLabelCodeSource.Entered, null, null);
        }
        else
        {
            assigned = await GenerateOutputLabelCodeAsync(outputId, labelId, format, registered, cancellationToken);
        }

        dbContext.OutputLabelCodes.Add(new OutputLabelCodeRow
        {
            CompanyId = companyId,
            OrderId = orderId,
            OutputKind = outputKind,
            OutputId = outputId,
            LabelId = labelId,
            LabelCode = assigned.LabelCode,
            Source = (int)assigned.Source,
            GeneratedNumber = assigned.GeneratedNumber,
            Format = assigned.Format,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        Audit(metadata, companyId, OrderEntityType, orderId, OutputLabelCodeEventType, "RecordOutputLabelCode",
            versionBefore: null, versionAfter: null, before: "{}",
            after: JsonSerializer.Serialize(new
            {
                outputId,
                labelId,
                labelCode = assigned.LabelCode,
                source = assigned.Source.ToString(),
                generatedNumber = assigned.GeneratedNumber,
                format = assigned.Format,
            }, Json),
            "Üretim çıktısının bağımsız etiket kodu");
        await SaveAsync(cancellationToken);
        return assigned;
    }

    /// <summary>The label code stored for an output of the resolved company; null when none is stored.</summary>
    public async Task<ManufacturingOutputLabelCodeV1?> FindOutputLabelCodeAsync(
        Guid outputId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var stored = await dbContext.OutputLabelCodes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OutputId == outputId, cancellationToken);
        return stored is null ? null : OutputLabelCodeView(stored);
    }

    /// <summary>
    /// The next number of the company's output label format: one past the largest number ever given (a number is never
    /// given twice and never reused), skipping a candidate already taken, within
    /// <see cref="ProductionOutputLabelCodes.MaximumGenerationAttempts"/>.
    /// </summary>
    private async Task<ManufacturingOutputLabelCodeV1> GenerateOutputLabelCodeAsync(Guid outputId, Guid labelId,
        string? format, Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlySet<string>>>? registered,
        CancellationToken cancellationToken)
    {
        if (format is null)
        {
            throw Refused(ManufacturingStoreCodes.OutputLabelCodeRequired,
                "Ayarlar'da çıktı etiket numarası biçimi yok; etiket kodu girilmelidir.");
        }

        if (!ProductionOutputLabelCodes.IsValidFormat(format))
        {
            throw Refused(ManufacturingStoreCodes.OutputLabelCodeFormatInvalid,
                "Ayarlar'daki çıktı etiket numarası biçimi geçerli değil; hiçbir şey kaydedilmedi.");
        }

        var largest = await dbContext.OutputLabelCodes.AsNoTracking()
            .Where(row => row.GeneratedNumber != null)
            .MaxAsync(row => row.GeneratedNumber, cancellationToken) ?? 0;
        var number = checked(largest + 1);
        for (var attempt = 0; attempt < ProductionOutputLabelCodes.MaximumGenerationAttempts; attempt++, number = checked(number + 1))
        {
            var candidate = ProductionOutputLabelCodes.Format(format, number);
            if (!ProductionOutputLabelCodes.IsValidCode(candidate))
            {
                break;
            }

            if (!await IsLabelCodeTakenAsync(candidate, registered, cancellationToken))
            {
                return new ManufacturingOutputLabelCodeV1(outputId, labelId, candidate,
                    ProductionOutputLabelCodeSource.Generated, number, format);
            }
        }

        throw Refused(ManufacturingStoreCodes.OutputLabelCodeConflict,
            "Ayarlar biçiminde boş bir çıktı etiket numarası bulunamadı; hiçbir şey kaydedilmedi.");
    }

    private async Task<bool> IsLabelCodeTakenAsync(string code,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlySet<string>>>? registered,
        CancellationToken cancellationToken)
    {
        if (await dbContext.OutputLabelCodes.AsNoTracking().AnyAsync(row => row.LabelCode == code, cancellationToken))
        {
            return true;
        }

        if (registered is null)
        {
            return false;
        }

        var answer = await registered([code], cancellationToken)
                     ?? throw Refused(ManufacturingStoreCodes.LineageInvalid,
                         "Etiket kodu kaydı okunamadı; hiçbir şey kaydedilmedi.");
        return answer.Any(taken => string.Equals(taken, code, StringComparison.OrdinalIgnoreCase));
    }

    private static ManufacturingOutputLabelCodeV1 OutputLabelCodeView(OutputLabelCodeRow row) =>
        new(row.OutputId, row.LabelId, row.LabelCode, (ProductionOutputLabelCodeSource)row.Source,
            row.GeneratedNumber, row.Format);
}
