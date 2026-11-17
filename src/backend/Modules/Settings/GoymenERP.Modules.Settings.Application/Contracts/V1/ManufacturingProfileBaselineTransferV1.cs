namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>URN-D5 / TK-38: copy the company's existing values unchanged, explicitly under its publish permission.</summary>
public sealed record ManufacturingProfileBaselineTransferRequestV1(string? ExpectedVersion, string? Reason);
