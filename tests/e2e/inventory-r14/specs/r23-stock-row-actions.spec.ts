import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * AT-063 / Master v3 §25.4 stock row actions on the REAL API and UI of the r14 disposable stack. Nothing is mocked.
 * READ/NAVIGATION only: a row action opens an existing ST-02 panel for the row's label; it never writes. No new actor,
 * permission or grant — the existing r14 actors are reused (r14 is at MAX_SUITE_ACTORS):
 *   - r14-recorder records the precondition label through the real opening command;
 *   - r14-counter (label/balance reads + inventory.stock-count.record) walks the journey;
 *   - r14-transformer (label/balance reads + inventory.label-transformation.record) proves the menu follows the actor.
 *
 * Journey:
 *  1. The counter filters ST-01 by the label code; the URL carries the filter.
 *  2. The row's menu lists exactly the actions its capabilities permit (label, count, genealogy) — no scrap, split/merge
 *     or allocation; the purchase-remainder section (AT-064) is absent, the counter holds no purchase work read.
 *  3. "Sayım / fark" opens ST-02 for that label with the count panel focused; nothing is written.
 *  4. The back link returns to ST-01 with the same filter, read once with the same q.
 *  5. The transformer's menu on the same row offers split/merge and not count.
 */
const env = readEnvironment()
const {
  recorderActor, counterActor, transformerActor, companyId, locationId,
  productId, productVersion, productCode, productName,
} = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const POSITIONS = '/api/v1/inventory/positions'
const CAPABILITIES = '/api/v1/shell/capabilities'
const LABEL = 'SYNR23-ROW-ACTION-1'
const MENU = `${LABEL} işlemleri`
const MENU_LIST = `${LABEL} için işlemler`
const COUNT_PANEL = 'Sayım ve fark (ST-05)'

function apiHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

async function capabilitiesOf(page: Page, actor: string): Promise<readonly string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
}

/** Opens ST-01 at a filtered URL and waits for exactly that filtered read. */
async function openFilteredPositions(page: Page, q: string): Promise<void> {
  await page.goto('about:blank')
  const answer = page.waitForResponse((response) => isCall(response, 'GET', POSITIONS)
    && new URL(response.url()).searchParams.get('q') === q)
  await page.goto(`/stok/pozisyonlar?etiket=${encodeURIComponent(q)}`)
  expect((await answer).status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: 'Stok / Malzeme Pozisyonu' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('link', { name: LABEL, exact: true })).toBeVisible()
}

type SqlPool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

/** Every write a row action could have caused on this label: counts, scraps, transformations, movements, allocations. */
async function writes(pool: SqlPool, labelId: string): Promise<Record<string, number>> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('labelId', sql.UniqueIdentifier, labelId)
    .query(`SELECT
              (SELECT COUNT(*) FROM [inventory].[StockCounts] WHERE [CompanyId] = @companyId AND [LabelId] = @labelId) AS counts,
              (SELECT COUNT(*) FROM [inventory].[Balances] WHERE [CompanyId] = @companyId AND [LabelId] = @labelId) AS balances,
              (SELECT CONVERT(nvarchar(40), SUM([QuantityKg])) FROM [inventory].[Balances]
                WHERE [CompanyId] = @companyId AND [LabelId] = @labelId) AS kg`)
  const row = result.recordset[0] as { counts: number; balances: number; kg: string }
  return { counts: row.counts, balances: row.balances, kg: Number(row.kg) }
}

