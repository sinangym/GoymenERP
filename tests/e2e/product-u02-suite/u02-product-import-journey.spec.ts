import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Response } from '@playwright/test'

/**
 * Q16 Product import on the real u02 stack. The workbook is a minimal deterministic OOXML zip generated in memory;
 * no test-only API, mocked route or fixture Product write participates in the journey.
 */

type Seed = {
  readonly companyId: string
  readonly codeGroupId: string
  readonly authorizedActor: string
  readonly unauthorizedActor: string
}

type Preview = {
  readonly fileSha256: string
  readonly previewToken: string
  readonly sourceRowCount: number
  readonly ready: boolean
}

type ImportedProduct = {
  readonly productId: string
  readonly code: string
  readonly legacyCode: string
  readonly name: string
  readonly groupId: string
}

type ImportResult = {
  readonly products: ImportedProduct[]
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

const PRODUCTS_PATH = '/api/v1/products'
const PREVIEW_PATH = `${PRODUCTS_PATH}/imports/preview`
const APPLY_PATH = `${PRODUCTS_PATH}/imports`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const LEGACY_HEADER = 'Eski Kod'
const NAME_HEADER = 'Ürün Adı'
const GROUP_HEADER = 'Grup Kimliği'

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the Product import journey runs only inside the disposable stack.`)
  return value
}

function seed(): Seed {
  const raw = JSON.parse(required('BROWSER_E2E_U02_SEED')) as Record<string, unknown>
  const text = (key: keyof Seed): string => {
    const value = raw[key]
    if (typeof value !== 'string' || value.trim() === '') throw new Error(`BROWSER_E2E_U02_SEED.${key} is missing.`)
    return value
  }
  return {
    companyId: text('companyId'),
    codeGroupId: text('codeGroupId'),
    authorizedActor: text('authorizedActor'),
    unauthorizedActor: text('unauthorizedActor'),
  }
}

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff
  for (const byte of bytes) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1))
  }
  return (crc ^ 0xffffffff) >>> 0
}

/** Minimal stored-entry ZIP. The importer resolves the first logical worksheet through the OOXML relationships. */
function zip(entries: readonly { readonly name: string, readonly content: string }[]): Buffer {
  const locals: Buffer[] = []
  const centrals: Buffer[] = []
  let offset = 0
  for (const entry of entries) {
    const fileName = Buffer.from(entry.name, 'utf8')
    const data = Buffer.from(entry.content, 'utf8')
    const checksum = crc32(data)
    const local = Buffer.alloc(30)
    local.writeUInt32LE(0x04034b50, 0)
    local.writeUInt16LE(20, 4)
    local.writeUInt16LE(0x0800, 6)
    local.writeUInt16LE(0, 8)
    local.writeUInt16LE(0, 10)
    local.writeUInt16LE(0x0021, 12)
    local.writeUInt32LE(checksum, 14)
    local.writeUInt32LE(data.length, 18)
    local.writeUInt32LE(data.length, 22)
    local.writeUInt16LE(fileName.length, 26)

    const central = Buffer.alloc(46)
    central.writeUInt32LE(0x02014b50, 0)
    central.writeUInt16LE(20, 4)
    central.writeUInt16LE(20, 6)
    central.writeUInt16LE(0x0800, 8)
    central.writeUInt16LE(0, 10)
    central.writeUInt16LE(0, 12)
    central.writeUInt16LE(0x0021, 14)
    central.writeUInt32LE(checksum, 16)
    central.writeUInt32LE(data.length, 20)
    central.writeUInt32LE(data.length, 24)
    central.writeUInt16LE(fileName.length, 28)
    central.writeUInt32LE(offset, 42)

    locals.push(local, fileName, data)
    centrals.push(central, fileName)
    offset += local.length + fileName.length + data.length
  }

  const directoryOffset = offset
  const directorySize = centrals.reduce((length, part) => length + part.length, 0)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(entries.length, 8)
  end.writeUInt16LE(entries.length, 10)
  end.writeUInt32LE(directorySize, 12)
  end.writeUInt32LE(directoryOffset, 16)
  return Buffer.concat([...locals, ...centrals, end])
}

function xml(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&apos;')
}

function workbook(rows: readonly (readonly [string, string, string])[]): Buffer {
  const all = [[LEGACY_HEADER, NAME_HEADER, GROUP_HEADER] as const, ...rows]
  const body = all.map((row, index) => {
    const number = index + 1
    const cells = row.map((value, column) => {
      const reference = `${String.fromCharCode(65 + column)}${number}`
      return `<c r="${reference}" t="inlineStr"><is><t>${xml(value)}</t></is></c>`
    }).join('')
    return `<row r="${number}">${cells}</row>`
  }).join('')
  return zip([
    {
      name: '[Content_Types].xml',
      content: '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/import.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>',
    },
    {
      name: 'xl/workbook.xml',
      content: '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Ürünler" sheetId="1" r:id="rIdImport"/></sheets></workbook>',
    },
    {
      name: 'xl/_rels/workbook.xml.rels',
      content: '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdImport" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/import.xml"/></Relationships>',
    },
    {
      name: 'xl/worksheets/import.xml',
      content: `<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>${body}</sheetData></worksheet>`,
    },
  ])
}

function filePayload(buffer: Buffer, name: string) {
  return { name, mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer }
}

function isResponse(response: Response, path: string, method: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

async function openImport(page: Page): Promise<void> {
  await page.goto('/urun')
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('main').getByRole('link', { name: 'Excel’den içe aktar', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Excel’den ürün içe aktar' })).toBeVisible()
  await page.getByLabel('Eski kod sütunu').fill(LEGACY_HEADER)
  await page.getByLabel('Ürün adı sütunu').fill(NAME_HEADER)
  await page.getByLabel('Grup kimliği sütunu').fill(GROUP_HEADER)
}

test('U02-IMPORT invalid rows download errors, then one valid batch applies/replays atomically and reopens every card', async ({ page }) => {
  test.setTimeout(180_000)
  const s = seed()
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL').replace(/\/$/, '')
  const tag = randomUUID().slice(0, 8)
  const duplicate = `LEG-${tag}`
  const invalidWorkbook = workbook([
    [duplicate, `SENTETIK import invalid A ${tag}`, s.codeGroupId],
    [duplicate, `SENTETIK import invalid B ${tag}`, s.codeGroupId],
  ])
  const validWorkbook = workbook([
    [`LEG-${tag}-1`, `SENTETIK import valid A ${tag}`, s.codeGroupId],
    [`LEG-${tag}-2`, `SENTETIK import valid B ${tag}`, s.codeGroupId],
  ])

  await openImport(page)

  await test.step('a rejected preview writes nothing and exposes its downloadable UTF-8 error list', async () => {
    await page.getByLabel('Excel dosyası (.xlsx)').setInputFiles(filePayload(invalidWorkbook, `invalid-${tag}.xlsx`))
    const response = page.waitForResponse(candidate => isResponse(candidate, PREVIEW_PATH, 'POST'))
    await page.getByRole('button', { name: 'Önizle', exact: true }).click()
    expect((await response).status(), 'the real invalid preview').toBe(200)
    await expect(page.getByText('Dosyada aynı eski kod birden çok kez bulunuyor.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Tümünü içe aktar', exact: true })).toHaveCount(0)

    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Hata listesini indir', exact: true }).click()
    const stream = await (await download).createReadStream()
    if (stream === null) throw new Error('The Product import error download stream is unavailable.')
    const chunks: Buffer[] = []
    for await (const chunk of stream) chunks.push(Buffer.from(chunk))
    const csv = Buffer.concat(chunks)
    expect([...csv.subarray(0, 3)], 'UTF-8 BOM').toEqual([0xef, 0xbb, 0xbf])
    expect(csv.subarray(3).toString('utf8')).toContain('repeated_legacy_code')
    expect(csv.toString('utf8')).not.toContain(`SENTETIK import valid A ${tag}`)
  })

  let preview!: Preview
  let imported!: ImportResult
  let idempotencyKey!: string
  await test.step('the valid workbook previews and applies as one real command', async () => {
    await page.getByLabel('Excel dosyası (.xlsx)').setInputFiles(filePayload(validWorkbook, `valid-${tag}.xlsx`))
    const previewAnswer = page.waitForResponse(candidate => isResponse(candidate, PREVIEW_PATH, 'POST'))
    await page.getByRole('button', { name: 'Önizle', exact: true }).click()
    const response = await previewAnswer
    expect(response.status(), 'the real valid preview').toBe(200)
    preview = await response.json() as Preview
    expect(preview).toMatchObject({ sourceRowCount: 2, ready: true })
    await expect(page.getByText('2 satır içe aktarmaya hazır.')).toBeVisible()

    const applyAnswer = page.waitForResponse(candidate => isResponse(candidate, APPLY_PATH, 'POST'))
    await page.getByRole('button', { name: 'Tümünü içe aktar', exact: true }).click()
    const applied = await applyAnswer
    expect(applied.status(), 'the real atomic apply').toBe(201)
    idempotencyKey = (await applied.request().allHeaders())['idempotency-key'] ?? ''
    expect(idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    imported = await applied.json() as ImportResult
    expect(imported.isReplay).toBe(false)
    expect(imported.products).toHaveLength(2)
    expect(new Set(imported.products.map(product => product.productId)).size).toBe(2)
    expect(imported.products.map(product => product.legacyCode).sort()).toEqual([`LEG-${tag}-1`, `LEG-${tag}-2`])
    for (const product of imported.products) {
      expect(product.groupId).toBe(s.codeGroupId)
      await expect(page.getByRole('link', { name: `${product.code} — ${product.name}`, exact: true }))
        .toHaveAttribute('href', `/urun/${product.productId}`)
    }
  })

  await test.step('the exact command replay returns the same products and adds no Product mutation', async () => {
    const replay = await page.request.post(`${apiBaseUrl}${APPLY_PATH}`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: s.authorizedActor,
        'X-Company-Id': s.companyId,
        'Idempotency-Key': idempotencyKey,
      },
      multipart: {
        file: filePayload(validWorkbook, `valid-${tag}.xlsx`),
        legacyCodeColumn: LEGACY_HEADER,
        nameColumn: NAME_HEADER,
        groupIdColumn: GROUP_HEADER,
        fileSha256: preview.fileSha256,
        previewToken: preview.previewToken,
      },
    })
    expect(replay.status(), 'the exact stored terminal replay').toBe(201)
    const body = await replay.json() as ImportResult
    expect(body.isReplay).toBe(true)
    expect(body.originalCorrelationId).toBe(imported.originalCorrelationId)
    expect(body.products).toEqual(imported.products)
  })

  await test.step('closing the result and reopening each created card proves the durable server identities', async () => {
    for (const product of imported.products) {
      await page.goto('about:blank')
      const cardRead = page.waitForResponse(candidate => isResponse(candidate, `${PRODUCTS_PATH}/${product.productId}`, 'GET'))
      const historyRead = page.waitForResponse(candidate => isResponse(candidate, `${PRODUCTS_PATH}/${product.productId}/history`, 'GET'))
      await page.goto(`/urun/${product.productId}`)
      expect((await cardRead).status(), 'the imported card reopens through the real read').toBe(200)
      const history = await historyRead
      expect(history.status(), 'the imported card audit history').toBe(200)
      expect(((await history.json()) as { entries: unknown[] }).entries).toHaveLength(1)
      await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible()
      await expect(page.getByText(product.code, { exact: true })).toBeVisible()
    }
  })
})

test('U02-IMPORT an unauthorized actor sees no route and preview discloses no workbook values', async ({ page }) => {
  const s = seed()
  const unauthorizedBaseUrl = required('BROWSER_E2E_UNAUTHORIZED_BASE_URL')
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL').replace(/\/$/, '')
  const marker = `SENTETIK-DENIED-${randomUUID().slice(0, 8)}`
  const deniedWorkbook = workbook([[marker, marker, s.codeGroupId]])
  const traffic: string[] = []
  page.on('request', request => {
    if (new URL(request.url()).pathname.startsWith(PRODUCTS_PATH)) traffic.push(request.url())
  })
  await page.goto(new URL('/urun/ice-aktar', unauthorizedBaseUrl).toString())
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  expect(traffic).toEqual([])

  const denied = await page.request.post(`${apiBaseUrl}${PREVIEW_PATH}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: s.unauthorizedActor, 'X-Company-Id': s.companyId },
    multipart: {
      file: filePayload(deniedWorkbook, 'denied.xlsx'),
      legacyCodeColumn: LEGACY_HEADER,
      nameColumn: NAME_HEADER,
      groupIdColumn: GROUP_HEADER,
    },
  })
  expect(denied.status()).toBe(403)
  const refusal = await denied.text()
  expect(refusal).not.toContain(marker)
  expect(refusal).not.toContain(s.codeGroupId)
})
