/**
 * Sales s04 — DEC-SAL-002 (KILITLI): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni revizyon
 * ve yeniden onay gerekir." — "Rev.0 korunur" — and AT-023 ("Onaylı siparişte kritik satır değişikliği eski onayı
 * geçersizleştirir") against the REAL stack.
 *
 * Real Chromium, the exact-head web build, the real API and SQL Server, the `normal` capability profile. NOTHING IS MOCKED
 * OR INTERCEPTED. The order is the suite's own: the preparer creates it through the real create route on the seed's REAL
 * Cari party and REAL Product candidate (the currency from the real currencies reference), submits it, and the approver
 * approves it — all through the real API. The quantity revision is made in the browser ("Revizyon oluştur"), the new
 * revision is submitted and approved in the browser, and close/reopen shows Rev.1 approved with the Rev.0 history.
 *
 * ACTORS (existing only — s04 is at MAX_SUITE_ACTORS): the preparer revises (central override delta, not written here: the
 * disposable preparer role additionally holds `sales.orders.quantity.write`, the S05 price-write precedent); the approver
 * approves; the reader and the approver hold no quantity name. Until the override carries the grant this file fails at its
 * first capability assertion — it never tests something else.
 *
 * Every effect is proven twice: through the real API and through READ-ONLY SQL counts on the archive, attempt and audit rows.
 * Every schema, column and alias name in the SQL is bracketed.
 *
 * The cases are one serial journey on that order, in file order:
 *   S04-QR-01 the preparer opens the approved order, "Revizyon oluştur", changes the quantity with a reason; the order reads
 *             Rev.1 back in preparation; one archived Rev.0 with the reason, one attempt, one audit event on SourceChannel Web
 *   S04-QR-02 the same key and body again is the stored answer (Idempotency-Replayed); a stale version is the key's terminal
 *             409; nothing else is written
 *   S04-QR-03 Rev.1 is submitted by the preparer and approved by the approver in the browser; close/reopen shows Rev.1
 *             approved with the new quantity and the Rev.0 history with its reason and the quantity that followed it
 *   S04-QR-04 actors without the name see no control and are refused 403 before anything is read or written
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const { preparerActor, approverActor, readerActor, companyId, realPartyId, realCompanyAccountId, productId, productVersion, productUnitId } = seed

const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const QUANTITY_CONTROL = 'sales.orders.quantity.write'
const QUANTITY_COMMAND = 'sales.order-draft.revise-quantity.v1'
const QUANTITY_EVENT = 'sales.order-draft.quantity-revised.v1'
/** The seed's own business date on which the approved TRY profile is in effect (s04-references.spec.ts). */
const BUSINESS_DATE = '2026-09-18'
const REASON = 'SENTETIK browser-suite:s04 müşteri miktarı 7 olarak güncelledi'
const RUN = testedCommit.slice(0, 12)

interface DraftReading {
  readonly draft: { readonly id: string; readonly state: string; readonly approvalState: string; readonly version: string; readonly revision: string }
  readonly approval: { readonly submittedBy: string | null; readonly decidedBy: string | null }
  readonly lines: readonly { readonly lineId: string; readonly lineNo: number; readonly quantity: string }[]
}

interface DraftCommandResponse {
  readonly detail: DraftReading
  readonly isReplay: boolean
}

interface QuantityRevisionRequest {
  readonly expectedVersion: string
  readonly reason: string
  readonly lines: readonly { readonly lineId: string; readonly quantity: string }[]
}

interface RevisionEffects {
  readonly archived: number
  readonly reasons: number
  readonly recordedAttempts: number
  readonly refusedAttempts: number
  readonly audits: number
  readonly webAudits: number
}

/** What S04-QR-01 recorded; the later cases replay, refuse and re-read against it. */
const journey: {
  draftId?: string
  lineId?: string
  approvedVersion?: string
  approvedDecidedBy?: string | null
  revisedVersion?: string
  key?: string
  body?: QuantityRevisionRequest
} = {}

