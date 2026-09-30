/**
 * Sales s04 D2: the accepted creator/persona distinction and withdrawal lifecycle on the REAL stack.
 * The fixture writes only Preparing records and real disposable Identity actors. Every transition below
 * crosses the real API, and UI assertions use the exact-head web build; no route is intercepted.
 *
 * The current API exposes the four state commands. Commercial revision is not yet an HTTP operation, so
 * this suite does not fabricate Rev.1 Preparing history through the fixture: the current-Approved refusal
 * is measured here, while Rev.1 Preparing remains paired with the price/discount endpoint slice.
 */

import { expect, test, type APIResponse, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const {
  preparerActor,
  approverActor,
  supportCreatorActor,
  ordinaryCreatorActor,
  companyId,
  supportDraftId,
  ordinaryApproveDraftId,
  ordinaryRejectDraftId,
  withdrawalDraftId,
} = env.seed
const DRAFTS_PATH = '/api/v1/sales/drafts'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

type StateCommand = 'submission' | 'approval' | 'rejection' | 'withdrawal'

interface DraftReading {
  readonly draft: { readonly id: string; readonly approvalState: string; readonly version: string; readonly revision: string }
  readonly approval: { readonly submittedBy: string | null; readonly decidedBy: string | null; readonly rejectionReason: string | null }
  readonly isOwnDraft: boolean
  readonly isOwnDecisionRestricted: boolean
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function readDraft(page: Page, actor: string, draftId: string): Promise<DraftReading> {
  const response = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}/${draftId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status()).toBe(200)
  const reading = (await response.json()) as DraftReading
  expect(reading.draft.id).toBe(draftId)
  return reading
}

async function command(
  page: Page,
  actor: string,
  draftId: string,
  kind: StateCommand,
  expectedVersion: string,
  key: string,
  reason?: string,
): Promise<APIResponse> {
  return page.request.post(`${env.apiBaseUrl}${DRAFTS_PATH}/${draftId}/${kind}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: actor,
      'X-Company-Id': companyId,
      'Idempotency-Key': `SENTETIK-s04-d2-${key}`,
    },
    data: reason === undefined ? { expectedVersion } : { expectedVersion, reason },
  })
}

async function expectProblem(response: APIResponse, status: number, code: string): Promise<void> {
  expect(response.status()).toBe(status)
  expect((await response.json()) as { code?: unknown }).toMatchObject({ code })
}

async function openDraft(page: Page, draftId: string): Promise<void> {
  await page.goto(`/satis/siparisler/${draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

const submitButton = (page: Page) => page.getByRole('button', { name: 'Onaya gönder' })
const approveButton = (page: Page) => page.getByRole('button', { name: 'Onayla' })
const rejectButton = (page: Page) => page.getByRole('button', { name: 'Reddet' })
const withdrawButton = (page: Page) => page.getByRole('button', { name: 'Geri çek', exact: true })
const state = (page: Page) => page.getByTestId('sales-draft-approval-state')

test('S04-D2-01 A creates, B submits, SalesSupport A is refused for both decisions and another authorized actor decides', async ({ page }) => {
  const created = await readDraft(page, supportCreatorActor, supportDraftId)
  expect([created.draft.approvalState, created.isOwnDraft, created.isOwnDecisionRestricted]).toEqual(['Preparing', true, true])

  const submission = await command(page, preparerActor, supportDraftId, 'submission', created.draft.version, 'support-submit-by-b')
  expect(submission.status()).toBe(200)
  const pending = await readDraft(page, supportCreatorActor, supportDraftId)
  expect(pending.draft.approvalState).toBe('PendingApproval')
  expect(pending.approval.submittedBy).toBeTruthy()

  await signInAs(page, supportCreatorActor)
  await openDraft(page, supportDraftId)
  await expect(page.getByTestId('sales-own-decision-note')).toBeVisible()
  await expect(approveButton(page)).toHaveCount(0)
  await expect(rejectButton(page)).toHaveCount(0)

  await expectProblem(
    await command(page, supportCreatorActor, supportDraftId, 'approval', pending.draft.version, 'support-own-approve'),
    403,
    'forbidden',
  )
  await expectProblem(
    await command(page, supportCreatorActor, supportDraftId, 'rejection', pending.draft.version, 'support-own-reject', 'SENTETIK kendi kararı'),
    403,
    'forbidden',
  )
  const unchanged = await readDraft(page, supportCreatorActor, supportDraftId)
  expect([unchanged.draft.approvalState, unchanged.draft.version]).toEqual(['PendingApproval', pending.draft.version])

  const approval = await command(page, approverActor, supportDraftId, 'approval', pending.draft.version, 'support-decided-by-other')
  expect(approval.status()).toBe(200)
  const approved = await readDraft(page, supportCreatorActor, supportDraftId)
  expect(approved.draft.approvalState).toBe('Approved')
  expect(approved.approval.decidedBy).toBeTruthy()
  expect(approved.approval.decidedBy).not.toBe(approved.approval.submittedBy)

  // The creator may not withdraw an already approved record; this is the real API's state refusal, not a hidden button.
  await expectProblem(
    await command(page, supportCreatorActor, supportDraftId, 'withdrawal', approved.draft.version, 'support-approved-withdrawal'),
    409,
    'sales_state_conflict',
  )
})

test('S04-D2-02 a non-SalesSupport creator with ordinary authority may approve and reject own records', async ({ page }) => {
  const approveCreated = await readDraft(page, ordinaryCreatorActor, ordinaryApproveDraftId)
  expect([approveCreated.isOwnDraft, approveCreated.isOwnDecisionRestricted]).toEqual([true, false])
  expect((await command(page, preparerActor, ordinaryApproveDraftId, 'submission', approveCreated.draft.version, 'ordinary-approve-submit')).status()).toBe(200)

  await signInAs(page, ordinaryCreatorActor)
  await openDraft(page, ordinaryApproveDraftId)
  await expect(page.getByTestId('sales-own-decision-note')).toHaveCount(0)
  await expect(approveButton(page)).toBeVisible()
  await expect(rejectButton(page)).toBeVisible()
  await approveButton(page).click()
  await expect(state(page)).toHaveText('Onaylı')
  const selfApproved = await readDraft(page, ordinaryCreatorActor, ordinaryApproveDraftId)
  expect(selfApproved.approval.decidedBy).toBeTruthy()
  expect(selfApproved.approval.decidedBy).not.toBe(selfApproved.approval.submittedBy)

  const rejectCreated = await readDraft(page, ordinaryCreatorActor, ordinaryRejectDraftId)
  expect([rejectCreated.isOwnDraft, rejectCreated.isOwnDecisionRestricted]).toEqual([true, false])
  expect((await command(page, preparerActor, ordinaryRejectDraftId, 'submission', rejectCreated.draft.version, 'ordinary-reject-submit')).status()).toBe(200)
  await openDraft(page, ordinaryRejectDraftId)
  await page.getByLabel('Ret nedeni (isteğe bağlı)').fill('SENTETIK ordinary creator red')
  await rejectButton(page).click()
  await expect(state(page)).toHaveText('Reddedildi')
  const rejected = await readDraft(page, ordinaryCreatorActor, ordinaryRejectDraftId)
  expect(rejected.approval.decidedBy).toBeTruthy()
  expect(rejected.approval.decidedBy).not.toBe(rejected.approval.submittedBy)
  expect(rejected.approval.rejectionReason).toBe('SENTETIK ordinary creator red')
})

test('S04-D2-03 the creator withdraws, closes and reopens the same record, whose state commands are all terminal', async ({ page }) => {
  await signInAs(page, ordinaryCreatorActor)
  const before = await readDraft(page, ordinaryCreatorActor, withdrawalDraftId)
  expect([before.draft.approvalState, before.draft.revision, before.isOwnDraft]).toEqual(['Preparing', 'Rev.0', true])
  await openDraft(page, withdrawalDraftId)
  await page.getByLabel('Geri çekme nedeni (isteğe bağlı)').fill('SENTETIK müşteri vazgeçti')
  await withdrawButton(page).click()
  await expect(state(page)).toHaveText('Geri çekildi')
  await expect(page.getByTestId('sales-approval-reason')).toHaveText('SENTETIK müşteri vazgeçti')

  const withdrawn = await readDraft(page, ordinaryCreatorActor, withdrawalDraftId)
  expect(withdrawn.draft.approvalState).toBe('Withdrawn')
  expect(withdrawn.approval.decidedBy).toBeTruthy()

  // Acceptance rule: close, reopen, and continue with the SAME stored record.
  await page.goto('/satis/siparisler')
  await openDraft(page, withdrawalDraftId)
  await expect(state(page)).toHaveText('Geri çekildi')
  await expect(submitButton(page)).toHaveCount(0)
  await expect(approveButton(page)).toHaveCount(0)
  await expect(rejectButton(page)).toHaveCount(0)
  await expect(withdrawButton(page)).toHaveCount(0)

  for (const kind of ['submission', 'approval', 'rejection', 'withdrawal'] as const) {
    await expectProblem(
      await command(page, ordinaryCreatorActor, withdrawalDraftId, kind, withdrawn.draft.version, `withdrawn-${kind}`),
      409,
      'sales_state_conflict',
    )
  }
  const after = await readDraft(page, ordinaryCreatorActor, withdrawalDraftId)
  expect([after.draft.approvalState, after.draft.version, after.approval.rejectionReason]).toEqual([
    'Withdrawn',
    withdrawn.draft.version,
    'SENTETIK müşteri vazgeçti',
  ])
})
