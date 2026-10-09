/**
 * FIN01 official-invoice import acceptance against the disposable real stack.
 *
 * The carrier and document ids below are SENTETIK acceptance data. Party, company-account and
 * currency identities come from the real FIN01 seed/Settings reads. This proves the mounted
 * upload -> another-checker -> approved source -> Finance open-item journey; it is not positive
 * UAT for a customer's real invoice file or mapping.
 * The maker-checker refusal needs a disposable actor that owns both Upload and Approve; the
 * proposer is intentionally Upload-only, so this journey does not mislabel its 403 as maker-checker proof.
 */

import { Buffer } from 'node:buffer'
import { randomUUID } from 'node:crypto'
import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const BATCHES = '/api/v1/finance/official-invoice-import-batches'
const PREVIEW = '/api/v1/finance/official-invoice-imports/preview'
const APPLY = '/api/v1/finance/official-invoice-imports/apply'
const BANK_ACCOUNTS = '/api/v1/finance/bank-accounts'
const RECEIPT_DRAFTS = '/api/v1/finance/manual-receipts/drafts'
const ALLOCATIONS = '/api/v1/finance/allocations'
const HEADER = 'openItemId,sourceDocumentId,sourceInstallmentId,sourceVersion,partyId,companyAccountId,currencyId,amount,dueOn'

function syntheticTurkishIban(): string {
  const bban = randomUUID().replaceAll('-', '').slice(0, 22)
    .split('').map((character) => (Number.parseInt(character, 16) % 10).toString()).join('')
  let remainder = 0
  for (const digit of `${bban}292700`) remainder = ((remainder * 10) + Number(digit)) % 97
  return `TR${String(98 - remainder).padStart(2, '0')}${bban}`
}

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

async function canonicalActorId(page: Page): Promise<string> {
  const identity = await page.evaluate(async () => {
    const response = await fetch('/api/v1/identity/me')
    return { status: response.status, body: await response.json() as { readonly userId?: unknown } }
  })
  expect(identity.status, JSON.stringify(identity.body)).toBe(200)
  expect(identity.body.userId).toEqual(expect.stringMatching(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i))
  return identity.body.userId as string
}

async function expectApprovalDetailFitsViewport(page: Page, width: 390 | 320, height: 844 | 720,
  batchId: string, sourceDocumentId: string, sourceInstallmentId: string): Promise<void> {
  await page.setViewportSize({ width, height })
  const detail = page.getByLabel(`Fatura kaynağı ${batchId}`, { exact: true })
  await expect(detail).toBeVisible()
  await expect(detail.getByText(sourceDocumentId, { exact: true })).toBeVisible()
  await expect(detail.getByText(sourceInstallmentId, { exact: true })).toBeVisible()
  await expect(detail.locator('tbody tr').filter({ hasText: sourceDocumentId })).toBeVisible()
  const approve = detail.getByRole('button', { name: 'Onayla', exact: true })
  const close = detail.getByRole('button', { name: 'Onay kuyruğuna dön', exact: true })
  await expect(approve).toBeVisible()
  await expect(close).toBeVisible()
  await approve.focus()
  await expect(approve).toBeFocused()
  await close.focus()
  await expect(close).toBeFocused()

  const widths = await page.evaluate(() => {
    const heading = document.getElementById('official-invoice-approvals-title')
    const approvalPage = heading?.closest('section') ?? null
    const main = document.querySelector('main')
    const measure = (element: Element | null) => element instanceof HTMLElement
      ? { clientWidth: element.clientWidth, scrollWidth: element.scrollWidth }
      : null
    return {
      document: measure(document.documentElement),
      body: measure(document.body),
      main: measure(main),
      approvalPage: measure(approvalPage),
    }
  })
  for (const [surface, measurement] of Object.entries(widths)) {
    expect(measurement, `${surface} must exist at ${width}px`).not.toBeNull()
    expect(measurement!.scrollWidth, `${surface} scrollWidth at ${width}px`)
      .toBeLessThanOrEqual(measurement!.clientWidth)
  }
}

