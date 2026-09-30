using System.Globalization;

namespace GoymenERP.Modules.Product.Domain.PriceLists;

/// <summary>
/// AT-018 / DEC-PRD-011: a company's price list, owned by the Product module (user decision 2026-09-30). The list
/// carries its context — company, currency, channel and VAT basis — once, and its prices in numbered, validity-dated
/// VERSIONS. A version is prepared as a Draft (its lines may be replaced) and becomes immutable when it is Published.
/// <list type="bullet">
/// <item><b>Validity:</b> a published version applies from its <see cref="PriceListVersion.ValidFrom"/> until the
/// next published version of the same list starts, or until its own <see cref="PriceListVersion.ValidThrough"/>,
/// whichever comes first. Published start dates are strictly increasing, so a date resolves to at most one version
/// of a list (<see cref="ResolveOn"/>); a gap resolves to none — nothing falls back to an older version.</item>
/// <item><b>No backdating:</b> a version is published only with a start date on or after the publishing date.</item>
/// <item><b>Context, fail-closed:</b> only the Sales channel and prices excluding VAT are accepted; purchase/common
/// channels and VAT-inclusive prices are open decisions (README, OPEN items).</item>
/// <item><b>Concurrency:</b> every recorded change names the expected <see cref="Version"/> and advances it by one.</item>
/// </list>
/// The aggregate checks only its own shape. Whether a product exists, is active, sellable and priced in one of its
/// own units is the service's check against the Product card (the same module), made on every draft save and again
/// at publication.
/// </summary>
public sealed class PriceList
{
    public const int NameMaximumLength = 200;
    public const int MaximumLines = 500;
    public const int MaximumVersions = 1000;
    /// <summary>Technical cap for a stored unit price (decimal(19,4)); an order applies its own money profile's scale on top.</summary>
    public const int PriceScale = 4;
    /// <summary>decimal(19,4): at most 15 integer digits.</summary>
    public const decimal PriceBound = 1_000_000_000_000_000m;

    private readonly List<PriceListVersion> versions;

