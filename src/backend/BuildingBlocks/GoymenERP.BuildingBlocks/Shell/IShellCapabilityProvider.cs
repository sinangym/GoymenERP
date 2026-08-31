namespace GoymenERP.BuildingBlocks.Shell;

public interface IShellCapabilityProvider
{
    string Owner { get; }

    string ContractVersion { get; }

    IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations();
}
