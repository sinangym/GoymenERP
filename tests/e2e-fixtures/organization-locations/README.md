# Organization location browser fixture

Status: **SOURCE_PREPARED**. This package has not started an environment or measured
runtime counts. It reuses normal Development migrations/seeding and the existing
Organization API. It contains a seed inventory and a Vite proxy override, not a
browser runner. Organization Claude owns the React journey and browser assertions.

Scope: DEC-ORG-001..004, DEC-GOV-002 and the Organization acceptance handoff in
[Issue #66](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5646417811).
[PR #241](https://github.com/sinangym/GoymenERP/pull/241) supplies the existing real
SQL lifecycle evidence. Its unchanged tests need not run again for these files.
Company, stock, financial, role and ownership policies remain outside this fixture.

## Bind the actual run before startup

Use a dedicated Organization checkout. Do not switch, modify or launch from a
Cari/Product checkout. `fixture.json` pins the inspected main baseline and Git
blob IDs (repository bytes, independent of checkout line endings). These are
source references, not final runtime pins. Review any changed source against that
inventory; a later shared provider change is expected, not silently interchangeable.

Before starting, record every `runtimeBinding.requiredBeforeStart` field in an
ignored `artifacts/organization-browser/<runId>/run-binding.json`. Use actual
40-character commits, absolute worktree paths and a unique run ID. The backend
must include #241 and the normal Organization capability provider; the frontend
must include the corrected #242 implementation and shared shell mount. Record
provider/mount source commits even when they are already ancestors of BE/FE.
Record the fixture and Claude's browser journey commits independently. Verify
`git rev-parse HEAD` and `git status --porcelain` in each checkout; no uncommitted
application changes or changing branches during a run. The referenced fixture
must be available in the frontend checkout for the relative config import.

At this source baseline the normal Organization provider and shell mount are
pending with their shared owners; #242 corrections are pending with Organization
Claude. Do not fabricate capabilities, use C01 activation flags or call this
environment ENV_READY with absent pins. No startup is authorized by a filled-in
status string alone: the actual prerequisites above must exist.

Reserve only loopback ports **27433 SQL**, **27080 API**, **27173 Vite**. Before
startup check `Get-NetTCPConnection -State Listen` for all three ports and
`docker ps -a` for the chosen container name. An occupied port/name is a conflict;
do not stop its owner or auto-select a different port. Record the checks and
chosen database/container names. Use .NET from `global.json`, Node 24 as in CI,
and package-lock dependencies. Record actual runtime versions.

## Start using existing tools

The following are manual PowerShell commands for the fully bound run, not an
automatic orchestrator. Each console belongs to the Organization run. Keep the
SQL secret in ephemeral process memory; do not enable command tracing, dump the
environment, print Docker inspect's `Config.Env`, or save connection strings.

1. In the owned runtime console set `$orgRunId` to the recorded unique run ID
   (letters/digits only), then start the existing pinned SQL image:

   ```powershell
   if ($orgRunId -cnotmatch '^[a-zA-Z0-9]+$') { throw 'Invalid run ID' }
   $orgContainer = "goymen-org-browser-$orgRunId"
   $orgDatabase = "GoymenERP_Organization_Browser_$orgRunId"
   $orgSecret = 'Org9!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
   $env:MSSQL_SA_PASSWORD = $orgSecret
   try {
     $orgRecordedContainerId = docker run -d --name $orgContainer --label com.goymen.owner=organization-backend-f800 --label "com.goymen.run=$orgRunId" -p 127.0.0.1:27433:1433 -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e MSSQL_SA_PASSWORD mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89
     if ($LASTEXITCODE -ne 0) { throw 'Organization SQL container startup failed' }
   } finally {
     Remove-Item Env:MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue
   }
   ```

   There is no mounted/named persistent volume or preloaded database. Record the
   returned container ID. Check only this container's logs for SQL readiness;
   startup is bounded by the run's chosen timeout. If it fails, clean up this
   run. No raw SQL console or new database gateway is needed.

2. In the same console, with the bound backend checkout as working directory:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   $env:ASPNETCORE_URLS = 'http://127.0.0.1:27080'
   $env:DB_PROVIDER = 'SqlServer'
   $env:DB_CONNECTION_STRING = "Server=127.0.0.1,27433;Database=$orgDatabase;User Id=sa;Password=$orgSecret;Encrypt=true;TrustServerCertificate=true;Connect Timeout=30"
   try {
     dotnet run --project src/backend/GoymenERP.Api/GoymenERP.Api.csproj --no-launch-profile
   } finally {
     Remove-Item Env:DB_CONNECTION_STRING -ErrorAction SilentlyContinue
     Remove-Item Env:DB_PROVIDER,Env:ASPNETCORE_ENVIRONMENT,Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
     $orgSecret = $null
   }
   ```

   Clear unrelated inherited application overrides before this step, particularly
   C01 activation, seed and authentication settings; use the bound Development
   configuration. Program applies existing module migrations and seed. The
   existing Identity hosted service checks the expected SQL database/logical
   endpoint and RCSI before the host accepts traffic. A startup failure is
   NOT_READY; do not bypass it or manually alter the database. `/health/live`
   alone is not proof of capabilities, seeded counts or browser readiness.

3. In a second owned console, in the bound frontend checkout's
   `src/frontend/goymen-erp-web` directory:

   ```powershell
   npm ci
   if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed' }
   $env:VITE_API_BASE_URL = '/api/v1'
   $env:VITE_DEV_USER = 'company-a-user'
   try {
     npm run dev -- --config ../../../tests/e2e-fixtures/organization-locations/vite.config.mjs
   } finally {
     Remove-Item Env:VITE_API_BASE_URL,Env:VITE_DEV_USER -ErrorAction SilentlyContinue
   }
   ```

   The fixture config imports the shared Vite config and overrides only the
   loopback host, strict port and API proxy. Browser requests stay same-origin,
   preserving replay headers without a shared CORS change. Restart this owned
   Vite process to select another existing fixture actor; do not invent runtime
   header switches or grants. Clear browser state between actor runs. Anonymous
   and direct API authorization assertions belong to the existing API boundary.

## Measure readiness and acceptance

Record actual API/Vite process IDs, start times, executable paths and checkout
bindings. Through the live API, resolve companies by existing code and use
`X-Company-Id`; Development identity uses `X-Development-User`. Fetch sites and
all location pages, then resolve OSM-H0 and its site ID from the results. Match
the source expectations in `fixture.json` to observed actor-scoped counts. Do
not compare historical company fiscal fields in the seed JSON to runtime data:
existing migrations may correct them. No topology is inserted for GSP/GIN.

Read the normal shell capabilities and verify the mounted Organization route
actually renders for the authorized actor. Verify the browser uses the owned
proxy and real API, not mocks. Only then record ENV_READY, observed counts,
source bindings and sanitized evidence. Claude owns journey execution and
loading/error/keyboard assertions; this package does not report their results.

Use offset pages (default 25, bounded 1..100), not Cari cursors or contract ACK.
Use fresh detail versions, trimmed reasons and explicit idempotency keys.
After an empty 204, GET detail and list again; do not predict the next version.
For an uncertain result preserve the exact original key/payload. A later GET
or an authorization failure on retry does not settle the original command.
Replayed 409 metadata can be historical. Preserve the reason draft on conflict.
The manifest's scenarios identify the narrow acceptance sequence; UI ownership
and final behavior remain in the reviewed frontend source.

## Owned cleanup, including failed startup

Stop the API and Vite in their own foreground consoles with Ctrl+C. If either
console is lost, verify the recorded PID **and start time**, executable/command
line and checkout binding before stopping it; a reused PID or unrelated listener
must not be killed. Confirm their ports are released. Never kill by port alone.

In the SQL owner console, compare against the recorded container ID and run ID:

```powershell
$orgObservedId = docker inspect --format '{{.Id}}' $orgContainer
$orgObservedOwner = docker inspect --format '{{index .Config.Labels "com.goymen.owner"}}' $orgContainer
$orgObservedRun = docker inspect --format '{{index .Config.Labels "com.goymen.run"}}' $orgContainer
if ($LASTEXITCODE -ne 0 -or $orgObservedId -cne $orgRecordedContainerId -or $orgObservedOwner -cne 'organization-backend-f800' -or $orgObservedRun -cne $orgRunId) {
  throw 'Container ownership does not match this run'
}
docker rm -f -v $orgRecordedContainerId
if ($LASTEXITCODE -ne 0) { throw 'Owned container cleanup failed' }
Remove-Item Env:MSSQL_SA_PASSWORD,Env:DB_CONNECTION_STRING -ErrorAction SilentlyContinue
$orgSecret = $null
```

`-v` removes only this container's anonymous volumes; the fixture mounts no
shared volume. This removes the disposable database without schema-reset SQL.
Verify the recorded container no longer exists and all three owned ports are
free. Keep sanitized evidence and record cleanup success/failure. Do not prune
Docker, delete another checkout or touch another lane's environment. No fixture
cleanup is a production retention policy.
