/**
 * Sales s04 — DEC-SAL-005 (KILITLI): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme,
 * faturalanmamış irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir." — "Etki önizlemesi olmadan iptal yok." — and
 * AT-007 ("İptal ve ters işlem geçmişi silmez") against the REAL stack.
 *
 * Real Chromium, the exact-head web build, the real API and SQL Server, the `normal` capability profile. NOTHING IS MOCKED
 * OR INTERCEPTED. The order is the suite's own: the preparer creates it through the real create route on the seed's REAL
 * Cari party and REAL Product candidate (the currency from the real currencies reference), submits it, and the approver
 * approves it — all through the real API. The cancellation itself is made in the browser by the disposable canceller.
 *
 * ACTOR (central delta, not written here): `s04-canceller` — a BootstrapIdentity user with ONE company-wide GDC role holding
 * exactly `sales.drafts.read` + `sales.orders.cancel` (the planner's shape, infra/browser-suites/s04.override.yml). No real
 * user or role receives the name; which persona holds it is a human gate. Until the override carries the actor this file
 * fails at its first capability assertion — it never tests something else.
 *
 * Every effect is proven twice: through the real API and through READ-ONLY SQL counts on the cancellation, attempt, audit and
 * allocation rows. Every schema, column and alias name in the SQL is bracketed.
 *
 * The cases are one serial journey on that order, in file order:
 *   S04-CX-01 the canceller opens the approved order, reads the impact preview (each category from its owner, "kaynak yok"
 *             never zero), cancels with a reason; close/reopen shows "İptal edildi" beside the unchanged approval, with the
 *             recorded reason; one cancellation row, one attempt, one audit event on SourceChannel Web
 *   S04-CX-02 the same key and body again is the stored answer (Idempotency-Replayed), with no second row or event
 *   S04-CX-03 a stale version and a second cancellation are the key's terminal 409s; nothing else is written
 *   S04-CX-04 the next module sees it: an Inventory allocation for the cancelled line is refused (422) and writes nothing;
 *             actors without the name see no control and are refused 403
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const {
  preparerActor, approverActor, readerActor, companyId, realPartyId, realCompanyAccountId,
  productId, productVersion, productUnitId, allocationLabelId, allocationLabelVersion,
} = seed

/** The disposable canceller of this run (central override delta; see the header). */
const cancellerActor = 's04-canceller'
const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const ALLOCATIONS_PATH = '/api/v1/inventory/physical-allocations'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const CANCEL_CONTROL = 'sales.orders.cancel'
const CANCEL_COMMAND = 'sales.order-draft.cancel.v1'
const CANCELLED_EVENT = 'sales.order-draft.cancelled.v1'
/** The seed's own business date on which the approved TRY profile is in effect (s04-references.spec.ts). */
const BUSINESS_DATE = '2026-09-18'
const REASON = 'SENTETIK browser-suite:s04 müşteri siparişten vazgeçti'
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

interface CancelRequest {
  readonly expectedVersion: string
  readonly reason: string
  readonly impactToken: string
}

interface CancellationEffects {
  readonly cancellations: number
  readonly recordedAttempts: number
  readonly refusedAttempts: number
  readonly audits: number
  readonly webAudits: number
  readonly allocations: number
}

/** What S04-CX-01 recorded; the later cases replay, refuse and re-read against it. */
const journey: {
  draftId?: string
  lineId?: string
  approvedVersion?: string
  approvedDecidedBy?: string | null
  cancelledVersion?: string
  key?: string
  body?: CancelRequest
} = {}

test.describe.configure({ mode: 'serial' })

function readHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
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

