/**
 * L-01 — the LIVE U-01/U-02 journey: real browser, real Product API, real store behind it,
 * against the ONE environment snapshot the gate verified and the run is bound to.
 *
 * create → list (found by search) → card → basic edit with a reason → fresh version →
 * back to the card → reopen from the list. BOTH commands go through a real lost reply
 * and a real same-key retry: the first create POST and the first edit PATCH are
 * re-issued to the real API by the test, and only once the real success and the real
 * product identity have been SEEN is the browser's delivery of that reply aborted. The
 * user then retries the SAME key, which travels as an ordinary browser request and
 * comes back as a real replay (`isReplay: true`) of the same record.
 *
 * NOTHING here is mocked: no `route.fulfill`, no synthesised body, no invented code or
 * version. Every code, id and version asserted below was read from a real response.
 *
 * THREE STAGE HANDOFFS to the Backend's real snapshot (`src/checkpoint.ts`): `baseline`
 * before the first mutation, `after-create` once the create is complete and before the
 * edit begins (the Product owner's Created assertion needs the real v1, which the final
 * v2 cannot yield), and `final` after the edit and the reopen. At each, the browser
 * quiesces (C009 barrier), writes a request naming the real record identity and the real
 * exchanges so far, and continues ONLY on the Backend's acknowledgement of those exact
 * bytes. No sleep stands in for the snapshot; the browser opens no SQL.
 */
import { expect, test } from '@playwright/test'
import { LiveRecorder, ProductDeliveryWithholder, evidenceIdentity, lShot, liveEnvironment, writeLJson } from '../src/live'
import { CHECKPOINT_STAGES, DEFAULT_ACK_TIMEOUT_MS, awaitCheckpointAck, writeCheckpointRequest, type CheckpointReceipt, type CheckpointStage } from '../src/checkpoint'
import { runDir, runId as browserRunId } from '../paths'
import { retrySame, saveDraft, saveEdit } from '../src/ui'

const env = liveEnvironment()
const ACK_TIMEOUT_MS = Number(process.env.URUN_CHECKPOINT_TIMEOUT_MS ?? DEFAULT_ACK_TIMEOUT_MS)
// The journey holds three quiet stages for the Backend; its own budget must cover them.
test.setTimeout(CHECKPOINT_STAGES.length * ACK_TIMEOUT_MS + 5 * 60_000)

/** Real HTTP exchange metadata — method, path, status, key, origin header; never a body. */
function exchangesOf(recorder: LiveRecorder) {
  return recorder.records
    .filter((r) => r.path.startsWith('/api/v1/products'))
    .map((r) => ({ seq: r.seq, method: r.method, path: r.path, status: r.status, idempotencyKey: r.idempotencyKey, companyId: r.companyId, transferOutcome: r.transferOutcome, at: r.at }))
}

/**
 * One stage: quiesce, write the request, wait for the Backend's ack of those exact bytes.
 * Returns the receipt; a missing or wrong ack fails the journey at THIS stage.
 */
async function handOff(stage: CheckpointStage, recorder: LiveRecorder, subject: { productId: string | null; code: string | null; version: string | null }): Promise<CheckpointReceipt> {
  const quiescence = await recorder.quiesce()
  expect(quiescence.accepted, `browser quiescent before ${stage}: ${JSON.stringify(quiescence.outstanding)}`).toBe(true)
  const request = writeCheckpointRequest(runDir, {
    stage, requestedAtUtc: new Date().toISOString(), browserRunId,
    resourceRunId: env.resourceRunId, resourceInstanceId: env.resourceInstanceId, manifestSha256: env.manifestSha256,
    subject, quiescence, exchanges: exchangesOf(recorder),
    note: `Stage ${stage}: the browser is quiet and holds; the Backend takes its real quiescent snapshot (control.ps1 -Product -Action snapshot) and acknowledges these request bytes at <stage>.ack.json.`,
  })
  console.log(`[checkpoint] ${stage} requested: ${request.path} (sha256 ${request.sha256}); waiting up to ${ACK_TIMEOUT_MS} ms for ${stage}.ack.json`)
  const receipt = await awaitCheckpointAck(runDir, { stage, resourceRunId: env.resourceRunId, requestSha256: request.sha256, requestPath: request.path }, { timeoutMs: ACK_TIMEOUT_MS })
  console.log(`[checkpoint] ${stage} acknowledged after ${receipt.waitedMs} ms: snapshot ${receipt.ack.snapshotSha256} (${receipt.snapshotBytes})`)
  return receipt
}
const RUN_TOKEN = new Date().toISOString().replace(/\D/g, '').slice(8, 14)
const NAME = `${env.browserPrefix} Kabul sac ${RUN_TOKEN}`
const EDITED_NAME = `${NAME} galvaniz`
const REASON = `${env.browserPrefix} kabul koşumu ${RUN_TOKEN}: ad düzeltildi`
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

