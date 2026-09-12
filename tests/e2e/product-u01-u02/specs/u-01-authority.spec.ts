import { expect, test } from '@playwright/test'
import { Lane, json, shell } from '../src/harness'
import { PRODUCT_1, PRODUCT_CAP } from '../src/fixtures'
import { neutralBlock } from '../src/ui'

test('U-01 without a published Product capability: the neutral block, no nav entry, no Product read', async ({ page }, info) => {
  // Today's real server publishes no product.* id, so this is also what the running
  // app shows for the Product routes until it does.
  const lane = await Lane.install(page, { slug: 'u01-no-capability', capabilities: () => ['cari.parties.list'] })
  try {
    await page.goto('/urun')
    await expect(neutralBlock(page)).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'Ürünler' })).toHaveCount(0)
    expect((await shell(page)).productCapabilities).toEqual([])
    expect(lane.product()).toHaveLength(0)

    await page.goto(`/urun/${PRODUCT_1}`)
    await expect(neutralBlock(page)).toBeVisible()
    expect(lane.product()).toHaveLength(0)
    await lane.finish(info, { scope: 'U-01', nonDisclosure: true })
  } finally { lane.dispose() }
})

test('U-01 list 403 closes the route to the same neutral block, without disclosing what exists', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u01-list-403', list: () => json({ code: 'forbidden' }, 403) })
  try {
    await page.goto('/urun')
    await expect(neutralBlock(page)).toBeVisible()
    await expect(page.getByRole('table')).toHaveCount(0)
    await expect(page.getByText('SAC-42')).toHaveCount(0)
    // The boundary re-fenced the capability: the shell now holds no product grant.
    await expect.poll(async () => (await shell(page)).productCapabilities).toEqual([])
    await lane.finish(info, { scope: 'U-01', refenced: true })
  } finally { lane.dispose() }
})

test('U-01 card 403 for a record id closes the route the same way — the id is not confirmed', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u01-card-403', card: () => json({ code: 'forbidden' }, 403) })
  try {
    await page.goto(`/urun/${PRODUCT_1}`)
    await expect(neutralBlock(page)).toBeVisible()
    await expect(page.getByText('Siyah sac')).toHaveCount(0)
    await expect(page.getByText('SAC-42')).toHaveCount(0)
    await lane.finish(info, { scope: 'U-01', nonDisclosure: true })
  } finally { lane.dispose() }
})

test('U-01 detail is its own grant: list without detail shows the code as text, not a link, and reads no card', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u01-list-only', capabilities: () => [PRODUCT_CAP.list] })
  try {
    await page.goto('/urun')
    await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
    await expect(page.getByText('SAC-42')).toBeVisible()
    await expect(page.getByRole('link', { name: 'SAC-42' })).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'Yeni ürün' })).toHaveCount(0)
    expect(lane.of('card')).toHaveLength(0)
    await lane.finish(info, { scope: 'U-01', separateGrants: true })
  } finally { lane.dispose() }
})
