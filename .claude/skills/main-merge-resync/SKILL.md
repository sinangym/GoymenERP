---
name: main-merge-resync
description: Use when origin/main moved under an open backend/frontend PR (another lane merged), the PR shows conflicts in pinned/generated files (contracts/openapi/goymen-erp-v1.json, OpenApiContractGenerationTests.cs, tools/generate_recovery_surface_inventory.py, contracts/recovery/*.schema.json, tests/recovery_surface_inventory, tests/release_manifest, PlatformTenantQueryFilterBypassGuardTests.cs, tools/relational_inventory/*.txt, ShellCapabilityRegistryAuthorities.cs), or CI went red only because main changed. Orders the merge and the regeneration so no hash is hand-merged.
---

# Resync a PR after origin/main moved

18 commit bodies on main since 2026-10-20 are "after merging origin/main … re-pin / regenerate". The pinned files
are hashes or generated outputs of the **combined** tree; a textual merge of them is always wrong.

## Steps

1. `git fetch origin main && git merge origin/main` (merge, not rebase, once review started: reviewers diff
   `<REVIEW-READY sha>..<new head>`; a rebase destroys that).
2. Resolve conflicts:
   - **Code and docs:** resolve normally; keep both sides' behaviour.
   - **Pinned/generated files:** take `--theirs` (main) for the file, then re-apply only your *structural*
     additions (your new dictionary entry, your inventory line, your OpenAPI test expectations), never your old
     hash values:
     ```bash
     git checkout --theirs -- contracts/openapi/goymen-erp-v1.json
     ```
     `goymen-erp-v1.json` is never edited by hand: after taking main's copy, regenerate it (`openapi-regenerate`)
     so your endpoints come back from the build, not from a manual re-apply.
   - `tools/relational_inventory/<module>.txt`: union of lines, re-sorted (`LC_ALL=C sort`, see
     `test-naming-and-groups`).
3. Commit the merge (worktree must be clean for the generators).
4. Regenerate in this order, each from the committed merge result:
   1. `openapi-regenerate` (if either side touched endpoints) — copy the generated artifact.
   2. `architecture-guard-repin` — recompute every fingerprint whose file changed on either side.
   3. `permission-capability-registration` step 5 — registry `Actual:` values if any provider moved.
   4. `recovery-inventory-repin` — last, because every step above that edited `src/backend/` moves the surface.
   Commit as `chore(recovery): re-inventory after merging origin/main <short sha> (#<pr>)`.
5. Re-run the light checks you need (`decision-docs-validation` if decisions moved) and push.
6. Review state after the merge: **K1/K2** — the delta contains an `origin/main` merge, so delta mode drops; ask
   for a full review of the new head (v4 §1). **K3** — two new full exact-head reviews. Docs-only PRs — the two DOCS
   reviews must name the new head.

## Pitfalls

- Re-pinning before the merge is committed pins your branch's tree, not the merged one; CI fails again.
- Two PRs in flight that both add a migration both bump the count; after the first merges, the second recomputes
  (counts and fingerprints), never adds "+1" by hand on top of a stale value.
- Keep one candidate head under review at a time (v4 "review dondurma"); do not push while a reviewer is reading.
