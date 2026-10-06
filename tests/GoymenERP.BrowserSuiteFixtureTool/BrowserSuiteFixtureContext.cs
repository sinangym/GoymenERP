using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// What a suite owner's seed may use. Every HTTP call goes through the in-process real host as a
/// declared synthetic actor, carries a server-validated correlation id, and fails the run on any
/// non-success answer; commands carry a deterministic idempotency key.
/// </summary>
public sealed partial class BrowserSuiteFixtureContext
{
    public const string ActorHeader = "X-Development-User";
    public const string CompanyHeader = "X-Company-Id";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string IdempotencyHeader = "Idempotency-Key";
    private const string FixtureDiagnosticHeader = "X-Browser-Fixture-Diagnostic";

    private static readonly HashSet<string> SafeFixtureDiagnosticPhases = new(StringComparer.Ordinal)
    {
        "mfg_plan_party_account",
        "mfg_plan_parent_roll",
        "mfg_plan_product_snapshot",
        "mfg_plan_settings_profile",
    };

    // HTTP header names are case-insensitive: a seed cannot replace these in any spelling.
    private static readonly HashSet<string> ContextHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ActorHeader,
        CompanyHeader,
        CorrelationHeader,
        IdempotencyHeader,
    };

    private readonly Func<DelegatingHandler[], HttpClient> _createClient;
    private readonly IReadOnlySet<string> _actors;
    private int _requests;

    internal BrowserSuiteFixtureContext(
        string suite,
        IServiceProvider services,
        IReadOnlySet<string> actors,
        Func<DelegatingHandler[], HttpClient> createClient)
    {
        Suite = suite;
        Services = services;
        _actors = actors;
        _createClient = createClient;
    }

    public string Suite { get; }

    /// <summary>
    /// The real host's root services, for an owner write port that has no endpoint (the gated fixture
    /// writers registered only inside the disposable envelope). A service that needs an actor or a
    /// company is reached through <see cref="Api"/> instead.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>Requests sent through <see cref="Api"/> so far; the evidence's step count.</summary>
    public int Requests => _requests;

    /// <summary>
    /// An HTTP client for one declared synthetic actor (a BootstrapIdentity user of this run), with the
    /// company header when the route needs one.
    /// </summary>
    public HttpClient Api(string actorUserId, Guid? companyId = null)
    {
        if (!_actors.Contains(actorUserId))
        {
            throw new InvalidOperationException(
                $"Actor '{actorUserId}' is not a BootstrapIdentity user of this run.");
        }

        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("The company id must not be empty.");
        }

        return _createClient([new FixtureRequestHandler(this, actorUserId, companyId)]);
    }

    /// <summary>The deterministic idempotency key of one seed step: browser-suite:&lt;suite&gt;:&lt;step&gt;.</summary>
    public string IdempotencyKey(string step)
    {
        if (!StepPattern().IsMatch(step))
        {
            throw new InvalidOperationException($"Seed step '{step}' is not a safe step name.");
        }

        return $"browser-suite:{Suite}:{step}";
    }

    /// <summary>A display text marked as synthetic data of this suite.</summary>
    public string Label(string text) => $"SENTETIK browser-suite:{Suite} {text}";

    /// <summary>
    /// POSTs a JSON command with the step's idempotency key (and any route-specific request headers,
    /// e.g. a negotiated client contract) and returns the JSON answer.
    /// </summary>
    public async Task<JsonElement> PostJsonAsync(
        string actorUserId,
        Guid? companyId,
        string path,
        object body,
        string step,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var client = Api(actorUserId, companyId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(IdempotencyHeader, IdempotencyKey(step));
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            if (ContextHeaders.Contains(name))
            {
                throw new InvalidOperationException($"Header {name} is set by the context.");
            }

            request.Headers.Add(name, value);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>GETs a JSON resource as the actor.</summary>
    public async Task<JsonElement> GetJsonAsync(
        string actorUserId,
        Guid? companyId,
        string path,
        CancellationToken cancellationToken)
    {
        using var client = Api(actorUserId, companyId);
        using var response = await client.GetAsync(path, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private int NextRequest() => Interlocked.Increment(ref _requests);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StepPattern();

    private sealed class FixtureRequestHandler(
        BrowserSuiteFixtureContext context,
        string actorUserId,
        Guid? companyId) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var sequence = context.NextRequest();
            request.Headers.Add(ActorHeader, actorUserId);
            request.Headers.Add(CorrelationHeader, $"browser-suite:{context.Suite}:{sequence}");
            if (companyId is { } company)
            {
                request.Headers.Add(CompanyHeader, company.ToString("D"));
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var status = (int)response.StatusCode;
                var diagnostic = response.Headers.TryGetValues(FixtureDiagnosticHeader, out var values)
                    ? values.SingleOrDefault()
                    : null;
                var safeDiagnostic = diagnostic is not null && SafeFixtureDiagnosticPhases.Contains(diagnostic)
                    ? $" [{diagnostic}]"
                    : "";
                response.Dispose();
                throw new InvalidOperationException(
                    $"Seed request {sequence} {request.Method} {request.RequestUri?.AbsolutePath} "
                    + $"answered {status}{safeDiagnostic}: {(body.Length > 512 ? body[..512] : body)}");
            }

            return response;
        }
    }
}
