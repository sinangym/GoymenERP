/**
 * Sales s04 — SALES-A1 user-flow acceptance against the REAL stack (`browser:s04:ci`).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the `normal`
 * capability profile. ONE draft, seeded Preparing with two lines by the stage-2 fixture (synthetic owner
 * snapshots, no attempt, no audit — tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04); every
 * transition below is a real command through the screens, and every step is re-read through the real API
 * as the acting actor. NOTHING IS MOCKED OR INTERCEPTED: the only thing the suite adds to the browser is
 * the actor's identity header (Playwright's extra headers replace the page's own `X-Development-User` on
 * every request — measured in s03; the API still authorizes the header itself).
 *
 * The cases are one serial journey on that draft, in file order:
 *   S04-01 preparer submits (Preparing → PendingApproval); a double click makes ONE transition
 *   S04-02 approver rejects with a reason (→ Rejected); the preparer resubmits (→ PendingApproval)
 *   S04-03 two approver tabs: one approves (→ Approved); the stale one is refused 409 and re-reads
 *   S04-04 the reader sees the facts and no command
 *   S04-05 the S-02 list filters by the server's approvalState
 */

import { expect, test, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { preparerActor, approverActor, readerActor, companyId, draftId,
  allocationLineId, allocationLabelId, allocationLabelVersion } = env.seed
const DRAFTS_PATH = '/api/v1/sales/drafts'
const DRAFT_PATH = `${DRAFTS_PATH}/${draftId}`
const DETAIL_ROUTE = `/satis/siparisler/${draftId}`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

interface DraftReading {
  readonly draft: { readonly id: string; readonly approvalState: string; readonly version: string; readonly lineCount: number }
  readonly approval: {
    readonly submittedBy: string | null
    readonly decidedBy: string | null
    readonly rejectionReason: string | null
  }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** The draft as the real API answers it to `actor` (the browser's own company header). */
async function readDraft(page: Page, actor: string): Promise<DraftReading> {
  const response = await page.request.get(`${env.apiBaseUrl}${DRAFT_PATH}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const reading = (await response.json()) as DraftReading
  expect(reading.draft.id).toBe(draftId)
  return reading
}

/** Collects the browser's own answers on one state command route of the draft. */
function watchCommand(page: Page, command: 'submission' | 'approval' | 'rejection'): Response[] {
  const answers: Response[] = []
  page.on('response', (response) => {
    const request = response.request()
    if (request.method() === 'POST' && new URL(response.url()).pathname === `${DRAFT_PATH}/${command}`) answers.push(response)
  })
  return answers
}

async function openDraft(page: Page): Promise<void> {
  await page.goto(DETAIL_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: /SENTETIK browser-suite:s04/ })).toBeVisible({ timeout: 30_000 })
}

function state(page: Page) {
  return page.getByTestId('sales-draft-approval-state')
}

function notice(page: Page) {
  return page.getByTestId('sales-approval-notice')
}

const approvalCommands = (page: Page) => page.getByTestId('sales-approval-commands')
const submitButton = (page: Page) => approvalCommands(page).getByRole('button', { name: 'Onaya gönder' })
const approveButton = (page: Page) => approvalCommands(page).getByRole('button', { name: 'Onayla' })
const rejectButton = (page: Page) => approvalCommands(page).getByRole('button', { name: 'Reddet' })

async function expectNoCommand(page: Page): Promise<void> {
  await expect(submitButton(page)).toHaveCount(0)
  await expect(approveButton(page)).toHaveCount(0)
  await expect(rejectButton(page)).toHaveCount(0)
}

test('S04-01 the preparer submits the seeded draft; a double click makes exactly one transition', async ({ page }) => {
  await signInAs(page, preparerActor)
  const before = await readDraft(page, preparerActor)
  expect([before.draft.approvalState, before.draft.version, before.draft.lineCount]).toEqual(['Preparing', '3', 2])

  const submissions = watchCommand(page, 'submission')
  await openDraft(page)
  await expect(state(page)).toHaveText('Hazırlanıyor')
  await expect(page.getByTestId('sales-line-1')).toBeVisible()
  await expect(page.getByTestId('sales-line-2')).toBeVisible()
  await expect(submitButton(page)).toBeVisible()
  await expect(approveButton(page)).toHaveCount(0)
  await expect(rejectButton(page)).toHaveCount(0)

  // Two clicks in the same tick: the attempt machine freezes the control after the first; if a second
  // command still leaves the browser, the real API answers it as a replay of the same key.
  await submitButton(page).dispatchEvent('click')
  await submitButton(page).dispatchEvent('click')
  await expect(notice(page)).toHaveText(/İşlem kaydedildi\.|daha önce kaydedilmişti/)
  await expect(state(page)).toHaveText('Onay bekliyor')
  await expect(submitButton(page)).toHaveCount(0)

  const after = await readDraft(page, preparerActor)
  expect([after.draft.approvalState, after.draft.version]).toEqual(['PendingApproval', '4'])
  expect(after.approval.submittedBy, 'the submission names its actor').toBeTruthy()
  await expect(page.getByTestId('sales-approval-submitted-by')).toHaveText(after.approval.submittedBy as string)

  expect(submissions.length, 'the browser sent the command').toBeGreaterThan(0)
  for (const answer of submissions) expect(answer.status()).toBe(200)
  const recorded = submissions.filter((answer) => answer.headers()['idempotency-replayed'] !== 'true')
  expect(recorded.length, 'exactly one submission was recorded; any other was a replay').toBe(1)

  // Re-read: the screen shows what the API holds, not what it just rendered.
  await page.reload()
  await expect(state(page)).toHaveText('Onay bekliyor')
  await expect(page.getByTestId('sales-draft-version')).toHaveText('4')
})

test('S04-02 the approver rejects with a reason and the preparer resubmits', async ({ page }) => {
  await signInAs(page, approverActor)
  const rejections = watchCommand(page, 'rejection')
  await openDraft(page)
  await expect(state(page)).toHaveText('Onay bekliyor')
  await expect(approveButton(page)).toBeVisible()
  await expect(rejectButton(page)).toBeVisible()
  await expect(submitButton(page)).toHaveCount(0)

  await page.getByLabel('Ret nedeni (isteğe bağlı)').fill('SENTETIK s04 red nedeni')
  await rejectButton(page).click()
  await expect(notice(page)).toHaveText('İşlem kaydedildi.')
  await expect(state(page)).toHaveText('Reddedildi')
  await expect(page.getByTestId('sales-approval-reason')).toHaveText('SENTETIK s04 red nedeni')
  expect(rejections.map((answer) => answer.status())).toEqual([200])

  const rejected = await readDraft(page, approverActor)
  expect([rejected.draft.approvalState, rejected.draft.version, rejected.approval.rejectionReason]).toEqual([
    'Rejected',
    '5',
    'SENTETIK s04 red nedeni',
  ])
  expect(rejected.approval.decidedBy).toBeTruthy()
  await expect(page.getByTestId('sales-approval-decided-by')).toHaveText(rejected.approval.decidedBy as string)
  await expectNoCommand(page)

  // The preparer sees the rejection and may submit the same revision again (DEC-SAL-002).
  await signInAs(page, preparerActor)
  const submissions = watchCommand(page, 'submission')
  await openDraft(page)
  await expect(state(page)).toHaveText('Reddedildi')
  await expect(page.getByText('Reddedilen taslak aynı revizyonla yeniden onaya gönderilebilir', { exact: false })).toBeVisible()
  await expect(page.getByTestId('sales-approval-reason')).toHaveText('SENTETIK s04 red nedeni')
  await submitButton(page).click()
  await expect(notice(page)).toHaveText('İşlem kaydedildi.')
  await expect(state(page)).toHaveText('Onay bekliyor')
  expect(submissions.map((answer) => answer.status())).toEqual([200])

  const resubmitted = await readDraft(page, preparerActor)
  expect([resubmitted.draft.approvalState, resubmitted.draft.version]).toEqual(['PendingApproval', '6'])
  expect(resubmitted.approval.decidedBy).toBeNull()
  expect(resubmitted.approval.rejectionReason).toBeNull()
})

test('S04-03 two approver tabs: one approves, the stale one is refused 409 and re-reads the approved draft', async ({ page, context }) => {
  await signInAs(page, approverActor)
  const stale = await context.newPage()
  await signInAs(stale, approverActor)
  await openDraft(page)
  await openDraft(stale)
  await expect(state(page)).toHaveText('Onay bekliyor')
  await expect(state(stale)).toHaveText('Onay bekliyor')
  await expect(page.getByTestId('sales-draft-version')).toHaveText('6')
  await expect(stale.getByTestId('sales-draft-version')).toHaveText('6')

  const approvals = watchCommand(page, 'approval')
  await approveButton(page).click()
  await expect(notice(page)).toHaveText('İşlem kaydedildi.')
  await expect(state(page)).toHaveText('Onaylı')
  expect(approvals.map((answer) => answer.status())).toEqual([200])

  // The other tab still holds version 6: its rejection is refused by the real API, and the screen re-reads.
  const rejections = watchCommand(stale, 'rejection')
  await rejectButton(stale).click()
  await expect(notice(stale)).toHaveText('İşlem taslağın güncel durumuyla çakıştı; taslak yeniden okundu. Taslak siz okuduktan sonra değişti; güncel durum gösteriliyor.')
  await expect(state(stale)).toHaveText('Onaylı')
  expect(rejections.map((answer) => answer.status())).toEqual([409])
  const refusal = (await rejections[0].json()) as { code?: unknown }
  expect(refusal.code).toBe('version_conflict')

  await expectNoCommand(page)
  await expectNoCommand(stale)
  await expect(stale.getByText('Onaylı taslağın satırları değişmez', { exact: false })).toBeVisible()

  const approved = await readDraft(page, approverActor)
  expect([approved.draft.approvalState, approved.draft.version, approved.approval.rejectionReason]).toEqual(['Approved', '7', null])
  expect(approved.approval.decidedBy).toBeTruthy()
  await stale.close()
})

test('S04-04 the reader sees the approval facts and no command', async ({ page }) => {
  await signInAs(page, readerActor)
  const reading = await readDraft(page, readerActor)
  expect(reading.draft.approvalState).toBe('Approved')

  await openDraft(page)
  await expect(state(page)).toHaveText('Onaylı')
  await expect(page.getByTestId('sales-approval')).toBeVisible()
  await expect(page.getByTestId('sales-approval-submitted-by')).toHaveText(reading.approval.submittedBy as string)
  await expect(page.getByTestId('sales-approval-decided-by')).toHaveText(reading.approval.decidedBy as string)
  await expectNoCommand(page)
  await expect(page.getByLabel('Ret nedeni (isteğe bağlı)')).toHaveCount(0)

  await page.goto('/satis/siparisler')
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId(`sales-row-approval-${draftId}`)).toHaveText('Taslak · Onaylı')
})

test('S04-05 the S-02 list filters by the server\'s approval state', async ({ page }) => {
  await signInAs(page, readerActor)
  const queries: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'GET' && url.pathname === DRAFTS_PATH) queries.push(url.searchParams.get('approvalState') ?? '')
  })
  await page.goto('/satis/siparisler')
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible({ timeout: 30_000 })
  const filter = page.getByLabel('İç onay durumu')
  await expect(filter.locator('option')).toHaveText(['Tümü', 'Hazırlanıyor', 'Onay bekliyor', 'Onaylı', 'Reddedildi', 'Geri çekildi'])

  await filter.selectOption('Approved')
  await expect(page.getByTestId(`sales-row-approval-${draftId}`)).toHaveText('Taslak · Onaylı')
  expect(queries, 'the filter is the server\'s query, not a local one').toContain('Approved')

  await filter.selectOption('PendingApproval')
  await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect(queries).toContain('PendingApproval')
})

test('S04-06 authorized allocation saves, closes, reopens, releases and reopens from server truth', async ({ page }) => {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const before = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('labelId', sql.UniqueIdentifier, allocationLabelId)
      .query<{ allocations: number; movements: number }>(`
        SELECT
          (SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@companyId AND LabelId=@labelId) AS allocations,
          (SELECT COUNT(DISTINCT movement.Id)
             FROM inventory.Movements movement
             INNER JOIN inventory.MovementLines movementLine
               ON movementLine.CompanyId=movement.CompanyId AND movementLine.MovementId=movement.Id
            WHERE movement.CompanyId=@companyId AND movementLine.LabelId=@labelId) AS movements`)
    expect(before.recordset[0]).toEqual({ allocations: 0, movements: 1 })

    await signInAs(page, preparerActor)
    await openDraft(page)
    await expect(state(page)).toHaveText('Onaylı')
    // S-04 linked effects: Inventory's server count and Logistics' owner-backed zero facts, never assumed values.
    // Exact text (salesText.ts linkedEffects: `${lineLabel(lineNo)}: ${active(count)}`); the allocation line is
    // the seed's first line (S04BrowserSeed adds the coil line first), so it renders as "Satır 1".
    const linkedAllocation = page.getByTestId(`sales-linked-allocation-line-${allocationLineId}`)
    const expectLinkedCount = async (count: number) => {
      await expect(linkedAllocation).toHaveText(`Satır 1: aktif tahsis ${count}`)
      await expect(linkedAllocation).toHaveAttribute('data-state', 'ready')
    }
    await expect(linkedAllocation).toHaveAttribute('data-state', 'ready')
    await expectLinkedCount(0)
    const loadingLine = page.getByTestId(`sales-linked-loading-line-${allocationLineId}`)
    await expect(loadingLine).toHaveAttribute('data-instruction-lines', '0')
    await expect(loadingLine.locator('[data-fact="ordered"]')).not.toHaveText('')
    for (const fact of ['instructed', 'loaded', 'shipped', 'delivered']) {
      await expect(loadingLine.locator(`[data-fact="${fact}"]`)).toHaveText('0')
    }
    const line = page.getByTestId(`sales-allocation-line-${allocationLineId}`)
    await expect(line).toBeVisible()
    // UI-13: the label is picked through Inventory's company-scoped search of this line's product, never typed.
    const seededLabel = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('labelId', sql.UniqueIdentifier, allocationLabelId)
      .query<{ code: string }>('SELECT Code AS code FROM inventory.Labels WHERE CompanyId=@companyId AND Id=@labelId')
    expect(seededLabel.recordset).toHaveLength(1)
    const labelCode = seededLabel.recordset[0].code
    const labelSearch = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/v1/inventory/positions')
    await line.getByLabel('Etiket kodu ara').fill(labelCode)
    await line.getByRole('button', { name: 'Etiketleri getir' }).click()
    expect((await labelSearch).status()).toBe(200)
    await line.getByTestId(`sales-allocation-label-option-${allocationLabelId.toLowerCase()}`).click()
    await expect(line.getByTestId(`sales-allocation-label-selected-${allocationLineId}`))
      .toContainText(`${labelCode} · sürüm ${allocationLabelVersion}`)
    await line.getByLabel('Tahsis kg').fill('5')
    await line.getByLabel('Tahsis nedeni').fill('SENTETIK s04 sipariş tahsisi')

    const createAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === '/api/v1/inventory/physical-allocations')
    await line.getByRole('button', { name: 'Tahsis kaydet' }).click()
    const createdResponse = await createAnswer
    expect(createdResponse.status()).toBe(201)
    const created = await createdResponse.json() as { readonly allocationId: string; readonly status: string; readonly version: string }
    expect(created).toMatchObject({ status: 'Active', version: '1' })
    await expect(line.getByText('Aktif')).toBeVisible()
    // In-page refresh: the panel's refetch of the shared query moves the linked count before any reopen.
    await expectLinkedCount(1)

    await page.goto('/satis/siparisler')
    await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible()
    await page.locator(`a[href="${DETAIL_ROUTE}"]`).click()
    const reopened = page.getByTestId(`sales-allocation-${created.allocationId}`)
    await expect(reopened.getByText('Aktif')).toBeVisible()
    await expectLinkedCount(1)

    const reopenedLine = page.getByTestId(`sales-allocation-line-${allocationLineId}`)
    await reopenedLine.getByLabel('Serbest bırakma nedeni').fill('SENTETIK s04 tahsis serbest')
    const releaseAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/api/v1/inventory/physical-allocations/${created.allocationId}/release`)
    await reopened.getByRole('button', { name: 'Tahsis serbest bırak' }).click()
    const releasedResponse = await releaseAnswer
    expect(releasedResponse.status()).toBe(200)
    expect(await releasedResponse.json()).toMatchObject({ allocationId: created.allocationId, status: 'Released', version: '2', remainingKg: '0' })
    await expect(reopenedLine.getByText('Serbest bırakıldı', { exact: true })).toBeVisible()

    await page.goto('/satis/siparisler')
    await page.locator(`a[href="${DETAIL_ROUTE}"]`).click()
    const finalRow = page.getByTestId(`sales-allocation-${created.allocationId}`)
    await expect(finalRow.getByText('Serbest bırakıldı')).toBeVisible()
    await expect(finalRow.getByRole('button', { name: 'Tahsis serbest bırak' })).toHaveCount(0)
    await expectLinkedCount(0)

    const fresh = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/physical-allocations?salesOrderId=${draftId}&salesOrderLineId=${allocationLineId}&limit=100`, {
      headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId },
    })
    expect(fresh.status()).toBe(200)
    expect(await fresh.json()).toMatchObject({ items: [{ allocation: {
      allocationId: created.allocationId, status: 'Released', version: '2', remainingKg: '0',
      demandId: draftId, demandLineId: allocationLineId, labelId: allocationLabelId,
    } }] })

    const effects = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('labelId', sql.UniqueIdentifier, allocationLabelId)
      .input('allocationId', sql.UniqueIdentifier, created.allocationId)
      .input('entityId', sql.NVarChar, created.allocationId)
      .query<{ allocations: number; commands: number; audits: number; movements: number }>(`
        SELECT
          (SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@companyId AND Id=@allocationId
            AND LabelId=@labelId AND Status=3 AND Version=2 AND RemainingKg=0) AS allocations,
          (SELECT COUNT(*) FROM inventory.Commands WHERE CompanyId=@companyId AND TargetId=@allocationId) AS commands,
          (SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@companyId
            AND EntityType=N'Inventory.StockAllocation' AND EntityId=@entityId) AS audits,
          (SELECT COUNT(DISTINCT movement.Id)
             FROM inventory.Movements movement
             INNER JOIN inventory.MovementLines movementLine
               ON movementLine.CompanyId=movement.CompanyId AND movementLine.MovementId=movement.Id
            WHERE movement.CompanyId=@companyId AND movementLine.LabelId=@labelId) AS movements`)
    expect(effects.recordset[0]).toEqual({ allocations: 1, commands: 2, audits: 2, movements: 1 })
  } finally {
    await pool.close()
  }
})
