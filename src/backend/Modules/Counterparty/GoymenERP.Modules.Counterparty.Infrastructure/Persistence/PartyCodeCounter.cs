namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class PartyCodeCounter
{
    public const int SingletonId = 1;

    private PartyCodeCounter()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public long LastValue { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public long Next()
    {
        checked
        {
            LastValue++;
        }

        return LastValue;
    }
}
