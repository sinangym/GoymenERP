/**
 * FIN01 official-invoice import acceptance against the disposable real stack.
 *
 * The carrier and document ids below are SENTETIK acceptance data. Party, company-account and
 * currency identities come from the real FIN01 seed/Settings reads. This proves the mounted
 * upload -> another-checker -> approved source -> Finance open-item journey; it is not positive
 * UAT for a customer's real invoice file or mapping.
 */

import { Buffer } from 'node:buffer'
import { randomUUID } from 'node:crypto'
import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const BATCHES = '/api/v1/finance/official-invoice-import-batches'
const PREVIEW = '/api/v1/finance/official-invoice-imports/preview'
const APPLY = '/api/v1/finance/official-invoice-imports/apply'
const HEADER = 'openItemId,sourceDocumentId,sourceInstallmentId,sourceVersion,partyId,companyAccountId,currencyId,amount,dueOn'

async function asActor(context: BrowserContext, actor: string): Promise<Set<Request>> {
  const rewritten = new Set<Request>()
  await context.route('**/api/v1/**', async (route: Route) => {
    rewritten.add(route.request())
    await route.fallback({ headers: { ...route.request().headers(), 'x-development-user': actor } })
  })
  return rewritten
}

function watchAnswers(page: Page): Request[] {
  const answered: Request[] = []
  page.on('requestfinished', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/')) answered.push(request)
  })
  return answered
}

function expectEveryAnswerRewritten(answered: readonly Request[], rewritten: ReadonlySet<Request>) {
  expect(answered.filter((request) => !rewritten.has(request)).map((request) => request.url()),
    'every API answer must belong to the actor-bound browser context').toEqual([])
}