    private PriceList(Guid id, Guid companyId, string name, string currencyCode, PriceListChannel channel,
        PriceListVatBasis vatBasis, long version, string createdBy, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        List<PriceListVersion> versions)
    {
        Id = id;
        CompanyId = companyId;
        Name = name;
        CurrencyCode = currencyCode;
        Channel = channel;
        VatBasis = vatBasis;
        Version = version;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        this.versions = versions;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public string Name { get; private set; }

    /// <summary>ISO 4217 alphabetic code (three uppercase ASCII letters), matched exactly at use; never converted.</summary>
    public string CurrencyCode { get; }

    public PriceListChannel Channel { get; }

    public PriceListVatBasis VatBasis { get; }

    /// <summary>The optimistic-concurrency token; starts at 1 and advances by one per recorded change.</summary>
    public long Version { get; private set; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versions in number order (1, 2, …).</summary>
    public IReadOnlyList<PriceListVersion> Versions => versions.AsReadOnly();

    /// <summary>The one version still being prepared, if any.</summary>
    public PriceListVersion? DraftVersion => versions.SingleOrDefault(version => version.State == PriceListVersionState.Draft);

    public static PriceList Create(Guid id, Guid companyId, string name, string currencyCode, PriceListChannel channel,
        PriceListVatBasis vatBasis, Guid firstVersionId, DateOnly validFrom, DateOnly? validThrough,
        IReadOnlyList<PriceListLineInput> lines, string actorId, DateTimeOffset now)
    {
        RequireIdentity(id, "id");
        RequireIdentity(companyId, "companyId");
        RequireIdentity(firstVersionId, "versionId");
        var normalizedName = NormalizeName(name);
        var currency = NormalizeCurrency(currencyCode);
        RequireChannel(channel);
        RequireVatBasis(vatBasis);
        var actor = RequireActor(actorId);
        var first = PriceListVersion.CreateDraft(firstVersionId, 1, validFrom, validThrough, lines, actor, now);
        return new PriceList(id, companyId, normalizedName, currency, channel, vatBasis, 1, actor, now, now, [first]);
    }

    /// <summary>
    /// Replaces the name and the draft version's validity and lines. Only a Draft version can be revised; a published
    /// version is immutable (a change is a new version).
    /// </summary>
    public void ReviseDraft(long expectedVersion, string name, DateOnly validFrom, DateOnly? validThrough,
        IReadOnlyList<PriceListLineInput> lines, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var draft = DraftVersion ?? throw new PriceListRuleException(PriceListRuleCodes.NoDraftVersion, "versionNo",
            "Düzenlenecek taslak sürüm yok; yayımlanmış sürüm değişmez, yeni sürüm açılır.");
        var normalizedName = NormalizeName(name);
        var revised = PriceListVersion.CreateDraft(draft.Id, draft.VersionNo, validFrom, validThrough, lines, draft.CreatedBy,
            draft.CreatedAtUtc);
        versions[versions.IndexOf(draft)] = revised;
        Name = normalizedName;
        Touch(now);
    }

    /// <summary>
    /// Opens the next version as a Draft carrying the latest published version's lines. Needs a published version and
    /// no open draft (one draft at a time).
    /// </summary>
    public PriceListVersion StartNextVersion(long expectedVersion, Guid versionId, DateOnly validFrom, DateOnly? validThrough,
        string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireIdentity(versionId, "versionId");
        var actor = RequireActor(actorId);
        if (DraftVersion is not null)
        {
            throw new PriceListRuleException(PriceListRuleCodes.DraftVersionOpen, "versionNo",
                "Açık bir taslak sürüm var; yeni sürüm ondan sonra açılır.");
        }

        if (versions.Count >= MaximumVersions)
        {
            throw new PriceListRuleException(PriceListRuleCodes.TooManyVersions, "versionNo",
                $"Bir fiyat listesi en fazla {MaximumVersions} sürüm taşır.");
        }

        if (versions.Any(version => version.Id == versionId))
        {
            throw new PriceListRuleException(PriceListRuleCodes.DuplicateIdentity, "versionId", "Sürüm kimliği bu listede kullanılıyor.");
        }

        var latest = versions.LastOrDefault(version => version.State == PriceListVersionState.Published)
            ?? throw new PriceListRuleException(PriceListRuleCodes.NoPublishedVersion, "versionNo",
                "Yeni sürüm yayımlanmış bir sürümden açılır.");
        var carried = latest.Lines
            .Select(line => new PriceListLineInput(Guid.NewGuid(), line.ProductId, line.PriceUnitId, line.UnitPrice, line.FloorUnitPrice))
            .ToList();
        var next = PriceListVersion.CreateDraft(versionId, versions[^1].VersionNo + 1, validFrom, validThrough, carried, actor, now);
        versions.Add(next);
        Touch(now);
        return next;
    }

    /// <summary>
    /// Publishes the draft version <paramref name="versionNo"/>: at least one line, a start on or after
    /// <paramref name="today"/>, and a start strictly after every other published version's start.
    /// </summary>
    public void Publish(long expectedVersion, int versionNo, DateOnly today, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        var target = versions.SingleOrDefault(version => version.VersionNo == versionNo)
            ?? throw new PriceListRuleException(PriceListRuleCodes.VersionNotFound, "versionNo", "Bu numarada sürüm yok.");
        if (target.State != PriceListVersionState.Draft)
        {
            throw new PriceListRuleException(PriceListRuleCodes.VersionNotDraft, "versionNo", "Yalnız taslak sürüm yayımlanır.");
        }

        if (target.Lines.Count == 0)
        {
            throw new PriceListRuleException(PriceListRuleCodes.VersionWithoutLines, "lines", "Satırsız sürüm yayımlanmaz.");
        }

        if (target.ValidFrom < today)
        {
            throw new PriceListRuleException(PriceListRuleCodes.ValidFromInPast, "validFrom",
                "Geçmiş tarihten başlayan fiyat yayımlanmaz; başlangıç bugün ya da sonrası olmalıdır.");
        }

        if (versions.Any(version => version.State == PriceListVersionState.Published && version.ValidFrom >= target.ValidFrom))
        {
            throw new PriceListRuleException(PriceListRuleCodes.ValidFromNotAfterPublished, "validFrom",
                "Yeni sürüm, yayımlanmış her sürümden sonra başlamalıdır.");
        }

        versions[versions.IndexOf(target)] = target.Published(actor, now);
        Touch(now);
    }

    /// <summary>
    /// The published version that prices <paramref name="date"/>: the latest published start on or before the date,
    /// provided its own end (if any) is not before the date. A gap is <c>null</c>; nothing falls back to an older version.
    /// </summary>
    public PriceListVersion? ResolveOn(DateOnly date)
    {
        var candidate = versions
            .Where(version => version.State == PriceListVersionState.Published && version.ValidFrom <= date)
            .OrderByDescending(version => version.ValidFrom)
            .FirstOrDefault();
        return candidate is not null && (candidate.ValidThrough is null || candidate.ValidThrough >= date) ? candidate : null;
    }

    /// <summary>The derived display status of a version on <paramref name="today"/> (the master's planned/active/ended stages).</summary>
    public PriceListVersionStatus StatusOf(PriceListVersion version, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.State == PriceListVersionState.Draft)
        {
            return PriceListVersionStatus.Draft;
        }

        if (version.ValidFrom > today)
        {
            return PriceListVersionStatus.Planned;
        }

        return ReferenceEquals(ResolveOn(today), version) ? PriceListVersionStatus.Active : PriceListVersionStatus.Ended;
    }

    /// <summary>
    /// Rebuilds a stored list. Every value is checked again as on the write path; the versions must be numbered
    /// 1..n without gaps, at most one may be a draft, and published starts must be strictly increasing.
    /// </summary>
    public static PriceList Restore(Guid id, Guid companyId, string name, string currencyCode, PriceListChannel channel,
        PriceListVatBasis vatBasis, long version, string createdBy, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        IReadOnlyList<PriceListVersion> storedVersions)
    {
        RequireIdentity(id, "id");
        RequireIdentity(companyId, "companyId");
        ArgumentNullException.ThrowIfNull(storedVersions);
        if (version < 1 || storedVersions.Count == 0)
        {
            throw new InvalidOperationException("Kayıtlı fiyat listesi sürümü ve en az bir fiyat sürümü taşır.");
        }

        var ordered = storedVersions.OrderBy(stored => stored.VersionNo).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].VersionNo != index + 1)
            {
                throw new InvalidOperationException("Kayıtlı fiyat sürümleri 1'den başlayıp boşluksuz numaralanır.");
            }
        }

        if (ordered.Count(stored => stored.State == PriceListVersionState.Draft) > 1)
        {
            throw new InvalidOperationException("Kayıtlı listede birden fazla taslak sürüm olamaz.");
        }

        var published = ordered.Where(stored => stored.State == PriceListVersionState.Published).ToList();
        for (var index = 1; index < published.Count; index++)
        {
            if (published[index].ValidFrom <= published[index - 1].ValidFrom)
            {
                throw new InvalidOperationException("Kayıtlı yayımlanmış sürümlerin başlangıçları artan sıradadır.");
            }
        }

        return new PriceList(id, companyId, NormalizeName(name), NormalizeCurrency(currencyCode), RequireChannel(channel),
            RequireVatBasis(vatBasis), version, RequireActor(createdBy), createdAtUtc, updatedAtUtc, ordered);
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaximumLength || trimmed.Any(char.IsControl))
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidName, "name",
                $"Liste adı 1–{NameMaximumLength} karakter olmalı ve kontrol karakteri içermemelidir.");
        }

        return trimmed;
    }

    public static string NormalizeCurrency(string? currencyCode)
    {
        if (currencyCode is not { Length: 3 } || !currencyCode.All(character => character is >= 'A' and <= 'Z'))
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidCurrency, "currencyCode",
                "Para birimi üç büyük harfli ISO 4217 kodudur (ör. TRY).");
        }

        return currencyCode;
    }

    /// <summary>The number of decimals a price carries (trailing zeros of the scale count: 1.50 has 2).</summary>
    public static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    public static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new PriceListVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        Version = checked(Version + 1);
        UpdatedAtUtc = now;
    }

    private static PriceListChannel RequireChannel(PriceListChannel channel) => channel == PriceListChannel.Sales
        ? channel
        : throw new PriceListRuleException(PriceListRuleCodes.UnsupportedChannel, "channel",
            "Bu sürümde yalnız satış kanalı fiyat listesi açılır; alış/ortak kanal açık karardır.");

    private static PriceListVatBasis RequireVatBasis(PriceListVatBasis vatBasis) => vatBasis == PriceListVatBasis.ExcludingVat
        ? vatBasis
        : throw new PriceListRuleException(PriceListRuleCodes.UnsupportedVatBasis, "vatBasis",
            "Bu sürümde fiyatlar KDV hariç tutulur; KDV dahil liste açık karardır.");

    internal static void RequireIdentity(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidIdentity, field, "Kimlik boş olamaz.");
        }
    }

    internal static string RequireActor(string? actorId)
    {
        var trimmed = actorId?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= 128
            ? trimmed
            : throw new PriceListRuleException(PriceListRuleCodes.InvalidActor, "actor", "İşlemi yapan kullanıcı kimliği gerekir.");
    }
}

