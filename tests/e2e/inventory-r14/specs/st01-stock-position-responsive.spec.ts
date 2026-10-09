import { randomUUID } from 'node:crypto'
import { expect, test, type Locator, type Page, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env'

/**
 * STOCK-DS-01 on the disposable R14 SQL/API/Chromium stack. The R14 fixture creates and activates the real SAC-1
 * Product through its public API. This test records 26 physical labels through Inventory's public opening command,
 * then reads the actual stock page. No route, response, Product card, or permission is mocked.
 */
const env = readEnvironment()
const { companyId, otherCompanyId, locationId, productId, productVersion, productCode, productName,
  recorderActor, arrangerActor, counterActor } = env.seed
const POSITIONS = '/api/v1/inventory/positions'
const OPENINGS = '/api/v1/inventory/opening-counts'
const LOCATIONS = '/api/v1/organization/locations?page=1&pageSize=100'
const AUTHORIZED_ACTOR = 'group-bootstrap-admin'
const STOCK_DISPLAY_HEADER = 'X-ERP-Inventory-Stock-Display'
const STOCK_DISPLAY_VERSION = '2.0'
const PREFIX = `SYNDS01-${randomUUID().slice(0, 8).toUpperCase()}`
const EXTREME = '999999999999999.999' // The largest positive decimal(18,3) value, sent as lossless text.
const NORMAL = '12345.678'
const QUANTITIES = ['Fiziksel kg', 'Bloklu kg', 'Tahsisli kg', 'Kullanılabilir kg'] as const
const LABEL_COUNT = 26 // The accepted stock-position page size is 25, so this forces a real cursor read.

type Position = {
  readonly companyId: string
  readonly labelId: string
  readonly labelCode: string
  readonly productId: string
  readonly locationId: string
  readonly physicalKg: string
  readonly blockedPhysicalKg: string
  readonly activeAllocationKg: string
  readonly availableKg: string
  readonly productDisplay: { readonly identity: { readonly code: string; readonly name: string } | null }
  readonly locationDisplay: {
    readonly locationId: string
    readonly locationCode: string
    readonly locationName: string
  }
}
type PositionPage = { readonly items: readonly Position[]; readonly nextCursor: string | null }

function headers(actor: string): Record<string, string> {
  return { 'X-Development-User': actor, 'X-Company-Id': companyId }
}

function isPositionRead(response: Response, q: string, cursor?: string): boolean {
  const url = new URL(response.url())
  return response.request().method() === 'GET' && url.pathname === POSITIONS
    && url.searchParams.get('q') === q && url.searchParams.get('limit') === '25'
    && (cursor === undefined || url.searchParams.get('cursor') === cursor)
}

async function openList(page: Page, q: string): Promise<PositionPage> {
  const answer = page.waitForResponse((response) => isPositionRead(response, q) && !new URL(response.url()).searchParams.has('cursor'))
  await page.goto(`/stok/pozisyonlar?etiket=${encodeURIComponent(q)}`)
  const response = await answer
  expect(response.status()).toBe(200)
  expect(await response.request().headerValue(STOCK_DISPLAY_HEADER)).toBe(STOCK_DISPLAY_VERSION)
  await expect(page.getByRole('heading', { level: 1, name: 'Stok / Malzeme Pozisyonu' })).toBeVisible()
  return response.json() as Promise<PositionPage>
}

function stockRow(page: Page, labelId: string): Locator {
  return page.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' }).locator(`tbody tr[data-label-id="${labelId}"]`)
}

function stockCard(page: Page, labelId: string): Locator {
  return page.getByTestId('stock-position-cards').locator(`li[data-label-id="${labelId}"]`)
}

function trNumber(value: string): string {
  const [whole, fraction] = value.split('.')
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return fraction === undefined ? grouped : `${grouped},${fraction}`
}

function amountOf(position: Position): readonly string[] {
  return [position.physicalKg, position.blockedPhysicalKg, position.activeAllocationKg, position.availableKg]
}

/** Measures text ink and hit testing, not DOM existence or the hover title. Scrolls a non-pinned cell into reach. */
async function desktopInk(row: Locator, header: string, expected: string, spanText = false): Promise<void> {
  const measured = await row.evaluate((element, input) => {
    const table = element.closest('table')
    const scroll = table?.parentElement
    const headers = [...(table?.querySelectorAll('thead th') ?? [])]
    const cells = [...element.querySelectorAll('td')]
    const index = headers.findIndex((candidate) => candidate.textContent?.trim() === input.header)
    if (!table || !scroll || index < 0 || cells.length !== headers.length) return null
    const target = input.spanText
      ? [...cells[index].querySelectorAll('span')].find((node) => node.textContent === input.expected)
      : cells[index]
    if (!target) return null
    element.scrollIntoView({ block: 'center', inline: 'nearest' })
    const label = cells[0]
    const available = cells[headers.findIndex((candidate) => candidate.textContent?.trim() === 'Kullanılabilir kg')]
    const actions = cells[cells.length - 1]
    if (!label || !available || !actions) return null
    // A user can move the horizontal scroll until an ordinary cell is between the three sticky columns.
    if (target !== label && target !== available && target !== actions) {
      for (let attempt = 0; attempt < 3; attempt += 1) {
        const rect = target.getBoundingClientRect()
        const left = label.getBoundingClientRect().right + 2
        const right = available.getBoundingClientRect().left - 2
        if (rect.left < left) scroll.scrollLeft += rect.left - left
        else if (rect.right > right) scroll.scrollLeft += rect.right - right
        else break
      }
    }
    const labelRect = label.getBoundingClientRect()
    const availableRect = available.getBoundingClientRect()
    const actionsRect = actions.getBoundingClientRect()
    const scrollRect = scroll.getBoundingClientRect()
    const cellRect = cells[index].getBoundingClientRect()
    const range = document.createRange()
    range.selectNodeContents(target)
    const ink = [...range.getClientRects()].filter((rect) => rect.width > 0 && rect.height > 0)
    const left = target === available ? availableRect.left : labelRect.right + 1
    const right = target === available ? actionsRect.left : availableRect.left - 1
    const fragmentsReachable = ink.length > 0 && ink.every((rect) => {
      const x = (rect.left + rect.right) / 2
      const y = (rect.top + rect.bottom) / 2
      const hit = document.elementFromPoint(x, y)
      return rect.left >= Math.max(left, scrollRect.left) - 1 && rect.right <= Math.min(right, scrollRect.right) + 1
        && rect.left >= cellRect.left - 1 && rect.right <= cellRect.right + 1
        && rect.top >= cellRect.top - 1 && rect.bottom <= cellRect.bottom + 1
        && hit !== null && cells[index].contains(hit)
    })
    const actionButton = actions.querySelector('button')
    const buttonRect = actionButton?.getBoundingClientRect()
    const buttonHit = buttonRect && document.elementFromPoint(
      (buttonRect.left + buttonRect.right) / 2, (buttonRect.top + buttonRect.bottom) / 2)
    return {
      text: target.textContent?.trim(), lines: ink.length, fragmentsReachable,
      pinsClear: labelRect.left >= scrollRect.left - 1 && labelRect.right + 1 < availableRect.left
        && availableRect.right <= actionsRect.left + 1 && actionsRect.right <= scrollRect.right + 1,
      actionHit: !!actionButton && !!buttonHit && actionButton.contains(buttonHit),
      noEllipsis: getComputedStyle(target).textOverflow !== 'ellipsis',
    }
  }, { header, expected, spanText })
  expect(measured, `${header}: ${expected} must be fully readable and uncovered`).not.toBeNull()
  expect(measured?.text).toBe(expected)
  expect(measured?.fragmentsReachable).toBe(true)
  expect(measured?.pinsClear).toBe(true)
  expect(measured?.actionHit).toBe(true)
  expect(measured?.noEllipsis).toBe(true)
  if (spanText && expected === productName) expect(measured?.lines).toBeGreaterThan(1)
}

async function assertDesktopPosition(page: Page, position: Position): Promise<void> {
  const row = stockRow(page, position.labelId)
  await expect(row).toBeVisible()
  await desktopInk(row, 'Ürün', productCode, true)
  await desktopInk(row, 'Ürün', productName, true)
  await desktopInk(row, 'Lokasyon', position.locationDisplay.locationCode, true)
  await desktopInk(row, 'Lokasyon', position.locationDisplay.locationName, true)
  for (const [index, label] of QUANTITIES.entries()) {
    await desktopInk(row, label, trNumber(amountOf(position)[index]))
  }
}

/** The phone cards have no pinned columns; every ink fragment must fit within its visible card and be hit-testable. */
async function assertMobilePosition(page: Page, position: Position): Promise<void> {
  const card = stockCard(page, position.labelId)
  await expect(card).toBeVisible()
  const measured = await card.evaluate((element, input) => {
    element.scrollIntoView({ block: 'center' })
    const spans = [...element.querySelectorAll('span')]
    const name = spans.find((node) => node.textContent === input.name)
    const code = spans.find((node) => node.textContent === input.code)
    const locationCode = spans.find((node) => node.textContent === input.locationCode)
    const locationName = spans.find((node) => node.textContent === input.locationName)
    const facts = [...element.querySelectorAll('dl:last-of-type > div')]
    const amounts = input.labels.map((label) => {
      const fact = facts.find((node) => node.querySelector('dt')?.textContent?.trim() === label)
      return fact?.querySelector('dd') ?? null
    })
    if (!name || !code || !locationCode || !locationName || amounts.some((node) => node === null)) return null
    const cardRect = element.getBoundingClientRect()
    const inspect = (node: Element) => {
      const range = document.createRange()
      range.selectNodeContents(node)
      const rects = [...range.getClientRects()].filter((rect) => rect.width > 0 && rect.height > 0)
      return {
        text: node.textContent?.trim(), lines: rects.length,
        reachable: rects.length > 0 && rects.every((rect) => {
          const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2)
          return rect.left >= cardRect.left - 1 && rect.right <= cardRect.right + 1
            && rect.left >= 0 && rect.right <= window.innerWidth + 1
            && rect.top >= cardRect.top - 1 && rect.bottom <= cardRect.bottom + 1
            && hit !== null && element.contains(hit)
        }),
        noEllipsis: getComputedStyle(node).textOverflow !== 'ellipsis',
      }
    }
    return {
      name: inspect(name), code: inspect(code),
      locationCode: inspect(locationCode), locationName: inspect(locationName),
      amounts: amounts.map((node) => inspect(node as Element)),
    }
  }, {
    name: productName, code: productCode,
    locationCode: position.locationDisplay.locationCode,
    locationName: position.locationDisplay.locationName,
    labels: QUANTITIES,
  })
  expect(measured).not.toBeNull()
  expect(measured?.code).toMatchObject({ text: productCode, reachable: true, noEllipsis: true })
  expect(measured?.name).toMatchObject({ text: productName, reachable: true, noEllipsis: true })
  expect(measured?.locationCode).toMatchObject({
    text: position.locationDisplay.locationCode, reachable: true, noEllipsis: true,
  })
  expect(measured?.locationName).toMatchObject({
    text: position.locationDisplay.locationName, reachable: true, noEllipsis: true,
  })
  expect(measured?.name.lines).toBeGreaterThan(1)
  for (const [index, value] of amountOf(position).entries()) {
    expect(measured?.amounts[index]).toMatchObject({ text: trNumber(value), reachable: true, noEllipsis: true })
  }
}

