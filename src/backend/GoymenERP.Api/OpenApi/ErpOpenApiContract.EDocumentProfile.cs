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

    private static void ApplyEDocumentProfileSchema(OpenApiSchema schema, Type type)
    {
        if (type == typeof(RecordCounterpartyEDocumentStatus) || type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetStringBounds(schema, "expectedVersion", 1, 19, EDocumentVersionPattern);
            SetSchemaExtension(schema, "x-erp-normalized-maximum-expectedVersion",
                JsonValue.Create("9223372036854775807")!);
        }

        if (type == typeof(RecordCounterpartyEDocumentStatus))
        {
            schema.Description = "Records the e-belge status a user checked for the customer's VKN-shared profile "
                + "(SAT-D102 / SAT-D119). A known status (EInvoice / EArchive) stamps the server time and the caller as "
                + "the check, even when the value is unchanged; Unknown resets the status and its check. expectedVersion "
                + "is the shared PROFILE version as canonical decimal TEXT (\"0\" = no profile yet; the first record "
                + "creates it). A party with no VKN/TCKN is 422 e_document_tax_identity_required. No external (GİB) call.";
        }

        if (type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetStringBounds(schema, "scenarioCode", 1, CustomerEDocumentProfile.ScenarioCodeMaximumLength,
                EInvoiceScenarioCodePattern);
            schema.Description = "Sets the customer's e-Fatura scenario override (VKN-shared) to a code of the writing "
                + "company's Settings scenario list in force today, or clears it with an explicit null (scenarioCode "
                + "must be present). expectedVersion is the shared PROFILE version (\"0\" creates). Settings without a "
                + "list is 422 einvoice_scenario_code_list_missing; a code the list does not carry is 422 "
                + "einvoice_scenario_not_listed; Settings that cannot be read is 503 service_unavailable (nothing "
                + "stored); a party with no VKN is 422 e_document_tax_identity_required. Re-sending the current code is a no-op.";
        }

        if (type == typeof(CounterpartyAccountEDocumentView))
        {
            SetStringBounds(schema, "version", 1, 19, EDocumentVersionPattern);
            schema.Description = "The account's customer VKN-shared e-belge profile (SAT-D119): a check or scenario set "
                + "in one company is seen on the same-VKN customer in the others. taxIdentityNumberPresent is false when "
                + "the party has no VKN (then status Unknown, scenario Unresolved). version is the shared profile "
                + "version (\"0\" = none yet). effectiveScenario is the override if present and listed, else the "
                + "Settings default, else Unresolved; the default starts empty, so Unresolved is the normal start. "
                + "evaluatedOn is the Europe/Istanbul business day the Settings keys were read for.";
        }
    }
}
