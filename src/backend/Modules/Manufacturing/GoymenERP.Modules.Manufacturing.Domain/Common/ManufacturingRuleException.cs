namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// A manufacturing rule was broken. <see cref="Code"/> is stable so a later application layer
/// can map it without parsing the message.
/// </summary>
public sealed class ManufacturingRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
