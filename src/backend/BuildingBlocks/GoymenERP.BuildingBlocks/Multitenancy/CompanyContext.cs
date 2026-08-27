namespace GoymenERP.BuildingBlocks.Multitenancy;

public sealed class CompanyContext : ICompanyContext
{
    private Guid? _companyId;

    public bool IsResolved => _companyId.HasValue;

    public Guid CompanyId => _companyId
        ?? throw new InvalidOperationException("Şirket bağlamı henüz belirlenmedi.");

    public void Set(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }

        if (_companyId.HasValue && _companyId.Value != companyId)
        {
            throw new InvalidOperationException("Bir istek içinde şirket bağlamı değiştirilemez.");
        }

        _companyId = companyId;
    }
}
