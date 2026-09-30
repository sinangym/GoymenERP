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
 *   FIN01-06 DEC-FIN-016: the proposer receives a customer cheque into the portfolio (no money effect),
 *            reopens it from the F-02 list, collects it ONCE into the approved account — one recorded
 *            receipt source at the face value — and a replay or a stale retry adds nothing. The risk
 *            view (user decision 2026-09-27: the debt closes at collection, the risk is tracked apart)
 *            follows it before and after receipt, collection and a second cheque's dishonour.
 *   FIN01-07 the first-release party ledger (cari hesap ekstresi): from the customer's F-02 context the proposer opens
 *            "Cari ekstre", picks a range and sees every RECORDED fact of the account once — the payment, the receipt,
 *            its reversal as its own row, the POS gross and the cheque collection — with the server's running balance
 *            from zero ("devir yok", OPEN-018); opens a row's source and comes back to the same range; reopens it.
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
const REVERSAL_REJECTION_RE = /\/api\/v1\/finance\/manual-receipts\/reversal-proposals\/[^/]+\/rejection$/
const MANUAL_PAYMENT_RE = /\/api\/v1\/finance\/manual-bank-payments$/
const MANUAL_PAYMENT_APPROVAL_RE = /\/api\/v1\/finance\/manual-bank-payments\/[^/]+\/approval$/
const MANUAL_PAYMENT_RECORD_RE = /\/api\/v1\/finance\/manual-bank-payments\/[^/]+\/record$/
const INSTRUMENT_RECEIVE_RE = /\/api\/v1\/finance\/customer-instruments$/
const INSTRUMENT_COLLECT_RE = /\/api\/v1\/finance\/customer-instruments\/[^/]+\/collection$/
const CHEQUE_SERIAL = `SENTETIK-FIN01-CEK-${Date.now()}`
const BOUNCED_SERIAL = `SENTETIK-FIN01-KRS-${Date.now()}`
const RISK_AS_OF = '2026-10-01'
const PARTY_LEDGER_RE = /\/api\/v1\/finance\/party-ledger\?/

/** The risk block's count and amount cells of one bucket, as shown (tr-TR). */
async function riskCells(page: Page, bucket: string): Promise<string[]> {
  const risk = page.getByRole('region', { name: 'Çek/senet riski', exact: true })
  return risk.locator(`table:first-of-type [data-risk-bucket="${bucket}"] td`).allTextContents()
}

async function setRiskDate(page: Page) {
  const risk = page.getByRole('region', { name: 'Çek/senet riski', exact: true })
  await expect(risk).toBeVisible({ timeout: 30_000 })
  await risk.getByLabel('Tarih itibarıyla', { exact: true }).fill(RISK_AS_OF)
}

