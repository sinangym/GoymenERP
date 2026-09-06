using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GoymenERP.CariAcceptance006;

public static class LiveSmoke
{
    public static async Task<object> RunAsync(RunSettings identity)
    {
        var before = await DatabaseEvidence.ReadAsync(identity);
        Require(before.Rcsi && before.Effects.Parties == 0 && before.Effects.Accounts == 0, "New smoke DB must be empty and RCSI enabled.");
        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(identity.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        var exchanges = new List<object>();
        async Task<JsonElement> Call(string method, string route, string? company, object? body, int status, string? key = null, string? actor = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), route);
            if (company is not null) request.Headers.Add("X-Company-Id", company);
            if (actor is not null || company is not null) request.Headers.Add("X-Development-User", actor ?? identity.Actor);
            request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Require((int)response.StatusCode == status, $"Smoke {method} {route}: expected {status}, got {(int)response.StatusCode}.");
            var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var replay) ? replay.Single() : null;
            var originalCorrelation = response.Headers.TryGetValues("Idempotency-Original-Correlation-Id", out var original) ? original.Single() : null;
            exchanges.Add(new { method, route, company, status, replayed, originalCorrelation, body = text });
            if (route == "/health/live") return default;
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        static JsonElement OneAccount(JsonElement item, string company, JsonValueKind versionKind)
        {
            var accounts = item.GetProperty("companyAccounts").EnumerateArray().ToArray();
            Require(accounts.Length == 1 && accounts[0].GetProperty("companyId").GetString() == company, "One target account required.");
            Require(accounts[0].GetProperty("version").ValueKind == versionKind, "Account version wire type.");
            Require(item.GetProperty("version").ValueKind == versionKind, "Party version wire type.");
            return accounts[0];
        }
        static void OneList(JsonElement result, string id, string code, string company)
        {
            var rows = result.GetProperty("items").EnumerateArray().ToArray();
            Require(rows.Length == 1 && rows[0].GetProperty("id").GetString() == id
                && rows[0].GetProperty("code").GetString() == code, "Same Party in list.");
            Require(result.GetProperty("totalCount").GetInt64() == 1, "Count is one Party.");
            OneAccount(rows[0], company, JsonValueKind.Number);
        }
        await Call("GET", "/health/live", null, null, 200);
        foreach (var company in new[] { identity.CompanyAId, identity.CompanyBId })
        {
            await Call("GET", "/api/v1/identity/me", company, null, 200);
            await Call("GET", "/api/v1/organization/companies", company, null, 200);
            var capabilities = await Call("GET", "/api/v1/shell/capabilities", company, null, 200);
            var text = capabilities.GetRawText();
            foreach (var capability in new[] { "cari.parties.list", "cari.parties.create", "cari.parties.detail", "cari.accounts.link" })
                Require(text.Contains('"' + capability + '"', StringComparison.Ordinal), "Missing live capability: " + capability);
        }
        await Call("GET", "/api/v1/counterparties/", identity.CompanyBId, null, 403, actor: "company-a-user");
        await Call("POST", "/api/v1/counterparties/", identity.CompanyAId,
            new { kind = 1, legalName = identity.SmokePrefix + " denied", tradeName = (string?)null, roles = 3 },
            403, Guid.NewGuid().ToString("N"), "company-a-viewer");
        Require((await DatabaseEvidence.ReadAsync(identity)).Effects == before.Effects, "Denied requests/reads produced business effects.");
        var createBody = new { kind = 1, legalName = identity.SmokePrefix + " Sentetik Metal", tradeName = (string?)null, roles = 3 };
        var created = await Call("POST", "/api/v1/counterparties/", identity.CompanyAId, createBody, 201, Guid.NewGuid().ToString("N"));
        var id = created.GetProperty("id").GetString()!;
        var code = created.GetProperty("code").GetString()!;
        var originalA = OneAccount(created, identity.CompanyAId, JsonValueKind.String);
        var afterCreate = await DatabaseEvidence.ReadAsync(identity);
        Require(afterCreate.Effects == before.Effects with { Parties = 1, Accounts = 1, CreateAudits = before.Effects.CreateAudits + 1, CreateExecutions = before.Effects.CreateExecutions + 1 }, "Create SQL effects.");
        var list = "/api/v1/counterparties/?q=" + Uri.EscapeDataString(code) + "&includeCount=true";
        var detail = "/api/v1/counterparties/" + id;
        var candidates = "/api/v1/counterparties/company-account-link-candidates?q=" + Uri.EscapeDataString(code);
        OneList(await Call("GET", list, identity.CompanyAId, null, 200), id, code, identity.CompanyAId);
        OneAccount(await Call("GET", detail, identity.CompanyAId, null, 200), identity.CompanyAId, JsonValueKind.String);
        Require((await Call("GET", list, identity.CompanyBId, null, 200)).GetProperty("items").GetArrayLength() == 0, "B list must be empty before link.");
        Require((await Call("GET", detail, identity.CompanyBId, null, 404)).GetProperty("code").GetString() == "not_found", "B detail before link.");
        var candidatePage = await Call("GET", candidates, identity.CompanyBId, null, 200);
        var candidate = candidatePage.GetProperty("items").EnumerateArray().Single();
        Require(candidate.GetProperty("id").GetString() == id && !candidate.TryGetProperty("version", out _), "Candidate identity/type.");
        var source = candidate.GetProperty("visibleSourceAccounts").EnumerateArray().Single();
        Require(source.GetProperty("companyId").GetString() == identity.CompanyAId && source.GetProperty("version").ValueKind == JsonValueKind.String, "Candidate source scope/type.");
        Require((await DatabaseEvidence.ReadAsync(identity)).Effects == afterCreate.Effects, "Reads produced business effects.");
        var linkKey = Guid.NewGuid().ToString("N");
        var link = detail + "/company-account";
        var linkBody = new { roles = 2 };
        var linked = await Call("POST", link, identity.CompanyBId, linkBody, 201, linkKey);
        var accountB = OneAccount(linked, identity.CompanyBId, JsonValueKind.String);
        Require(linked.GetProperty("id").GetString() == id && linked.GetProperty("code").GetString() == code, "Link must preserve Party identity.");
        Require(accountB.GetProperty("roles").GetInt32() == 2, "B roles.");
        var afterLink = await DatabaseEvidence.ReadAsync(identity);
        Require(afterLink.Effects == before.Effects.AddJourney(1), "One Party/two accounts/exact audit and command delta.");
        OneList(await Call("GET", list, identity.CompanyBId, null, 200), id, code, identity.CompanyBId);
        OneAccount(await Call("GET", detail, identity.CompanyBId, null, 200), identity.CompanyBId, JsonValueKind.String);
        Require((await Call("GET", candidates, identity.CompanyBId, null, 200)).GetProperty("items").GetArrayLength() == 0, "Linked Party leaves candidates.");
        OneList(await Call("GET", list, identity.CompanyAId, null, 200), id, code, identity.CompanyAId);
        var preservedA = OneAccount(await Call("GET", detail, identity.CompanyAId, null, 200), identity.CompanyAId, JsonValueKind.String);
        foreach (var property in new[] { "id", "roles", "version", "isActive" })
            Require(originalA.GetProperty(property).GetRawText() == preservedA.GetProperty(property).GetRawText(), "A account changed: " + property);
        var replayedLink = await Call("POST", link, identity.CompanyBId, linkBody, 201, linkKey);
        Require(linked.GetRawText() == replayedLink.GetRawText(), "Replay body mismatch.");
        var last = JsonSerializer.SerializeToElement(exchanges[^1]);
        Require(last.GetProperty("replayed").GetString() == "true" && !string.IsNullOrEmpty(last.GetProperty("originalCorrelation").GetString()), "Replay metadata missing.");
        var final = await DatabaseEvidence.ReadAsync(identity);
        Require(final.Effects == afterLink.Effects, "Replay/reads added effects.");
        Evidence.Write("baseline.json", final);
        var evidence = new { observedAtUtc = DateTimeOffset.UtcNow, identity.RunId, identity.InstanceId,
            transport = "real TCP HttpClient -> loopback Kestrel -> new SQL Server", before, afterCreate, afterLink, final, exchanges, passed = true };
        Evidence.Write("smoke.json", evidence);
        return evidence;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("C006 " + message);
    }
}