test.describe.serial('FIN01 official invoice import (real API, real SQL, real Chromium)', () => {
  test('different checker approves, applies once and reopens the exact Finance open item', async ({ browser }) => {
    const batchId = randomUUID()
    const openItemId = randomUUID()
    const sourceDocumentId = randomUUID()
    const sourceInstallmentId = randomUUID()
    const batchVersion = `SENTETIK-FIN01-${Date.now()}`
    const sourceVersion = 'SENTETIK-invoice-v1'
    const amount = '432.10'
    const dueOn = '2026-10-31'

    const makerContext = await browser.newContext()
    const makerRewritten = await asActor(makerContext, env.seed.proposerActor)
    const maker = await makerContext.newPage()
    const makerAnswers = watchAnswers(maker)
    await maker.goto('/finans/banka-hesaplari/yeni')
    const tryOption = maker.getByLabel('Para birimi', { exact: true }).locator('option', { hasText: /^TRY — / })
    await expect(tryOption).toHaveCount(1, { timeout: 30_000 })
    const currencyId = await tryOption.getAttribute('value')
    expect(currencyId).toMatch(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)

    const csv = [HEADER, [
      openItemId,
      sourceDocumentId,
      sourceInstallmentId,
      sourceVersion,
      env.seed.partyId,
      env.seed.companyAccountId,
      currencyId!,
      amount,
      dueOn,
    ].join(',')].join('\n')
    const uploadBody = { batchId, batchVersion, utf8Csv: Buffer.from(csv, 'utf8').toString('base64') }
    const uploadKey = randomUUID()

    const uploaded = await maker.evaluate(async ({ path, companyId, key, body }) => {
      const response = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { path: BATCHES, companyId: env.seed.companyId, key: uploadKey, body: uploadBody })
    expect(uploaded.status, JSON.stringify(uploaded.body)).toBe(200)
    expect(uploaded.body).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      resource: {
        batchId,
        companyId: env.seed.companyId,
        batchVersion,
        status: 'Pending',
        version: '1',
        uploadedBy: env.seed.proposerActor,
        approvedBy: null,
      },
    })

    const selfApproval = await maker.evaluate(async ({ batchId: id, companyId }) => {
      const response = await fetch(`/api/v1/finance/official-invoice-import-batches/${id}/approval`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID(), 'X-Company-Id': companyId },
        body: JSON.stringify({ batchId: id, expectedVersion: '1' }),
      })
      return { status: response.status, body: await response.text() }
    }, { batchId, companyId: env.seed.companyId })
    expect(selfApproval.status, selfApproval.body).toBe(403)

    const foreignBatchId = randomUUID()
    const foreignUpload = await maker.evaluate(async ({ path, companyId, body }) => {
      const response = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID(), 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.text() }
    }, {
      path: BATCHES,
      companyId: randomUUID(),
      body: { ...uploadBody, batchId: foreignBatchId },
    })
    expect(foreignUpload.status, foreignUpload.body).toBe(403)
    expectEveryAnswerRewritten(makerAnswers, makerRewritten)
    await makerContext.close()

    const checkerContext = await browser.newContext()
    const checkerRewritten = await asActor(checkerContext, env.seed.approverActor)
    const checker = await checkerContext.newPage()
    const checkerAnswers = watchAnswers(checker)
    expect(env.seed.approverActor).not.toBe(env.seed.proposerActor)

    await checker.goto('/finans')
    await checker.getByRole('link', { name: 'Resmî fatura aktarım onayları', exact: true }).click()
    await expect(checker.getByRole('heading', { level: 1, name: 'Resmî fatura aktarım onayları', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    const pending = checker.getByRole('article').filter({ hasText: batchVersion })
    await expect(pending).toHaveCount(1)
    await pending.getByRole('button', { name: 'İncele', exact: true }).click()
    const detail = checker.getByLabel(`Fatura kaynağı ${batchId}`, { exact: true })
    await expect(detail).toContainText(sourceDocumentId)
    await expect(detail).toContainText(sourceInstallmentId)
    await expect(detail).toContainText(amount)

    const approvalPost = checker.waitForRequest((request) =>
      request.url().endsWith(`/api/v1/finance/official-invoice-import-batches/${batchId}/approval`)
      && request.method() === 'POST')
    const approvalAnswer = checker.waitForResponse((response) =>
      response.url().endsWith(`/api/v1/finance/official-invoice-import-batches/${batchId}/approval`)
      && response.request().method() === 'POST')
    await detail.getByRole('button', { name: 'Onayla', exact: true }).click()
    const approvalSent = await approvalPost
    expect(approvalSent.postDataJSON()).toEqual({ batchId, expectedVersion: '1' })
    expect(approvalSent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/i)
    const approval = await approvalAnswer
    const approvalBody = await approval.json() as {
      readonly originalCorrelationId?: unknown
      readonly resource?: { readonly status?: unknown; readonly version?: unknown; readonly approvedBy?: unknown }
    }
    expect(approval.status(), JSON.stringify(approvalBody)).toBe(200)
    expect(approvalBody.resource).toMatchObject({ status: 'Approved', version: '2', approvedBy: env.seed.approverActor })
    await expect(checker.getByText('Kaynak onaylandı. Sürüm 2.', { exact: true })).toBeVisible()

    await checker.getByRole('button', { name: 'Onaylanan kaynağı aktar', exact: true }).click()
    await expect(checker.getByRole('heading', { level: 1, name: "Onaylı fatura taksitlerini Finans'a aktar", exact: true }))
      .toBeVisible({ timeout: 30_000 })
    await checker.getByLabel('UTF-8 CSV dosyası', { exact: true }).setInputFiles({
      name: 'SENTETIK-FIN01-official-invoice.csv',
      mimeType: 'text/csv',
      buffer: Buffer.from(csv, 'utf8'),
    })
    const previewAnswer = checker.waitForResponse((response) => response.url().endsWith(PREVIEW) && response.status() === 200)
    await checker.getByRole('button', { name: 'Önizle', exact: true }).click()
    const preview = await previewAnswer
    const previewBody = await preview.json() as { readonly sourceSha256?: unknown }
    expect(previewBody.sourceSha256).toMatch(/^[0-9a-f]{64}$/)

    const applyPost = checker.waitForRequest((request) => request.url().endsWith(APPLY) && request.method() === 'POST')
    const applyAnswer = checker.waitForResponse((response) => response.url().endsWith(APPLY) && response.request().method() === 'POST')
    await checker.getByRole('button', { name: 'Yetkili uygula ve açık kalemleri oku', exact: true }).click()
    const applySent = await applyPost
    const applyKey = applySent.headers()['idempotency-key']
    const applyBody = applySent.postDataJSON()
    expect(applyKey).toBe(`official-invoice:${batchId}:${previewBody.sourceSha256 as string}`)
    expect(applyBody).toEqual(uploadBody)
    const applied = await applyAnswer
    expect(applied.status()).toBe(200)
    await expect(checker.getByText("1 açık kalem Finans'tan yeniden okundu.", { exact: true })).toBeVisible()

    const replay = await checker.evaluate(async ({ key, body, companyId }) => {
      const response = await fetch('/api/v1/finance/official-invoice-imports/apply', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, replayed: response.headers.get('Idempotency-Replayed'),
        body: await response.json() as unknown }
    }, { key: applyKey, body: applyBody, companyId: env.seed.companyId })
    expect(replay.status).toBe(200)
    expect(replay.replayed).toBe('true')
    expect(replay.body).toMatchObject({
      admission: { outcome: 'Succeeded', isReplay: true, openItemIds: [openItemId] },
      freshRead: { outcome: 'Found', resource: { batchId, openItems: [{ id: openItemId }] } },
    })

    const fresh = await checker.evaluate(async ({ batchId: id, companyId }) => {
      const response = await fetch(`/api/v1/finance/official-invoice-imports/${id}`, {
        headers: { 'X-Company-Id': companyId },
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { batchId, companyId: env.seed.companyId })
    expect(fresh.status).toBe(200)
    expect(fresh.body).toMatchObject({
      batchId,
      companyId: env.seed.companyId,
      batchVersion,
      openItems: [{
        id: openItemId,
        sourceDocumentId,
        sourceInstallmentId,
        partyId: env.seed.partyId,
        companyAccountId: env.seed.companyAccountId,
        currencyId,
        originalAmount: amount,
        outstandingAmount: amount,
        dueOn,
        version: '1',
      }],
    })

    const close = checker.getByRole('button', { name: 'Sonucu kapat', exact: true })
    await expect(close).toBeVisible()
    await close.click()
    await expect(checker.getByText("1 açık kalem Finans'tan yeniden okundu.", { exact: true })).toHaveCount(0)
    const reopenAnswer = checker.waitForResponse((response) =>
      response.url().endsWith(`/api/v1/finance/official-invoice-imports/${batchId}`)
      && response.request().method() === 'GET')
    await checker.getByRole('button', { name: 'Kaydedilmiş açık kalemleri yeniden aç', exact: true }).click()
    const reopened = await reopenAnswer
    const reopenedBody = await reopened.json() as unknown
    expect(reopened.status(), JSON.stringify(reopenedBody)).toBe(200)
    expect(reopenedBody).toMatchObject({
      batchId,
      companyId: env.seed.companyId,
      batchVersion,
      openItems: [{
        id: openItemId,
        sourceDocumentId,
        sourceInstallmentId,
        originalAmount: amount,
        outstandingAmount: amount,
      }],
    })
    await expect(checker.getByText("1 açık kalem Finans'tan yeniden okundu.", { exact: true })).toBeVisible()
    expectEveryAnswerRewritten(checkerAnswers, checkerRewritten)
    await checkerContext.close()
  })
})
