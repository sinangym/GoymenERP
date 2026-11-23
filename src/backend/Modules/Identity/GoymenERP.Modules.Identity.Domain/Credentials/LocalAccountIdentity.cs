namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// The external identity pair of a local account: <c>(Issuer, UserId "D")</c>. The session cookie carries the same
/// pair and the claims transformation resolves it through <c>ExternalIdentities</c> like any other identity.
/// </summary>
public static class LocalAccountIdentity
{
    public const string Issuer = "urn:goymen:local-account";

    public static string SubjectOf(Guid userId) => userId.ToString("D");
}