test('L-01 live journey: create (lost reply → same-key replay) → list → card → edit (lost reply → same-key replay) → card → reopen', async ({ page }, info) => {
  const recorder = LiveRecorder.attach(page)
  const withholder = await ProductDeliveryWithholder.install(page, [
    { label: 'create', method: 'POST', expectedStatus: 201, match: (p) => p === '/api/v1/products' },
    { label: 'edit', method: 'PATCH', expectedStatus: 200, match: (p) => /^\/api\/v1\/products\/[^/]+\/draft$/.test(p) },
  ])
  await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), env.companyId)

  // ── create ────────────────────────────────────────────────────────────────
  await page.goto('/urun/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
  await page.getByLabel(/Ürün adı/).fill(NAME)
  // The contract's exact `codeGroupId`, and only if the LIVE authorized catalogue offers
  // it (contract step 4) — never an arbitrary first group.
  const group = page.getByLabel(/Kod grubu/)
  await expect(group.locator(`option[value="${env.codeGroupId}"]`)).toBeAttached()
  await group.selectOption(env.codeGroupId)
  await lShot(page, 'l01', '01-create-filled')

  // ── stage 1: baseline, before the first mutation leaves the browser ─────────
  const baseline = await handOff('baseline', recorder, { productId: null, code: null, version: null })

  await saveDraft(page).click()
  // The browser never received the 201: the outcome is unknown, the key is held.
  await expect(page.getByTestId('product-create-unknown')).toBeVisible()
  await lShot(page, 'l01', '02-create-unknown')
  expect(withholder.withheld.filter((w) => w.label === 'create')).toHaveLength(1)
  const createWithheld = withholder.withheld[0]
  expect(createWithheld.marker, createWithheld.upstreamError ?? '').toBe('upstream_success_then_delivery_abort')
  // The validated origin (§15): the committed company, sent — not the Product's owner.
  expect(createWithheld.companyId).toBe(env.companyId)
  expect(createWithheld.identity.lifecycle).toBe('Draft')
  const productId = createWithheld.identity.id as string
  const createdCode = createWithheld.identity.code as string
  expect(productId).toMatch(UUID_RE)

  await retrySame(page).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
  // The replay of the SAME record: the server says so, and the code is the same one.
  await expect(page.getByText('Bu istek daha önce işlenmişti; ikinci bir kayıt oluşturulmadı.')).toBeVisible()
  await expect(page.getByText(`${createdCode} — ${NAME}`)).toBeVisible()
  const createPosts = recorder.records.filter((r) => r.method === 'POST' && r.path === '/api/v1/products')
  expect(createPosts).toHaveLength(2)
  expect(createPosts[0].idempotencyKey).toBe(createWithheld.idempotencyKey)
  expect(createPosts[1].idempotencyKey).toBe(createWithheld.idempotencyKey)
  // Frozen origin: the retry went out under the same company as the withheld send.
  expect(createPosts[1].companyId).toBe(createPosts[0].companyId)
  expect(createPosts[1].status).toBe(201)
  await lShot(page, 'l01', '03-create-replayed')

  // ── stage 2: after-create — the create is complete, the edit has not begun ───
  // The Created assertion wants the REAL v1; it cannot be derived from the final v2.
  const afterCreate = await handOff('after-create', recorder, { productId, code: createdCode, version: createWithheld.identity.version })

  // ── list → card ───────────────────────────────────────────────────────────
  await page.getByRole('link', { name: 'Listeye dön' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
  await page.getByLabel('Kod, eski kod veya ad ara').fill(RUN_TOKEN)
  const row = page.getByRole('link', { name: createdCode, exact: true })
  await expect(row).toBeVisible()
  await lShot(page, 'l01', '04-list-found')
  await row.click()
  await expect(page.getByRole('heading', { level: 1, name: NAME })).toBeVisible()
  const versionBefore = (await page.getByText('Sürüm').locator('xpath=following-sibling::dd').first().textContent())?.trim() ?? ''
  expect(versionBefore).toBe(createWithheld.identity.version)
  await lShot(page, 'l01', '05-card')

  // ── edit ──────────────────────────────────────────────────────────────────
  await page.getByRole('link', { name: 'Düzenle' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })).toBeVisible()
  await page.getByLabel(/Ürün adı/).fill(EDITED_NAME)
  await page.getByLabel(/Değişiklik gerekçesi/).fill(REASON)
  await saveEdit(page).click()
  await expect(page.getByTestId('product-edit-unknown')).toBeVisible()
  await lShot(page, 'l01', '06-edit-unknown')
  const editWithheld = withholder.withheld.find((w) => w.label === 'edit')
  expect(editWithheld?.marker, editWithheld?.upstreamError ?? '').toBe('upstream_success_then_delivery_abort')
  expect(editWithheld?.companyId).toBe(env.companyId)
  expect(editWithheld?.identity.id).toBe(productId)
  expect(editWithheld?.identity.version).not.toBe(versionBefore)

  await retrySame(page).click()
  await expect(page.getByText('Değişiklikler kaydedildi')).toBeVisible()
  await expect(page.getByText('Bu istek daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.')).toBeVisible()
  const patches = recorder.records.filter((r) => r.method === 'PATCH' && r.path === `/api/v1/products/${productId}/draft`)
  expect(patches).toHaveLength(2)
  expect(patches[0].idempotencyKey).toBe(patches[1].idempotencyKey)
  expect(patches[1].companyId).toBe(patches[0].companyId)
  expect(patches[1].status).toBe(200)
  const sentBody = patches[0].requestBody as { expectedVersion?: unknown; changes?: Record<string, unknown>; reason?: unknown }
  expect(sentBody.expectedVersion).toBe(versionBefore)
  expect(Object.keys(sentBody.changes ?? {})).toEqual(['name'])
  await lShot(page, 'l01', '07-edit-replayed')

  // ── back to the card, then reopen from the list ───────────────────────────
  await page.getByRole('link', { name: 'Ürün kartına dön' }).click()
  await expect(page.getByRole('heading', { level: 1, name: EDITED_NAME })).toBeVisible()
  const versionAfter = (await page.getByText('Sürüm').locator('xpath=following-sibling::dd').first().textContent())?.trim() ?? ''
  expect(versionAfter).toBe(editWithheld?.identity.version)
  await page.goto('/urun')
  await page.getByLabel('Kod, eski kod veya ad ara').fill(RUN_TOKEN)
  await page.getByRole('link', { name: createdCode, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: EDITED_NAME })).toBeVisible()
  await lShot(page, 'l01', '08-reopened')

  // ── stage 3: final — the edit is complete and the card was reopened ─────────
  const final = await handOff('final', recorder, { productId, code: createdCode, version: versionAfter })

  // Global master with a validated origin: every Product request went as the bound
  // actor under the bound company, and no payload carried a companyId.
  for (const r of recorder.records.filter((x) => x.path.startsWith('/api/v1/products'))) {
    expect(r.companyId, `${r.method} ${r.path}`).toBe(env.companyId)
    expect(r.devIdentityHeader, `${r.method} ${r.path}`).toBe(env.actor)
    if (r.requestBody !== null && typeof r.requestBody === 'object') expect(r.requestBody).not.toHaveProperty('companyId')
  }

  writeLJson('withheld', 'l01-withheld.json', { ...evidenceIdentity(env), withheld: withholder.withheld, passedThrough: withholder.passedThrough })
  // ONE handover for the Product owner: the three stage receipts (request/ack paths and
  // hashes, snapshot hashes), the real create/edit/replay exchanges and the withheld
  // deliveries — all from this one bound run. A snapshot hash here is the Backend's
  // artifact, acknowledged against these request bytes; nothing is derived from JSON.
  writeLJson('checkpoints', 'l01-handover.json', {
    ...evidenceIdentity(env), productId, createdCode, versionBefore, versionAfter,
    stages: { baseline, afterCreate, final },
    exchanges: exchangesOf(recorder), withheld: withholder.withheld,
    note: 'Baseline, after-create and final are the Backend’s real quiescent snapshots, each acknowledged against the exact request bytes the browser wrote at that quiet stage. The browser ran no SQL and derived no state from responses.',
  })
  await recorder.finish(info, 'l01-journey', { ...evidenceIdentity(env), productId, createdCode, versionBefore, versionAfter, checkpoints: { baseline: baseline.ack.snapshotSha256, afterCreate: afterCreate.ack.snapshotSha256, final: final.ack.snapshotSha256 } })
})
