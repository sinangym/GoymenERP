using System.Text.Json.Nodes;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

/// <summary>SAT-D57 customer group schemas: versions as canonical decimal TEXT and the group name's length.</summary>
public static partial class ErpOpenApiContract
{
    private static void ApplyCustomerGroupSchema(OpenApiSchema schema, Type type)
    {
        if (type == typeof(RenameCounterpartyCustomerGroup)
            || type == typeof(SetCounterpartyCustomerGroupActiveState)
            || type == typeof(AssignCounterpartyCustomerGroup))
        {
            SetPositiveIntegerStringProperty(schema, "expectedVersion");
            SetSchemaExtension(schema, "x-erp-normalized-maximum-expectedVersion",
                JsonValue.Create("9223372036854775807")!);
        }

        if (type == typeof(CreateCounterpartyCustomerGroup) || type == typeof(RenameCounterpartyCustomerGroup))
        {
            AddCanonicalLengthContract(schema, "name", 1, CustomerGroup.NameMaximumLength);
        }

        if (type == typeof(CreateCounterpartyCustomerGroup)
            && schema.Properties!["id"] is OpenApiSchema id)
        {
            SetSchemaExtension(id, NonEmptyUuidExtensionName, JsonValue.Create(true)!);
            schema.Description = "Creates one customer group of the active company with a caller-chosen UUID, so an "
                + "unanswered create is retried with the same Idempotency-Key and id. The name is trimmed (NFC), "
                + "1..100 characters, unique in the company among active and passive groups. Nothing is seeded.";
        }

        if (type == typeof(AssignCounterpartyCustomerGroup))
        {
            schema.Description = "Assigns the active company's account a group of the SAME company, or clears it with "
                + "an explicit null (customerGroupId must be present). expectedVersion is the ACCOUNT version as "
                + "canonical decimal TEXT. Another company's or an unknown group is 422 customer_group_not_found; a "
                + "passive group is never newly assigned (409 customer_group_inactive).";
        }

        if (type == typeof(CounterpartyCustomerGroupView) || type == typeof(CounterpartyAccountCustomerGroupView))
        {
            SetPositiveIntegerStringProperty(schema, "version");
        }
    }
}
