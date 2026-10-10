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
  recorderActor, arrangerActor, counterActor, ownerPartyId, ownerCompanyAccountId } = env.seed
const POSITIONS = '/api/v1/inventory/positions'
const OPENINGS = '/api/v1/inventory/opening-counts'
const LOCATIONS = '/api/v1/organization/locations?page=1&pageSize=100'
const STOCK_DISPLAY_HEADER = 'X-ERP-Inventory-Stock-Display'
const STOCK_DISPLAY_VERSION = '3.0'
const COUNTERPARTIES = '/api/v1/counterparties'
/** The Cari owner's negotiated client contract (`CounterpartyClientContract`), required on every Cari read. */
const CARI_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
const CARI_CONTRACT_VERSION = 'optional-trade-roles-v1'
const OWNER_PREFIX = `SYNOWN3-${randomUUID().slice(0, 8).toUpperCase()}`
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
  readonly ownerKind: 'Company' | 'Counterparty'
  readonly ownerId: string
  /** STOCK-OWNER-DISPLAY-3 (#664): the Cari owner's identity, and null exactly for a Company owner. */
  readonly ownerDisplay: OwnerDisplay | null
}
type OwnerDisplay = {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
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

/** The "Mal sahibi" fact of a phone card. */
function ownerFact(card: Locator): Locator {
  return card.locator('dl').first().locator('div').filter({ has: card.page().locator('dt', { hasText: /^Mal sahibi$/ }) }).locator('dd')
}

/** The Cari owner's identity as the screen draws it: `KOD — Unvan`. */
function identityOf(owner: OwnerDisplay): string {
  return `${owner.code} — ${owner.legalName}`
}

async function expectNoPageOverflow(page: Page, where: string): Promise<void> {
  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
  )
  expect(overflows, `the page scrolls horizontally on ${where}`).toBe(false)
}

/**
 * The whole identity is drawn inside its own row or card: every ink fragment is within the viewport and within
 * that container, hit-testable there, and not ellipsised. Scrolls the table sideways the way a user would when a
 * pinned column covers the cell.
 */
async function assertOwnerIdentityReadable(container: Locator, labelId: string, expected: string, where: string): Promise<void> {
  const measured = await container.evaluate((element, input) => {
    element.scrollIntoView({ block: 'center', inline: 'nearest' })
    const target = element.querySelector(`[data-testid="stock-owner-${input.labelId}"]`)
    if (!target) return null
    const scroll = element.closest('table')?.parentElement ?? null
    if (scroll) {
      const cells = [...element.querySelectorAll('td')]
      const headers = [...(element.closest('table')?.querySelectorAll('thead th') ?? [])]
      const label = cells[0]
      const available = cells[headers.findIndex((candidate) => candidate.textContent?.trim() === 'Kullanılabilir kg')]
      for (let attempt = 0; attempt < 3 && label && available; attempt += 1) {
        const rect = target.getBoundingClientRect()
        const left = label.getBoundingClientRect().right + 2
        const right = available.getBoundingClientRect().left - 2
        if (rect.left < left) scroll.scrollLeft += rect.left - left
        else if (rect.right > right) scroll.scrollLeft += rect.right - right
        else break
      }
    }
    const box = element.getBoundingClientRect()
    const range = document.createRange()
    range.selectNodeContents(target)
    const ink = [...range.getClientRects()].filter((rect) => rect.width > 0 && rect.height > 0)
    return {
      text: target.textContent,
      lines: ink.length,
      reachable: ink.length > 0 && ink.every((rect) => {
        const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2)
        return rect.left >= 0 && rect.right <= window.innerWidth + 1
          && rect.left >= box.left - 1 && rect.right <= box.right + 1
          && rect.top >= box.top - 1 && rect.bottom <= box.bottom + 1
          && hit !== null && target.contains(hit)
      }),
      noEllipsis: getComputedStyle(target).textOverflow !== 'ellipsis',
    }
  }, { labelId })
  expect(measured, `the owner identity is missing on ${where}`).not.toBeNull()
  expect(measured?.text, where).toBe(expected)
  expect(measured?.reachable, `the owner identity is covered or cut on ${where}`).toBe(true)
  expect(measured?.noEllipsis, where).toBe(true)
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

  // The disposable recorder has both the existing stock-list reads and Organization location-detail access.
  // Select it explicitly and enter through the shell root so company/capability context is committed before the
  // filtered deep link. The stock counter remains the independent no-link actor below.
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.setExtraHTTPHeaders({ 'X-Development-User': recorderActor })
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
    // STOCK-OWNER-DISPLAY-3: the company's own stock has no owner display — the key is there, and it is null.
    expect(position.ownerKind).toBe('Company')
    expect(position.ownerId).toBe(companyId)
    expect(Object.prototype.hasOwnProperty.call(position, 'ownerDisplay')).toBe(true)
    expect(position.ownerDisplay).toBeNull()
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
    await desktopInk(stockRow(page, extreme!.labelId), 'Mal sahibi', 'Şirket malı', true)
    await assertMenu(page, all[0].labelId, all[0].labelCode, false)
    await assertMenu(page, all[all.length - 1].labelId, all[all.length - 1].labelCode, false)
  }

  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByTestId('stock-position-cards').locator('li')).toHaveCount(LABEL_COUNT)
  await assertMobilePosition(page, extreme!)
  await assertMobilePosition(page, normal!)
  await expect(ownerFact(stockCard(page, extreme!.labelId))).toHaveText('Şirket malı')
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
    `${extreme!.locationDisplay.locationCode} ${extreme!.locationDisplay.locationName}`)

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

