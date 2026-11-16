---
name: review-signatures
description: Use when writing an independent review, a REVIEW-READY / DELTA-OK signature, a PR body, a merge decision or an Issue #66 receipt in GoymenERP. Gives the exact first-line signature formats (K3-REVIEW-A/B, K2-REVIEW, DOCS-REVIEW-1/2, DELTA-OK), how to post them (COMMENT review on the exact head, because author and reviewer share one GitHub account), the K-level merge gates from AGENTS.md / v4 / 25 Oct decision, the PR body lines ("Modül bağlantıları") and the one-line #66 receipt.
---

# Review signatures, merge gates and #66 receipts

Sources: `AGENTS.md`, `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v4.md` §1/§3,
`docs/decisions/SINAN_KARARLARI_2026-10-21.md` (row 3), `docs/decisions/SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`
(M1/M2, ASSUMPTION-MERGE-1 confirmed by SAT-D13), `docs/decisions/SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md`.

## 1. Pick the level

| Level | Scope | Reviews | CI |
|---|---|---|---|
| **K3** | DB migration/constraint · money/formula/tolerance · permission/role · cross-module contract · `.github/**` · production activation | two independent **full** exact-head reviews (A, B); a later head may take `DELTA-OK` **only if the delta is mechanical**: (1) conflict-free main merge, (2) conflicts only in generator-owned files resolved by re-running the generator, (3) test rename/format with byte-identical asserts, (4) comments/docs; hand-resolved conflicts in `src/**`, `tests/**` or migrations are not mechanical — any business logic, migration, money, permission, contract, `.github/**` or production change needs two new full reviews (GOV-D1, 2026-11-07). A mechanical merge that meets GOV-D7 needs no separate `DELTA-OK`; the machine evidence goes in the receipt (§4) | fresh required CI on the head + real SQL/browser/failure evidence |
| **K1/K2** | everything else | one independent exact-head review; later heads by `DELTA-OK` | fresh required CI |
| **Docs/governance** | `docs/**`, `AGENTS.md`, `.claude/**` (and decisions) | two independent exact-head reviews | none for doc/settings-only; `docs/decisions/**`, `docs/backlog/**`, `.github/**` need the local light checks (`decision-docs-validation`) recorded in the PR |

`.claude/settings.json` additionally needs Sinan's explicit one-line approval on Issue #66. `.github/**` reviews also
confirm every `runs-on` is `ubuntu-latest`/`windows-latest`, no self-hosted label, artifacts only secret-free CI
evidence ≤ 7 days, no cache/Packages/secret/environment/deploy surface.

## 2. First-line signatures (exact, 40-char sha)

```
K3-REVIEW-A APPROVE <40-char head sha>
K3-REVIEW-B APPROVE <40-char head sha>
K2-REVIEW APPROVE <40-char head sha>          # K1 uses the same form
DOCS-REVIEW-1 APPROVE <40-char head sha>
DOCS-REVIEW-2 APPROVE <40-char head sha>
REVIEW-READY <40-char sha>                    # author, K1/K2: freezes the head; no push until reviewed
DELTA-OK <40-char new head sha>               # reviewer: read git diff <approved sha>..<new head>; K3 only for a mechanical delta (GOV-D1): paste `git diff --stat` and the class (1–4) of each file
```
Use `REQUEST-CHANGES` in place of `APPROVE` when a P0/P1 (or a blocking P2) is open. Examples on main: #902
(`K3-REVIEW-A APPROVE 3ef10bb…`), #872 (`K2-REVIEW APPROVE 5ed23b1…`), #933 (`DOCS-REVIEW-1 APPROVE 3bc18a0…`).

Body after the first line (Turkish, plain): independence statement (own worktree at that sha, did not read the other
review), scope (`git diff origin/main...<sha>`, file count), sources checked (decision ids), "Kontroller"
(CompanyId, authority, idempotency/replay, audit, concurrency, migration/rollback, OpenAPI ↔ decoder, UI states,
tests), "Bulgular" with P0/P1/P2/P3 and `path:line`, "Kanıt" (commands run, CI run).

Delta mode falls back to a full review when the delta touches a file outside the REVIEW-READY diff, or contains an
`origin/main` merge that is neither GOV-D1/GOV-D6 mechanical (then one independent `DELTA-OK`) nor GOV-D7 mechanical
(then no `DELTA-OK`, machine evidence in the receipt, §4).

## 3. Posting