/// <summary>One numbered, validity-dated set of prices of a list.</summary>
public sealed class PriceListVersion
{
    private readonly List<PriceListLine> lines;

    private PriceListVersion(Guid id, int versionNo, DateOnly validFrom, DateOnly? validThrough, PriceListVersionState state,
        List<PriceListLine> lines, string createdBy, DateTimeOffset createdAtUtc, string? publishedBy, DateTimeOffset? publishedAtUtc)
    {
        Id = id;
        VersionNo = versionNo;
        ValidFrom = validFrom;
        ValidThrough = validThrough;
        State = state;
        this.lines = lines;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
        PublishedBy = publishedBy;
        PublishedAtUtc = publishedAtUtc;
    }

    public Guid Id { get; }

    public int VersionNo { get; }

    public DateOnly ValidFrom { get; }

    /// <summary>The version's own last day, or open-ended (null) until the next published version starts.</summary>
    public DateOnly? ValidThrough { get; }

    public PriceListVersionState State { get; }

    public IReadOnlyList<PriceListLine> Lines => lines.AsReadOnly();

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string? PublishedBy { get; }

    public DateTimeOffset? PublishedAtUtc { get; }

    public PriceListLine? LineFor(Guid productId, Guid priceUnitId) =>
        lines.SingleOrDefault(line => line.ProductId == productId && line.PriceUnitId == priceUnitId);

