/**
 * FIN-01 — the Finance user path against the REAL stack (`browser:fin01:ci`), card FIN-UI-1 §9.
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the real
 * Settings money profile (GDC/TRY) and currency candidates. The customer is the seed's, opened
 * through the real Cari API. Everything else happens on the screens:
 *
 *   FIN01-01 the proposer proposes a bank account (a currency picked from Settings' candidates,
 *            nothing preselected); it is pending and marked as the proposer's own, with no approval;
 *   FIN01-02 ANOTHER user approves it once (Ö4) — it becomes usable;
 *   FIN01-03 the proposer saves a draft against it, entered from the customer, and reopens the
 *            draft from the list — a draft, no money effect;
 *   FIN01-04 the approver (no draft grant) and the ungranted profile are closed out of the screens
 *            they may not use, and send no Finance request.
 *
 * NOTHING IS FULFILLED LOCALLY: the only interception is the Development identity rewrite, and
 * every answer a case relies on is asserted to belong to a rewritten request (the mfg pattern).
 * The cases run in order on one database: FIN01-02 approves what FIN01-01 proposed.
 */

import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const ACCOUNT_NAME = `SENTETIK FIN01 Ana TL ${Date.now()}`
const NULL_INTENT_NAME = `SENTETIK FIN01 NULL intent ${Date.now()}`
const BANK_NAME = 'SENTETIK FIN01 Banka'
const ACCOUNT_TYPE = 'SENTETIK Vadesiz TL'
const IBAN = 'TR330006100519786457841326'
const CHANGED_IBAN = 'TR060006100519786457841327'
const IBAN_CHANGE_REASON = 'SENTETIK FIN01 banka değişikliği'
const FINANCE_API = /\/api\/v1\/finance\//
const PROPOSE_RE = /\/api\/v1\/finance\/bank-accounts$/
const APPROVAL_RE = /\/api\/v1\/finance\/bank-accounts\/[^/]+\/approval$/
const IBAN_CHANGE_RE = /\/api\/v1\/finance\/bank-accounts\/[^/]+\/iban-change$/
const IBAN_CHANGE_APPROVAL_RE = /\/api\/v1\/finance\/bank-accounts\/[^/]+\/iban-change\/approval$/
const DRAFT_RE = /\/api\/v1\/finance\/manual-receipts\/drafts$/
const RECORD_RE = /\/api\/v1\/finance\/manual-receipts\/[^/]+\/record$/
const REVERSAL_PROPOSAL_RE = /\/api\/v1\/finance\/manual-receipts\/[^/]+\/reversal-proposals$/
const REVERSAL_APPROVAL_RE = /\/api\/v1\/finance\/manual-receipts\/reversal-proposals\/[^/]+\/approval$/
const MANUAL_PAYMENT_RE = /\/api\/v1\/finance\/manual-bank-payments$/
const MANUAL_PAYMENT_APPROVAL_RE = /\/api\/v1\/finance\/manual-bank-payments\/[^/]+\/approval$/
const MANUAL_PAYMENT_RECORD_RE = /\/api\/v1\/finance\/manual-bank-payments\/[^/]+\/record$/

let recordedSourceId = ''
let reversalSourceId = ''
let bankAccountId = ''
let manualPaymentId = ''

/** Binds `actor` to every API request of this context and records which requests it rewrote. */
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
  const foreign = answered.filter((request) => !rewritten.has(request)).map((request) => request.url())
  expect(foreign, 'every API answer must belong to a request sent as the bound actor').toEqual([])
}

function accountRow(page: Page) {
  return page.getByRole('row', { name: new RegExp(ACCOUNT_NAME) })
}

