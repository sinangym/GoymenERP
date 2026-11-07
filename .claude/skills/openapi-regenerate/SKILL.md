---
name: openapi-regenerate
description: Use when a PR adds or changes an HTTP endpoint, request/response DTO, status code or problem code (src/backend/GoymenERP.Api/Endpoints/**, *ContractsV1.cs, ErpOpenApiContract*.cs), or when the "OpenAPI contract drift" CI job fails — regenerates contracts/openapi/goymen-erp-v1.json byte-for-byte and updates the expectations in tests/GoymenERP.Api.IntegrationTests/OpenApiContractGenerationTests.cs.
---

# OpenAPI regenerate

`contracts/openapi/goymen-erp-v1.json` and `OpenApiContractGenerationTests.cs` were each changed by 18 commits on
main since 2026-10-20. The document is generated at build time; the baseline must equal it **byte for byte**.

## Steps

1. Change the endpoint/DTO in code (Codex lane owns backend; Claude lanes edit only their own module's surface).
2. Generate the document — one of:
   - **CI (no local .NET):** add the label `openapi:generate` to the PR, or dispatch
     `.github/workflows/openapi-generate.yml` with input `pr=<number>`. It builds the exact PR head and uploads the
     artifact `openapi-<head sha>` (`artifacts/openapi.json`, kept 3 days). The PR must already contain
     `origin/main` (`OPENAPI_BASE_NOT_MERGED` otherwise), and the head must not move after labelling.
     ```bash
     gh workflow run openapi-generate.yml -f pr=<number>
     gh run list --workflow openapi-generate.yml -L 3
     gh run download <run-id> -n openapi-<head-sha> -D /tmp/openapi
     cp /tmp/openapi/openapi.json contracts/openapi/goymen-erp-v1.json
     ```
   - **Local .NET 10:** the API project writes `artifacts/openapi.json` on build
     (`OpenApiGenerateDocumentsOptions` in `src/backend/GoymenERP.Api/GoymenERP.Api.csproj`):
     ```bash
     python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py
     dotnet restore src/backend/GoymenERP.Api/GoymenERP.Api.csproj -noAutoResponse --configfile NuGet.Config
     dotnet build src/backend/GoymenERP.Api/GoymenERP.Api.csproj -noAutoResponse --configuration Release --no-restore
     cp artifacts/openapi.json contracts/openapi/goymen-erp-v1.json
     ```
3. Verify byte equality exactly as CI does:
   ```bash
   python3 -I -S -B tools/validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json
   ```
4. Update `OpenApiContractGenerationTests.cs` for every new/changed operation (operationId, e.g.
   `WithdrawPurchaseRequestV1`). The lists to touch (see #907 `06c34c0`):
   - `ExpectedOperations` — `new("post", "/api/v1/…", "<OperationId>", AuthenticatedResponses("200:application/json", "400:application/problem+json", …), "#/components/schemas/<Request>", ["path:id:true:type:string;format:uuid"])`
   - `ExpectedSuccessSchemas` — `["<OperationId>"] = "ref:<Response>"`
   - `RequiredCompanyOperations` (or `OptionalCompanyOperations`)
   - `IdempotentOperations` and `ReplayableResponseStatuses` (`[200, 409]` for commands with terminal refusals)
   - `EmptyBodyPossible400Operations`, `CodeLessProblemResponses` (`"<OperationId>:404"`), `MixedProblemResponses`
   - `AdditionalProblemCodes` — `["<OperationId>:409"] = ["concurrent_change", …, "version_conflict"]` (sorted, all codes the service can emit)
   - `ExpectedSuccessComponentSchemas` / `ExpectedRequestComponentSchemas` / `ExpectedProblemComponentSchemas` —
     `ObjectSchema([required…], ("field", "type:string;format:uuid"), …)`; nullable = `"type:null|string"`,
     optional object = `"oneOf:ref:<X>|type:null"`.
5. Frontend: update the lane's `contract.ts` decoder field lists for the same change (`frontend-screen-states`).
6. Re-run `recovery-inventory-repin` (endpoint sources moved the surface).

## Pitfalls

- Never hand-edit `goymen-erp-v1.json`; always copy the generated artifact. Formatting/ordering differences fail
  the drift job.
- After merging `origin/main`, regenerate again ("regenerate OpenAPI and re-pin … after the main merge" commits):
  two PRs adding operations conflict in the JSON; resolve by regenerating, not by merging hunks.
- 401/403/404 and every 503 are code-less on the wire (endpoint `Problem` helpers return
  `Results.Problem(statusCode: …)`); list them in `CodeLessProblemResponses`, not `AdditionalProblemCodes`.
- The generate workflow refuses fork branches and non-`main` bases; it only reports, it never commits.