Author and reviewer agents use the same GitHub account, so GitHub refuses APPROVE on one's own PR: post the review
as a **COMMENT review bound to the exact head**:
```bash
gh api repos/sinangym/GoymenERP/pulls/<n>/reviews -X POST \
  -f event=COMMENT -f commit_id=<40-char head sha> -F body=@/path/to/review.md
```
(PR issue comments are also used; the first line is what counts.) GraphQL is blocked in cloud sessions — use
`gh api` REST, not `gh pr view`.

## 4. Merge (coordinator, or Claude lane for its own `claude/*` PR)

Before merging check: author ≠ reviewers; required reviews present for **this** head (last APPROVE/DELTA-OK sha =
expected head); fresh required CI green on that head (not for doc-only); no open P0/P1; new behaviour test; K3:
locked decisions only, no open policy chosen.

**Main freshness check (Faz 0, GOV-D2/GOV-D3 — `docs/decisions/SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`,
AGENTS.md).** For any PR touching `src/backend/`, a migration, a root build-control file (`REPOSITORY_BUILD_CONTROL_PATHS` in
`tools/generate_recovery_surface_inventory.py`: `Directory.Build.*`, `Directory.Packages.*`, `NuGet.Config`, `global.json`,
`.editorconfig`/`.globalconfig`, `src/.editorconfig` …) or the recovery inventory
(`tools/generate_recovery_surface_inventory.py`, `contracts/recovery/**`, `tests/recovery_surface_inventory/**`,
`tests/release_manifest/**`), every merger (`CODEX-ORCHESTRATOR` and the Claude lane) runs, immediately before the
squash:
```bash
git fetch origin main <pr-branch> && git merge-base --is-ancestor origin/main <expected head sha>; echo "exit=$? main=$(git rev-parse origin/main)"
```
Write the main SHA and exit code in the merge receipt. Fresh required CI must be green on that same head. Exit ≠ 0:
pick the tier (Faz 0.1, GOV-D3 scope logic, #976 measurement); try (c) first.
**(c)** (GOV-D5, `docs/decisions/SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md`) Run the `SKIPPED` / `READ_BY_CI`
block from the Faz 0 paragraph of `AGENTS.md` (same lists, word for word; the pathspecs are pinned to the
repository root with `:(top)`, so the working folder does not matter; it judges every commit on
`main` alone, so a code commit plus its revert does not pass as prose). Exit 0:
main moved only in prose that never starts CI (CIH-D5), so merge **without** a mechanical merge commit, a new CI
run or `DELTA-OK`; the green CI and the reviews on that head stay valid. Receipt: main SHA, `(c)`, the block and
its exit code. The block exits ≠ 0 (a git error included): (c) does not apply, do not merge yet. (c) is
judged with the lists at the PR head and never applies to a PR that touches the `ci.yml` path filter or these lists.
"Green" means the required run on that head ended with conclusion `success`; a `cancelled` run or a `skipped`
aggregator job is not green. Run the
`SCOPE=(…)` diff from the same paragraph (same list, word for word):
`git diff --quiet "$(git merge-base origin/main <expected head>)" origin/main -- "${SCOPE[@]}"`.
**(a)** exit ≠ 0 (main moved inside the scope): merge main into the PR, re-pin (`main-merge-resync`), rerun CI
(K3: GOV-D1, two reviews; K1/K2: the new head needs `DELTA-OK` per v4). **(b)** exit 0 (main moved only outside the
scope): no re-pin expected; if CI goes red, fall back to (a). CI tests the merge ref, so a fresh one is needed; the only
way is a **non-empty mechanical merge commit** of origin/main into the PR branch, conflict-free (never an empty commit,
"re-run jobs" or close/reopen). No merge without green required CI on the new head. `DELTA-OK` proof:
`git rev-parse <new head>^1` equals `<old head>`, and `git diff <old head> <new head>` equals
`git diff $(git merge-base origin/main <old head>) origin/main` (a conflict resolution breaks the equality and leaves
(b)). An independent reviewer signs, writing the `--stat` and the class; the author or merger cannot sign their own
merge. K3 keeps its two exact-head reviews; (b) covers only the mechanical delta. Receipt for (a)/(b): main SHA, tier, new green CI run id.
**GOV-D7** (`docs/decisions/SINAN_KARARLARI_2026-11-14_TREN_GOV_D7.md`; the conditions are an author reading,
reversible): no separate `DELTA-OK` when **all** hold: `<head>^1` is the signed head and `<head>^2` is origin/main (or
the previous stack PR head that meets the same conditions); `git show --remerge-diff <head>` is empty or shows only
generator-owned files equal to fresh generator output; generator and recovery/release tests exit 0; patch-id equal
outside generator files; required CI green on the tested tree. Regenerated files must be inside the merge commit (a
separate re-pin commit on top breaks `^1`). Hand conflict resolution in `src/**`, `tests/**` or a migration: GOV-D1/GOV-D6.
Stack: after the front PR is squashed, if `git rev-parse origin/main^{tree}` equals the previous stack head's tree,
merge the next PR without a new CI run, after aligning it with `git merge -s ours origin/main` (lineage only, tree
unchanged); write both tree hashes in the receipt. Trees differ: the tiers above. Update only the next PR or stack
after a merge.
**GOV-D8** (`docs/decisions/SINAN_KARARLARI_2026-11-14_TREN_GOV_D8.md`; author reading, reversible): work PRs target a
`claude/tren-<n>` branch cut from main and merge there after their own review gate plus local fast checks written in
the receipt (build, relevant unit/architecture tests, generator exit 0); their GitHub CI run is not required and may be
cancelled. The train → main PR regenerates the pins once and needs green full required CI on its head plus this Faz 0
check; a GOV-D7-mechanical difference from the work PRs' signed heads needs no new review, hand resolution in `src/**`,
`tests/**` or a migration falls back to GOV-D1/GOV-D6. The #66 receipt lists the work PRs and their review links. P3: the
squash `sha=` parameter checks only the head, not the base, so a seconds-wide race remains between this check and the
merge call; if main moved in between, re-run the check. GitHub "Require branches to be up to date" stays off (GOV-D3).

Then squash with expected head:
```bash
gh api -X PUT repos/sinangym/GoymenERP/pulls/<n>/merge -f merge_method=squash -f sha=<expected head sha>
```
Never merge without the evidence written on GitHub. Human gates never move: deployment, environment, secret,
enabling workflows in Settings → Actions, grant/rol/Critical, production activation, closing `OPEN-*`.

## 5. Issue #66 receipt (one line, right after merge)

```
MERGED #<n> <40-char merge sha> — <K-level> <short title>; reviews: <url A> · <url B> (head <8-char sha>)[; CI <x>/<y> green][; main <8-char sha> ancestor exit 0 | main <8-char sha> Faz 0 (c) exit 0/0][; GOV-D7 ^1 <8> ^2 <8> remerge-diff empty|generator-only, gen/tests exit 0, patch-id equal, CI run <id> | GOV-D7 stack tree <8> = <8>]
```
```bash
gh api repos/sinangym/GoymenERP/issues/66/comments -f body='MERGED #… …'
```
Other lines used on #66: `NEEDS-SINAN: <one question>` (then continue with the next slice) and the START-GO 5-line
form (slice, owner, base, exact files, K level).

## 6. PR body lines

- First line for v4 slices: `READ-ACK <start-go-id> base <sha>` when a START-GO exists.
- References: decision ids (`SAT-D…`, `ALS-D…`, `FNS-D…`, `DEC-…`), spec/plan section.
- `Modül bağlantıları: <port/event name + version>, <counter module>, <✅/🟡/❌>` — or `Modül bağlantıları: yok`.
  Required by `docs/handoffs/SATIS_MODUL_BAGLANTILARI_v1.md` "Kalıcı kural", ALS-D (EK9 §0.1 item 4),
  `docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md` §11a, `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §18,
  `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md`.
- The `.github/PULL_REQUEST_TEMPLATE.md` checklist (CompanyId/authority, audit/idempotency, concurrency, migration
  rollback, UI states, secrets, P0/P1).
- Attribution lines required by the session.

## Pitfalls

- A signature for an older sha is not a signature for the head; re-sign after every push (K3: full review again unless GOV-D1 mechanical delta).
- Short shas are fine in prose, but the first-line signature uses the full 40-char sha.
- GOV-D1 (`docs/decisions/SINAN_KARARLARI_2026-11-07_K3_DELTA.md`): K3 `DELTA-OK` is allowed only for a mechanical
  delta, written by one of the A/B reviewers, who says why it is mechanical. When in doubt, two full reviews.
  GOV-D6 (`docs/decisions/SINAN_KARARLARI_2026-11-13_EK1.md`): one independent `DELTA-OK` is enough for a mechanical
  main-merge diff. GOV-D7: a merge that meets every §4 GOV-D7 condition needs no `DELTA-OK`; if any condition is
  missing (a separate re-pin commit, a hand resolution), it does.
- Do not write review evidence only in the PR body; it must be a review/comment by the reviewer agent.
