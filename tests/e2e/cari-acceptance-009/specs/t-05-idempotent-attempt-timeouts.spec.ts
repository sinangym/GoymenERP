/**
 * T-05 — the idempotent attempt in a REAL browser (G-SUPPORT-004 row 6; G-002
 * check 7): double submit, unknown outcome, user retry with the SAME key and body,
 * the processed / unprocessed timeout distinction (replay headers present / absent),
 * and 409 idempotency_conflict. Controlled responses.
 */
import { expect, test, type Page } from '@playwright/test'
import { A_ID, LEGAL_NAME, PROBLEM, ROLE, mutationView } from '../src/fixtures.js'
import { TLane, held, json, problem, shot, writeJsonEvidence } from '../src/tHarness.js'

async function openCreate(page: Page): Promise<void> {
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
}

async function fillValid(page: Page): Promise<void> {
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByRole('checkbox', { name: 'Müşteri' }).check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
}

test('T-05a a double click sends ONE create request; the button is busy and the fields are frozen meanwhile', async ({ page }, testInfo) => {
  const slug = 't05a-double-submit'
  const gate = held()
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
    create: async () => {
      await gate.promise
      return json(mutationView(), 201)
    },
  })
  await openCreate(page)
  await fillValid(page)
  const button = page.getByRole('button', { name: /^(Cari oluştur|Gönderiliyor…)$/ })
  await button.dblclick()
  await button.click({ force: true }).catch(() => undefined)
  await expect.poll(() => lane.creates().length).toBe(1)
  await expect(button).toBeDisabled()
  await expect(button).toHaveAttribute('aria-busy', 'true')
  await expect(page.getByLabel('Hukuki ad')).toBeDisabled()
  await page.waitForTimeout(500)
  expect(lane.creates()).toHaveLength(1)
  await shot(page, 'T', slug, 'busy-frozen')
  gate.release()
  await expect(page.locator('[data-outcome="created"]')).toBeVisible()
  expect(lane.creates()).toHaveLength(1)
  await lane.finish(testInfo, slug)
})

for (const variant of [
  { label: 'processed', replayed: true },
  { label: 'unprocessed', replayed: false },
] as const) {
  test(`T-05b ${variant.label} timeout: outcome UNKNOWN, no automatic resend, user retry resends the SAME key and body, completes exactly once`, async ({ page }, testInfo) => {
    const slug = `t05b-timeout-${variant.label}`
    let attempts = 0
    const lane = await TLane.install(page, {
      preferCompany: A_ID,
      capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
      create: () => {
        attempts += 1
        // The first attempt never produces a response (transport failure). Whether the
        // server processed it is what the replay headers on the retry tell the client.
        if (attempts === 1) return { abort: 'failed' }
        return variant.replayed
          ? json(mutationView(), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-0001' })
          : json(mutationView(), 201)
      },
    })
    await openCreate(page)
    await fillValid(page)
    await page.getByRole('button', { name: 'Cari oluştur' }).click()
    const notice = page.locator('[data-attempt="unknown"]')
    await expect(notice).toBeVisible()
    await expect(notice).toHaveAttribute('role', 'alert')
    await expect(notice).toContainText('İşlem sonucu belirsiz')
    await expect(page.getByLabel('Hukuki ad')).toBeDisabled()
    await page.waitForTimeout(700)
    expect(lane.creates(), 'no automatic resend').toHaveLength(1)
    await shot(page, 'T', slug, 'unknown-outcome')

    await notice.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
    await expect.poll(() => lane.creates().length).toBe(2)
    const [first, second] = lane.creates()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    expect(second.companyId).toBe(A_ID)
    expect(first.body).toEqual({ kind: 1, legalName: LEGAL_NAME, tradeName: null, roles: ROLE.Customer | ROLE.Supplier })

    await expect(page.locator('[data-outcome="created"]')).toHaveCount(1)
    await expect(page.getByText('Cari CAR-000001 oluşturuldu.')).toHaveCount(1)
    await page.waitForTimeout(500)
    await expect(page.locator('[data-outcome="created"]')).toHaveCount(1)
    expect(lane.creates()).toHaveLength(2)
    await shot(page, 'T', slug, 'completed-once')
    writeJsonEvidence('T', 'measurements', `${slug}.json`, {
      variant,
      firstAttempt: { delivered: first.delivered, abortReason: first.abortReason },
      retry: { sameKey: second.idempotencyKey === first.idempotencyKey, sameBody: true, status: second.status },
      completions: 1,
      announcements: 1,
    })
    await lane.finish(testInfo, slug)
  })
}

test('T-05c 409 idempotency_conflict is shown by code, never resent, and acknowledged', async ({ page }, testInfo) => {
  const slug = 't05c-idempotency-conflict'
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
    create: () => problem(409, PROBLEM.idempotencyConflict),
  })
  await openCreate(page)
  await fillValid(page)
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  const notice = page.locator('[data-failure="idempotency-conflict"]')
  await expect(notice).toBeVisible()
  await expect(notice).toContainText('İşlem çakışması')
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Cari oluştur' })).toBeDisabled()
  await page.waitForTimeout(500)
  expect(lane.creates()).toHaveLength(1)
  await shot(page, 'T', slug, 'conflict-by-code')
  await notice.getByRole('button', { name: 'Anladım' }).click()
  await expect(notice).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Cari oluştur' })).toBeEnabled()
  await expect(page.getByLabel('Hukuki ad')).toHaveValue(LEGAL_NAME)
  await lane.finish(testInfo, slug)
})
