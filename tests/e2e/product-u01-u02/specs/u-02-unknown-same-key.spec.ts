import { expect, test } from '@playwright/test'
import { Lane, go, json, pending, shell } from '../src/harness'
import { mutation } from '../src/fixtures'
import { fillMinimum, neutralBlock, openCreate, retrySame, saveDraft } from '../src/ui'

test('U-02 unknown outcome: resubmitting the unchanged form and the retry both re-send the SAME key and body; the replay is accepted', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'u02-unknown-same-key',
    create: ({ nth }) => (nth <= 2 ? json({}, 503) : json(mutation({ isReplay: true }), 201)),
  })
  try {
    await openCreate(page)
    await fillMinimum(page)
    await saveDraft(page).click()
    await expect(page.getByTestId('product-create-unknown')).toBeVisible()

    // The unchanged form submitted again IS the same attempt: no second key.
    await saveDraft(page).click()
    await expect.poll(() => lane.of('create').length).toBe(2)
    await expect(page.getByTestId('product-create-unknown')).toBeVisible()

    await retrySame(page).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
    await expect(page.getByText('Bu istek daha önce işlenmişti; ikinci bir kayıt oluşturulmadı.')).toBeVisible()

    const posts = lane.of('create')
    expect(posts).toHaveLength(3)
    for (const post of posts) {
      expect(post.key).toBe(posts[0].key)
      expect(post.payloadSha256).toBe(posts[0].payloadSha256)
    }
    await lane.finish(info, { scope: 'U-02', sameKey: true, sameBody: true, sends: 3 })
  } finally { lane.dispose() }
})

test('U-02 unknown → retry refused 403 → route closed → fresh entry under a NEW capability generation finds the attempt; nothing retried on its own', async ({ page }, info) => {
  let generation = 'gen-1'
  const lane = await Lane.install(page, {
    slug: 'u02-gen2-recovery',
    generation: () => generation,
    create: ({ nth }) => (nth === 1 ? json({}, 503) : nth === 2 ? json({ code: 'forbidden' }, 403) : json(mutation({ isReplay: true }), 201)),
  })
  try {
    await openCreate(page)
    await fillMinimum(page)
    await saveDraft(page).click()
    await expect(page.getByTestId('product-create-unknown')).toBeVisible()
    await retrySame(page).click()
    // The boundary re-fences the capability: the route closes, the form is gone.
    await expect(neutralBlock(page)).toBeVisible()
    await expect(page.getByTestId('product-create-unknown')).toHaveCount(0)
    const firstKey = lane.of('create')[0].key
    // The attempt outlives the screen — key only, never a payload, in this probe.
    expect((await pending(page, 'create'))?.key).toBe(firstKey)

    // Same actor, same session, same server: a fresh entry the server now answers with gen-2.
    generation = 'gen-2'
    const before = lane.of('capabilities').length
    await go(page, '/urun')
    await go(page, '/urun/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
    expect(lane.of('capabilities').length).toBeGreaterThan(before)
    expect((await shell(page)).generation).toBe('gen-2')

    await expect(page.getByTestId('product-create-unknown')).toBeVisible()
    await expect(page.getByText(/Tekrar denemesi reddedildi/)).toBeVisible()
    expect(lane.of('create')).toHaveLength(2)

    await retrySame(page).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
    const posts = lane.of('create')
    expect(posts).toHaveLength(3)
    expect(posts[2].key).toBe(firstKey)
    await lane.finish(info, { scope: 'U-02', gen2Recovery: true, sameKeyAcrossRefence: true, autoRetries: 0 })
  } finally { lane.dispose() }
})