async function assertMenu(page: Page, labelId: string, labelCode: string, cards: boolean): Promise<void> {
  const row = cards ? stockCard(page, labelId) : stockRow(page, labelId)
  const toggle = row.getByRole('button', { name: `${labelCode} işlemleri`, exact: true })
  await toggle.click()
  const list = row.getByRole('list', { name: `${labelCode} için işlemler`, exact: true })
  await expect(list).toBeVisible()
  await expect(list.getByRole('link', { name: 'Etiket 360’ı aç', exact: true })).toBeVisible()
  await toggle.press('Escape')
  await expect(list).toHaveCount(0)
}

test('ST-01 shows long genuine Product and exact stock quantities at desktop and phone widths, then follows cursor, filters and row actions', async ({ page }) => {
  test.setTimeout(240_000)
  const stockDisplayVersions: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'GET' && new URL(request.url()).pathname === POSITIONS) {
      stockDisplayVersions.push(request.headers()[STOCK_DISPLAY_HEADER.toLowerCase()] ?? '')
    }
  })
  expect(productCode).toBe('SAC-1')
  expect(productName).toHaveLength(70)
  const lines = Array.from({ length: LABEL_COUNT }, (_, index) => ({
    lineId: randomUUID(), labelId: randomUUID(), labelCode: `${PREFIX}-${String(index).padStart(2, '0')}`,
    lotId: randomUUID(), heatNumber: `SYNDS01-HEAT-${index}`, coilNumber: null,
    productId, productVersion, quantityKg: index === 0 ? EXTREME : index === 1 ? NORMAL : String(index + 1),
  }))
  const created = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
    headers: { ...headers(recorderActor), 'Content-Type': 'application/json', 'Idempotency-Key': `st01-responsive-${randomUUID()}` },
    data: { countId: randomUUID(), locationId, countedOn: '2026-09-18',
      sheetReference: `SENTETIK browser-suite:r14 STOCK-DS-01 ${PREFIX}`, lines },
  })
  expect(created.status()).toBe(201)
  const stored = await created.json() as { readonly lines: readonly { readonly labelId: string; readonly quantityKg: string }[] }
  expect(stored.lines).toHaveLength(LABEL_COUNT)
  expect(stored.lines.map((line) => line.labelId).sort()).toEqual(lines.map((line) => line.labelId).sort())

  // Bind the Inventory projection to Organization's own public row, not to a string copied into this test.
  const locations = await page.request.get(`${env.apiBaseUrl}${LOCATIONS}`, { headers: headers(arrangerActor) })
  expect(locations.status()).toBe(200)
  const locationRows = (await locations.json() as {
    readonly items: readonly { readonly id: string; readonly code: string; readonly name: string }[]
  }).items.filter((item) => item.id === locationId)
  expect(locationRows).toHaveLength(1)
  const ownerLocation = locationRows[0]

  // The normal profile's existing group administrator has both stock-list and Organization location-detail access.
  // Select it explicitly and enter through the shell root so company/capability context is committed before the
  // filtered deep link. It proves that the exact row location opens its real owner card without a disposable grant.
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.setExtraHTTPHeaders({ 'X-Development-User': AUTHORIZED_ACTOR })
  await page.goto('/')
  await expect(page.getByRole('link', { name: 'Stok / Malzeme Pozisyonu', exact: true })).toBeVisible()
  const authorized = await openList(page, PREFIX)
  expect(authorized.items).toHaveLength(25)
  const authorizedRow = stockRow(page, authorized.items[0].labelId)
  await expect(authorizedRow).toBeVisible()
  await expect(authorizedRow.getByRole('link', { name: 'Lokasyon kartı', exact: true }))
    .toHaveAttribute('href', `/ayarlar/organizasyon/lokasyon/${locationId}`)

  // The existing stock counter can read the same physical rows but has no Organization location read grant.
  // A full navigation rereads shell capabilities under that actor; code and name remain visible, the card link does not.
  await page.setExtraHTTPHeaders({ 'X-Development-User': counterActor })
  const first = await openList(page, PREFIX)
  expect(first.items).toHaveLength(25)
  expect(first.nextCursor).toBeTruthy()
  const counterRow = stockRow(page, first.items[0].labelId)
  await expect(counterRow).toBeVisible()
  await expect(counterRow.getByRole('link', { name: 'Lokasyon kartı', exact: true })).toHaveCount(0)
  await expect(page.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' }).locator('tbody tr')).toHaveCount(25)
  const next = page.waitForResponse((response) => isPositionRead(response, PREFIX, first.nextCursor!))
  await page.getByRole('button', { name: 'Daha fazla göster', exact: true }).click()
  const nextResponse = await next
  expect(nextResponse.status()).toBe(200)
  const second = await nextResponse.json() as PositionPage
  expect(second.items).toHaveLength(1)
  expect(second.nextCursor).toBeNull()
  const all = [...first.items, ...second.items]
  expect(new Set(all.map((item) => item.labelId)).size).toBe(LABEL_COUNT)
  expect(all.map((item) => item.labelCode).sort()).toEqual(lines.map((line) => line.labelCode).sort())
  for (const position of all) {
    expect(position.companyId).toBe(companyId)
    expect(position.locationId).toBe(locationId)
    expect(position.locationDisplay.locationId).toBe(position.locationId)
    expect(position.locationDisplay.locationCode.trim()).toBe(position.locationDisplay.locationCode)
    expect(position.locationDisplay.locationCode).not.toBe('')
    expect(position.locationDisplay.locationName.trim()).toBe(position.locationDisplay.locationName)
    expect(position.locationDisplay.locationName).not.toBe('')
    expect(position.locationDisplay).toEqual({
      locationId, locationCode: ownerLocation.code, locationName: ownerLocation.name,
    })
  }
  const extreme = all.find((item) => item.labelId === lines[0].labelId)
  const normal = all.find((item) => item.labelId === lines[1].labelId)
  expect(extreme).toMatchObject({ productId, physicalKg: EXTREME, blockedPhysicalKg: '0', activeAllocationKg: '0', availableKg: EXTREME,
    productDisplay: { identity: { code: productCode, name: productName } } })
  expect(normal).toMatchObject({ productId, physicalKg: NORMAL, blockedPhysicalKg: '0', activeAllocationKg: '0', availableKg: NORMAL,
    productDisplay: { identity: { code: productCode, name: productName } } })
  await expect(page.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' }).locator('tbody tr')).toHaveCount(LABEL_COUNT)

  for (const width of [1440, 1366, 1024]) {
    await page.setViewportSize({ width, height: 900 })
    await expect(page.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' })).toBeVisible()
    await assertDesktopPosition(page, extreme!)
    await assertDesktopPosition(page, normal!)
    await assertMenu(page, all[0].labelId, all[0].labelCode, false)
    await assertMenu(page, all[all.length - 1].labelId, all[all.length - 1].labelCode, false)
  }

  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByTestId('stock-position-cards').locator('li')).toHaveCount(LABEL_COUNT)
  await assertMobilePosition(page, extreme!)
  await assertMobilePosition(page, normal!)
  await expect(stockCard(page, extreme!.labelId).getByRole('link', { name: 'Lokasyon kartı', exact: true })).toHaveCount(0)
  await assertMenu(page, all[0].labelId, all[0].labelCode, true)
  await assertMenu(page, all[all.length - 1].labelId, all[all.length - 1].labelCode, true)

  // The row's existing Product filter follows its real id, and the URL and response carry the same request.
  const productFiltered = page.waitForResponse((response) => isPositionRead(response, PREFIX)
    && new URL(response.url()).searchParams.get('productId') === productId)
  await stockCard(page, extreme!.labelId).getByRole('button', { name: `${lines[0].labelCode}: bu ürüne göre filtrele` }).click()
  expect((await productFiltered).status()).toBe(200)
  await expect(page).toHaveURL(new RegExp(`etiket=${PREFIX}.*urun=${productId}`))
  await expect(page.getByRole('list', { name: 'Etkin filtreler' })).toContainText(productName)

  // Location filtering carries the row's exact id; the returned rows keep the same owner-bound display tuple.
  const locationFiltered = page.waitForResponse((response) => isPositionRead(response, PREFIX)
    && new URL(response.url()).searchParams.get('productId') === productId
    && new URL(response.url()).searchParams.get('locationId') === locationId)
  await stockCard(page, extreme!.labelId)
    .getByRole('button', { name: `${lines[0].labelCode}: bu lokasyona göre filtrele` }).click()
  const locationResponse = await locationFiltered
  expect(locationResponse.status()).toBe(200)
  expect(await locationResponse.request().headerValue(STOCK_DISPLAY_HEADER)).toBe(STOCK_DISPLAY_VERSION)
  const locationPage = await locationResponse.json() as PositionPage
  expect(locationPage.items.length).toBeGreaterThan(0)
  expect(locationPage.items.every((item) => item.locationId === locationId
    && item.locationDisplay.locationId === item.locationId)).toBe(true)
  await expect(page).toHaveURL(new RegExp(`urun=${productId}.*lokasyon=${locationId}`))
  await expect(page.getByRole('list', { name: 'Etkin filtreler' })).toContainText(
    `${extreme!.locationDisplay.locationCode} ${extreme.locationDisplay.locationName}`)

  // The label link and its existing back link return to the same label filter after a fresh owner read.
  await openList(page, PREFIX)
  const chosen = first.items[0]
  const detail = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === `/api/v1/inventory/labels/${chosen.labelId}/detail`)
  await stockCard(page, chosen.labelId).getByRole('button', { name: `${chosen.labelCode} işlemleri` }).click()
  await stockCard(page, chosen.labelId).getByRole('list', { name: `${chosen.labelCode} için işlemler` })
    .getByRole('link', { name: 'Etiket 360’ı aç' }).click()
  expect((await detail).status()).toBe(200)
  await expect(page).toHaveURL(new RegExp(`/stok/etiket/${chosen.labelId}$`))
  const returned = page.waitForResponse((response) => isPositionRead(response, PREFIX))
  await page.getByRole('link', { name: '← Stok pozisyonlarına dön', exact: true }).click()
  expect((await returned).status()).toBe(200)
  await expect(page).toHaveURL(new RegExp(`/stok/pozisyonlar\\?etiket=${PREFIX}$`))
  await expect(page.getByLabel('Etiket kodu', { exact: true })).toHaveValue(PREFIX)

  // The same actor cannot move the read into the other company, and no browser read silently retries 1.0 or legacy.
  const foreign = await page.request.get(`${env.apiBaseUrl}${POSITIONS}?q=${encodeURIComponent(PREFIX)}&limit=25`, {
    headers: { ...headers(counterActor), 'X-Company-Id': otherCompanyId, [STOCK_DISPLAY_HEADER]: STOCK_DISPLAY_VERSION },
  })
  expect(foreign.status()).toBe(403)
  expect(stockDisplayVersions.length).toBeGreaterThan(0)
  expect(stockDisplayVersions.every((version) => version === STOCK_DISPLAY_VERSION)).toBe(true)
})
