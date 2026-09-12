using System.Text.Json.Nodes;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static partial class ErpOpenApiContract
{
    private static void ApplyContactInformationSchema(OpenApiSchema schema, Type type)
    {
        if (type == typeof(UpdateCounterpartyContactInformation))
        {
            SetPositiveIntegerStringProperty(schema, "expectedVersion");
            schema.Description = "Updates only the selected company account. expectedVersion is its logical "
                + "Version as canonical decimal TEXT (1..9223372036854775807), never SQL rowversion bytes "
                + "or a JSON number. Missing fields are unchanged; explicit null clears optional text. "
                + "A 409 version_conflict requires a fresh GET; the error does not echo numeric versions.";
            SetSchemaExtension(schema, "x-erp-normalized-maximum-expectedVersion",
                JsonValue.Create("9223372036854775807")!);
        }

        if (type == typeof(CounterpartyContactInformationView)
            || type == typeof(CounterpartyOperationalAddress)
            || type == typeof(CounterpartyOperationalContact)
            || type == typeof(CounterpartyContactChannel))
        {
            SetPositiveIntegerStringProperty(schema, "version");
        }

        if (type == typeof(CounterpartyContactInformationChanges))
        {
            ContactText(schema, "phone", 32, optional: true);
            ContactText(schema, "phoneExtension", 10, optional: true);
            ContactText(schema, "email", 254, optional: true);
            ContactText(schema, "website", 2048, optional: true);
            ContactOperations(schema, "addresses");
            ContactOperations(schema, "contacts");
            schema.Description = "Omitted fields retain their values. Null/trim-empty clears optional text. "
                + "Collections are explicit child operations, not replacement lists. Null collections are invalid. "
                + "An omitted child is never deleted or deactivated. Phone prefixes, punctuation and leading zeroes "
                + "are preserved. Email format is not deliverability verification; website permits http/https without credentials.";
        }

        if (type == typeof(CounterpartyAddressChange))
        {
            ContactOperation(schema);
            ContactText(schema, "purpose", 120, optional: false);
            ContactText(schema, "countryCode", 2, optional: false);
            ContactText(schema, "line1", 250, optional: false);
            ContactText(schema, "line2", 250, optional: true);
            ContactText(schema, "city", 100, optional: true);
            ContactText(schema, "district", 100, optional: true);
            ContactText(schema, "postalCode", 20, optional: true);
            schema.Description = "An upsert of a new UUID requires purpose (billing/delivery/operational), "
                + "countryCode (two ASCII letters, stored uppercase), and line1. Existing fields may be omitted. "
                + "No official/global address is changed. Country validation is syntactic, not an official country registry check. "
                + "deactivate/reactivate requires an existing child and no value fields.";
        }

        if (type == typeof(CounterpartyContactChange))
        {
            ContactOperation(schema);
            ContactText(schema, "displayName", 200, optional: false);
            ContactText(schema, "jobTitle", 120, optional: true);
            ContactText(schema, "purpose", 120, optional: false);
            ContactOperations(schema, "channels");
            schema.Description = "New contacts require displayName and purpose. Channels are explicit operations; "
                + "missing channels are retained. A contact never creates an Identity user or grants permissions.";
        }

        if (type == typeof(CounterpartyContactChannelChange))
        {
            ContactOperation(schema);
            ContactText(schema, "kind", 10, optional: false);
            ContactText(schema, "value", 254, optional: false);
            ContactText(schema, "extension", 10, optional: true);
            schema.Description = "New channel requires kind (phone/email) and value. Kind is immutable for a child UUID. "
                + "Phone value maximum 32; email maximum 254. Extension maximum 10, phone only. "
                + "deactivate/reactivate requires an existing child and no value fields.";
        }
    }

    private static void ContactText(OpenApiSchema schema, string name, int maximum, bool optional)
    {
        schema.Properties![name] = new OpenApiSchema
        {
            Type = optional ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String,
        };
        schema.Required?.Remove(name);
        AddCanonicalLengthContract(schema, name, optional ? null : 1, maximum);
    }

    private static void ContactOperations(OpenApiSchema schema, string name)
    {
        schema.Required?.Remove(name);
        if (schema.Properties![name] is OpenApiSchema property)
        {
            property.MaxItems = CounterpartyContactInformationEditor.MaximumOperationsPerCollection;
        }
    }

    private static void ContactOperation(OpenApiSchema schema)
    {
        if (schema.Properties!["operation"] is OpenApiSchema operation)
        {
            operation.Pattern = "^(upsert|deactivate|reactivate)$";
        }

        if (schema.Properties["id"] is OpenApiSchema id)
        {
            SetSchemaExtension(id, NonEmptyUuidExtensionName, JsonValue.Create(true)!);
        }
    }
}