    internal static PriceListVersion CreateDraft(Guid id, int versionNo, DateOnly validFrom, DateOnly? validThrough,
        IReadOnlyList<PriceListLineInput> inputs, string createdBy, DateTimeOffset createdAtUtc)
    {
        RequireValidity(validFrom, validThrough);
        return new PriceListVersion(id, versionNo, validFrom, validThrough, PriceListVersionState.Draft, BuildLines(inputs),
            createdBy, createdAtUtc, null, null);
    }

    internal PriceListVersion Published(string actorId, DateTimeOffset now) =>
        new(Id, VersionNo, ValidFrom, ValidThrough, PriceListVersionState.Published, [.. lines], CreatedBy, CreatedAtUtc, actorId, now);

    /// <summary>Rebuilds a stored version; the lines are checked as on the write path and must be numbered 1..n.</summary>
    public static PriceListVersion Restore(Guid id, int versionNo, DateOnly validFrom, DateOnly? validThrough, PriceListVersionState state,
        IReadOnlyList<PriceListLine> storedLines, string createdBy, DateTimeOffset createdAtUtc, string? publishedBy,
        DateTimeOffset? publishedAtUtc)
    {
        PriceList.RequireIdentity(id, "versionId");
        ArgumentNullException.ThrowIfNull(storedLines);
        if (versionNo < 1 || state is not (PriceListVersionState.Draft or PriceListVersionState.Published)
            || (state == PriceListVersionState.Published) != (publishedBy is not null && publishedAtUtc is not null))
        {
            throw new InvalidOperationException("Kayıtlı fiyat sürümünün numarası, durumu ya da yayım bilgisi tutarsız.");
        }

        RequireValidity(validFrom, validThrough);
        var ordered = storedLines.OrderBy(line => line.LineNo).ToList();
        var rebuilt = BuildLines(ordered.Select(line => new PriceListLineInput(line.LineId, line.ProductId, line.PriceUnitId,
            line.UnitPrice, line.FloorUnitPrice)).ToList());
        if (ordered.Where((line, index) => line.LineNo != index + 1).Any())
        {
            throw new InvalidOperationException("Kayıtlı fiyat satırları 1'den başlayıp boşluksuz numaralanır.");
        }

        return new PriceListVersion(id, versionNo, validFrom, validThrough, state, rebuilt, PriceList.RequireActor(createdBy),
            createdAtUtc, publishedBy is null ? null : PriceList.RequireActor(publishedBy), publishedAtUtc);
    }

