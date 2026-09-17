namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Read-only money currency candidates for the server-resolved company (Business #66
/// 5702138790). A separate port on purpose: <see cref="ISettingsPrerequisiteProvider"/> is
/// implemented by other modules' test doubles and does not change.
/// <para>
/// Being listed is NOT validity. A candidate carries no scale, profile, version, operation
/// or effective range; a consumer revalidates the chosen <c>CurrencyId</c> with its own
/// known-id resolution (<see cref="ISettingsPrerequisiteProvider.GetMoneyProfileAsync"/>
/// for its operation and business date) before any write.
/// </para>
/// <para>
/// The company is never a request input. A missing source throws — never an empty list, a
/// hard-coded currency, or a first entry chosen as the default.
/// </para>
/// </summary>
public interface ISettingsMoneyCandidateReader
{
    Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One candidate, and only what a picker may show and send back: the owner's stable
/// currency identity (the sole reference for selection and revalidation), the owner's
/// real code, and its display label. The code is a label, never an identity.
/// </summary>
public sealed record MoneyCurrencyCandidateV1(Guid CurrencyId, string Code, string DisplayLabel);
