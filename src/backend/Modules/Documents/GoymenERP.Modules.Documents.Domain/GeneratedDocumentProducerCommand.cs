using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// KLN-D59: the stable technical name of the producing command that handed a generated PDF to the vault
/// ("inventory.material-label.print", "sales.order-pdf.render"). It is recorded in the request hash and the audit so the
/// vault answers "which command produced this document"; it is never interpreted and grants nothing.
/// </summary>
public sealed partial record GeneratedDocumentProducerCommand
{
    public const int MaximumLength = 128;

    private GeneratedDocumentProducerCommand(string value) => Value = value;

    public string Value { get; }

    public static GeneratedDocumentProducerCommand? TryCreate(string? value) =>
        value is not null && value.Length <= MaximumLength && Pattern().IsMatch(value)
            ? new GeneratedDocumentProducerCommand(value)
            : null;

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z0-9][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