    private static void RequireValidity(DateOnly validFrom, DateOnly? validThrough)
    {
        if (validFrom == default)
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidValidity, "validFrom", "Başlangıç tarihi gerekir.");
        }

        if (validThrough is { } through && through < validFrom)
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidValidity, "validThrough",
                "Bitiş tarihi başlangıçtan önce olamaz.");
        }
    }

    private static List<PriceListLine> BuildLines(IReadOnlyList<PriceListLineInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count > PriceList.MaximumLines)
        {
            throw new PriceListRuleException(PriceListRuleCodes.TooManyLines, "lines",
                $"Bir sürüm en fazla {PriceList.MaximumLines} satır taşır.");
        }

        var built = new List<PriceListLine>(inputs.Count);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index] ?? throw new PriceListRuleException(PriceListRuleCodes.InvalidLine, $"lines[{index}]", "Satır boş olamaz.");
            var field = $"lines[{index}]";
            PriceList.RequireIdentity(input.LineId, $"{field}.lineId");
            PriceList.RequireIdentity(input.ProductId, $"{field}.productId");
            PriceList.RequireIdentity(input.PriceUnitId, $"{field}.priceUnitId");
            if (built.Any(line => line.LineId == input.LineId))
            {
                throw new PriceListRuleException(PriceListRuleCodes.DuplicateIdentity, $"{field}.lineId", "Satır kimliği tekrar edemez.");
            }

            if (built.Any(line => line.ProductId == input.ProductId && line.PriceUnitId == input.PriceUnitId))
            {
                throw new PriceListRuleException(PriceListRuleCodes.DuplicateProductUnit, $"{field}.productId",
                    "Aynı ürün aynı fiyat biriminde bir sürümde bir kez fiyatlanır.");
            }

            RequirePrice(input.UnitPrice, $"{field}.unitPrice");
            if (input.FloorUnitPrice is { } floor)
            {
                RequirePrice(floor, $"{field}.floorUnitPrice");
                if (floor > input.UnitPrice)
                {
                    throw new PriceListRuleException(PriceListRuleCodes.FloorAbovePrice, $"{field}.floorUnitPrice",
                        "Alt fiyat liste fiyatını aşamaz.");
                }
            }

            built.Add(new PriceListLine(input.LineId, index + 1, input.ProductId, input.PriceUnitId, input.UnitPrice, input.FloorUnitPrice));
        }

        return built;
    }

    private static void RequirePrice(decimal value, string field)
    {
        if (value <= 0m || value >= PriceList.PriceBound)
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidPrice, field, "Fiyat sıfırdan büyük ve 15 tam haneyi aşmayan bir tutardır.");
        }

        if (PriceList.Scale(value) > PriceList.PriceScale)
        {
            throw new PriceListRuleException(PriceListRuleCodes.PriceScaleExceeded, field,
                $"Fiyat en fazla {PriceList.PriceScale} ondalık taşır; fazlası yuvarlanmaz, reddedilir.");
        }
    }
}

