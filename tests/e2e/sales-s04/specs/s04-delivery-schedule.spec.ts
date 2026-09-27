/**
 * Sales s04 — DEC-SAL-003 (KILITLI): "Termin üst bilgi, satır ve miktar dilimleriyle izlenir; termin güncellemesi
 * planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir." — against the REAL stack.
 *
 * Real Chromium, the exact-head web build, the real API and SQL Server, the `normal` capability profile. The seed
 * (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04) writes ONE separate one-line Preparing draft
 * (`scheduleDraftId`) and names the disposable planner (`plannerActor`). By the user's decision (2026-09-27) the
 * planner is the ONLY holder of `sales.orders.delivery-schedule.write`, and only in this per-run database; it also
 * holds `sales.drafts.read` and nothing else. No real user or role receives the name.
 *
 * NOTHING IS MOCKED OR INTERCEPTED. The arrangement (submit, approve) goes through the real API as the preparer and
 * the approver; the update itself is made in the browser by the planner. Every effect is proven twice: through the
 * real API as the acting actor, and through READ-ONLY SQL counts on the schedule, attempt and audit rows. Every
 * schema, column and alias name in the SQL is bracketed (`[sales].[…]`, `[LineNo]` and the `lineNo` alias are reserved words).
 *
 * The cases are one serial journey on that draft, in file order:
 *   S04-DS-01 the planner moves the dates of the APPROVED order in the form; the server confirms, the order stays
 *             approved (no re-approval), and a close/reopen shows the new dates and exactly one recorded change
 *   S04-DS-02 the same key and body again is the stored answer (Idempotency-Replayed), with no second change row
 *   S04-DS-03 a stale expected version is refused 409 version_conflict (terminal: its retry replays the refusal)
 *   S04-DS-04 actors without the name see no control and are refused 403; nothing is written
 */

import { expect, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const { plannerActor, preparerActor, approverActor, readerActor, companyId, scheduleDraftId } = seed

const DRAFT_PATH = `/api/v1/sales/drafts/${scheduleDraftId}`
const SCHEDULE_PATH = `${DRAFT_PATH}/delivery-schedule`
const SCHEDULES_PATH = `${DRAFT_PATH}/delivery-schedules`
const DETAIL_ROUTE = `/satis/siparisler/${scheduleDraftId}`
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const SCHEDULE_CONTROL = 'sales.orders.delivery-schedule.write'
const RESCHEDULE_COMMAND = 'sales.order-draft.reschedule-delivery.v1'
const RESCHEDULED_EVENT = 'sales.order-draft.delivery-rescheduled.v1'
const HEADER_DATE = '2026-10-15'
const LINE_DATE = '2026-10-20'
const STALE_HEADER_DATE = '2026-10-30'
const RUN = testedCommit.slice(0, 12)

interface DraftLine {
  readonly lineId: string
  readonly lineNo: number
  readonly requestedDeliveryDate: string | null
  readonly requestedDeliverySlices?: readonly { readonly sliceId: string; readonly requestedDeliveryDate: string }[]
}

interface DraftReading {
  readonly draft: { readonly id: string; readonly approvalState: string; readonly version: string; readonly revision: string }
  readonly approval: { readonly submittedBy: string | null; readonly decidedBy: string | null }
  readonly requestedDeliveryDate: string | null
  readonly lines: readonly DraftLine[]
}

interface DraftCommandResponse {
  readonly detail: DraftReading
  readonly isReplay: boolean
}

interface ScheduleRequest {
  readonly expectedVersion: string
  readonly requestedDeliveryDate: string | null
  readonly lines: readonly {
    readonly lineId: string
    readonly requestedDeliveryDate: string | null
    readonly slices: readonly { readonly sliceId: string; readonly requestedDeliveryDate: string }[]
  }[]
}

interface ScheduleHistory {
  readonly items: readonly {
    readonly scheduleNo: number
    readonly changedBy: string | null
    readonly changedAtUtc: string | null
    readonly requestedDeliveryDate: string | null
    readonly lines: readonly { readonly lineNo: number; readonly requestedDeliveryDate: string | null }[]
  }[]
}

interface ScheduleEffects {
  readonly schedules: number
  readonly scheduleDates: number
  readonly recordedAttempts: number
  readonly refusedAttempts: number
  readonly audits: number
}

const NOTHING: ScheduleEffects = { schedules: 0, scheduleDates: 0, recordedAttempts: 0, refusedAttempts: 0, audits: 0 }
const ONE_CHANGE: ScheduleEffects = { schedules: 1, scheduleDates: 1, recordedAttempts: 1, refusedAttempts: 0, audits: 1 }
const ONE_CHANGE_ONE_REFUSAL: ScheduleEffects = { ...ONE_CHANGE, refusedAttempts: 1 }

/** What S04-DS-01 recorded; the later cases replay, refuse and re-read against it. */
const journey: {
  approvedVersion?: string
  approvedSubmittedBy?: string | null
  approvedDecidedBy?: string | null
  rescheduledVersion?: string
  key?: string
  body?: ScheduleRequest
  correlation?: string
} = {}

test.describe.configure({ mode: 'serial' })

function readHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string, correlation?: string): Record<string, string> {
  return {
    ...readHeaders(actor),
    'Idempotency-Key': key,
    ...(correlation === undefined ? {} : { 'X-Correlation-Id': correlation }),
  }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function readDraft(request: APIRequestContext, actor: string): Promise<DraftReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFT_PATH}`, { headers: readHeaders(actor) })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const reading = (await response.json()) as DraftReading
  expect(reading.draft.id).toBe(scheduleDraftId)
  return reading
}

async function readHistory(request: APIRequestContext, actor: string): Promise<ScheduleHistory> {
  const response = await request.get(`${apiBaseUrl}${SCHEDULES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status(), `the real schedule history answers ${actor}`).toBe(200)
  return (await response.json()) as ScheduleHistory
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? (snapshot.capabilities as string[]) : []
}