/**
 * STOCK-OWNER-DISPLAY-3 (#664 consumer) on the same disposable stack. The R14 seed opens ONE synthetic Cari owner
 * through the public Cari create; this test records two labels it owns and one the company owns through the real
 * opening command, then reads the real stock page. The expected identity is the Cari owner's own public row — never
 * a string kept in this test — and nothing is mocked.
 */
test('ST-01 names a Cari owner exactly, links its card only for the capability holder, and keeps a company owner as "Şirket malı"', async ({ page }) => {
  test.setTimeout(240_000)
  const positionReads: { readonly version: string; readonly company: string }[] = []
  const cariReads: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'GET' && url.pathname === POSITIONS) {
      positionReads.push({
        version: request.headers()[STOCK_DISPLAY_HEADER.toLowerCase()] ?? '',
        company: request.headers()['x-company-id'] ?? '',
      })
    }
    if (url.pathname.startsWith(COUNTERPARTIES)) cariReads.push(url.pathname)
  })

  // The Cari owner's own public row is the truth the stock page must repeat. Only the recorder may read it.
  const cari = await page.request.get(`${env.apiBaseUrl}${COUNTERPARTIES}/${ownerPartyId}`, {
    headers: { ...headers(recorderActor), [CARI_CONTRACT_HEADER]: CARI_CONTRACT_VERSION },
  })
  expect(cari.status()).toBe(200)
  const party = await cari.json() as {
    readonly id: string; readonly code: string; readonly legalName: string; readonly tradeName: string | null
    readonly companyAccounts: readonly { readonly id: string; readonly companyId: string }[]
  }
  expect(party.id).toBe(ownerPartyId)
  expect(party.companyAccounts.filter((account) => account.companyId === companyId).map((account) => account.id))
    .toEqual([ownerCompanyAccountId])
  const owner: OwnerDisplay = { partyId: party.id, code: party.code, legalName: party.legalName, tradeName: party.tradeName }
  expect(owner.code.trim()).not.toBe('')
  // The fixture's owner is long on purpose and has a trade name that differs from the legal name.
  expect(owner.legalName.length).toBeGreaterThan(90)
  expect(owner.tradeName).not.toBeNull()
  expect(owner.tradeName).not.toBe(owner.legalName)
  const identity = identityOf(owner)
  const restrictedCari = await page.request.get(`${env.apiBaseUrl}${COUNTERPARTIES}/${ownerPartyId}`, {
    headers: { ...headers(counterActor), [CARI_CONTRACT_HEADER]: CARI_CONTRACT_VERSION },
  })
  expect(restrictedCari.status(), 'the stock counter must hold no Cari read').toBe(403)

  // Two Cari-owned labels and one company-owned label, through the real opening command.
  const cariLines = [0, 1].map((index) => ({
    lineId: randomUUID(), labelId: randomUUID(), labelCode: `${OWNER_PREFIX}-C${index}`,
    lotId: randomUUID(), heatNumber: `SYNOWN3-HEAT-${index}`, coilNumber: null,
    productId, productVersion, quantityKg: String(100 + index),
  }))
  const companyLine = {
    lineId: randomUUID(), labelId: randomUUID(), labelCode: `${OWNER_PREFIX}-S0`,
    lotId: randomUUID(), heatNumber: 'SYNOWN3-HEAT-S', coilNumber: null,
    productId, productVersion, quantityKg: '50',
  }
  const open = (lines: readonly unknown[], positionContext?: unknown) => page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
    headers: { ...headers(recorderActor), 'Content-Type': 'application/json', 'Idempotency-Key': `st01-owner-${randomUUID()}` },
    data: {
      countId: randomUUID(), locationId, countedOn: '2026-09-18',
      sheetReference: `SENTETIK browser-suite:r14 STOCK-OWNER-DISPLAY-3 ${OWNER_PREFIX}`, lines,
      ...(positionContext === undefined ? {} : { positionContext }),
    },
  })
  expect((await open(cariLines, {
    ownerKind: 'Counterparty', ownerId: ownerPartyId, ownerCompanyPartyAccountId: ownerCompanyAccountId,
    custodianKind: 'Company', custodianId: companyId,
  })).status()).toBe(201)
  expect((await open([companyLine])).status()).toBe(201)
  const cariLabelIds: readonly string[] = cariLines.map((line) => line.labelId)

  const assertPage = (pageBody: PositionPage) => {
    expect(pageBody.items.map((item) => item.labelCode).sort())
      .toEqual([...cariLines.map((line) => line.labelCode), companyLine.labelCode].sort())
    for (const item of pageBody.items) {
      expect(item.companyId).toBe(companyId)
      expect(Object.prototype.hasOwnProperty.call(item, 'ownerDisplay')).toBe(true)
      if (cariLabelIds.includes(item.labelId)) {
        expect(item.ownerKind).toBe('Counterparty')
        expect(item.ownerId).toBe(ownerPartyId)
        // Exactly the Cari owner's row: four members, bound to this row's own owner id.
        expect(item.ownerDisplay).toEqual(owner)
      } else {
        expect(item.ownerKind).toBe('Company')
        expect(item.ownerId).toBe(companyId)
        expect(item.ownerDisplay).toBeNull()
      }
    }
  }

  // ── Capability holder (recorder), desktop: exact identity, linked to the Cari card. ──
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.setExtraHTTPHeaders({ 'X-Development-User': recorderActor })
  await page.goto('/')
  await expect(page.getByRole('link', { name: 'Stok / Malzeme Pozisyonu', exact: true })).toBeVisible()
  assertPage(await openList(page, OWNER_PREFIX))
  const table = page.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' })
  await expect(table.locator('tbody tr')).toHaveCount(3)
  const cariRow = stockRow(page, cariLabelIds[0])
  const companyRow = stockRow(page, companyLine.labelId)
  await desktopInk(companyRow, 'Mal sahibi', 'Şirket malı', true)
  await expect(companyRow.locator('[data-testid^="stock-owner-"]')).toHaveCount(0)
  await expect(companyRow.locator('a[href^="/cari"]')).toHaveCount(0)
  await expect(cariRow.getByText('Cari malı', { exact: true })).toBeVisible()
  await assertOwnerIdentityReadable(cariRow, cariLabelIds[0], identity, 'the desktop table (capability holder)')
  await expect(cariRow.getByText(owner.tradeName!, { exact: true })).toBeVisible()
  await expect(cariRow).not.toContainText(ownerPartyId)
  const cariLink = cariRow.getByRole('link', { name: identity, exact: true })
  await expect(cariLink).toHaveAttribute('href', `/cari/${ownerPartyId}`)
  // The existing location link and both row menus are still there beside it.
  await expect(cariRow.getByRole('link', { name: 'Lokasyon kartı', exact: true }))
    .toHaveAttribute('href', `/ayarlar/organizasyon/lokasyon/${locationId}`)
  await assertMenu(page, cariLabelIds[0], cariLines[0].labelCode, false)
  await assertMenu(page, companyLine.labelId, companyLine.labelCode, false)
  await expectNoPageOverflow(page, 'the desktop table')
  // Keyboard: the link takes focus and Enter opens that party's own card; Back returns to the same filtered list.
  await cariLink.focus()
  await expect(cariLink).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(new RegExp(`/cari/${ownerPartyId}$`))
  await expect(page.getByRole('heading', { level: 1, name: owner.legalName, exact: true })).toBeVisible({ timeout: 30_000 })
  const returned = page.waitForResponse((response) => isPositionRead(response, OWNER_PREFIX))
  await page.goBack()
  expect((await returned).status()).toBe(200)
  await expect(page).toHaveURL(new RegExp(`/stok/pozisyonlar\\?etiket=${OWNER_PREFIX}$`))
  await expect(stockRow(page, cariLabelIds[0]).getByRole('link', { name: identity, exact: true })).toBeVisible()

  // The owner filter still carries the row's exact owner id, and the chip names the Cari from the same read.
  const ownerFiltered = page.waitForResponse((response) => isPositionRead(response, OWNER_PREFIX)
    && new URL(response.url()).searchParams.get('ownerKind') === 'Counterparty'
    && new URL(response.url()).searchParams.get('ownerId') === ownerPartyId)
  await stockRow(page, cariLabelIds[0])
    .getByRole('button', { name: `${cariLines[0].labelCode}: bu mal sahibine göre filtrele` }).click()
  const ownerResponse = await ownerFiltered
  expect(ownerResponse.status()).toBe(200)
  expect(await ownerResponse.request().headerValue(STOCK_DISPLAY_HEADER)).toBe(STOCK_DISPLAY_VERSION)
  const ownerPage = await ownerResponse.json() as PositionPage
  expect(ownerPage.items.map((item) => item.labelId).sort()).toEqual([...cariLabelIds].sort())
  expect(ownerPage.items.every((item) => item.ownerId === ownerPartyId && item.ownerDisplay?.partyId === ownerPartyId)).toBe(true)
  await expect(page).toHaveURL(new RegExp(`sahipTuru=cari.*sahip=${ownerPartyId}`))
  await expect(page.getByRole('list', { name: 'Etkin filtreler' })).toContainText(`Mal sahibi: ${identity}`)
  await expect(table.locator('tbody tr')).toHaveCount(2)
  await expect(stockRow(page, companyLine.labelId)).toHaveCount(0)

  // ── Capability holder, 390 px card: the same link, reachable by touch. ──
  await page.setViewportSize({ width: 390, height: 844 })
  assertPage(await openList(page, OWNER_PREFIX))
  await expect(page.getByTestId('stock-position-cards').locator('li')).toHaveCount(3)
  await expect(ownerFact(stockCard(page, companyLine.labelId))).toHaveText('Şirket malı')
  const holderCard = stockCard(page, cariLabelIds[0])
  await assertOwnerIdentityReadable(holderCard, cariLabelIds[0], identity, 'the phone card (capability holder)')
  await expect(holderCard.getByText(owner.tradeName!, { exact: true })).toBeVisible()
  await expect(holderCard.getByRole('link', { name: identity, exact: true })).toHaveAttribute('href', `/cari/${ownerPartyId}`)
  await assertMenu(page, cariLabelIds[0], cariLines[0].labelCode, true)
  await expectNoPageOverflow(page, 'the phone cards (capability holder)')

  // ── Restricted reader (stock counter): the identical identity as plain text, on the card and the table. ──
  await page.setExtraHTTPHeaders({ 'X-Development-User': counterActor })
  const cariReadsBefore = cariReads.length
  assertPage(await openList(page, OWNER_PREFIX))
  const readerCard = stockCard(page, cariLabelIds[0])
  await expect(ownerFact(stockCard(page, companyLine.labelId))).toHaveText('Şirket malı')
  await assertOwnerIdentityReadable(readerCard, cariLabelIds[0], identity, 'the phone card (restricted reader)')
  await expect(readerCard.getByText('Cari malı', { exact: true })).toBeVisible()
  await expect(readerCard.getByText(owner.tradeName!, { exact: true })).toBeVisible()
  await expect(readerCard.getByRole('link', { name: identity, exact: true })).toHaveCount(0)
  await expect(page.locator('a[href^="/cari"]')).toHaveCount(0)
  await expect(readerCard).not.toContainText(ownerPartyId)
  await assertMenu(page, cariLabelIds[0], cariLines[0].labelCode, true)
  await expectNoPageOverflow(page, 'the phone cards (restricted reader)')

  await page.setViewportSize({ width: 1440, height: 900 })
  await expect(table.locator('tbody tr')).toHaveCount(3)
  const readerRow = stockRow(page, cariLabelIds[0])
  await assertOwnerIdentityReadable(readerRow, cariLabelIds[0], identity, 'the desktop table (restricted reader)')
  await expect(readerRow.getByRole('link', { name: identity, exact: true })).toHaveCount(0)
  await expect(readerRow.getByRole('link', { name: 'Lokasyon kartı', exact: true })).toHaveCount(0)
  await expect(page.locator('a[href^="/cari"]')).toHaveCount(0)
  await desktopInk(stockRow(page, companyLine.labelId), 'Mal sahibi', 'Şirket malı', true)
  // The identity came with the stock read: the screen asked the Cari owner nothing for this reader.
  expect(cariReads.slice(cariReadsBefore), 'a Cari read was issued for the restricted reader').toEqual([])

  // ── A superseded read cannot put its rows back: the older answer is held, a newer search replaces it. ──
  const emptyQuery = `${OWNER_PREFIX}-YOK`
  let releaseStale: () => void = () => {}
  const staleGate = new Promise<void>((resolve) => { releaseStale = resolve })
  let staleSeen = false
  await page.route((url) => url.pathname === POSITIONS && url.searchParams.get('q') === OWNER_PREFIX, async (route) => {
    staleSeen = true
    const response = await route.fetch()
    await staleGate
    await route.fulfill({ response }).catch(() => {})
  })
  await page.getByLabel('Etiket kodu', { exact: true }).fill(emptyQuery)
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  await expect(page.getByText('Filtrelere uyan fiziksel stok yok.', { exact: true })).toBeVisible()
  await expect(table.locator('tbody tr')).toHaveCount(0)
  // Ask for the owner list again and, while its answer is held, move on to the empty search once more.
  await page.getByLabel('Etiket kodu', { exact: true }).fill(OWNER_PREFIX)
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  await expect.poll(() => staleSeen, { timeout: 20_000 }).toBe(true)
  const superseding = page.waitForResponse((response) => isPositionRead(response, emptyQuery))
  await page.getByLabel('Etiket kodu', { exact: true }).fill(emptyQuery)
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  expect((await superseding).status()).toBe(200)
  releaseStale()
  await page.waitForTimeout(1500)
  await expect(page).toHaveURL(new RegExp(`etiket=${emptyQuery}$`))
  await expect(page.getByText('Filtrelere uyan fiziksel stok yok.', { exact: true })).toBeVisible()
  await expect(page.locator(`[data-label-id="${cariLabelIds[0]}"]`)).toHaveCount(0)
  expect(await page.getByRole('main').innerText()).not.toContain(owner.legalName)
  await page.unroute((url) => url.pathname === POSITIONS && url.searchParams.get('q') === OWNER_PREFIX)

  // ── The other company stays closed, and the answer carries nothing of this owner. ──
  const foreign = await page.request.get(`${env.apiBaseUrl}${POSITIONS}?q=${encodeURIComponent(OWNER_PREFIX)}&limit=25`, {
    headers: { ...headers(counterActor), 'X-Company-Id': otherCompanyId, [STOCK_DISPLAY_HEADER]: STOCK_DISPLAY_VERSION },
  })
  expect(foreign.status()).toBe(403)
  const foreignBody = await foreign.text()
  expect(foreignBody).not.toContain(owner.legalName)
  expect(foreignBody).not.toContain(owner.code)
  expect(foreignBody).not.toContain(ownerPartyId)
  expect(foreignBody).not.toContain(OWNER_PREFIX)

  // Every browser read asked for 3.0, in this company, and never fell back to 2.0, 1.0 or the legacy shape.
  expect(positionReads.length).toBeGreaterThan(0)
  expect(positionReads.every((read) => read.version === STOCK_DISPLAY_VERSION && read.company === companyId)).toBe(true)
})
