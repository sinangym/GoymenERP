using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// The terminal side of <c>identity recover-founder</c>: a redirected input is not interactive, passwords are read
/// without echo (the same reader as <c>bootstrap-founder</c>), and the confirmation needs the operator to type the
/// confirmation word; there is no flag that skips it.
/// </summary>
internal sealed class ConsoleFounderRecoveryConsole(TextWriter prompts, string environmentName) : IFounderRecoveryConsole
{
    private readonly ConsoleFounderBootstrapConsole secretReader = new(prompts);

    public bool IsInteractive => !Console.IsInputRedirected;

    public string? ReadNewPassword() => secretReader.ReadSecret(FounderRecoveryTexts.PasswordPrompt);

    public string? ReadNewPasswordConfirmation() =>
        secretReader.ReadSecret(FounderRecoveryTexts.PasswordConfirmationPrompt);

    public bool Confirm(FounderRecoveryConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        prompts.WriteLine(FounderRecoveryTexts.ConfirmationHeader);
        prompts.WriteLine(FounderRecoveryTexts.Environment(environmentName));
        prompts.WriteLine(FounderRecoveryTexts.Database(confirmation.DatabaseServer, confirmation.DatabaseCatalog));
        prompts.WriteLine(FounderRecoveryTexts.Operation(confirmation.Subcommand));
        prompts.WriteLine(FounderRecoveryTexts.FounderUserName(confirmation.FounderUserName));
        if (confirmation.Companies.Count == 0)
        {
            prompts.WriteLine(FounderRecoveryTexts.NoCompanies);
        }
        else
        {
            prompts.WriteLine(FounderRecoveryTexts.CompaniesHeader);
            foreach (var company in confirmation.Companies)
            {
                prompts.WriteLine(FounderRecoveryTexts.Company(company));
            }
        }

        prompts.Write(FounderRecoveryTexts.ConfirmationPrompt);
        var answer = Console.ReadLine();
        return string.Equals(answer?.Trim(), FounderRecoveryTexts.ConfirmationAnswer, StringComparison.Ordinal);
    }
}
