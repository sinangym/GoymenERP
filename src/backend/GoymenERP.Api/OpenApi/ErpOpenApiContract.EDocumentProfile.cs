using System.Text.Json.Nodes;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

/// <summary>
/// SAT-D102 / SAT-D119 e-belge profile schemas: the VKN-shared PROFILE version as canonical decimal TEXT (with "0"
/// meaning "no profile yet; create") and the scenario code's shape.
/// </summary>
public static partial class ErpOpenApiContract
{
    private const string EInvoiceScenarioCodePattern = "^[A-Z][A-Z0-9_]*$";

    // The profile version is "0" (no profile yet) or a positive canonical integer; no leading zero.
    private const string EDocumentVersionPattern = "^(0|[1-9][0-9]*)$";

    // The opaque profile token (P2-1): 64 lower-case hex digits, never parsed by the client.
    private const string EDocumentProfileTokenPattern = "^[0-9a-f]{64}$";

    private static void ApplyEDocumentProfileSchema(OpenApiSchema schema, Type type)
    {
        if (type == typeof(RecordCounterpartyEDocumentStatus) || type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetStringBounds(schema, "expectedVersion", 1, 19, EDocumentVersionPattern);
            SetSchemaExtension(schema, "x-erp-normalized-maximum-expectedVersion",
                JsonValue.Create("9223372036854775807")!);
            SetStringBounds(schema, "expectedProfileToken", 64, 64, EDocumentProfileTokenPattern);
        }

        if (type == typeof(RecordCounterpartyEDocumentStatus))
        {
            schema.Description = "Records the e-belge status a user checked for the customer's VKN-shared profile "
                + "(SAT-D102 / SAT-D119). A known status (EInvoice / EArchive) stamps the server time, the caller and the "
                + "active company as the check, even when the value is unchanged; Unknown resets the status and its "
                + "check. The fence is the pair the read returned: expectedVersion (the shared PROFILE version as "
                + "canonical decimal TEXT, \"0\" = no profile yet; the first record creates it) and expectedProfileToken "
                + "(which profile the read showed). Another profile than the one read — e.g. after the party's VKN "
                + "changed — is 409 version_conflict. A party with no VKN/TCKN is 422 e_document_tax_identity_required. "
                + "No external (GİB) call.";
        }

        if (type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetStringBounds(schema, "scenarioCode", 1, CustomerEDocumentProfile.ScenarioCodeMaximumLength,
                EInvoiceScenarioCodePattern);
            schema.Description = "Sets the customer's e-Fatura scenario override (VKN-shared) to a code of the writing "
                + "company's Settings scenario list in force today, or clears it with an explicit null (scenarioCode "
                + "must be present). Fenced on expectedVersion (the shared PROFILE version, \"0\" creates) and "
                + "expectedProfileToken as the status command. Settings without a "
                + "list is 422 einvoice_scenario_code_list_missing; a code the list does not carry is 422 "
                + "einvoice_scenario_not_listed; Settings that cannot be read is 503 service_unavailable (nothing "
                + "stored); a party with no VKN is 422 e_document_tax_identity_required. Re-sending the current code is a no-op.";
        }

        if (type == typeof(CounterpartyAccountEDocumentView))
        {
            SetStringBounds(schema, "version", 1, 19, EDocumentVersionPattern);
            SetStringBounds(schema, "profileToken", 64, 64, EDocumentProfileTokenPattern);
            schema.Description = "The account's customer VKN-shared e-belge profile (SAT-D119): a check or scenario set "
                + "in one company is seen on the same-VKN customer in the others; the checker is shown only inside the "
                + "company the check was recorded in (statusCheckedInActiveCompany, statusCheckedByDisplayName). "
                + "taxIdentityNumberPresent is false when the party has no VKN (then status Unknown, scenario Unresolved, "
                + "profileToken null). version + profileToken are the command fence (\"0\" = no profile yet). After a "
                + "VKN change the card shows the NEW VKN's profile; the old one stays as it is. effectiveScenario is the override if present and listed, else the "
                + "Settings default, else Unresolved; the default starts empty, so Unresolved is the normal start. "
                + "evaluatedOn is the Europe/Istanbul business day the Settings keys were read for.";
        }
    }
}