async function cancel(request: APIRequestContext, actor: string, key: string, body: CancelRequest): Promise<APIResponse> {
  return request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/cancellation`, { headers: commandHeaders(actor, key), data: body })
}

async function expectProblem(response: APIResponse, status: number, code: string): Promise<void> {
  expect(response.status()).toBe(status)
  expect((await response.json()) as { code?: unknown }).toMatchObject({ code })
}

/** Read-only SQL: the order's cancellation rows, its cancel attempts (recorded / refused), its cancel audits and allocations. */
async function readEffects(): Promise<CancellationEffects> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, journey.draftId)
      .input('entityId', sql.NVarChar, journey.draftId)
      .input('command', sql.NVarChar, CANCEL_COMMAND)
      .input('eventType', sql.NVarChar, CANCELLED_EVENT)
      .query<CancellationEffects>(`SELECT
          (SELECT COUNT(*) FROM [sales].[DraftCancellations]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId) AS [cancellations],
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
               AND [SourceChannel] = N'Web') AS [webAudits],
          (SELECT COUNT(*) FROM [inventory].[Allocations]
             WHERE [CompanyId] = @companyId AND [DemandId] = @draftId) AS [allocations]`)
    return result.recordset[0]
  } finally {
    await pool.close()
  }
}

/** Read-only SQL: the stored cancellation row as recorded. */
async function readCancellationRow(): Promise<Record<string, unknown> | undefined> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, journey.draftId)
      .query(`SELECT [Reason] AS [reason], [CancelledBy] AS [cancelledBy], [Revision] AS [revision],
            CAST([DraftVersion] AS nvarchar(20)) AS [draftVersion], [ImpactToken] AS [impactToken],
            (SELECT [State] FROM [sales].[Drafts] WHERE [CompanyId] = @companyId AND [Id] = @draftId) AS [state]
          FROM [sales].[DraftCancellations]
          WHERE [CompanyId] = @companyId AND [DraftId] = @draftId`)
    return result.recordset[0]
  } finally {
    await pool.close()
  }
}

async function openDetail(page: Page, actor: string): Promise<void> {
  await signInAs(page, actor)
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

test('S04-CX-01 the canceller previews the approved order\'s impact, cancels with a reason; reopen shows Cancelled with its record', async ({ page, request }) => {
  test.setTimeout(240_000)

  // The control is server-measured: only the canceller holds it, and the canceller holds nothing else of Sales.
  const cancellerCapabilities = await capabilitiesOf(request, cancellerActor)
  expect(cancellerCapabilities).toEqual(expect.arrayContaining(['sales.drafts.list', 'sales.drafts.detail', CANCEL_CONTROL]))
  for (const other of ['sales.drafts.create', 'sales.orders.approve', 'sales.orders.price.write', 'sales.orders.delivery-schedule.write']) {
    expect(cancellerCapabilities).not.toContain(other)
  }

  // Arrange through the real API: the preparer creates on the REAL party and product, submits; the approver approves.
  const currencies = await request.get(`${apiBaseUrl}${CURRENCIES_PATH}?effectiveOn=${BUSINESS_DATE}`, { headers: readHeaders(preparerActor) })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as { readonly currencies: readonly { readonly currencyId: string; readonly code: string }[] })
    .currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the approved TRY profile is listed for the seed business date').toBeTruthy()
  const lineId = randomUUID()
  const created = await request.post(`${apiBaseUrl}${DRAFTS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-cx-create-${RUN}`),
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
  expect([draft.draft.state, draft.draft.approvalState]).toEqual(['Draft', 'Preparing'])
  const submit = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/submission`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-cx-submit-${RUN}`), data: { expectedVersion: draft.draft.version },
  })
  expect(submit.status()).toBe(200)
  const pending = await readDraft(request, approverActor)
  const approve = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/approval`, {
    headers: commandHeaders(approverActor, `SENTETIK-s04-cx-approve-${RUN}`), data: { expectedVersion: pending.draft.version },
  })
  expect(approve.status()).toBe(200)
  const approved = await readDraft(request, cancellerActor)
  expect([approved.draft.state, approved.draft.approvalState]).toEqual(['Draft', 'Approved'])
  journey.approvedVersion = approved.draft.version
  journey.approvedDecidedBy = approved.approval.decidedBy
  expect(await readEffects()).toEqual({ cancellations: 0, recordedAttempts: 0, refusedAttempts: 0, audits: 0, webAudits: 0, allocations: 0 })

  // The canceller opens the order: nothing is read or offered before the preview is asked for.
  await openDetail(page, cancellerActor)
  const section = page.getByTestId('sales-cancellation')
  await expect(section).toBeVisible()
  await expect(section.getByTestId('sales-cancellation-submit')).toHaveCount(0)
  const previewRead = page.waitForResponse((candidate) =>
    candidate.request().method() === 'GET' && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/cancellation-impact`)
  await section.getByRole('button', { name: 'İptal etkisini göster' }).click()
  expect((await previewRead).status()).toBe(200)

  // Each category as its owner answered: Inventory and Logistics read "nothing recorded"; the four without an owner on main
  // read "kaynak yok" — never 0.
  await expect(page.getByTestId('sales-cancellation-source-inventory.active-allocations')).toHaveAttribute('data-status', 'None')
  await expect(page.getByTestId('sales-cancellation-source-logistics.loading-without-dispatch-note')).toHaveAttribute('data-status', 'None')
  for (const source of ['purchasing.linked-orders', 'manufacturing.linked-orders', 'invoicing.uninvoiced-dispatch-notes', 'finance.unmatched-advances']) {
    await expect(page.getByTestId(`sales-cancellation-source-${source}`)).toHaveAttribute('data-status', 'NoSource')
    await expect(page.getByTestId(`sales-cancellation-source-${source}`)).toContainText('Kaynak yok')
  }
  await expect(page.getByTestId('sales-cancellation-line-1-allocations')).toHaveText('0')

  await page.getByLabel('İptal nedeni (zorunlu)').fill(REASON)
  const sent = page.waitForRequest((candidate) =>
    candidate.method() === 'POST' && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/cancellation`)
  const answered = page.waitForResponse((candidate) =>
    candidate.request().method() === 'POST' && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/cancellation`)
  await page.getByTestId('sales-cancellation-submit').click()
  const request0 = await sent
  const response0 = await answered

  expect(response0.status()).toBe(200)
  expect(response0.headers()['idempotency-replayed']).toBeUndefined()
  const recorded = (await response0.json()) as DraftCommandResponse
  expect([recorded.isReplay, recorded.detail.draft.state, recorded.detail.draft.approvalState]).toEqual([false, 'Cancelled', 'Approved'])
  expect(BigInt(recorded.detail.draft.version)).toBe(BigInt(approved.draft.version) + 1n)
  journey.cancelledVersion = recorded.detail.draft.version
  journey.key = request0.headers()['idempotency-key']
  expect(journey.key, 'the browser sends one idempotency key per attempt').toBeTruthy()
  journey.body = request0.postDataJSON() as CancelRequest
  expect([journey.body.expectedVersion, journey.body.reason]).toEqual([approved.draft.version, REASON])
  expect(journey.body.impactToken).toMatch(/^[0-9a-f]{64}$/)

  // ONE save, as SQL Server holds it: the row with the reason, one recorded attempt, one audit event on the Web channel.
  expect(await readEffects()).toEqual({ cancellations: 1, recordedAttempts: 1, refusedAttempts: 0, audits: 1, webAudits: 1, allocations: 0 })
  expect(await readCancellationRow()).toEqual({
    reason: REASON, cancelledBy: expect.any(String), revision: 0, draftVersion: journey.cancelledVersion,
    impactToken: journey.body.impactToken, state: 2,
  })

  // Close and reopen: Cancelled beside the unchanged approval, the recorded reason, and no cancel control anymore.
  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, cancellerActor)
  await expect(page.getByTestId('sales-draft-lifecycle-state')).toHaveText('İptal edildi')
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await expect(page.getByTestId('sales-cancellation-reason-text')).toHaveText(REASON)
  await expect(page.getByTestId('sales-cancellation')).toHaveCount(0)
  const reopened = await readDraft(request, cancellerActor)
  expect([reopened.draft.state, reopened.approval.decidedBy]).toEqual(['Cancelled', journey.approvedDecidedBy])
})

test('S04-CX-02 the same key and body again is the stored answer with no second row or event', async ({ request }) => {
  expect(journey.key && journey.body, 'S04-CX-01 recorded the cancellation').toBeTruthy()
  const replay = await cancel(request, cancellerActor, journey.key!, journey.body!)
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  const body = (await replay.json()) as DraftCommandResponse
  expect([body.isReplay, body.detail.draft.state, body.detail.draft.version]).toEqual([true, 'Cancelled', journey.cancelledVersion])
  expect(await readEffects()).toEqual({ cancellations: 1, recordedAttempts: 1, refusedAttempts: 0, audits: 1, webAudits: 1, allocations: 0 })
})

test('S04-CX-03 a stale version and a second cancellation are the key\'s terminal 409s; nothing else is written', async ({ request }) => {
  const staleKey = `SENTETIK-s04-cx-stale-${RUN}`
  const stale = { ...journey.body!, expectedVersion: journey.approvedVersion! }
  await expectProblem(await cancel(request, cancellerActor, staleKey, stale), 409, 'version_conflict')
  const staleReplay = await cancel(request, cancellerActor, staleKey, stale)
  await expectProblem(staleReplay, 409, 'version_conflict')
  expect(staleReplay.headers()['idempotency-replayed']).toBe('true')

  await expectProblem(await cancel(request, cancellerActor, `SENTETIK-s04-cx-again-${RUN}`,
    { ...journey.body!, expectedVersion: journey.cancelledVersion! }), 409, 'sales_state_conflict')
  expect(await readEffects()).toEqual({ cancellations: 1, recordedAttempts: 1, refusedAttempts: 2, audits: 1, webAudits: 1, allocations: 0 })
  expect((await readDraft(request, cancellerActor)).draft.version).toBe(journey.cancelledVersion)
})

test('S04-CX-04 the next module sees it: an allocation for the cancelled line is refused; actors without the name are refused', async ({ request }) => {
  // Inventory asks the Sales line port for an APPROVED demand: a cancelled order's line is not one (422, nothing written).
  const allocation = await request.post(`${apiBaseUrl}${ALLOCATIONS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-cx-allocation-${RUN}`),
    data: {
      allocationId: randomUUID(), salesOrderId: journey.draftId, salesOrderLineId: journey.lineId,
      labelId: allocationLabelId, expectedLabelVersion: allocationLabelVersion, quantityKg: '1',
      reason: 'SENTETIK browser-suite:s04 iptal edilen siparişe tahsis denemesi', reservedUntilUtc: null,
    },
  })
  await expectProblem(allocation, 422, 'inventory_allocation_invalid_reference')
  expect((await readEffects()).allocations).toBe(0)

  // No other s04 actor holds the cancel name: no control, and the preview and the command are 403 before anything is read.
  for (const actor of [preparerActor, approverActor, readerActor]) {
    expect(await capabilitiesOf(request, actor), `${actor} must not get the cancel control`).not.toContain(CANCEL_CONTROL)
    const preview = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/cancellation-impact`, { headers: readHeaders(actor) })
    expect(preview.status()).toBe(403)
    const refused = await cancel(request, actor, `SENTETIK-s04-cx-forbidden-${actor}-${RUN}`, journey.body!)
    expect(refused.status()).toBe(403)
  }

  // The record stays readable under the draft read (history is not deleted).
  const record = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/cancellation`, { headers: readHeaders(readerActor) })
  expect(record.status()).toBe(200)
  expect(((await record.json()) as { cancellation: { reason: string } }).cancellation.reason).toBe(REASON)
  expect(await readEffects()).toEqual({ cancellations: 1, recordedAttempts: 1, refusedAttempts: 2, audits: 1, webAudits: 1, allocations: 0 })
})
