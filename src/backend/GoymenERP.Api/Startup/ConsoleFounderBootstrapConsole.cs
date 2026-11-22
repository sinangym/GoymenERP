using System.Text;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// The terminal side of <c>identity bootstrap-founder</c>: a redirected input is not interactive, and the password is
/// read key by key without echo, so it never reaches arguments, environment variables, logs or the shell history.
/// </summary>
internal sealed class ConsoleFounderBootstrapConsole(TextWriter prompts) : IFounderBootstrapConsole
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public string? ReadUserName()
    {
        prompts.Write(FounderBootstrapTexts.UserNamePrompt);
        return Console.ReadLine();
    }

    public string? ReadDisplayName()
    {
        prompts.Write(FounderBootstrapTexts.DisplayNamePrompt);
        return Console.ReadLine();
    }

    public FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration() => IsInteractive
        ? new(Environment.UserName, Environment.MachineName) : null;

    public string? ReadNewPassword() => ReadSecret(FounderBootstrapTexts.PasswordPrompt);

    public string? ReadNewPasswordConfirmation() => ReadSecret(FounderBootstrapTexts.PasswordConfirmationPrompt);

    internal string ReadSecret(string prompt)
    {
        prompts.Write(prompt);
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                prompts.WriteLine();
                return secret.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
    }
}
