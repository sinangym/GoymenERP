using System.Text.Json.Nodes;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

/// <summary>SAT-D102 e-belge profile schemas: the ACCOUNT version as canonical decimal TEXT and the scenario code's shape.</summary>
public static partial class ErpOpenApiContract
{
    private const string EInvoiceScenarioCodePattern = "^[A-Z][A-Z0-9_]*$";

    private static void ApplyEDocumentProfileSchema(OpenApiSchema schema, Type type)
    {
        if (type == typeof(RecordCounterpartyEDocumentStatus) || type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetPositiveIntegerStringProperty(schema, "expectedVersion");
            SetSchemaExtension(schema, "x-erp-normalized-maximum-expectedVersion",
                JsonValue.Create("9223372036854775807")!);
        }

        if (type == typeof(RecordCounterpartyEDocumentStatus))
        {
            schema.Description = "Records the e-belge status a user checked for the active company's account (SAT-D102). "
                + "A known status (EInvoice / EArchive) stamps the server time and the caller as the check, even when "
                + "the value is unchanged; Unknown resets the status and its check. expectedVersion is the ACCOUNT "
                + "version as canonical decimal TEXT. No external (GİB) call is made.";
        }

        if (type == typeof(SetCounterpartyEInvoiceScenarioOverride))
        {
            SetStringBounds(schema, "scenarioCode", 1, CompanyPartyAccount.EInvoiceScenarioCodeMaximumLength,
                EInvoiceScenarioCodePattern);
            schema.Description = "Sets the customer's e-Fatura scenario override to a code of the Settings scenario "
                + "list in force today, or clears it with an explicit null (scenarioCode must be present). Settings "
                + "without a list is 422 einvoice_scenario_code_list_missing; a code the list does not carry is 422 "
                + "einvoice_scenario_not_listed; Settings that cannot be read is 503 service_unavailable (nothing "
                + "stored). Re-sending the current code is a no-op.";
        }

        if (type == typeof(CounterpartyAccountEDocumentView))
        {
            SetPositiveIntegerStringProperty(schema, "version");
            schema.Description = "The active company's account e-belge profile (SAT-D102). effectiveScenario is the "
                + "customer override if present and listed, else the Settings default, else Unresolved; the Settings "
                + "default starts empty, so Unresolved is the normal starting answer. evaluatedOn is the Europe/Istanbul "
                + "business day the Settings keys were read for.";
        }
    }
}
