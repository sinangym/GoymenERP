using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

internal sealed class ConsoleLocalTwoFactorOperatorRecoveryConsoleV1(TextWriter prompts, string environmentName)
    : ILocalTwoFactorOperatorRecoveryConsoleV1
{
    public bool IsInteractive => !Console.IsInputRedirected;
    public string? ReadTargetUserName() => Read(OperatorTwoFactorRecoveryTexts.TargetPrompt);
    public string? ReadReason() => Read(OperatorTwoFactorRecoveryTexts.ReasonPrompt);
    public string? ReadIdempotencyKey()
    {
        var value = Read(OperatorTwoFactorRecoveryTexts.KeyPrompt);
        if (value is null) return null;
        var key = value.Length == 0 ? Guid.NewGuid().ToString("N") : value;
        prompts.WriteLine(OperatorTwoFactorRecoveryTexts.Key(key));
        return key;
    }
    public LocalTwoFactorRecoveryOperatorDeclarationV1? ReadOperatorDeclaration() => IsInteractive
        ? new(Environment.UserName, Environment.MachineName) : null;
    public bool Confirm(LocalTwoFactorOperatorRecoveryConfirmationV1 value)
    {
        if (!IsInteractive) return false;
        prompts.WriteLine(OperatorTwoFactorRecoveryTexts.Target(value));
        prompts.WriteLine(OperatorTwoFactorRecoveryTexts.Environment(environmentName, value.DatabaseServer, value.DatabaseCatalog));
        prompts.WriteLine(OperatorTwoFactorRecoveryTexts.Operator(value.Operator));
        prompts.WriteLine(value.IsRecordedReplay ? OperatorTwoFactorRecoveryTexts.Replay : OperatorTwoFactorRecoveryTexts.Effect);
        prompts.Write(OperatorTwoFactorRecoveryTexts.ConfirmationPrompt);
        return string.Equals(Console.ReadLine()?.Trim(), OperatorTwoFactorRecoveryTexts.ConfirmationAnswer, StringComparison.Ordinal);
    }
    private string? Read(string prompt)
    {
        if (!IsInteractive) return null;
        prompts.Write(prompt);
        return Console.ReadLine();
    }
}