test.describe.configure({ mode: 'serial' })

function readHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key }
}

async function readDraft(request: APIRequestContext, actor: string): Promise<DraftReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, { headers: readHeaders(actor) })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const reading = (await response.json()) as DraftReading
  expect(reading.draft.id).toBe(journey.draftId)
  return reading
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? (snapshot.capabilities as string[]) : []
}

async function revise(request: APIRequestContext, actor: string, key: string, body: QuantityRevisionRequest): Promise<APIResponse> {
  return request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/quantity-revision`, { headers: commandHeaders(actor, key), data: body })
}

async function expectProblem(response: APIResponse, status: number, code: string): Promise<void> {
  expect(response.status()).toBe(status)
  expect((await response.json()) as { code?: unknown }).toMatchObject({ code })
}

/** Read-only SQL: the order's archived revisions (and those with a reason), its quantity attempts and its quantity audits. */
async function readEffects(): Promise<RevisionEffects> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, journey.draftId)
      .input('entityId', sql.NVarChar, journey.draftId)
      .input('command', sql.NVarChar, QUANTITY_COMMAND)
      .input('eventType', sql.NVarChar, QUANTITY_EVENT)
      .query<RevisionEffects>(`SELECT
          (SELECT COUNT(*) FROM [sales].[DraftRevisions]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId) AS [archived],
          (SELECT COUNT(*) FROM [sales].[DraftRevisions]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId AND [Reason] IS NOT NULL) AS [reasons],
          (SELECT COUNT(*) FROM [sales].[DraftAttempts]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId AND [CommandName] = @command
               AND [RefusalJson] IS NULL) AS [recordedAttempts],
          (SELECT COUNT(*) FROM [sales].[DraftAttempts]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId AND [CommandName] = @command
               AND [RefusalJson] IS NOT NULL) AS [refusedAttempts],
          (SELECT COUNT(*) FROM [audit].[AuditEvents]
             WHERE [CompanyId] = @companyId AND [EntityId] = @entityId AND [EventType] = @eventType) AS [audits],
          (SELECT COUNT(*) FROM [audit].[AuditEvents]
             WHERE [CompanyId] = @companyId AND [EntityId] = @entityId AND [EventType] = @eventType
               AND [SourceChannel] = N'Web') AS [webAudits]`)
    return result.recordset[0]
  } finally {
    await pool.close()
  }
}

/** Read-only SQL: the archived Rev.0 as recorded (the reason and the approved quantity of its one line). */
async function readArchivedRev0(): Promise<Record<string, unknown> | undefined> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, journey.draftId)
      .query(`SELECT [r].[Reason] AS [reason], [r].[ApprovedBy] AS [approvedBy], [l].[QuantityText] AS [quantity]
          FROM [sales].[DraftRevisions] AS [r]
          INNER JOIN [sales].[DraftRevisionLines] AS [l]
            ON [l].[CompanyId] = [r].[CompanyId] AND [l].[DraftId] = [r].[DraftId] AND [l].[Revision] = [r].[Revision]
          WHERE [r].[CompanyId] = @companyId AND [r].[DraftId] = @draftId AND [r].[Revision] = 0`)
    return result.recordset[0]
  } finally {
    await pool.close()
  }
}

async function openDetail(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

test('S04-QR-01 the preparer revises the approved order\'s quantity with a reason; it reads Rev.1 back in preparation', async ({ page, request }) => {
  test.setTimeout(240_000)

  // The control is server-measured: the preparer holds it (central override delta); the approver and the reader do not.
  expect(await capabilitiesOf(request, preparerActor)).toEqual(expect.arrayContaining(['sales.drafts.detail', QUANTITY_CONTROL]))

  // Arrange through the real API: the preparer creates on the REAL party and product, submits; the approver approves.
  const currencies = await request.get(`${apiBaseUrl}${CURRENCIES_PATH}?effectiveOn=${BUSINESS_DATE}`, { headers: readHeaders(preparerActor) })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as { readonly currencies: readonly { readonly currencyId: string; readonly code: string }[] })
    .currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the approved TRY profile is listed for the seed business date').toBeTruthy()
  const lineId = randomUUID()
  const created = await request.post(`${apiBaseUrl}${DRAFTS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-qr-create-${RUN}`),
    data: {
      kind: 'Order', partyId: realPartyId, companyAccountId: realCompanyAccountId, businessDate: BUSINESS_DATE,
      currencyId: lira!.currencyId,
      lines: [{
        lineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId, quantity: '5',
        unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null, discountPercent: null,
      }],
      requestedDeliveryDate: null, note: null, deliveryAddress: null, contact: null, responsible: null, paymentTerms: null,
      sourceStrategy: null, package: null,
    },
  })
  expect(created.status(), 'the real create stores the order').toBe(201)
  journey.draftId = ((await created.json()) as DraftCommandResponse).detail.draft.id
  journey.lineId = lineId
  const draft = await readDraft(request, preparerActor)
  const submit = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/submission`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-qr-submit-${RUN}`), data: { expectedVersion: draft.draft.version },
  })
  expect(submit.status()).toBe(200)
  const pending = await readDraft(request, approverActor)
  const approve = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/approval`, {
    headers: commandHeaders(approverActor, `SENTETIK-s04-qr-approve-${RUN}`), data: { expectedVersion: pending.draft.version },
  })
  expect(approve.status()).toBe(200)
  const approved = await readDraft(request, preparerActor)
  expect([approved.draft.approvalState, approved.draft.revision, approved.lines[0].quantity]).toEqual(['Approved', 'Rev.0', '5'])
  journey.approvedVersion = approved.draft.version
  journey.approvedDecidedBy = approved.approval.decidedBy
  expect(await readEffects()).toEqual({ archived: 0, reasons: 0, recordedAttempts: 0, refusedAttempts: 0, audits: 0, webAudits: 0 })

  // The preparer opens the order and creates the revision in the browser.
  await openDetail(page, preparerActor)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await page.getByTestId('sales-quantity-revision').getByRole('button', { name: 'Revizyon oluştur' }).click()
  const form = page.getByTestId('sales-quantity-revision-form')
  await expect(form).toBeVisible()
  await form.getByLabel(/^Yeni miktar/).fill('7')
  await form.getByLabel('Revizyon gerekçesi').fill(REASON)
  const sent = page.waitForRequest((candidate) =>
    candidate.method() === 'POST' && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/quantity-revision`)
  const answered = page.waitForResponse((candidate) =>
    candidate.request().method() === 'POST' && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/quantity-revision`)
  await form.getByRole('button', { name: 'Revizyonu kaydet' }).click()
  const request0 = await sent
  const response0 = await answered

  expect(response0.status()).toBe(200)
  expect(response0.headers()['idempotency-replayed']).toBeUndefined()
  const recorded = (await response0.json()) as DraftCommandResponse
  expect([recorded.isReplay, recorded.detail.draft.approvalState, recorded.detail.draft.revision, recorded.detail.lines[0].quantity])
    .toEqual([false, 'Preparing', 'Rev.1', '7'])
  expect(BigInt(recorded.detail.draft.version)).toBe(BigInt(approved.draft.version) + 1n)
  journey.revisedVersion = recorded.detail.draft.version
  journey.key = request0.headers()['idempotency-key']
  expect(journey.key, 'the browser sends one idempotency key per attempt').toBeTruthy()
  journey.body = request0.postDataJSON() as QuantityRevisionRequest
  expect(journey.body).toEqual({ expectedVersion: approved.draft.version, reason: REASON, lines: [{ lineId, quantity: '7' }] })

  // ONE save, as SQL Server holds it: the archived Rev.0 with the reason and its approved quantity, one attempt, one Web audit.
  expect(await readEffects()).toEqual({ archived: 1, reasons: 1, recordedAttempts: 1, refusedAttempts: 0, audits: 1, webAudits: 1 })
  expect(await readArchivedRev0()).toEqual({ reason: REASON, approvedBy: expect.any(String), quantity: '5' })
  await expect(page.getByTestId('sales-draft-revision')).toHaveText('Rev.1')
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Hazırlanıyor')
})