test.describe.serial('FIN01 official invoice import (real API, real SQL, real Chromium)', () => {
  test('different checker approves, applies once and reopens the exact Finance open item', async ({ browser }) => {
    const openItemId = randomUUID()
    const sourceDocumentId = randomUUID()
    const sourceInstallmentId = randomUUID()
    const batchVersion = `SENTETIK-FIN01-${Date.now()}`
    const sourceVersion = 'SENTETIK-invoice-v1'
    const amount = '432.10'
    const dueOn = '2026-10-31'
    const allocationAccountName = `SENTETIK FIN01 Dağıtım TL ${Date.now()}`
    const allocationAccountIban = syntheticTurkishIban()

    const makerContext = await browser.newContext()
    const makerRewritten = await asActor(makerContext, env.seed.proposerActor)
    const maker = await makerContext.newPage()
    const makerAnswers = watchAnswers(maker)
    await maker.goto('/finans/banka-hesaplari/yeni')
    const makerActorId = await canonicalActorId(maker)
    const currency = maker.getByLabel('Para birimi', { exact: true })
    const tryOption = currency.locator('option', { hasText: /^TRY — / })
    await expect(tryOption).toHaveCount(1, { timeout: 30_000 })
    const currencyId = await tryOption.getAttribute('value')
    expect(currencyId).toMatch(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)
    await maker.getByLabel('Hesap adı', { exact: true }).fill(allocationAccountName)
    await maker.getByLabel('Banka adı', { exact: true }).fill('SENTETIK FIN01 Dağıtım Bankası')
    await maker.getByLabel('Hesap türü', { exact: true }).fill('SENTETIK Vadesiz TL')
    await maker.getByLabel('Bu hesap ödeme/tahsilatta kullanılacak', { exact: true }).check()
    await maker.getByLabel('IBAN', { exact: true }).fill(allocationAccountIban)
    await currency.selectOption(currencyId!)
    const bankProposalPost = maker.waitForRequest((request) => request.url().endsWith(BANK_ACCOUNTS)
      && request.method() === 'POST')
    const bankProposalAnswer = maker.waitForResponse((response) => response.url().endsWith(BANK_ACCOUNTS)
      && response.request().method() === 'POST')
    await maker.getByRole('button', { name: 'Öner', exact: true }).click()
    const bankProposalSent = await bankProposalPost
    expect(bankProposalSent.postDataJSON()).toEqual({
      displayName: allocationAccountName,
      currencyId,
      bankName: 'SENTETIK FIN01 Dağıtım Bankası',
      accountType: 'SENTETIK Vadesiz TL',
      moneyMovementEligible: true,
      branchName: null,
      branchCode: null,
      iban: allocationAccountIban,
      accountNumber: null,
    })
    const bankProposal = await bankProposalAnswer
    const bankProposalBody = await bankProposal.json() as {
      readonly outcome?: unknown
      readonly isReplay?: unknown
      readonly originalCorrelationId?: unknown
      readonly issues?: unknown
      readonly resource?: {
        readonly id?: unknown
        readonly companyId?: unknown
        readonly currencyId?: unknown
        readonly displayName?: unknown
        readonly isActive?: unknown
        readonly isUsableForReceipt?: unknown
        readonly version?: unknown
      }
    }
    expect(bankProposal.status(), JSON.stringify(bankProposalBody)).toBe(200)
    expect(bankProposalBody).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      issues: [],
      resource: {
        companyId: env.seed.companyId,
        currencyId,
        displayName: allocationAccountName,
        isActive: false,
        isUsableForReceipt: false,
        version: '1',
      },
    })
    expect(bankProposalBody.originalCorrelationId).toEqual(expect.stringMatching(/\S/))
    expect(bankProposalBody.resource?.id).toEqual(expect.stringMatching(/^[0-9a-f-]{36}$/))
    const bankAccountId = bankProposalBody.resource!.id as string

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
    await maker.goto('/finans')
    await maker.getByRole('link', { name: 'Onaylı fatura taksit kaynağı yükle', exact: true }).click()
    await expect(maker.getByRole('heading', { level: 1, name: 'Onaylı fatura taksit kaynağı yükle', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    await maker.getByLabel('Kaynak sürümü', { exact: true }).fill(batchVersion)
    await maker.getByLabel('UTF-8 CSV dosyası', { exact: true }).setInputFiles({
      name: 'SENTETIK-FIN01-official-invoice.csv',
      mimeType: 'text/csv',
      buffer: Buffer.from(csv, 'utf8'),
    })
    const uploadPreviewAnswer = maker.waitForResponse((response) => response.url().endsWith(PREVIEW)
      && response.request().method() === 'POST')
    await maker.getByRole('button', { name: 'Önizle', exact: true }).click()
    const uploadPreview = await uploadPreviewAnswer
    expect(uploadPreview.status()).toBe(200)
    const uploadPreviewBody = await uploadPreview.json() as {
      readonly sourceSha256?: unknown
      readonly batch?: { readonly batchId?: unknown; readonly batchVersion?: unknown; readonly batchChecksum?: unknown }
    }
    expect(uploadPreviewBody.sourceSha256).toMatch(/^[0-9a-f]{64}$/)
    expect(uploadPreviewBody.batch).toMatchObject({
      batchVersion,
      batchChecksum: uploadPreviewBody.sourceSha256,
    })
    const uploadPost = maker.waitForRequest((request) => request.url().endsWith(BATCHES) && request.method() === 'POST')
    const uploadAnswer = maker.waitForResponse((response) => response.url().endsWith(BATCHES)
      && response.request().method() === 'POST')
    await maker.getByRole('button', { name: 'Onaya gönder', exact: true }).click()
    const uploadSent = await uploadPost
    const uploadBody = uploadSent.postDataJSON() as { readonly batchId: string; readonly batchVersion: string; readonly utf8Csv: string }
    const batchId = uploadBody.batchId
    expect(batchId).toMatch(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)
    expect(uploadPreviewBody.batch?.batchId).toBe(batchId)
    expect(uploadBody).toEqual({ batchId, batchVersion, utf8Csv: Buffer.from(csv, 'utf8').toString('base64') })
    expect(uploadSent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/i)
    const uploaded = await uploadAnswer
    const uploadedBody = await uploaded.json() as unknown
    expect(uploaded.status(), JSON.stringify(uploadedBody)).toBe(200)
    expect(uploadedBody).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      resource: {
        batchId,
        companyId: env.seed.companyId,
        batchVersion,
        batchChecksum: uploadPreviewBody.sourceSha256,
        status: 'Pending',
        version: '1',
        uploadedBy: makerActorId,
        approvedBy: null,
      },
    })
    await expect(maker.getByRole('heading', { level: 2, name: 'Onay bekliyor', exact: true })).toBeVisible()

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

    const checkerContext = await browser.newContext()
    const checkerRewritten = await asActor(checkerContext, env.seed.approverActor)
    const checker = await checkerContext.newPage()
    const checkerAnswers = watchAnswers(checker)
    expect(env.seed.approverActor).not.toBe(env.seed.proposerActor)

    await checker.goto('/finans/banka-hesaplari')
    const checkerActorId = await canonicalActorId(checker)
    expect(checkerActorId).not.toBe(makerActorId)
    const accountRow = checker.getByRole('row').filter({ hasText: allocationAccountName })
    await expect(accountRow).toContainText('Onay bekliyor', { timeout: 30_000 })
    await accountRow.getByRole('button', { name: new RegExp(`Onayla: ${allocationAccountName}`) }).click()
    const bankApprovalPost = checker.waitForRequest((request) =>
      new URL(request.url()).pathname === `${BANK_ACCOUNTS}/${bankAccountId}/approval`
      && request.method() === 'POST')
    const bankApprovalAnswer = checker.waitForResponse((response) =>
      new URL(response.url()).pathname === `${BANK_ACCOUNTS}/${bankAccountId}/approval`
      && response.request().method() === 'POST')
    await checker.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    const bankApprovalSent = await bankApprovalPost
    expect(bankApprovalSent.postDataJSON()).toEqual({ expectedVersion: '1' })
    expect((await bankApprovalAnswer).status()).toBe(200)
    await expect(accountRow).toContainText('Onaylı', { timeout: 15_000 })

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

    const approvalsPath = '/finans/resmi-fatura-aktarim-onaylari'
    const approvalUrl = `${approvalsPath}?approvalId=${batchId}`
    await expect(checker).toHaveURL(new RegExp(`${approvalUrl.replaceAll('?', '\\?')}$`))

    await checker.reload()
    await expect(checker.getByLabel(`Fatura kaynağı ${batchId}`, { exact: true })).toContainText(batchVersion)
    await expect(checker).toHaveURL(new RegExp(`${approvalUrl.replaceAll('?', '\\?')}$`))

    await checker.goBack()
    await expect(checker).toHaveURL(new RegExp(`${approvalsPath}$`))
    await expect(pending).toHaveCount(1)

    await checker.goto(approvalUrl)
    await expect(checker.getByLabel(`Fatura kaynağı ${batchId}`, { exact: true })).toContainText(sourceDocumentId)

    await expectApprovalDetailFitsViewport(checker, 390, 844, batchId, sourceDocumentId, sourceInstallmentId)
    await expectApprovalDetailFitsViewport(checker, 320, 720, batchId, sourceDocumentId, sourceInstallmentId)
    await checker.setViewportSize({ width: 1400, height: 900 })

    const detailRequestsDuringCompanySwitch: string[] = []
    checker.on('request', (request) => {
      if (new URL(request.url()).pathname === `${BATCHES}/${batchId}`) {
        detailRequestsDuringCompanySwitch.push(request.url())
      }
    })
    const companyPicker = checker.getByRole('combobox', { name: 'Şirket', exact: true })
    await companyPicker.selectOption(env.seed.otherCompanyId)
    const leaveGdc = checker.getByRole('dialog')
    if (await leaveGdc.count()) await leaveGdc.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
    await expect(companyPicker).toHaveValue(env.seed.otherCompanyId, { timeout: 30_000 })
    await expect(checker).toHaveURL(new RegExp(`${approvalsPath}$`))
    await expect(checker.getByText('Onay bekleyen kaynak yok.', { exact: true })).toBeVisible({ timeout: 30_000 })
    expect(detailRequestsDuringCompanySwitch).toEqual([])

    await companyPicker.selectOption(env.seed.companyId)
    const leaveGsp = checker.getByRole('dialog')
    if (await leaveGsp.count()) await leaveGsp.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
    await expect(companyPicker).toHaveValue(env.seed.companyId, { timeout: 30_000 })
    await expect(pending).toHaveCount(1, { timeout: 30_000 })
    await pending.getByRole('button', { name: 'İncele', exact: true }).click()
    await expect(detail).toContainText(sourceDocumentId)

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
    expect(approvalBody.resource).toMatchObject({ status: 'Approved', version: '2', approvedBy: checkerActorId })
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
    const freshBody = fresh.body as {
      readonly batchId: string
      readonly companyId: string
      readonly batchVersion: string
      readonly openItems: readonly {
        readonly id: string
        readonly status: number
        readonly outstandingAmount: string
        readonly version: string
      }[]
    }
    expect(freshBody).toMatchObject({
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
        status: 1,
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

    const receiptEntry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await maker.goto(`/finans/tahsilatlar/yeni?${receiptEntry.toString()}`)
    await expect(maker.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    await maker.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: allocationAccountName })
    await maker.getByLabel('Banka işlem tarihi', { exact: true }).fill('2026-09-18')
    await maker.getByLabel(/^Tutar/).fill(amount.replace('.', ','))
    const receiptDraftPost = maker.waitForRequest((request) => request.url().endsWith(RECEIPT_DRAFTS)
      && request.method() === 'POST')
    const receiptDraftAnswer = maker.waitForResponse((response) => response.url().endsWith(RECEIPT_DRAFTS)
      && response.request().method() === 'POST')
    await maker.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const receiptDraftSent = await receiptDraftPost
    expect(receiptDraftSent.postDataJSON()).toMatchObject({
      draftId: null,
      expectedVersion: null,
      preparation: {
        partyId: env.seed.partyId,
        companyAccountId: env.seed.companyAccountId,
        money: { amount },
      },
    })
    const receiptDraft = await receiptDraftAnswer
    const receiptDraftBody = await receiptDraft.json() as {
      readonly resource?: {
        readonly source?: { readonly id?: unknown; readonly stage?: unknown; readonly version?: unknown }
      }
    }
    expect(receiptDraft.status(), JSON.stringify(receiptDraftBody)).toBe(200)
    expect(receiptDraftBody.resource?.source).toMatchObject({ stage: 'Draft', version: '1' })
    expect(receiptDraftBody.resource?.source?.id).toEqual(expect.stringMatching(/^[0-9a-f-]{36}$/))
    const sourceId = receiptDraftBody.resource!.source!.id as string

    const recorded = await maker.evaluate(async ({ sourceId: id, companyId }) => {
      const body = { draftId: id, expectedVersion: '1' }
      const key = `SENTETIK-FIN01-record-${crypto.randomUUID()}`
      const response = await fetch(`/api/v1/finance/manual-receipts/${id}/record`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { sourceId, companyId: env.seed.companyId })
    expect(recorded.status).toBe(200)
    expect(recorded.body).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      resource: {
        source: {
          id: sourceId,
          companyId: env.seed.companyId,
          companyAccountId: env.seed.companyAccountId,
          currencyId,
          stage: 'Recorded',
          version: '2',
        },
      },
    })

    const hiddenAllocation: {
      request: Request | null
      status: number | null
      body: unknown
    } = { request: null, status: null, body: null }
    await checker.route('**/api/v1/finance/allocations', async (route) => {
      const request = route.request()
      if (request.method() !== 'POST' || hiddenAllocation.request !== null) {
        await route.fallback()
        return
      }
      hiddenAllocation.request = request
      checkerRewritten.add(request)
      const actual = await route.fetch({
        headers: { ...request.headers(), 'x-development-user': env.seed.approverActor },
      })
      hiddenAllocation.status = actual.status()
      hiddenAllocation.body = await actual.json() as unknown
      await route.fulfill({ status: 503, body: '' })
    })

    const initialAllocationListAnswer = checker.waitForResponse((response) =>
      new URL(response.url()).pathname === ALLOCATIONS && response.request().method() === 'GET')
    await checker.goto('/finans/dagitimlar')
    expect((await initialAllocationListAnswer).status()).toBe(200)
    const allocationForm = checker.getByRole('form', { name: 'Finansal dağıtım oluştur' })
    await expect(allocationForm).toBeVisible({ timeout: 30_000 })
    await allocationForm.getByLabel('Gerçekleşmiş tahsilat kaynağı', { exact: true }).selectOption(sourceId)
    await allocationForm.getByLabel('Onaylı fatura kaynağı', { exact: true }).selectOption(batchId)
    await allocationForm.getByLabel('Açık kalem', { exact: true }).selectOption(openItemId)
    await allocationForm.getByLabel('Dağıtım tutarı', { exact: true }).fill(amount.replace('.', ','))
    await allocationForm.getByLabel('Dağıtım nedeni', { exact: true }).fill('SENTETIK FIN01 aynı kayıt kapama')
    await allocationForm.getByRole('button', { name: 'Dağıtım oluştur', exact: true }).click()
    await expect(allocationForm.getByRole('button', { name: 'Aynı anahtarla yeniden dene', exact: true }))
      .toBeVisible({ timeout: 30_000 })

    expect(hiddenAllocation.status, JSON.stringify(hiddenAllocation.body)).toBe(200)
    const capturedAllocationRequest = hiddenAllocation.request
    if (capturedAllocationRequest === null) throw new Error('allocation create request was not captured')
    const allocationKey = capturedAllocationRequest.headers()['idempotency-key']
    const allocationBody = capturedAllocationRequest.postDataJSON() as {
      readonly allocationId: string
      readonly sourceId: string
      readonly expectedSourceVersion: string
      readonly openItemId: string
      readonly expectedOpenItemVersion: string
      readonly amount: string
      readonly reason: string
    }
    const allocationId = allocationBody.allocationId
    expect(allocationKey).toMatch(/^[0-9a-f-]{36}$/i)
    expect(allocationBody).toEqual({
      allocationId,
      sourceId,
      expectedSourceVersion: '2',
      openItemId,
      expectedOpenItemVersion: '1',
      amount,
      reason: 'SENTETIK FIN01 aynı kayıt kapama',
    })
    expect(hiddenAllocation.body).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      resource: {
        id: allocationId,
        companyId: env.seed.companyId,
        sourceId,
        openItemId,
        amount,
        status: 'Active',
        version: '1',
      },
    })

    const allocationDetailPath = `${ALLOCATIONS}/${allocationId}`
    const replayPost = checker.waitForRequest((request) => new URL(request.url()).pathname === ALLOCATIONS
      && request.method() === 'POST')
    const replayAnswer = checker.waitForResponse((response) => new URL(response.url()).pathname === ALLOCATIONS
      && response.request().method() === 'POST')
    const refreshedAllocationListAnswer = checker.waitForResponse((response) =>
      new URL(response.url()).pathname === ALLOCATIONS && response.request().method() === 'GET')
    const allocationDetailAnswer = checker.waitForResponse((response) =>
      new URL(response.url()).pathname === allocationDetailPath && response.request().method() === 'GET')
    await allocationForm.getByRole('button', { name: 'Aynı anahtarla yeniden dene', exact: true }).click()
    const replaySent = await replayPost
    expect(replaySent.headers()['idempotency-key']).toBe(allocationKey)
    expect(replaySent.postDataJSON()).toEqual(allocationBody)
    const replayedAllocation = await replayAnswer
    const replayedAllocationBody = await replayedAllocation.json() as unknown
    expect(replayedAllocation.status(), JSON.stringify(replayedAllocationBody)).toBe(200)
    expect(replayedAllocation.headers()['idempotency-replayed']).toBe('true')
    expect(replayedAllocationBody).toMatchObject({
      outcome: 'Succeeded',
      isReplay: true,
      resource: { id: allocationId, status: 'Active', version: '1' },
    })
    const refreshedAllocationListResponse = await refreshedAllocationListAnswer
    expect(refreshedAllocationListResponse.status()).toBe(200)
    const allocationList = await refreshedAllocationListResponse.json() as {
      readonly companyId: string
      readonly items: readonly { readonly id: string; readonly sourceId: string; readonly openItemId: string }[]
    }
    const allocationDetailResponse = await allocationDetailAnswer
    expect(allocationDetailResponse.status()).toBe(200)
    const allocationDetail = await allocationDetailResponse.json() as Record<string, unknown>

    const closedBatch = await checker.evaluate(async ({ id, companyId }) => {
      const response = await fetch(`/api/v1/finance/official-invoice-imports/${id}`, {
        headers: { 'X-Company-Id': companyId },
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: batchId, companyId: env.seed.companyId })
    expect(closedBatch.status).toBe(200)
    expect(closedBatch.body).toMatchObject({
      batchId,
      openItems: [{ id: openItemId, outstandingAmount: '0.00', status: 2, version: '2' }],
    })

    expect(allocationList.companyId).toBe(env.seed.companyId)
    expect(allocationList.items.filter((item) => item.id === allocationId)).toEqual([
      expect.objectContaining({ id: allocationId, sourceId, openItemId }),
    ])
    const allocationRow = checker.getByRole('row').filter({ hasText: sourceId }).filter({ hasText: openItemId })
    await expect(allocationRow).toHaveCount(1)
    expect(allocationDetail).toMatchObject({
      allocation: {
        id: allocationId,
        companyId: env.seed.companyId,
        sourceId,
        openItemId,
        status: 'Active',
        version: '1',
      },
      openItemVersion: '2',
    })
    const allocationRegion = checker.getByRole('region', { name: 'Dağıtım detayı' })
    await expect(allocationRegion).toContainText(allocationId)
    await checker.getByRole('button', { name: 'Kapat', exact: true }).click()
    await expect(allocationRegion).toHaveCount(0)
    const reopenedAllocationAnswer = checker.waitForResponse((response) =>
      new URL(response.url()).pathname === allocationDetailPath && response.request().method() === 'GET')
    await allocationRow.getByRole('button', { name: 'Aç', exact: true }).click()
    const reopenedAllocation = await reopenedAllocationAnswer
    expect(reopenedAllocation.status()).toBe(200)
    expect(await reopenedAllocation.json()).toMatchObject(allocationDetail)
    await expect(checker.getByRole('region', { name: 'Dağıtım detayı' })).toContainText(allocationId)

    const releaseForm = checker.getByRole('form', { name: 'Dağıtımı bırak' })
    await releaseForm.getByLabel('Bırakma nedeni', { exact: true }).fill('SENTETIK FIN01 aynı kayıt yeniden açma')
    const releasePath = `${allocationDetailPath}/release`
    const releasePost = checker.waitForRequest((request) => new URL(request.url()).pathname === releasePath
      && request.method() === 'POST')
    const releaseAnswer = checker.waitForResponse((response) => new URL(response.url()).pathname === releasePath
      && response.request().method() === 'POST')
    await releaseForm.getByRole('button', { name: 'Dağıtımı bırak', exact: true }).click()
    const releaseSent = await releasePost
    const releaseKey = releaseSent.headers()['idempotency-key']
    const releaseBody = releaseSent.postDataJSON()
    expect(releaseKey).toMatch(/^[0-9a-f-]{36}$/i)
    expect(releaseBody).toEqual({
      allocationId,
      expectedAllocationVersion: '1',
      expectedOpenItemVersion: '2',
      reason: 'SENTETIK FIN01 aynı kayıt yeniden açma',
    })
    const releaseResponse = await releaseAnswer
    const releaseResponseBody = await releaseResponse.json() as unknown
    expect(releaseResponse.status(), JSON.stringify(releaseResponseBody)).toBe(200)
    expect(releaseResponseBody).toMatchObject({
      outcome: 'Succeeded',
      isReplay: false,
      resource: { id: allocationId, status: 'Released', version: '2' },
    })
    const replayedRelease = await checker.evaluate(async ({ path, key, body, companyId }) => {
      const response = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return {
        status: response.status,
        replayed: response.headers.get('Idempotency-Replayed'),
        body: await response.json() as unknown,
      }
    }, { path: releasePath, key: releaseKey, body: releaseBody, companyId: env.seed.companyId })
    expect(replayedRelease.status).toBe(200)
    expect(replayedRelease.replayed).toBe('true')
    expect(replayedRelease.body).toMatchObject({
      outcome: 'Succeeded',
      isReplay: true,
      resource: { id: allocationId, status: 'Released', version: '2' },
    })

    const releasedDetail = await checker.evaluate(async ({ path, companyId }) => {
      const response = await fetch(path, { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as unknown }
    }, { path: allocationDetailPath, companyId: env.seed.companyId })
    expect(releasedDetail.status).toBe(200)
    expect(releasedDetail.body).toMatchObject({
      allocation: { id: allocationId, sourceId, openItemId, status: 'Released', version: '2' },
      openItemVersion: '3',
    })
    const reopenedBatch = await checker.evaluate(async ({ id, companyId }) => {
      const response = await fetch(`/api/v1/finance/official-invoice-imports/${id}`, {
        headers: { 'X-Company-Id': companyId },
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: batchId, companyId: env.seed.companyId })
    expect(reopenedBatch.status).toBe(200)
    expect(reopenedBatch.body).toMatchObject({
      batchId,
      openItems: [{ id: openItemId, outstandingAmount: amount, status: 1, version: '3' }],
    })

    expectEveryAnswerRewritten(makerAnswers, makerRewritten)
    await makerContext.close()
    expectEveryAnswerRewritten(checkerAnswers, checkerRewritten)
    await checkerContext.close()
  })
})