/// <summary>One product price in one price unit.</summary>
public sealed record PriceListLine(Guid LineId, int LineNo, Guid ProductId, Guid PriceUnitId, decimal UnitPrice, decimal? FloorUnitPrice);

/// <summary>A line as a command gives it; the line number is its position.</summary>
public sealed record PriceListLineInput(Guid LineId, Guid ProductId, Guid PriceUnitId, decimal UnitPrice, decimal? FloorUnitPrice);

public enum PriceListChannel
{
    Sales = 1,
}

public enum PriceListVatBasis
{
    ExcludingVat = 1,
}

public enum PriceListVersionState
{
    Draft = 1,
    Published = 2,
}

/// <summary>Derived, never stored: the master §7.4 stages a published version passes through by date.</summary>
public enum PriceListVersionStatus
{
    Draft = 1,
    Planned = 2,
    Active = 3,
    Ended = 4,
}

public static class PriceListRuleCodes
{
    public const string InvalidIdentity = "price_list_invalid_identity";
    public const string InvalidActor = "price_list_invalid_actor";
    public const string InvalidName = "price_list_invalid_name";
    public const string InvalidCurrency = "price_list_invalid_currency";
    public const string UnsupportedChannel = "price_list_unsupported_channel";
    public const string UnsupportedVatBasis = "price_list_unsupported_vat_basis";
    public const string InvalidValidity = "price_list_invalid_validity";
    public const string InvalidLine = "price_list_invalid_line";
    public const string TooManyLines = "price_list_too_many_lines";
    public const string TooManyVersions = "price_list_too_many_versions";
    public const string DuplicateIdentity = "price_list_duplicate_identity";
    public const string DuplicateProductUnit = "price_list_duplicate_product_unit";
    public const string InvalidPrice = "price_list_invalid_price";
    public const string PriceScaleExceeded = "price_list_price_scale_exceeded";
    public const string FloorAbovePrice = "price_list_floor_above_price";
    public const string NoDraftVersion = "price_list_no_draft_version";
    public const string DraftVersionOpen = "price_list_draft_version_open";
    public const string NoPublishedVersion = "price_list_no_published_version";
    public const string VersionNotFound = "price_list_version_not_found";
    public const string VersionNotDraft = "price_list_version_not_draft";
    public const string VersionWithoutLines = "price_list_version_without_lines";
    public const string ValidFromInPast = "price_list_valid_from_in_past";
    public const string ValidFromNotAfterPublished = "price_list_valid_from_not_after_published";
}

/// <summary>A rule of the aggregate refused the change; <see cref="Field"/> names the input it concerns.</summary>
public sealed class PriceListRuleException(string code, string field, string message) : Exception(message)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}

public sealed class PriceListVersionConflictException(long expectedVersion, long currentVersion)
    : Exception("Fiyat listesi başka bir kayıtla değişti.")
{
    public long ExpectedVersion { get; } = expectedVersion;

    public long CurrentVersion { get; } = currentVersion;
}