let recordedSourceId = ''
let reversalSourceId = ''
let rejectedReceiptSourceId = ''
let rejectedProposalId = ''
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

  test('FIN01-06 the recorder receives a customer cheque, reopens it, collects it once and a replay adds nothing', async ({ page, context }) => {
    // DEC-FIN-016: receiving has no money effect; the collection is the one money event at the face value.
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Tahsilat aracı', { exact: true }).selectOption('instrument')
    await expect(page.getByRole('heading', { level: 2, name: 'Çek / senet ile tahsilat (portföye al)' })).toBeVisible()
    // Before the receipt: the customer's cheque / note risk (facts only) is empty.
    await setRiskDate(page)
    await expect(page.getByRole('region', { name: 'Çek/senet riski', exact: true }).getByText('Çek/senet riski yok.'))
      .toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Tür', { exact: true }).selectOption('Cheque')
    await page.getByLabel('Seri / belge no', { exact: true }).fill(CHEQUE_SERIAL)
    await page.getByLabel('Keşideci / borçlu', { exact: true }).fill('SENTETIK FIN01 Keşideci A.Ş.')
    await page.getByLabel('Çekin bankası', { exact: true }).fill('SENTETIK FIN01 Çek Bankası')
    await page.getByLabel('Vade tarihi', { exact: true }).fill('2026-10-15')
    await page.getByLabel('Alış tarihi', { exact: true }).fill('2026-09-18')
    await page.getByLabel('Nominal tutar', { exact: true }).fill('1500,00')
    const currency = page.getByLabel('Para birimi', { exact: true })
    await expect(currency).toBeEnabled({ timeout: 30_000 })
    await currency.selectOption({ label: 'TRY' })

    const post = page.waitForRequest((request) => INSTRUMENT_RECEIVE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => INSTRUMENT_RECEIVE_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Portföye al', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toMatchObject({
      kind: 'Cheque',
      partyId: env.seed.partyId,
      companyAccountId: env.seed.companyAccountId,
      serialNumber: CHEQUE_SERIAL,
      receivedOn: '2026-09-18',
      dueOn: '2026-10-15',
      money: { amount: '1500.00' },
    })
    const received = await answer
    const receivedBody = await received.json() as { readonly resource?: { readonly id?: unknown; readonly status?: unknown } }
    expect(received.status(), JSON.stringify(receivedBody)).toBe(200)
    expect(receivedBody.resource?.status).toBe('InPortfolio')
    const instrumentId = receivedBody.resource!.id as string
    expect(instrumentId).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByText('Portföye alındı — para etkisi yok.')).toBeVisible()
    // After the receipt: still the customer's open exposure — the debt closes only at collection.
    await expect.poll(() => riskCells(page, 'inPortfolioNotDue'), { timeout: 30_000 }).toEqual(['1', '1.500,00'])
    await expect.poll(() => riskCells(page, 'openExposure')).toEqual(['1', '1.500,00'])
    await expect.poll(() => riskCells(page, 'collected')).toEqual(['0', '0,00'])

    // Close and reopen: the canonical portfolio navigation lists it and opens the same record from the row.
    await page.goto('/finans')
    await page.getByRole('link', { name: 'Müşteri Çekleri ve Senetleri', exact: true }).click()
    await expect(page).toHaveURL('/finans/musteri-cek-senetleri')
    const row = page.getByRole('row').filter({ has: page.getByRole('rowheader', { name: CHEQUE_SERIAL, exact: true }) })
    await expect(row).toHaveCount(1, { timeout: 30_000 })
    await expect(row).toContainText('Portföyde')
    await expect(row).toContainText('1.500,00')
    await row.getByRole('button', { name: `Aç: ${CHEQUE_SERIAL}`, exact: true }).click()
    const panel = page.getByRole('region', { name: `Müşteri çeki · ${CHEQUE_SERIAL}`, exact: true })
    await expect(panel).toBeVisible({ timeout: 30_000 })
    await expect(page).toHaveURL(new RegExp(`cekSenet=${instrumentId}`))

    // The next step on the same record: collect it into the approved account.
    await panel.getByRole('button', { name: 'Tahsil edildi olarak kaydet', exact: true }).click()
    const form = panel.getByRole('form', { name: 'Tahsil edildi olarak kaydet' })
    await form.getByLabel('Tahsil tarihi', { exact: true }).fill('2026-09-18')
    await form.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    const collectPost = page.waitForRequest((request) => INSTRUMENT_COLLECT_RE.test(request.url()) && request.method() === 'POST')
    const collectAnswer = page.waitForResponse((response) => INSTRUMENT_COLLECT_RE.test(response.url()) && response.request().method() === 'POST')
    await form.getByRole('button', { name: 'Onayla', exact: true }).click()
    const collectSent = await collectPost
    const collectBody = collectSent.postDataJSON()
    expect(collectBody).toEqual({
      instrumentId, expectedVersion: '1', treasuryAccountId: bankAccountId, collectedOn: '2026-09-18', valueOn: null,
    })
    const collected = await collectAnswer
    const first = await collected.json() as {
      readonly originalCorrelationId?: unknown
      readonly resource?: { readonly status?: unknown; readonly collectionSourceId?: unknown; readonly version?: unknown }
    }
    expect(collected.status(), JSON.stringify(first)).toBe(200)
    expect(first.resource).toMatchObject({ status: 'Collected', version: '2' })
    const sourceId = first.resource!.collectionSourceId as string
    expect(sourceId).toMatch(/^[0-9a-f-]{36}$/)
    await expect(panel.getByRole('link', { name: 'Tahsilat kaydını aç', exact: true })).toBeVisible({ timeout: 30_000 })
    // After the collection: out of the open exposure, counted as collected (company view on the portfolio page).
    await setRiskDate(page)
    await expect.poll(() => riskCells(page, 'collected'), { timeout: 30_000 }).toEqual(['1', '1.500,00'])
    await expect.poll(() => riskCells(page, 'openExposure')).toEqual(['0', '0,00'])

    // The same key and request again: the stored answer with the first correlation; a fresh key at the old version is
    // stale (409). Neither makes a second collection: the instrument names its one source, and the source one event.
    const replay = await page.evaluate(async ({ id, key, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/customer-instruments/${id}/collection`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: instrumentId, key: collectSent.headers()['idempotency-key'], body: collectBody, companyId: env.seed.companyId })
    expect(replay.status).toBe(200)
    expect(replay.body).toMatchObject({
      isReplay: true,
      originalCorrelationId: first.originalCorrelationId,
      resource: { id: instrumentId, status: 'Collected', collectionSourceId: sourceId },
    })
    const stale = await page.evaluate(async ({ id, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/customer-instruments/${id}/collection`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID(), 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return response.status
    }, { id: instrumentId, body: collectBody, companyId: env.seed.companyId })
    expect(stale).toBe(409)

    // Reopen: the instrument is Collected with two movements and the same one source; that source is a recorded
    // receipt of the face value, open for distribution (F-03) like any receipt.
    await page.goto(`/finans/musteri-cek-senetleri?cekSenet=${instrumentId}`)
    const reopened = page.getByRole('region', { name: `Müşteri çeki · ${CHEQUE_SERIAL}`, exact: true })
    await expect(reopened).toBeVisible({ timeout: 30_000 })
    await expect(reopened.getByText('Tahsil edildi', { exact: true })).toBeVisible()
    await expect(reopened.getByRole('button', { name: 'Tahsil edildi olarak kaydet' })).toHaveCount(0)
    const reread = await page.evaluate(async ({ id, companyId }) => {
      const response = await fetch(`/api/v1/finance/customer-instruments/${id}`, { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as { readonly movements?: readonly unknown[] } }
    }, { id: instrumentId, companyId: env.seed.companyId })
    expect(reread.status).toBe(200)
    expect(reread.body).toMatchObject({ instrument: { status: 'Collected', collectionSourceId: sourceId, version: '2' } })
    expect(reread.body.movements).toHaveLength(2)

    const second = await page.evaluate(async ({ body, companyId }) => {
      const response = await fetch('/api/v1/finance/customer-instruments', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID(), 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as { readonly resource?: { readonly id?: string } } }
    }, {
      companyId: env.seed.companyId,
      body: {
        kind: 'Cheque', partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId, serialNumber: BOUNCED_SERIAL,
        drawer: 'SENTETIK FIN01 Keşideci A.Ş.', bankName: 'SENTETIK FIN01 Çek Bankası', branchName: null,
        dueOn: '2026-10-15', receivedOn: '2026-09-18', money: { amount: '700.00', currencyId: sent.postDataJSON().money.currencyId },
        description: null,
      },
    })
    expect(second.status).toBe(200)
    const secondId = second.body.resource!.id!
    await page.goto(`/finans/musteri-cek-senetleri?cekSenet=${secondId}`)
    const bouncedPanel = page.getByRole('region', { name: `Müşteri çeki · ${BOUNCED_SERIAL}`, exact: true })
    await expect(bouncedPanel).toBeVisible({ timeout: 30_000 })
    await bouncedPanel.getByRole('button', { name: 'Karşılıksız olarak işaretle', exact: true }).click()
    const bounceForm = bouncedPanel.getByRole('form', { name: 'Karşılıksız olarak işaretle' })
    await bounceForm.getByLabel('İşlem tarihi', { exact: true }).fill('2026-09-18')
    await bounceForm.getByRole('button', { name: 'Onayla', exact: true }).click()
    await expect(bouncedPanel.getByText('Karşılıksız', { exact: true })).toBeVisible({ timeout: 30_000 })
    await setRiskDate(page)
    await expect.poll(() => riskCells(page, 'dishonoured'), { timeout: 30_000 }).toEqual(['1', '700,00'])
    await expect.poll(() => riskCells(page, 'openExposure')).toEqual(['1', '700,00'])
    await expect.poll(() => riskCells(page, 'collected')).toEqual(['1', '1.500,00'])
    const risk = await page.evaluate(async ({ asOf, partyId, companyAccountId, companyId }) => {
      const query = new URLSearchParams({ asOf, partyId, companyAccountId })
      const response = await fetch(`/api/v1/finance/customer-instruments/risk?${query.toString()}`,
        { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as unknown }
    }, { asOf: RISK_AS_OF, partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId, companyId: env.seed.companyId })
    expect(risk.status).toBe(200)
    expect(risk.body).toMatchObject({
      asOf: RISK_AS_OF,
      customers: [{
        partyId: env.seed.partyId,
        figures: {
          collected: { count: 1, amount: { disclosure: 'Visible', money: { amount: '1500.00' } } },
          dishonoured: { count: 1, amount: { money: { amount: '700.00' } } },
          openExposure: { count: 1, amount: { money: { amount: '700.00' } } },
          inPortfolioNotDue: { count: 0 },
        },
      }],
    })

    // Point in time (K3 5332370492 P2-1): the day before both receipts shows nothing; every step here is dated
    // 2026-09-18, so that day's picture already holds the collection and the dishonour.
    const riskOn = async (asOf: string) => page.evaluate(async ({ day, partyId, companyAccountId, companyId }) => {
      const query = new URLSearchParams({ asOf: day, partyId, companyAccountId })
      const response = await fetch(`/api/v1/finance/customer-instruments/risk?${query.toString()}`,
        { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as { readonly customers?: readonly unknown[] } }
    }, { day: asOf, partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId, companyId: env.seed.companyId })
    const dayBefore = await riskOn('2026-09-17')
    expect(dayBefore.status).toBe(200)
    expect(dayBefore.body.customers).toEqual([])
    const receiptDay = await riskOn('2026-09-18')
    expect(receiptDay.status).toBe(200)
    expect(receiptDay.body).toMatchObject({
      customers: [{ figures: { collected: { count: 1 }, dishonoured: { count: 1 }, openExposure: { count: 1 } } }],
    })

    await page.goto(`/finans/musteri-cek-senetleri?cekSenet=${instrumentId}`)
    await reopened.getByRole('link', { name: 'Tahsilat kaydını aç', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${sourceId}$`))
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('dt').getByText('Dağıtılmamış tutar', { exact: true }).locator('xpath=following-sibling::dd'))
      .toHaveText('1.500,00')
    const source = await page.evaluate(async ({ id, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/${id}`, { headers: { 'X-Company-Id': companyId } })
      return { status: response.status, body: await response.json() as unknown }
    }, { id: sourceId, companyId: env.seed.companyId })
    expect(source.status).toBe(200)
    expect(source.body).toMatchObject({
      source: { id: sourceId, stage: 'Recorded', amount: { money: { amount: '1500.00' } }, treasuryAccountId: bankAccountId },
      unallocated: { knowledge: 'Known', amount: { money: { amount: '1500.00' } } },
    })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-07 the proposer reads the customer ledger from zero, opens a row source, comes back and reopens it', async ({ page, context }) => {
    // Every fact below was recorded by the cases above on this database, for the seed customer and in TRY: the
    // payment (FIN01-02E, 875.25 on 09-18), the receipt (FIN01-03, 1250.50 on 09-18) and its reversal (FIN01-03C, its
    // own row dated 09-13), the POS gross (FIN01-05, 1000.00) and the cheque collection (FIN01-06, 1500.00). Drafts,
    // the portfolio receipt and the dishonoured cheque have no money event, so they are no rows.
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)
    expect(manualPaymentId).toMatch(/^[0-9a-f-]{36}$/)
    expect(reversalSourceId).toMatch(/^[0-9a-f-]{36}$/)

    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await page.getByRole('link', { name: 'Cari ekstre', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Cari ekstre', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Devir yok (OPEN-018)', { exact: false })).toBeVisible()

    // Start on the second day so this explicit range can never equal the screen's clock-derived
    // "this month through today" default (which was exactly 09-01..09-30 on the 30 September run).
    // Every recorded FIN01 fact starts on 09-13, so the same five rows and totals remain in scope.
    await page.getByLabel('Başlangıç tarihi', { exact: true }).fill('2026-09-02')
    await page.getByLabel('Bitiş tarihi', { exact: true }).fill('2026-09-30')
    const answer = page.waitForResponse((response) => PARTY_LEDGER_RE.test(response.url())
      && new URL(response.url()).searchParams.get('from') === '2026-09-02')
    await page.getByRole('button', { name: 'Ekstreyi göster', exact: true }).click()
    const response = await answer
    expect(response.status()).toBe(200)
    const ledger = await response.json() as {
      readonly carryForward?: unknown
      readonly currencies?: readonly {
        readonly entries: readonly {
          readonly kind: string
          readonly sourceId: string
          readonly reversesSourceId: string | null
          readonly runningBalance: { readonly side: string | null; readonly amount: { readonly money: { readonly amount: string } | null } }
        }[]
        readonly debitTotal: { readonly money: { readonly amount: string } | null }
        readonly creditTotal: { readonly money: { readonly amount: string } | null }
        readonly closingBalance: { readonly side: string | null; readonly amount: { readonly money: { readonly amount: string } | null } }
      }[]
    }
    expect(ledger.carryForward).toBe('NoneOpen018')
    const lira = ledger.currencies!
    expect(lira).toHaveLength(1)
    const rows = lira[0].entries
    expect(rows.map((row) => row.kind)).toEqual([
      'ManualReceiptReversal', 'ManualBankPayment', 'ManualReceipt', 'ManualReceipt', 'CustomerInstrumentCollection',
    ])
    expect(rows.map((row) => `${row.runningBalance.amount.money?.amount} ${row.runningBalance.side}`)).toEqual([
      '1250.50 Debit', '2125.75 Debit', '875.25 Debit', '124.75 Credit', '1624.75 Credit',
    ])
    // The reversal is its own row and names the receipt it reverses; each fact appears once (no replay copy).
    expect(rows[0]).toMatchObject({ sourceId: reversalSourceId, reversesSourceId: recordedSourceId })
    expect(rows.filter((row) => row.sourceId === recordedSourceId)).toHaveLength(1)
    expect(rows[1].sourceId).toBe(manualPaymentId)
    expect([lira[0].debitTotal.money?.amount, lira[0].creditTotal.money?.amount]).toEqual(['2125.75', '3750.50'])
    expect(lira[0].closingBalance).toMatchObject({ side: 'Credit', amount: { money: { amount: '1624.75' } } })

    const closing = page.locator('[data-ledger-closing] td')
    await expect(closing).toHaveText('1.624,75 (A)', { timeout: 30_000 })
    await expect(page.locator('[data-ledger-entry]')).toHaveCount(5)
    await expect(page).toHaveURL(/ekstreBaslangic=2026-09-02&ekstreBitis=2026-09-30/)

    // Open a row's source on its own screen, then come back: the same range and the same ledger.
    await page.getByRole('link', { name: `Ödemeyi aç · ref. …${manualPaymentId.slice(-8)}`, exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/odemeler/${manualPaymentId}$`))
    await expect(page.getByRole('heading', { level: 1, name: 'Giden banka ödemesi', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.goBack()
    await expect(page).toHaveURL(/ekstreBaslangic=2026-09-02&ekstreBitis=2026-09-30/)
    await expect(page.getByLabel('Başlangıç tarihi', { exact: true })).toHaveValue('2026-09-02')
    await expect(page.getByLabel('Bitiş tarihi', { exact: true })).toHaveValue('2026-09-30')
    await expect(closing).toHaveText('1.624,75 (A)', { timeout: 30_000 })

    // The reversal row opens the original receipt too.
    await page.getByRole('link', { name: `Ters kaydedilen tahsilatı aç · ref. …${recordedSourceId.slice(-8)}`, exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${recordedSourceId}$`))
    await expect(page.getByText('Ters kayıtla düzeltildi', { exact: true })).toBeVisible({ timeout: 30_000 })
    await page.goBack()
    await expect(closing).toHaveText('1.624,75 (A)', { timeout: 30_000 })

    // Reopen the same address: the same ledger.
    await page.reload()
    await expect(page.getByRole('heading', { level: 1, name: 'Cari ekstre', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(closing).toHaveText('1.624,75 (A)', { timeout: 30_000 })

    // A narrower range carries nothing in (OPEN-018): the 09-13 reversal is outside it and the balance starts at zero.
    await page.getByLabel('Başlangıç tarihi', { exact: true }).fill('2026-09-14')
    await page.getByRole('button', { name: 'Ekstreyi göster', exact: true }).click()
    await expect(page).toHaveURL(/ekstreBaslangic=2026-09-14&ekstreBitis=2026-09-30/)
    await expect(page.locator('[data-ledger-entry]')).toHaveCount(4, { timeout: 30_000 })
    await expect(closing).toHaveText('2.875,25 (A)')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-08A the maker records a separate receipt and proposes its reversal for rejection', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)
    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })

    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    await page.getByLabel('Banka işlem tarihi', { exact: true }).fill('2026-09-19')
    const rejectedAmount = page.getByLabel(/^Tutar/)
    await expect(rejectedAmount).toBeEnabled({ timeout: 30_000 })
    await rejectedAmount.fill('321,45')

    const saveAnswer = page.waitForResponse((response) =>
      DRAFT_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const saved = await saveAnswer
    const savedBody = await saved.json() as {
      readonly resource?: { readonly source?: { readonly id?: unknown; readonly version?: unknown } }
    }
    expect(saved.status(), JSON.stringify(savedBody)).toBe(200)
    expect(savedBody.resource?.source).toMatchObject({ version: '1' })
    rejectedReceiptSourceId = savedBody.resource!.source!.id as string
    expect(rejectedReceiptSourceId).toMatch(/^[0-9a-f-]{36}$/)

    await page.goto(`/finans/tahsilatlar/${rejectedReceiptSourceId}`)
    await expect(page.getByText('Taslak — para etkisi yok.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('button', { name: 'Kesin kayda al', exact: true }).click()
    const recordPost = page.waitForRequest((request) => RECORD_RE.test(request.url()) && request.method() === 'POST')
    const recordAnswer = page.waitForResponse((response) => RECORD_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kesin kaydı onayla', exact: true }).click()
    expect((await recordPost).postDataJSON()).toEqual({ draftId: rejectedReceiptSourceId, expectedVersion: '1' })
    const recorded = await recordAnswer
    const recordedBody = await recorded.json() as {
      readonly resource?: { readonly source?: { readonly id?: unknown; readonly stage?: unknown; readonly version?: unknown } }
    }
    expect(recorded.status(), JSON.stringify(recordedBody)).toBe(200)
    expect(recordedBody.resource?.source).toMatchObject({
      id: rejectedReceiptSourceId, stage: 'Recorded', version: '2',
    })

    await page.getByRole('button', { name: 'Ters kayıt öner', exact: true }).click()
    await page.getByLabel('Ters kayıt işlem tarihi', { exact: true }).fill('2026-09-14')
    await page.getByLabel('Gerekçe', { exact: true }).fill('SENTETIK FIN01 rejection candidate')
    const proposalPost = page.waitForRequest((request) =>
      REVERSAL_PROPOSAL_RE.test(request.url()) && request.method() === 'POST')
    const proposalAnswer = page.waitForResponse((response) =>
      REVERSAL_PROPOSAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Onaya gönder', exact: true }).click()
    expect((await proposalPost).postDataJSON()).toEqual({
      sourceId: rejectedReceiptSourceId,
      expectedVersion: '2',
      occurredOn: '2026-09-14',
      reason: 'SENTETIK FIN01 rejection candidate',
    })
    const proposed = await proposalAnswer
    const proposedBody = await proposed.json() as {
      readonly resource?: { readonly id?: unknown; readonly status?: unknown; readonly version?: unknown }
    }
    expect(proposed.status(), JSON.stringify(proposedBody)).toBe(200)
    expect(proposedBody.resource).toMatchObject({ status: 'Pending', version: '1' })
    rejectedProposalId = proposedBody.resource!.id as string
    expect(rejectedProposalId).toMatch(/^[0-9a-f-]{36}$/)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-08B another actor rejects it, removes it from pending and replays the same terminal answer', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)
    expect(env.seed.approverActor).not.toBe(env.seed.proposerActor)
    expect(rejectedProposalId).toMatch(/^[0-9a-f-]{36}$/)
    expect(rejectedReceiptSourceId).toMatch(/^[0-9a-f-]{36}$/)
    let rejectionCommands = 0
    page.on('request', (request) => {
      if (REVERSAL_REJECTION_RE.test(request.url()) && request.method() === 'POST') rejectionCommands += 1
    })

    await page.goto('/finans/ters-kayit-onaylari')
    await expect(page.getByRole('heading', { level: 1, name: 'Ters kayıt onayları', exact: true }))
      .toBeVisible({ timeout: 30_000 })
    const proposal = page.getByLabel(`Ters kayıt önerisi ${rejectedProposalId}`, { exact: true })
    await expect(proposal).toContainText('SENTETIK FIN01 rejection candidate')
    const reason = proposal.getByLabel('Ret gerekçesi', { exact: true })
    const reject = proposal.getByRole('button', { name: 'Reddet', exact: true })
    await expect(reject).toBeDisabled()
    await reason.focus()
    await page.keyboard.press('Enter')
    expect(rejectionCommands).toBe(0)

    const decisionReason = 'SENTETIK FIN01 belge düzeltilecek'
    await reason.fill(decisionReason)
    await expect(reject).toBeEnabled()
    const post = page.waitForRequest((request) =>
      REVERSAL_REJECTION_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) =>
      REVERSAL_REJECTION_RE.test(response.url()) && response.request().method() === 'POST')
    const pending = page.waitForResponse((response) =>
      response.url().endsWith('/api/v1/finance/manual-receipts/reversal-proposals/pending')
      && response.request().method() === 'GET')
    await reject.click()
    const sent = await post
    const sentBody = sent.postDataJSON()
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f]{32}$/)
    expect(sentBody).toEqual({ proposalId: rejectedProposalId, expectedVersion: '1', reason: decisionReason })
    const rejected = await answer
    const first = await rejected.json() as {
      readonly isReplay?: unknown
      readonly originalCorrelationId?: unknown
      readonly resource?: {
        readonly id?: unknown
        readonly originalSourceId?: unknown
        readonly status?: unknown
        readonly decisionReason?: unknown
        readonly reversalSourceId?: unknown
        readonly version?: unknown
      }
    }
    expect(rejected.status(), JSON.stringify(first)).toBe(200)
    expect(first).toMatchObject({
      isReplay: false,
      resource: {
        id: rejectedProposalId,
        originalSourceId: rejectedReceiptSourceId,
        status: 'Rejected',
        decisionReason,
        reversalSourceId: null,
        version: '2',
      },
    })
    const pendingBody = await (await pending).json() as { readonly items?: readonly { readonly id?: unknown }[] }
    expect(pendingBody.items?.some((item) => item.id === rejectedProposalId)).toBe(false)
    await expect(page.getByText('Öneri reddedildi; para olayı oluşturulmadı.', { exact: true }))
      .toBeVisible({ timeout: 30_000 })
    const original = page
      .getByLabel(`Sonuçlanan ters kayıt önerisi ${rejectedProposalId}`, { exact: true })
      .getByRole('link', { name: 'Orijinal kaydı yeniden aç', exact: true })
    await expect(original).toHaveAttribute('href', `/finans/tahsilatlar/${rejectedReceiptSourceId}`)

    const replay = await page.evaluate(async ({ proposalId, key, body, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/reversal-proposals/${proposalId}/rejection`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Company-Id': companyId },
        body: JSON.stringify(body),
      })
      return { status: response.status, body: await response.json() as unknown }
    }, {
      proposalId: rejectedProposalId,
      key: sent.headers()['idempotency-key'],
      body: sentBody,
      companyId: env.seed.companyId,
    })
    expect(replay.status).toBe(200)
    expect(replay.body).toMatchObject({
      isReplay: true,
      originalCorrelationId: first.originalCorrelationId,
      resource: {
        id: rejectedProposalId,
        originalSourceId: rejectedReceiptSourceId,
        status: 'Rejected',
        decisionReason,
        reversalSourceId: null,
        version: '2',
      },
    })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-08C the maker closes and reopens the unchanged recorded receipt', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)
    expect(rejectedReceiptSourceId).toMatch(/^[0-9a-f-]{36}$/)

    await page.goto('/finans/tahsilatlar')
    const row = page.getByRole('row').filter({
      has: page.getByRole('button', { name: new RegExp(`^Aç:.*${rejectedReceiptSourceId.slice(-8)}$`) }),
    })
    await expect(row).toHaveCount(1, { timeout: 30_000 })
    await row.getByRole('button', { name: /^Aç/ }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/tahsilatlar/${rejectedReceiptSourceId}$`))
    await expect(page.getByText('Kesin kaynak kaydı.', { exact: false })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Ters kayıtla düzeltildi', { exact: true })).toHaveCount(0)

    const fresh = await page.evaluate(async ({ sourceId, companyId }) => {
      const response = await fetch(`/api/v1/finance/manual-receipts/${sourceId}`, {
        headers: { 'X-Company-Id': companyId },
      })
      return { status: response.status, body: await response.json() as unknown }
    }, { sourceId: rejectedReceiptSourceId, companyId: env.seed.companyId })
    expect(fresh.status).toBe(200)
    expect(fresh.body).toMatchObject({
      source: {
        id: rejectedReceiptSourceId,
        stage: 'Recorded',
        version: '2',
        amount: { money: { amount: '321.45' } },
      },
      reversalSourceId: null,
      reversesSourceId: null,
      unallocated: { knowledge: 'Known', amount: { money: { amount: '321.45' } } },
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