async function stateCommand(
  request: APIRequestContext,
  actor: string,
  kind: 'submission' | 'approval',
  expectedVersion: string,
): Promise<APIResponse> {
  return request.post(`${apiBaseUrl}${DRAFT_PATH}/${kind}`, {
    headers: commandHeaders(actor, `SENTETIK-s04-ds-${kind}-${RUN}`),
    data: { expectedVersion },
  })
}

async function reschedule(
  request: APIRequestContext,
  actor: string,
  key: string,
  body: ScheduleRequest,
  correlation?: string,
): Promise<APIResponse> {
  return request.post(`${apiBaseUrl}${SCHEDULE_PATH}`, { headers: commandHeaders(actor, key, correlation), data: body })
}

async function expectProblem(response: APIResponse, status: number, code: string): Promise<void> {
  expect(response.status()).toBe(status)
  expect((await response.json()) as { code?: unknown }).toMatchObject({ code })
}

/** Read-only SQL: the draft's schedule rows, its reschedule attempts (recorded / refused) and its reschedule audits. */
async function readEffects(): Promise<ScheduleEffects> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, scheduleDraftId)
      .input('entityId', sql.NVarChar, scheduleDraftId)
      .input('command', sql.NVarChar, RESCHEDULE_COMMAND)
      .input('eventType', sql.NVarChar, RESCHEDULED_EVENT)
      .query<ScheduleEffects>(`SELECT
          (SELECT COUNT(*) FROM [sales].[DraftDeliverySchedules]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId) AS [schedules],
          (SELECT COUNT(*) FROM [sales].[DraftDeliveryScheduleDates]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId) AS [scheduleDates],
          (SELECT COUNT(*) FROM [sales].[DraftAttempts]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId AND [CommandName] = @command
               AND [RefusalJson] IS NULL) AS [recordedAttempts],
          (SELECT COUNT(*) FROM [sales].[DraftAttempts]
             WHERE [CompanyId] = @companyId AND [DraftId] = @draftId AND [CommandName] = @command
               AND [RefusalJson] IS NOT NULL) AS [refusedAttempts],
          (SELECT COUNT(*) FROM [audit].[AuditEvents]
             WHERE [CompanyId] = @companyId AND [EntityId] = @entityId AND [EventType] = @eventType) AS [audits]`)
    return result.recordset[0]
  } finally {
    await pool.close()
  }
}