test('S04-QR-02 the same key and body again is the stored answer; a stale version is the key\'s terminal 409', async ({ request }) => {
  expect(journey.key && journey.body, 'S04-QR-01 recorded the revision').toBeTruthy()
  const replay = await revise(request, preparerActor, journey.key!, journey.body!)
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  const body = (await replay.json()) as DraftCommandResponse
  expect([body.isReplay, body.detail.draft.revision, body.detail.draft.version]).toEqual([true, 'Rev.1', journey.revisedVersion])

  const staleKey = `SENTETIK-s04-qr-stale-${RUN}`
  const stale = { ...journey.body!, lines: [{ lineId: journey.lineId!, quantity: '8' }] }
  await expectProblem(await revise(request, preparerActor, staleKey, stale), 409, 'version_conflict')
  const staleReplay = await revise(request, preparerActor, staleKey, stale)
  await expectProblem(staleReplay, 409, 'version_conflict')
  expect(staleReplay.headers()['idempotency-replayed']).toBe('true')
  expect(await readEffects()).toEqual({ archived: 1, reasons: 1, recordedAttempts: 1, refusedAttempts: 1, audits: 1, webAudits: 1 })
  expect((await readDraft(request, preparerActor)).draft.version).toBe(journey.revisedVersion)
})

test('S04-QR-03 Rev.1 is re-approved in the browser; reopen shows Rev.1 approved and the Rev.0 history with its reason', async ({ page, request }) => {
  test.setTimeout(240_000)
  await openDetail(page, preparerActor)
  await page.getByTestId('sales-approval-commands').getByRole('button', { name: 'Onaya gönder' }).click()
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onay bekliyor')

  await openDetail(page, approverActor)
  await page.getByRole('button', { name: 'Onayla' }).click()
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')

  // Close and reopen: Rev.1 approved with the new quantity, and Rev.0 archived with the reason and the quantity after it.
  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, readerActor)
  await expect(page.getByTestId('sales-draft-revision')).toHaveText('Rev.1')
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await expect(page.getByTestId('sales-revision-Rev.0-reason')).toHaveText(REASON)
  await expect(page.getByTestId('sales-revision-Rev.0-line-1')).toContainText('5')
  await expect(page.getByTestId('sales-revision-Rev.0-line-1-next')).toHaveText('7 (değişti)')
  const reopened = await readDraft(request, readerActor)
  expect([reopened.draft.approvalState, reopened.draft.revision, reopened.lines[0].quantity]).toEqual(['Approved', 'Rev.1', '7'])
  expect(reopened.approval.decidedBy).not.toBeNull()
})

test('S04-QR-04 actors without the quantity name see no control and are refused 403; nothing is written', async ({ page, request }) => {
  const before = await readEffects()
  for (const actor of [approverActor, readerActor]) {
    expect(await capabilitiesOf(request, actor), `${actor} must not get the quantity control`).not.toContain(QUANTITY_CONTROL)
    const current = await readDraft(request, actor)
    const refused = await revise(request, actor, `SENTETIK-s04-qr-forbidden-${actor}-${RUN}`,
      { expectedVersion: current.draft.version, reason: REASON, lines: [{ lineId: journey.lineId!, quantity: '9' }] })
    expect(refused.status()).toBe(403)
  }

  await openDetail(page, readerActor)
  await expect(page.getByTestId('sales-quantity-revision')).toHaveCount(0)
  expect(await readEffects()).toEqual(before)
})
