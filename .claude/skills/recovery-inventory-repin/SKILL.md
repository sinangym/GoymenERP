---
name: recovery-inventory-repin
description: Use whenever a branch changes ANY file under src/backend/ or a build-control file (Directory.Build.props, NuGet.Config, global.json, .editorconfig…), adds/removes an EF migration or DbContext, edits a reviewed store method/blob, or after merging origin/main into such a branch — i.e. whenever the "Recovery surface inventory" or "Release manifest" CI job fails with "drifted" or a count mismatch. Re-pins tools/generate_recovery_surface_inventory.py, contracts/recovery/recovery-surface-inventory-v1.schema.json, tests/recovery_surface_inventory and tests/release_manifest.
---

# Recovery inventory re-pin

The recovery generator is source-bound: it reads the exact committed blobs. Since recovery pin Faz 1
(`docs/delivery/RECOVERY_PIN_CAKISMA_ONERISI_v1.md`, Sinan Q1 = yes) it pins only a **narrow risky surface**
(`EXPECTED_RISKY_SOURCE_SURFACE_*`): build-control files, every non-`.cs` file under `src/backend/`, and every
backend `.cs` that mentions a DB/SQL/EF/object-store token, declares a `using X =` alias or `global using`, is a
reviewed path, declares a type named like a reviewed DbContext/store/adapter, or has zero/several namespaces —
plus a shadow-declaration index. An ordinary endpoint/business `.cs` edit no longer needs a re-pin. The full
`src/backend` byte digest is still emitted as `sourceSurface` (inventory and release manifest) as non-blocking
post-hoc evidence; it is never pinned and is not a merge block.

## Pinned values and where they live

| Pin | File(s) |
|---|---|
| `EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT`, `EXPECTED_RISKY_SOURCE_SURFACE_FINGERPRINT` (narrow risky surface only) | `tools/generate_recovery_surface_inventory.py` (tests read the constants; no test literal) |
| `EXPECTED_MIGRATION_COUNT`, `EXPECTED_MIGRATION_DECLARATION_FINGERPRINT` | generator; schema `counts.migrations.const` **and** `migrations.minItems/maxItems`; recovery test (`"migrations": N` dict and `assertEqual(N, len(migrations))`); `tests/release_manifest/test_generate_release_manifest.py` (`"migrations": N`) |
| `EXPECTED_DB_CONTEXT_COUNT` / `_DECLARATION_FINGERPRINT`, `EXPECTED_DURABLE_STORE_COUNT` | generator; schema `counts.dbContexts`/`durableStores`; both tests' counts dicts |
| `REVIEWED_OPERATIVE_METHOD_FINGERPRINTS[(path, method)]` | generator (e.g. `PurchasingStore.cs` `StoreRequestAsync`) |
| `REVIEWED_LIVE_SOURCE_SHA256[path]` | generator (plain blob sha256 of reviewed stores such as `SalesStore*.cs`, `PurchasingStore.cs`) |
| `EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT` / `_FINGERPRINT` | generator (any Audit module live source change) |

## Steps

1. Finish the code change first; re-pin last (every later backend edit invalidates it again).
2. Commit everything (a `chore(recovery): WIP re-pin` commit is fine). The generator refuses a dirty worktree
   (`_require_inventory_inputs_clean`: *any* tracked or untracked change) and reads HEAD's blobs, not the files.
3. Print the actual values (read-only helper shipped with this skill; ~30 s):
   ```bash
   python3 -I .claude/skills/recovery-inventory-repin/scripts/print_recovery_actuals.py
   ```
   `DRIFT` lines name what to change. The real generator also prints actual count/fingerprint for the source
   surface and the declaration sets in its error text:
   ```bash
   python3 -I -S -B tools/generate_recovery_surface_inventory.py > /dev/null   # ~70 s; exit 0 = pins hold
   ```
4. Edit the pins (table above). For a new migration: +1 in the generator, schema (`const`, `minItems`, `maxItems`),
   recovery test (two places) and release-manifest test. For a risky-surface change: the two `EXPECTED_RISKY_SOURCE_SURFACE_*`
   constants in the generator only. Add a one-line comment above a changed pin only where the file already keeps such notes.
5. Commit (amend the WIP or a new `chore(recovery): re-pin …` commit), then verify on the committed HEAD:
   ```bash
   python3 -I -S -B tools/generate_recovery_surface_inventory.py > /tmp/inventory.json && echo OK
   python3 -I -S -B -m unittest discover -s tests/recovery_surface_inventory -p "test_*.py"   # slow (~18 min)
   python3 -I -S -B -m unittest discover -s tests/release_manifest -p "test_*.py"             # ~8 min
   ```
   The CI jobs `recovery-surface-inventory` and `release-manifest` in `.github/workflows/ci.yml` run exactly these.
   The recovery unittest loads the **real** repository at HEAD (`_load_source_snapshot(REPOSITORY_ROOT, …)`): do not
   edit or create any file in the worktree while it runs, or it fails with "worktree contains tracked or untracked
   changes". Use a separate `git worktree` for parallel edits.
6. Usually also needed in the same PR: `architecture-guard-repin` (store/DbContext/migration fingerprints) and,
   for endpoint changes, `openapi-regenerate`.

## Pitfalls

- **Pins are never hand-merged.** On a merge conflict in any pin file, take either side, finish the merge, commit,
  then recompute from step 3 (see `main-merge-resync`). A pin that "looks merged" is wrong.
- Re-pin **after** the last backend edit, including review fixes; the risky-surface fingerprint changes on any
  byte edit (comments too) of a file in the narrow set.
- Reviewed live sources must be ASCII in code tokens: `_live_csharp_code` raises "non-ASCII identifier or code
  token" (one commit had to make comments ASCII-only in a reviewed writer). Keep Turkish text inside string
  literals or resource files, not in identifiers.
- The helper evaluates method fingerprints on `_read_csharp` (comments stripped) — the same input the generator uses.
- Do not touch `tools/generate_release_manifest.py` for a count change; only its test pins the counts.
- `python3 -I -S -B` is how CI calls the tools; keep it so local results match.