/** Read-only SQL: the one recorded change's own row and its date rows, as stored. */
async function readRecordedChange(): Promise<{
  readonly schedule: readonly Record<string, unknown>[]
  readonly dates: readonly Record<string, unknown>[]
}> {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const schedule = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, scheduleDraftId)
      .query(`SELECT [ScheduleNo] AS [scheduleNo],
            CONVERT(char(10), [RequestedDeliveryDate], 23) AS [requestedDeliveryDate],
            [ChangedBy] AS [changedBy], CAST([DraftVersion] AS nvarchar(20)) AS [draftVersion]
          FROM [sales].[DraftDeliverySchedules]
          WHERE [CompanyId] = @companyId AND [DraftId] = @draftId
          ORDER BY [ScheduleNo]`)
    const dates = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('draftId', sql.UniqueIdentifier, scheduleDraftId)
      .query(`SELECT [ScheduleNo] AS [scheduleNo], [LineNo] AS [lineNo], [SliceNo] AS [sliceNo],
            CONVERT(char(10), [RequestedDeliveryDate], 23) AS [requestedDeliveryDate]
          FROM [sales].[DraftDeliveryScheduleDates]
          WHERE [CompanyId] = @companyId AND [DraftId] = @draftId
          ORDER BY [ScheduleNo], [LineNo], [SliceNo]`)
    return { schedule: schedule.recordset, dates: dates.recordset }
  } finally {
    await pool.close()
  }
}

