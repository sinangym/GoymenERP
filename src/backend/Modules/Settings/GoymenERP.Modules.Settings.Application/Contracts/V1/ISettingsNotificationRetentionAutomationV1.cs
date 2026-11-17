namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>Non-HTTP automation-only read of the single decided retention key in the server-resolved company.
/// Does not confer policy management or access to any other company policy; no user impersonation is required.</summary>
public interface ISettingsNotificationRetentionAutomationV1
{
    Task<SettingsNotificationRetentionPolicyV1> ReadAsync(CancellationToken cancellationToken = default);
}