test('AT-063 opens permitted stock row actions on the row label and returns to the same filtered ST-01', async ({ page }) => {
  test.setTimeout(180_000)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const counter = await capabilitiesOf(page, counterActor)
    expect(counter).toEqual(expect.arrayContaining(['inventory.stock-positions.list', 'inventory.stock-count.record']))
    for (const absent of ['inventory.stock-scrap.record', 'inventory.stock-scrap.approve', 'inventory.label-transformation.record',
      'inventory.physical-allocation.write', 'logistics.operations-work.purchase']) {
      expect(counter).not.toContain(absent)
    }
    const transformer = await capabilitiesOf(page, transformerActor)
    expect(transformer).toEqual(expect.arrayContaining(['inventory.stock-positions.list', 'inventory.label-transformation.record']))
    expect(transformer).not.toContain('inventory.stock-count.record')
    expect(transformer).not.toContain('inventory.stock-count.approve')

    // ── Precondition: one 12 kg label through the recorder's real opening command. ─────────────────────────
    const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
      headers: { ...apiHeaders(recorderActor), 'Content-Type': 'application/json', 'Idempotency-Key': `r23-opening-${randomUUID()}` },
      data: {
        countId: randomUUID(), locationId, countedOn: '2026-09-18', sheetReference: 'SENTETIK browser-suite:r14 AT-063 row actions',
        lines: [{
          lineId: randomUUID(), labelId: randomUUID(), labelCode: LABEL, lotId: randomUUID(),
          heatNumber: 'SYNR23-HEAT-1', coilNumber: null, productId, productVersion, quantityKg: '12',
        }],
      },
    })
    expect(opening.status()).toBe(201)
    const labelId = ((await opening.json()) as { readonly lines: readonly { readonly labelId: string }[] }).lines[0].labelId
    const before = await writes(pool, labelId)
    expect(before).toMatchObject({ counts: 0, kg: 12 })

    // ── 1–2. The counter's filtered ST-01 and its row menu. ────────────────────────────────────────────────
    await signInAs(page, counterActor)
    await openFilteredPositions(page, LABEL)
    await expect(page.getByLabel('Etiket kodu', { exact: true })).toHaveValue(LABEL)
    await expect(page.getByRole('heading', { name: 'Gelecek arz — Alış Siparişi' })).toHaveCount(0)
    const row = page.locator(`tr[data-label-id="${labelId}"]`)
    await expect(row.getByText(productCode, { exact: true })).toBeVisible()
    await expect(row.getByText(productName, { exact: true })).toBeVisible()
    await row.getByRole('button', { name: MENU, exact: true }).click()
    const menu = page.getByRole('list', { name: MENU_LIST, exact: true })
    await expect(menu.getByRole('link')).toHaveText(['Etiket 360’ı aç', 'Sayım / fark', 'Etiket / lot soy ağacı'])

    // ── 3. The count action opens ST-02 on the row label with the count panel focused; nothing is written. ─
    const detailRead = page.waitForResponse((response) => isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
    await menu.getByRole('link', { name: 'Sayım / fark', exact: true }).click()
    expect((await detailRead).status()).toBe(200)
    await expect(page).toHaveURL(new RegExp(`/stok/etiket/${labelId}\\?islem=sayim$`))
    await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { level: 2, name: COUNT_PANEL, exact: true })).toBeVisible()
    await expect(page.locator('#islem-sayim')).toBeFocused()
    await expect(page.locator('#islem-sayim').getByRole('button', { name: 'Sayımı kaydet', exact: true })).toBeVisible()
    expect(await writes(pool, labelId)).toEqual(before)

    // ── 4. Back returns to the same filtered ST-01, read once with the same q. ──────────────────────────────
    const back = page.waitForResponse((response) => isCall(response, 'GET', POSITIONS)
      && new URL(response.url()).searchParams.get('q') === LABEL)
    await page.getByRole('link', { name: '← Stok pozisyonlarına dön', exact: true }).click()
    expect((await back).status()).toBe(200)
    await expect(page).toHaveURL(new RegExp(`/stok/pozisyonlar\\?etiket=${LABEL}$`))
    await expect(page.getByLabel('Etiket kodu', { exact: true })).toHaveValue(LABEL)
    await expect(page.locator(`tr[data-label-id="${labelId}"]`)).toBeVisible()

    // ── 5. The same row offers the transformer split/merge, not count. ──────────────────────────────────────
    await signInAs(page, transformerActor)
    await openFilteredPositions(page, LABEL)
    await page.locator(`tr[data-label-id="${labelId}"]`).getByRole('button', { name: MENU, exact: true }).click()
    await expect(page.getByRole('list', { name: MENU_LIST, exact: true }).getByRole('link'))
      .toHaveText(['Etiket 360’ı aç', 'Böl / birleştir', 'Etiket / lot soy ağacı'])
    expect(await writes(pool, labelId)).toEqual(before)
  } finally {
    await pool.close()
  }
})