async function openDetail(page: Page, actor: string): Promise<void> {
  await signInAs(page, actor)
  await page.goto(DETAIL_ROUTE)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

const scheduleForm = (page: Page) => page.getByTestId('sales-delivery-schedule-form')
const historyBlock = (page: Page) => page.getByTestId('sales-delivery-schedule-history')
/** Every schedule section of the history (schedule 0 as created, then each recorded change). */
const historySections = (page: Page) => historyBlock(page).locator('section[data-testid^="sales-delivery-schedule-"]')

test('S04-DS-01 the planner moves the approved order\'s dates in the form; no re-approval; reopen shows the new dates and one recorded change', async ({ page, request }) => {
  test.setTimeout(240_000)

  // The control is server-measured: only the planner holds it, and the planner holds nothing else of Sales.
  const plannerCapabilities = await capabilitiesOf(request, plannerActor)
  expect(plannerCapabilities).toEqual(expect.arrayContaining(['sales.drafts.list', 'sales.drafts.detail', SCHEDULE_CONTROL]))
  for (const other of ['sales.drafts.create', 'sales.orders.approve', 'sales.orders.price.write']) {
    expect(plannerCapabilities).not.toContain(other)
  }
  for (const actor of [preparerActor, approverActor, readerActor]) {
    expect(await capabilitiesOf(request, actor), `${actor} must not get the delivery-schedule control`).not.toContain(SCHEDULE_CONTROL)
  }

  // Arrange through the real API: the preparer submits, the approver approves (DEC-SAL-003 moves an APPROVED order).
  const seeded = await readDraft(request, preparerActor)
  expect([seeded.draft.approvalState, seeded.requestedDeliveryDate, seeded.lines.length]).toEqual(['Preparing', null, 1])
  expect((await stateCommand(request, preparerActor, 'submission', seeded.draft.version)).status()).toBe(200)
  const pending = await readDraft(request, approverActor)
  expect(pending.draft.approvalState).toBe('PendingApproval')
  expect((await stateCommand(request, approverActor, 'approval', pending.draft.version)).status()).toBe(200)
  const approved = await readDraft(request, plannerActor)
  expect(approved.draft.approvalState).toBe('Approved')
  expect(approved.approval.decidedBy).toBeTruthy()
  journey.approvedVersion = approved.draft.version
  journey.approvedSubmittedBy = approved.approval.submittedBy
  journey.approvedDecidedBy = approved.approval.decidedBy
  const line = approved.lines[0]
  expect([line.lineNo, line.requestedDeliveryDate, line.requestedDeliverySlices ?? []]).toEqual([1, null, []])

  // Nothing is scheduled yet: schedule 0 only, no row, attempt or audit.
  expect(await readEffects()).toEqual(NOTHING)
  expect((await readHistory(request, plannerActor)).items.map((item) => item.scheduleNo)).toEqual([0])

  // The planner opens the approved order; the form is there, the history shows only the dates as created.
  await openDetail(page, plannerActor)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await expect(scheduleForm(page)).toBeVisible()
  await expect(historyBlock(page)).toBeVisible()
  await expect(page.getByTestId('sales-delivery-schedule-0')).toBeVisible()
  await expect(historySections(page)).toHaveCount(1)

  await scheduleForm(page).getByLabel('Üst termin tarihi').fill(HEADER_DATE)
  await scheduleForm(page).getByLabel('Satır termini').fill(LINE_DATE)
  const sent = page.waitForRequest((candidate) =>
    candidate.method() === 'POST' && new URL(candidate.url()).pathname === SCHEDULE_PATH)
  const answered = page.waitForResponse((candidate) =>
    candidate.request().method() === 'POST' && new URL(candidate.url()).pathname === SCHEDULE_PATH)
  await scheduleForm(page).getByRole('button', { name: 'Terminleri kaydet' }).click()
  const request0 = await sent
  const response0 = await answered

  // The server confirms: 200, not a replay, still Approved, the dates moved, the version advanced by one step.
  expect(response0.status()).toBe(200)
  expect(response0.headers()['idempotency-replayed']).toBeUndefined()
  const recorded = (await response0.json()) as DraftCommandResponse
  expect(recorded.isReplay).toBe(false)
  expect(recorded.detail.draft.approvalState).toBe('Approved')
  expect(recorded.detail.requestedDeliveryDate).toBe(HEADER_DATE)
  expect(recorded.detail.lines.map((item) => item.requestedDeliveryDate)).toEqual([LINE_DATE])
  expect(BigInt(recorded.detail.draft.version)).toBe(BigInt(approved.draft.version) + 1n)
  await expect(scheduleForm(page).getByText('Termin güncellemesi kaydedildi; sipariş yeniden onaya girmedi.')).toBeVisible()

  const key = request0.headers()['idempotency-key']
  expect(key, 'the browser sends one idempotency key per attempt').toBeTruthy()
  journey.key = key
  journey.body = request0.postDataJSON() as ScheduleRequest
  expect(journey.body).toEqual({
    expectedVersion: approved.draft.version,
    requestedDeliveryDate: HEADER_DATE,
    lines: [{ lineId: line.lineId, requestedDeliveryDate: LINE_DATE, slices: [] }],
  })
  journey.correlation = response0.headers()['x-correlation-id']
  journey.rescheduledVersion = recorded.detail.draft.version

  // No re-approval: the decision facts are those of the approval, re-read through the real API.
  const after = await readDraft(request, plannerActor)
  expect([after.draft.approvalState, after.approval.decidedBy, after.draft.revision])
    .toEqual(['Approved', journey.approvedDecidedBy, approved.draft.revision])
  expect([after.requestedDeliveryDate, after.lines[0].requestedDeliveryDate]).toEqual([HEADER_DATE, LINE_DATE])

  // Close and reopen: the new dates and exactly ONE recorded change beside the dates as created.
  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, plannerActor)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await expect(page.getByTestId('sales-draft-header-due')).toHaveText('15.10.2026')
  await expect(scheduleForm(page).getByLabel('Üst termin tarihi')).toHaveValue(HEADER_DATE)
  await expect(scheduleForm(page).getByLabel('Satır termini')).toHaveValue(LINE_DATE)
  await expect(historySections(page)).toHaveCount(2)
  await expect(page.getByTestId('sales-delivery-schedule-0-header')).toHaveText('Yok')
  await expect(page.getByTestId('sales-delivery-schedule-1-header')).toHaveText('15.10.2026')
  await expect(page.getByTestId('sales-delivery-schedule-1-line-1')).toContainText('20.10.2026')
  await expect(page.getByTestId('sales-delivery-schedule-2')).toHaveCount(0)

  // The change is attributed to the planner's own identity: neither the submitter's nor the approver's.
  const history = await readHistory(request, plannerActor)
  const plannerIdentity = history.items[1]?.changedBy ?? null
  expect(plannerIdentity).toBeTruthy()
  expect([journey.approvedSubmittedBy, journey.approvedDecidedBy]).not.toContain(plannerIdentity)
  expect(history.items.map((item) => [item.scheduleNo, item.changedBy, item.requestedDeliveryDate])).toEqual([
    [0, null, null],
    [1, plannerIdentity, HEADER_DATE],
  ])
  expect(history.items[1].lines.map((item) => [item.lineNo, item.requestedDeliveryDate])).toEqual([[1, LINE_DATE]])

  // SQL: one schedule row by the planner at the new version, one date row (the line; the line has no slice), one
  // recorded attempt and one audit event.
  expect(await readEffects()).toEqual(ONE_CHANGE)
  const stored = await readRecordedChange()
  expect(stored.schedule).toEqual([
    { scheduleNo: 1, requestedDeliveryDate: HEADER_DATE, changedBy: plannerIdentity, draftVersion: journey.rescheduledVersion },
  ])
  expect(stored.dates).toEqual([{ scheduleNo: 1, lineNo: 1, sliceNo: 0, requestedDeliveryDate: LINE_DATE }])
})