test.describe.serial('FIN-01 bank account and draft (real API, real SQL)', () => {
  test('FIN01-00 the real host refuses a null money intent and stores no account', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    // Supplementary negative transport evidence, not a substitute for the normal-screen cases below.
    // The candidate identity still comes from the real F-04 form/Settings read; no fixture invents it.
    await page.goto('/finans/banka-hesaplari/yeni')
    const currency = page.getByLabel('Para birimi', { exact: true })
    const tryOption = currency.locator('option').filter({ hasText: /TRY/ })
    const currencyId = await tryOption.getAttribute('value')
    expect(currencyId).toMatch(/^[0-9a-f-]{36}$/)

    const result = await page.evaluate(async ({ name, selectedCurrency }) => {
      const response = await fetch('/api/v1/finance/bank-accounts', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': crypto.randomUUID(),
          'X-Correlation-Id': crypto.randomUUID(),
        },
        body: JSON.stringify({
          displayName: name,
          currencyId: selectedCurrency,
          bankName: 'SENTETIK FIN01 Banka',
          accountType: 'SENTETIK Vadesiz',
          moneyMovementEligible: null,
          branchName: null,
          branchCode: null,
          iban: null,
          accountNumber: null,
        }),
      })
      return { status: response.status, body: await response.text() }
    }, { name: NULL_INTENT_NAME, selectedCurrency: currencyId! })

    expect(result.status).toBe(400)
    await page.goto('/finans/banka-hesaplari')
    await expect(page.getByText(NULL_INTENT_NAME, { exact: true })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-01 the proposer proposes an account with a currency from Settings, pending and not self-approvable', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni banka hesabı öner', exact: true })).toBeVisible({ timeout: 30_000 })
    const currency = page.getByLabel('Para birimi', { exact: true })
    await expect(currency).toHaveValue('')
    await page.getByLabel('Hesap adı', { exact: true }).fill(ACCOUNT_NAME)
    await page.getByLabel('Banka adı', { exact: true }).fill(BANK_NAME)
    await page.getByLabel('Hesap türü', { exact: true }).fill(ACCOUNT_TYPE)
    await page.getByLabel('Bu hesap ödeme/tahsilatta kullanılacak', { exact: true }).check()
    await page.getByLabel('IBAN', { exact: true }).fill(IBAN)
    // selectOption takes a string label only; the display label is Settings' own text, so the TRY
    // candidate is found by its code prefix and chosen by its server-given value (the currency id).
    const tryOption = currency.locator('option', { hasText: /^TRY — / })
    await expect(tryOption).toHaveCount(1, { timeout: 30_000 })
    const tryCurrencyId = await tryOption.getAttribute('value')
    expect(tryCurrencyId).toMatch(/^[0-9a-f-]{36}$/)
    await currency.selectOption(tryCurrencyId!)

    const post = page.waitForRequest((request) => PROPOSE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => PROPOSE_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Öner', exact: true }).click()
    const sent = await post
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f]{32}$/)
    expect(sent.postDataJSON()).toEqual({
      displayName: ACCOUNT_NAME,
      currencyId: tryCurrencyId,
      bankName: BANK_NAME,
      accountType: ACCOUNT_TYPE,
      moneyMovementEligible: true,
      branchName: null,
      branchCode: null,
      iban: IBAN,
      accountNumber: null,
    })
    const proposedAnswer = await answer
    expect(proposedAnswer.status()).toBe(200)
    const proposed = await proposedAnswer.json() as { readonly resource?: { readonly id?: unknown } }
    expect(proposed.resource?.id).toEqual(expect.stringMatching(/^[0-9a-f-]{36}$/))
    bankAccountId = proposed.resource!.id as string
    await expect(page.getByText('Hesap önerildi; onay bekliyor.', { exact: false })).toBeVisible()

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('Onay bekliyor')
    await expect(accountRow(page)).toContainText('Sizin öneriniz')
    await expect(accountRow(page)).toContainText(BANK_NAME)
    await expect(accountRow(page)).toContainText(ACCOUNT_TYPE)
    await expect(accountRow(page)).toContainText(IBAN)
    await expect(accountRow(page)).toContainText('Para hareketine uygun')
    await expect(accountRow(page).getByRole('button', { name: /Onayla/ })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02 another user approves it once and it becomes usable', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('Onay bekliyor', { timeout: 30_000 })
    await accountRow(page).getByRole('button', { name: new RegExp(`Onayla: ${ACCOUNT_NAME}`) }).click()
    const post = page.waitForRequest((request) => APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toEqual({ expectedVersion: '1' })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Hesap onaylandı.', { exact: false })).toBeVisible()
    await expect(accountRow(page)).toContainText('Onaylı', { timeout: 15_000 })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02B the proposer creates a pending IBAN change but cannot approve it', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('Onaylı', { timeout: 30_000 })
    await accountRow(page).getByRole('button', { name: 'IBAN değişikliği öner', exact: true }).click()
    await accountRow(page).getByLabel('Yeni IBAN', { exact: true }).fill(CHANGED_IBAN)
    await accountRow(page).getByLabel('Gerekçe', { exact: true }).fill(IBAN_CHANGE_REASON)

    const post = page.waitForRequest((request) => IBAN_CHANGE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => IBAN_CHANGE_RE.test(response.url()) && response.request().method() === 'POST')
    await accountRow(page).getByRole('button', { name: 'Değişikliği öner', exact: true }).click()
    expect((await post).postDataJSON()).toEqual({ expectedVersion: '2', iban: CHANGED_IBAN, reason: IBAN_CHANGE_REASON })
    expect((await answer).status()).toBe(200)

    await expect(accountRow(page)).toContainText('IBAN değişikliği onay bekliyor', { timeout: 30_000 })
    await expect(accountRow(page)).toContainText(CHANGED_IBAN)
    await expect(accountRow(page)).toContainText(IBAN)
    await expect(accountRow(page)).toContainText(/Onaylanana kadar.*mevcut onayl.*IBAN.*kullan.*devam eder/)
    await expect(accountRow(page)).toContainText('Bu değişikliği siz önerdiniz')
    await expect(accountRow(page).getByRole('button', { name: 'IBAN değişikliğini onayla', exact: true })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02C another user approves the pending IBAN and the same account reopens with it', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('IBAN değişikliği onay bekliyor', { timeout: 30_000 })
    const post = page.waitForRequest((request) => IBAN_CHANGE_APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => IBAN_CHANGE_APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await accountRow(page).getByRole('button', { name: 'IBAN değişikliğini onayla', exact: true }).click()
    expect((await post).postDataJSON()).toEqual({ expectedVersion: '3' })
    expect((await answer).status()).toBe(200)

    await expect(accountRow(page)).toContainText(CHANGED_IBAN, { timeout: 30_000 })
    await expect(accountRow(page)).not.toContainText('IBAN değişikliği onay bekliyor')
    await expect(accountRow(page)).not.toContainText(IBAN)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02D the maker creates, lists and reopens one real outgoing bank payment draft', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)
    expect(bankAccountId).toMatch(/^[0-9a-f-]{36}$/)
    await page.goto('/finans/odemeler')
    await page.getByRole('button', { name: 'Yeni', exact: true }).click()
    await expect(page).toHaveURL(/\/finans\/odemeler\/yeni$/)
    await page.getByLabel('Cari hesabı', { exact: true }).selectOption(env.seed.partyId)
    await page.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    await page.getByLabel('İşlem tarihi', { exact: true }).fill('2026-09-18')
    const continueButton = page.getByRole('button', { name: 'Ödeme formuna devam et', exact: true })
    await expect(continueButton).toBeEnabled({ timeout: 30_000 })
    await continueButton.click()
    await expect(page).toHaveURL(/\/finans\/odemeler\/yeni\?.+/)
    const canonical = new URL(page.url()).searchParams
    const entry = {
      partyId: canonical.get('partyId'),
      companyAccountId: canonical.get('companyAccountId'),
      treasuryAccountId: canonical.get('treasuryAccountId'),
      currencyId: canonical.get('currencyId'),
      profileId: canonical.get('profileId'),
      profileVersion: canonical.get('profileVersion'),
    }
    expect(entry).toMatchObject({
      partyId: env.seed.partyId,
      companyAccountId: env.seed.companyAccountId,
      treasuryAccountId: bankAccountId,
      currencyId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      profileId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      profileVersion: expect.stringMatching(/^[1-9][0-9]*$/),
    })
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni giden banka ödemesi', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Tutar', { exact: true }).fill('875,25')
    await page.getByLabel('İşlem tarihi', { exact: true }).fill('2026-09-18')
    await page.getByLabel('Açıklama', { exact: true }).fill('SENTETIK FIN01 giden ödeme')

    const post = page.waitForRequest((request) => MANUAL_PAYMENT_RE.test(request.url()) && request.method() === 'POST')
    const response = page.waitForResponse((answer) => MANUAL_PAYMENT_RE.test(answer.url()) && answer.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const sent = await post
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(sent.postDataJSON()).toMatchObject({
      paymentId: null,
      expectedVersion: null,
      preparation: {
        partyId: entry.partyId,
        companyAccountId: entry.companyAccountId,
        treasuryAccountId: entry.treasuryAccountId,
        money: { amount: '875.25', currencyId: entry.currencyId },
        profile: { profileId: entry.profileId, version: entry.profileVersion },
        occurredOn: '2026-09-18',
        bankReference: null,
      },
      linkedDocument: null,
    })
    const createdAnswer = await response
    expect(createdAnswer.status()).toBe(200)
    const created = await createdAnswer.json() as {
      readonly resource?: { readonly id?: unknown; readonly status?: unknown; readonly version?: unknown }
    }
    expect(created.resource).toMatchObject({ status: 'Draft', version: '1' })
    expect(created.resource?.id).toEqual(expect.stringMatching(/^[0-9a-f-]{36}$/))
    manualPaymentId = created.resource!.id as string

    await page.getByRole('button', { name: 'Kaydı aç', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/odemeler/${manualPaymentId}$`))
    await expect(page.getByText('Draft', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Onayla', exact: true })).toHaveCount(0)

    await page.goto('/finans/odemeler')
    const row = page.getByRole('row').filter({ hasText: '875.25' })
    await expect(row).toHaveCount(1, { timeout: 30_000 })
    await row.getByRole('button', { name: 'Aç', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/odemeler/${manualPaymentId}$`))
    await expect(page.getByText('Draft', { exact: true })).toBeVisible()
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02E another actor approves, confirms, records, replays and reopens the same payment', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)
    expect(manualPaymentId).toMatch(/^[0-9a-f-]{36}$/)

    await page.goto('/finans/odemeler')
    await expect(page.getByRole('button', { name: 'Yeni', exact: true })).toHaveCount(0)
    await page.goto(`/finans/odemeler/${manualPaymentId}`)
    const approvalPost = page.waitForRequest((request) => MANUAL_PAYMENT_APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const approvalAnswer = page.waitForResponse((answer) => MANUAL_PAYMENT_APPROVAL_RE.test(answer.url()) && answer.request().method() === 'POST')
    await page.getByRole('button', { name: 'Onayla', exact: true }).click()
    expect((await approvalPost).postDataJSON()).toEqual({ paymentId: manualPaymentId, expectedVersion: '1' })
    expect((await approvalAnswer).status()).toBe(200)
    await expect(page.getByText('Ödeme onaylandı.', { exact: true })).toBeVisible()

    const recordButton = page.getByRole('button', { name: 'Kesin kaydet', exact: true })
    await expect(recordButton).toBeDisabled()
    await page.getByLabel('Banka sonucu gerçekleşti', { exact: true }).check()
    const recordPost = page.waitForRequest((request) => MANUAL_PAYMENT_RECORD_RE.test(request.url()) && request.method() === 'POST')
    const recordAnswer = page.waitForResponse((answer) => MANUAL_PAYMENT_RECORD_RE.test(answer.url()) && answer.request().method() === 'POST')
    await recordButton.click()
    const recordedRequest = await recordPost
    const recordedBody = recordedRequest.postDataJSON()
    expect(recordedBody).toEqual({ paymentId: manualPaymentId, expectedVersion: '2', bankResultConfirmed: true })
    const recordedResponse = await recordAnswer
    const first = await recordedResponse.json() as {
      readonly isReplay?: unknown
      readonly originalCorrelationId?: unknown
      readonly resource?: { readonly id?: unknown; readonly status?: unknown; readonly version?: unknown }
    }
    expect(recordedResponse.status(), JSON.stringify(first)).toBe(200)
    expect(first).toMatchObject({ isReplay: false, resource: { id: manualPaymentId, status: 'Recorded', version: '3' } })
    await expect(page.getByText('Giden ödeme kesin kaydedildi.', { exact: true })).toBeVisible()

    const replay = await page.evaluate(async ({ paymentId, key, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-bank-payments/${paymentId}/record`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, {
      paymentId: manualPaymentId,
      key: recordedRequest.headers()['idempotency-key'],
      body: recordedBody,
      companyId: env.seed.companyId,
    })
    expect(replay.status).toBe(200)
    expect(replay.body).toMatchObject({
      isReplay: true,
      originalCorrelationId: first.originalCorrelationId,
      resource: { id: manualPaymentId, status: 'Recorded', version: '3' },
    })

    await page.getByRole('button', { name: 'Kesin kaydı yeniden aç', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/odemeler/${manualPaymentId}$`))
    await expect(page.getByText('Recorded', { exact: true })).toBeVisible()
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-03 the proposer saves, reopens and records the draft, then reopens the same source', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    await page.getByLabel('Banka işlem tarihi', { exact: true }).fill('2026-09-18')
    const amount = page.getByLabel(/^Tutar/)
    await expect(amount).toBeEnabled({ timeout: 30_000 })
    await amount.fill('1250,50')

    const post = page.waitForRequest((request) => DRAFT_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => DRAFT_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toMatchObject({
      draftId: null,
      expectedVersion: null,
      preparation: { partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId, money: { amount: '1250.50' } },
    })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Taslak kaydedildi — para etkisi yok.')).toBeVisible()

    await page.goto('/finans/tahsilatlar')
    const rows = page.getByRole('row').filter({ hasText: 'Taslak' })
    await expect(rows).toHaveCount(1, { timeout: 30_000 })
    await rows.first().getByRole('button', { name: /^Aç/ }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Tahsilat taslağı', exact: true })).toBeVisible()
    await expect(page.getByText('Taslak — para etkisi yok.', { exact: false })).toBeVisible()

    const sourceId = new URL(page.url()).pathname.split('/').at(-1)!
    expect(sourceId).toMatch(/^[0-9a-f-]{36}$/)
    recordedSourceId = sourceId
    await page.getByRole('button', { name: 'Kesin kayda al', exact: true }).click()
    const recordPost = page.waitForRequest((request) => RECORD_RE.test(request.url()) && request.method() === 'POST')
    const recordAnswer = page.waitForResponse((response) => RECORD_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kesin kaydı onayla', exact: true }).click()
    const recordSent = await recordPost
    expect(recordSent.postDataJSON()).toEqual({ draftId: sourceId, expectedVersion: '1' })
    expect((await recordAnswer).status()).toBe(200)
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: 'Kesin kayda al' })).toHaveCount(0)

    await page.goto('/finans/tahsilatlar')
    // Reopen by the persisted source identity, not by locale-sensitive amount formatting.
    const recordedRow = page.getByRole('row').filter({ has: page.getByRole('button', { name: new RegExp(`^Aç:.*${sourceId.slice(-8)}$`) }) })
    await expect(recordedRow).toHaveCount(1, { timeout: 30_000 })
    await recordedRow.getByRole('button', { name: /^Aç/ }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${sourceId}$`))
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible()
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-03B the maker proposes a reversal from the recorded source and cannot open checker actions', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)
    expect(recordedSourceId).toMatch(/^[0-9a-f-]{36}$/)

    await page.goto(`/finans/tahsilatlar/${recordedSourceId}`)
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('button', { name: 'Ters kayıt öner', exact: true }).click()
    await page.getByLabel('Ters kayıt işlem tarihi', { exact: true }).fill('2026-09-13')
    await page.getByLabel('Gerekçe', { exact: true }).fill('SENTETIK FIN-T06 correction')
    const post = page.waitForRequest((request) => REVERSAL_PROPOSAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => REVERSAL_PROPOSAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Onaya gönder', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toEqual({
      sourceId: recordedSourceId,
      expectedVersion: '2',
      occurredOn: '2026-09-13',
      reason: 'SENTETIK FIN-T06 correction',
    })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('başka bir yetkilinin onayı bekleniyor', { exact: false })).toBeVisible()

    await page.goto('/finans/ters-kayit-onaylari')
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-03C the checker approves in the reversal queue and creates a separate source', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/ters-kayit-onaylari')
    await expect(page.getByRole('heading', { level: 1, name: 'Ters kayıt onayları', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('SENTETIK FIN-T06 correction', { exact: true })).toBeVisible()
    const post = page.waitForRequest((request) => REVERSAL_APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => REVERSAL_APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Onayla ve ters kaydı oluştur', exact: true }).click()
    expect((await post).postDataJSON()).toEqual({ proposalId: expect.any(String), expectedVersion: '1' })
    expect((await answer).status()).toBe(200)
    const link = page
      .getByLabel('Sonuçlanan ters kayıt önerisi')
      .getByRole('link', { name: 'Oluşturulan ters kaydı aç', exact: true })
    await expect(link).toBeVisible({ timeout: 30_000 })
    reversalSourceId = (await link.getAttribute('href'))!.split('/').at(-1)!
    expect(reversalSourceId).toMatch(/^[0-9a-f-]{36}$/)
    expect(reversalSourceId).not.toBe(recordedSourceId)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-03D the maker reopens both immutable sources through their historical links', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    await page.goto(`/finans/tahsilatlar/${recordedSourceId}`)
    await expect(page.getByText('Ters kayıtla düzeltildi', { exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('link', { name: new RegExp(reversalSourceId.slice(-8)) }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${reversalSourceId}$`))
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })

    await page.getByRole('link', { name: new RegExp(recordedSourceId.slice(-8)) }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${recordedSourceId}$`))
    await expect(page.getByText('Ters kayıtla düzeltildi', { exact: true })).toBeVisible()
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-05 the recorder saves a customer POS collection, reopens it, records it once and a replay adds nothing', async ({ page, context }) => {
    // DEC-FIN-014: the customer's debt closes at the GROSS; the bank net and the commission are kept apart.
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    await page.getByLabel('Banka işlem tarihi', { exact: true }).fill('2026-09-18')
    await page.getByLabel('Tahsilat şekli', { exact: true }).selectOption('pos')
    const gross = page.getByLabel(/^Brüt POS tutarı/)
    await expect(gross).toBeEnabled({ timeout: 30_000 })
    await gross.fill('1000,00')
    await page.getByLabel('Banka POS komisyonu', { exact: true }).fill('25,50')
    await page.getByLabel('Komisyonu üstlenen', { exact: true }).selectOption('Company')
    // The live net is the exact gross − commission, shown before anything is sent.
    await expect(page.locator('[data-bank-net-preview="ready"]')).toContainText('974,50')

    const post = page.waitForRequest((request) => DRAFT_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => DRAFT_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const sent = await post
    const sentBody = sent.postDataJSON() as { readonly preparation?: Record<string, unknown> }
    expect(sentBody).toMatchObject({
      draftId: null,
      expectedVersion: null,
      preparation: {
        partyId: env.seed.partyId,
        companyAccountId: env.seed.companyAccountId,
        treasuryAccountId: bankAccountId,
        money: { amount: '1000.00' },
        occurredOn: '2026-09-18',
        pos: { commissionAmount: '25.50', commissionBearer: 'Company' },
      },
    })
    // The net is the server's to derive; the screen never sends it.
    expect(JSON.stringify(sentBody)).not.toMatch(/bankNet|974/)
    const savedAnswer = await answer
    expect(savedAnswer.status()).toBe(200)
    const saved = await savedAnswer.json() as {
      readonly resource?: { readonly source?: { readonly id?: unknown; readonly version?: unknown }; readonly pos?: unknown }
    }
    expect(saved.resource?.source).toMatchObject({ version: '1' })
    expect(saved.resource?.pos).toEqual({
      commissionBearer: 'Company',
      commission: { disclosure: 'Visible', money: { amount: '25.50', currencyId: expect.any(String) } },
      bankNet: { disclosure: 'Visible', money: { amount: '974.50', currencyId: expect.any(String) } },
    })
    const sourceId = saved.resource!.source!.id as string
    expect(sourceId).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByText('Taslak kaydedildi — para etkisi yok.')).toBeVisible()

    // Close and reopen: a fresh navigation reads the stored facts back from the server.
    const posFacts = page.getByRole('region', { name: 'Müşteri POS tahsilatı', exact: true })
    const fact = (term: string) => posFacts.locator('div').filter({ has: page.locator('dt').getByText(term, { exact: true }) }).locator('dd')
    const expectPosFacts = async () => {
      await expect(fact('Tahsilat şekli')).toHaveText('Müşteri POS (kredi kartı)', { timeout: 30_000 })
      await expect(fact('Brüt tutar (müşteri borcu bu tutarla kapanır)')).toHaveText('1.000,00')
      await expect(fact('Banka POS komisyonu')).toHaveText('25,50')
      await expect(fact('Bankaya geçecek net')).toHaveText('974,50')
      await expect(fact('Komisyonu üstlenen')).toHaveText('Şirket (komisyon gideri)')
      await expect(posFacts.getByText('hesap planı kararı bekleniyor', { exact: false })).toBeVisible()
    }
    await page.goto('/finans/tahsilatlar')
    await page.goto(`/finans/tahsilatlar/${sourceId}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Tahsilat taslağı', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Taslak — para etkisi yok.', { exact: false })).toBeVisible()
    await expectPosFacts()

    // Record once, from the screen.
    await page.getByRole('button', { name: 'Kesin kayda al', exact: true }).click()
    const recordPost = page.waitForRequest((request) => RECORD_RE.test(request.url()) && request.method() === 'POST')
    const recordAnswer = page.waitForResponse((response) => RECORD_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kesin kaydı onayla', exact: true }).click()
    const recordSent = await recordPost
    const recordBody = recordSent.postDataJSON()
    expect(recordBody).toEqual({ draftId: sourceId, expectedVersion: '1' })
    const recordedResponse = await recordAnswer
    const first = await recordedResponse.json() as {
      readonly isReplay?: unknown
      readonly originalCorrelationId?: unknown
      readonly resource?: { readonly source?: { readonly stage?: unknown; readonly version?: unknown } }
    }
    expect(recordedResponse.status(), JSON.stringify(first)).toBe(200)
    expect(first).toMatchObject({
      isReplay: false,
      resource: {
        source: { id: sourceId, stage: 'Recorded', version: '2', amount: { money: { amount: '1000.00' } } },
        unallocated: { knowledge: 'Known', amount: { money: { amount: '1000.00' } } },
        pos: { commissionBearer: 'Company', bankNet: { money: { amount: '974.50' } } },
      },
    })
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })

    // The same key and request again: the stored answer, marked as a replay with the first correlation — no second
    // Record, so no second money event (the event is unique per source and kind in SQL).
    const replay = await page.evaluate(async ({ id, key, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/${id}/record`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: sourceId, key: recordSent.headers()['idempotency-key'], body: recordBody, companyId: env.seed.companyId })
    expect(replay.status).toBe(200)
    expect(replay.body).toMatchObject({
      isReplay: true,
      originalCorrelationId: first.originalCorrelationId,
      resource: { source: { id: sourceId, stage: 'Recorded', version: '2' } },
    })
    // A fresh key at the old version is refused as stale (409) — nothing is recorded a second time.
    const stale = await page.evaluate(async ({ id, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/${id}/record`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID(), 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return response.status
    }, { id: sourceId, body: recordBody, companyId: env.seed.companyId })
    expect(stale).toBe(409)

    // Reopen the recorded source: still version 2, the customer credit is the gross, the POS facts unchanged.
    await page.goto('/finans/tahsilatlar')
    await page.goto(`/finans/tahsilatlar/${sourceId}`)
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: 'Kesin kayda al' })).toHaveCount(0)
    await expect(page.locator('dt').getByText('Dağıtılmamış tutar', { exact: true }).locator('xpath=following-sibling::dd'))
      .toHaveText('1.000,00')
    await expectPosFacts()
    const reread = await page.evaluate(async ({ id, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/${id}`, { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: sourceId, companyId: env.seed.companyId })
    expect(reread.status).toBe(200)
    expect(reread.body).toMatchObject({
      source: { id: sourceId, stage: 'Recorded', version: '2', amount: { money: { amount: '1000.00' } } },
      pos: { commissionBearer: 'Company', commission: { money: { amount: '25.50' } }, bankNet: { money: { amount: '974.50' } } },
    })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-04 the approver has no draft screens and the ungranted profile no Finance screen', async ({ page, context }) => {
    await asActor(context, env.seed.approverActor)
    let financeRequests = 0
    page.on('request', (request) => {
      if (FINANCE_API.test(request.url()) && new URL(request.url()).pathname.includes('/manual-receipts')) financeRequests += 1
    })
    await page.goto('/finans/tahsilatlar')
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    expect(financeRequests).toBe(0)

    const ungranted = await context.browser()!.newContext({ baseURL: env.unauthorizedBaseUrl })
    try {
      const other = await ungranted.newPage()
      let anyFinance = 0
      other.on('request', (request) => {
        if (FINANCE_API.test(request.url())) anyFinance += 1
      })
      for (const route of ['/finans/banka-hesaplari', '/finans/tahsilatlar', '/finans/odemeler']) {
        await other.goto(new URL(route, env.unauthorizedBaseUrl).toString())
        await expect(other.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
      }
      expect(anyFinance).toBe(0)
    } finally {
      await ungranted.close()
    }
  })
})