test('S04-DS-02 the same key and body again replays the stored answer and records no second change', async ({ request }) => {
  const { key, body, rescheduledVersion, correlation } = journey
  expect(key && body && rescheduledVersion, 'S04-DS-01 recorded the first attempt').toBeTruthy()

  const replay = await reschedule(request, plannerActor, key!, body!, `SENTETIK-s04-ds-replay-${RUN}`)
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  if (correlation !== undefined) {
    expect(replay.headers()['idempotency-original-correlation-id']).toBe(correlation)
  }
  const replayed = (await replay.json()) as DraftCommandResponse
  expect(replayed.isReplay).toBe(true)
  expect([replayed.detail.draft.version, replayed.detail.requestedDeliveryDate]).toEqual([rescheduledVersion, HEADER_DATE])

  // The same key with ANOTHER body is not a second change either: it is the key's mismatch refusal.
  const mismatch = await reschedule(request, plannerActor, key!, { ...body!, requestedDeliveryDate: STALE_HEADER_DATE })
  await expectProblem(mismatch, 409, 'same_key_payload_mismatch')

  expect((await readHistory(request, plannerActor)).items.map((item) => item.scheduleNo)).toEqual([0, 1])
  expect((await readDraft(request, plannerActor)).draft.version).toBe(rescheduledVersion)
  expect(await readEffects()).toEqual(ONE_CHANGE)
})

test('S04-DS-03 a stale expected version is refused 409 version_conflict, terminally, and moves no date', async ({ request }) => {
  const { body, approvedVersion, rescheduledVersion } = journey
  expect(body && approvedVersion && rescheduledVersion, 'S04-DS-01 recorded the first attempt').toBeTruthy()
  const stale: ScheduleRequest = { ...body!, expectedVersion: approvedVersion!, requestedDeliveryDate: STALE_HEADER_DATE }
  const staleKey = `SENTETIK-s04-ds-stale-${RUN}`
  const staleCorrelation = `SENTETIK-s04-ds-stale-correlation-${RUN}`

  const refused = await reschedule(request, plannerActor, staleKey, stale, staleCorrelation)
  await expectProblem(refused, 409, 'version_conflict')
  expect(refused.headers()['idempotency-replayed']).toBeUndefined()

  // A version conflict is the key's terminal answer: the same key replays the refusal, it is not re-evaluated.
  const again = await reschedule(request, plannerActor, staleKey, stale, `SENTETIK-s04-ds-stale-again-${RUN}`)
  await expectProblem(again, 409, 'version_conflict')
  expect(again.headers()['idempotency-replayed']).toBe('true')
  expect(again.headers()['idempotency-original-correlation-id']).toBe(staleCorrelation)

  const current = await readDraft(request, plannerActor)
  expect([current.draft.version, current.requestedDeliveryDate, current.draft.approvalState])
    .toEqual([rescheduledVersion, HEADER_DATE, 'Approved'])
  expect((await readHistory(request, plannerActor)).items.map((item) => item.scheduleNo)).toEqual([0, 1])
  expect(await readEffects()).toEqual(ONE_CHANGE_ONE_REFUSAL)
})

test('S04-DS-04 actors without the name see no control and are refused 403; nothing is written', async ({ page, request }) => {
  const { body, rescheduledVersion } = journey
  expect(body && rescheduledVersion, 'S04-DS-01 recorded the first attempt').toBeTruthy()
  const current: ScheduleRequest = { ...body!, expectedVersion: rescheduledVersion!, requestedDeliveryDate: STALE_HEADER_DATE }

  for (const actor of [readerActor, approverActor, preparerActor]) {
    // The screen: the detail and its schedule history are readable (sales.drafts.read), the control is absent.
    await openDetail(page, actor)
    await expect(page.getByTestId('sales-draft-header-due')).toHaveText('15.10.2026')
    await expect(historySections(page)).toHaveCount(2)
    await expect(scheduleForm(page)).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Terminleri kaydet' })).toHaveCount(0)

    // The API measures the grant afresh: 403, even with the current version and a real change.
    await expectProblem(await reschedule(request, actor, `SENTETIK-s04-ds-denied-${actor}-${RUN}`, current), 403, 'forbidden')
  }

  const unchanged = await readDraft(request, plannerActor)
  expect([unchanged.draft.version, unchanged.requestedDeliveryDate]).toEqual([rescheduledVersion, HEADER_DATE])
  expect((await readHistory(request, readerActor)).items.map((item) => item.scheduleNo)).toEqual([0, 1])
  expect(await readEffects()).toEqual(ONE_CHANGE_ONE_REFUSAL)
})
