import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import { CATALOG_EMPTY } from './productText'
import {
  CLASS_FLAT,
  COMPANY_A,
  COMPANY_B,
  FAMILY_FLAT,
  GROUP_SAC,
  installFetch,
  json,
  referenceOptions,
  renderProductApp,
  type RecordedCall,
} from './testHarness'

/*
 * U-02 (Business #66 5706740724; measured in ORG's #272 review, P3-1): a class the owner
 * offered stays chosen while its class-scoped reference read is still out. Pending and
 * unavailable are not "this class is no longer on the list".
 */

const CREATE_ONLY = [PRODUCT_CAPABILITY.productsCreate]
const PATH = '/urun/yeni'
const GROUP_BORU = 'aaaaaaa1-0000-0000-0000-000000000002'
const CLASS_PIPE = 'cccccccc-0000-0000-0000-000000000002'
const CLASS_OTHER = 'cccccccc-0000-0000-0000-000000000003'
const FAMILY_FRESH = 'bbbbbbb1-0000-0000-0000-000000000002'

const CHECKING_HOLD = 'Sınıf/aile seçenekleri doğrulanıyor; tamamlanınca kaydedebilirsiniz.'
const CHECKING_FIELD = 'Seçenekler doğrulanıyor…'
const FAILED_FIELD = 'Sınıf/aile seçenekleri okunamadı; seçiminiz silinmedi.'
const STALE_CLASS = 'Seçilen ürün sınıfı artık listede yok. Listeyi yenileyip tekrar seçin.'

afterEach(() => {
  cleanup()
  resetPendingAttempts()
  try {
    window.localStorage.clear()
  } catch {
    // Storage unavailable in this environment: nothing was remembered.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function deferred() {
  let resolve!: (response: Response) => void
  const promise = new Promise<Response>((done) => (resolve = done))
  return { promise, resolve }
}

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))
const classSelect = () => screen.getByLabelText(/Ürün sınıfı/) as HTMLSelectElement
const familySelect = () => screen.getByLabelText(/Ürün ailesi/) as HTMLSelectElement
const shownLabel = (select: HTMLSelectElement) => select.selectedOptions[0]?.textContent ?? null
const saveButton = () => screen.getByRole('button', { name: 'Taslak olarak kaydet' })
const posts = (calls: readonly RecordedCall[]) => calls.filter((call) => call.method === 'POST')
const classParam = (url: string) => new URL(url, 'http://localhost').searchParams.get('classId')
const groupParam = (url: string) => new URL(url, 'http://localhost').searchParams.get('groupId')
const classReads = (calls: readonly RecordedCall[], classId: string) =>
  calls.filter((call) => call.url.includes('reference-options') && classParam(call.url) === classId)

/** Name and group, then the offered class — whose own scoped read `onOptions` decides. */
async function chooseOfferedClass(calls: readonly RecordedCall[], companyId = COMPANY_A) {
  await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
  await waitFor(() => expect(screen.getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy())
  await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
  await waitFor(() => screen.getByRole('option', { name: 'Yassı ürün' }))
  await userEvent.selectOptions(classSelect(), CLASS_FLAT)
  await waitFor(() =>
    expect(classReads(calls, CLASS_FLAT).filter((call) => call.companyId === companyId)).toHaveLength(1),
  )
}

/** The group-only read answers at once; the read scoped to CLASS_FLAT is `forClass`. */
const optionsFor = (forClass: () => Response | Promise<Response>) => (url: string) =>
  classParam(url) === CLASS_FLAT ? forClass() : json(referenceOptions())

describe('a chosen class while its scoped reference read is out', () => {
  it('stays chosen, family waits without the old choices, and a save is held with a neutral note', async () => {
    const held = deferred()
    const { calls } = installFetch({ capabilities: CREATE_ONLY, onOptions: optionsFor(() => held.promise) })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)

    expect(classSelect().value).toBe(CLASS_FLAT)
    expect(shownLabel(classSelect())).toBe('Yassı ürün')
    // The group-level family list is not the chosen class's list: it is not offered.
    expect(familySelect().value).toBe('')
    expect(within(familySelect()).queryByRole('option', { name: 'Soğuk hadde' })).toBeNull()
    expect(screen.getByText(CHECKING_FIELD)).toBeTruthy()
    expect(screen.queryByText(CATALOG_EMPTY)).toBeNull()
    expect(screen.queryByText(/artık listede yok/)).toBeNull()

    await userEvent.click(saveButton())
    expect(screen.getByText(CHECKING_HOLD)).toBeTruthy()
    expect(screen.queryByText(/artık listede yok/)).toBeNull()
    expect(classSelect().value).toBe(CLASS_FLAT)
    await new Promise((settle) => setTimeout(settle, 30))
    expect(posts(calls)).toHaveLength(0)
  })

  it('once the owner answers with the class, keeps it, offers the fresh families and lets the save through', async () => {
    const held = deferred()
    const { calls } = installFetch({ capabilities: CREATE_ONLY, onOptions: optionsFor(() => held.promise) })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)
    await userEvent.click(saveButton())
    expect(screen.getByText(CHECKING_HOLD)).toBeTruthy()

    await act(async () => {
      held.resolve(
        json(referenceOptions({ classId: CLASS_FLAT, families: [{ id: FAMILY_FRESH, displayName: 'Taze aile' }] })),
      )
    })
    await waitFor(() => screen.getByRole('option', { name: 'Taze aile' }))
    expect(classSelect().value).toBe(CLASS_FLAT)
    expect(within(familySelect()).queryByRole('option', { name: 'Soğuk hadde' })).toBeNull()
    expect(screen.queryByText(CHECKING_HOLD)).toBeNull()
    expect(posts(calls)).toHaveLength(0)

    await userEvent.selectOptions(familySelect(), FAMILY_FRESH)
    await userEvent.click(saveButton())
    await waitFor(() => expect(posts(calls)).toHaveLength(1))
    const body = JSON.parse(posts(calls)[0].body ?? '{}')
    expect([body.classId, body.familyId]).toEqual([CLASS_FLAT, FAMILY_FRESH])
  })

  it('says the class is gone only when a fresh owner answer no longer offers it, and sends nothing', async () => {
    const held = deferred()
    const { calls } = installFetch({ capabilities: CREATE_ONLY, onOptions: optionsFor(() => held.promise) })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)
    expect(screen.queryByText(STALE_CLASS)).toBeNull()

    await act(async () => {
      held.resolve(
        json(
          referenceOptions({
            classId: CLASS_FLAT,
            classes: [{ id: CLASS_OTHER, displayName: 'Başka sınıf' }],
            families: [{ id: FAMILY_FRESH, displayName: 'Taze aile' }],
          }),
        ),
      )
    })

    // The real stale choice, said at once — the person's pick stays in view, in error.
    await waitFor(() => expect(screen.getByText(STALE_CLASS)).toBeTruthy())
    expect(classSelect().value).toBe(CLASS_FLAT)
    expect(classSelect().getAttribute('aria-invalid')).toBe('true')
    expect(familySelect().value).toBe('')
    expect(within(familySelect()).queryByRole('option', { name: 'Taze aile' })).toBeNull()

    await userEvent.click(saveButton())
    await new Promise((settle) => setTimeout(settle, 30))
    expect(posts(calls)).toHaveLength(0)
  })

  it.each([
    ['a 503', () => json({ code: 'service_unavailable' }, 503)],
    ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['a malformed answer', () => json({ unexpected: true })],
  ])('treats %s as unavailable, never as an empty or changed catalogue; a retry that succeeds applies', async (_label, failure) => {
    let classReadCount = 0
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onOptions: optionsFor(() => {
        classReadCount += 1
        return classReadCount === 1 ? failure() : json(referenceOptions({ classId: CLASS_FLAT }))
      }),
    })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)

    await waitFor(() => expect(screen.getByText(FAILED_FIELD)).toBeTruthy())
    expect(classSelect().value).toBe(CLASS_FLAT)
    expect(shownLabel(classSelect())).toBe('Yassı ürün')
    expect(screen.queryByText(CATALOG_EMPTY)).toBeNull()
    expect(screen.queryByText(/artık listede yok/)).toBeNull()

    await userEvent.click(saveButton())
    await new Promise((settle) => setTimeout(settle, 30))
    expect(posts(calls)).toHaveLength(0)
    expect(screen.queryByText(/artık listede yok/)).toBeNull()

    await userEvent.click(screen.getByRole('button', { name: 'Seçenekleri yeniden oku' }))
    await waitFor(() => screen.getByRole('option', { name: 'Soğuk hadde' }))
    expect(screen.queryByText(FAILED_FIELD)).toBeNull()
    expect(classSelect().value).toBe(CLASS_FLAT)
    await userEvent.click(saveButton())
    await waitFor(() => expect(posts(calls)).toHaveLength(1))
    expect(JSON.parse(posts(calls)[0].body ?? '{}').classId).toBe(CLASS_FLAT)
  })

  it('still lets a Draft be saved without a class when the person clears it', async () => {
    const held = deferred()
    const { calls } = installFetch({ capabilities: CREATE_ONLY, onOptions: optionsFor(() => held.promise) })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)

    await userEvent.selectOptions(classSelect(), '')
    await userEvent.click(saveButton())
    await waitFor(() => expect(posts(calls)).toHaveLength(1))
    expect(Object.hasOwn(JSON.parse(posts(calls)[0].body ?? '{}'), 'classId')).toBe(false)
    expect(screen.queryByText(CHECKING_HOLD)).toBeNull()
  })

  it('a real group change still resets class and family, and never offers the old group’s classes', async () => {
    const held = deferred()
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onGroups: () =>
        json([
          { id: GROUP_SAC, name: 'Sac', prefix: 'SAC' },
          { id: GROUP_BORU, name: 'Boru', prefix: 'BRU' },
        ]),
      onOptions: (url) =>
        groupParam(url) === GROUP_BORU
          ? classParam(url) === null
            ? held.promise
            : json(referenceOptions({ groupId: GROUP_BORU, classId: classParam(url) }))
          : classParam(url) === CLASS_FLAT
            ? json(referenceOptions({ classId: CLASS_FLAT }))
            : json(referenceOptions()),
    })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)
    await waitFor(() => screen.getByRole('option', { name: 'Soğuk hadde' }))
    await userEvent.selectOptions(familySelect(), FAMILY_FLAT)

    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_BORU)
    expect(classSelect().value).toBe('')
    expect(familySelect().value).toBe('')
    // Boru's own list is still out: Sac's classes are not Boru's.
    expect(within(classSelect()).queryByRole('option', { name: 'Yassı ürün' })).toBeNull()

    await act(async () => {
      held.resolve(
        json(referenceOptions({ groupId: GROUP_BORU, classes: [{ id: CLASS_PIPE, displayName: 'Boru sınıfı' }], families: [] })),
      )
    })
    await waitFor(() => screen.getByRole('option', { name: 'Boru sınıfı' }))
    expect(classSelect().value).toBe('')
    expect(screen.queryByText(/artık listede yok/)).toBeNull()
  })

  it('an answer still out under company A does not choose, hold or fail anything under B', async () => {
    const held = deferred()
    const { calls } = installFetch({ capabilities: CREATE_ONLY, onOptions: optionsFor(() => held.promise) })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)
    expect(classReads(calls, CLASS_FLAT)[0].companyId).toBe(COMPANY_A)

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => {
      const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
      expect(picker.disabled).toBe(false)
      expect(picker.value).toBe(COMPANY_B)
    })
    await formVisible()

    await act(async () => {
      held.resolve(
        json(referenceOptions({ classId: CLASS_FLAT, classes: [{ id: CLASS_OTHER, displayName: 'Başka sınıf' }] })),
      )
    })
    await new Promise((settle) => setTimeout(settle, 50))
    expect(classSelect().value).toBe('')
    expect(screen.queryByText(STALE_CLASS)).toBeNull()
    expect(screen.queryByText(CHECKING_FIELD)).toBeNull()
    expect(screen.queryByText(FAILED_FIELD)).toBeNull()
    expect(classReads(calls, CLASS_FLAT).filter((call) => call.companyId === COMPANY_B)).toHaveLength(0)
  })

  it('an answer already in hand for company A is not B’s answer for the same group and class', async () => {
    // The switch remounts the form, but the query cache outlives it: only the company
    // context in the options key keeps A's settled families from standing in for B's read.
    const heldForB = deferred()
    let classReadCount = 0
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onOptions: optionsFor(() => {
        classReadCount += 1
        return classReadCount === 1
          ? json(referenceOptions({ classId: CLASS_FLAT, families: [{ id: FAMILY_FRESH, displayName: 'A-ONLY aile' }] }))
          : heldForB.promise
      }),
    })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)
    await waitFor(() => screen.getByRole('option', { name: 'A-ONLY aile' }))

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => {
      const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
      expect(picker.disabled).toBe(false)
      expect(picker.value).toBe(COMPANY_B)
    })
    await formVisible()
    await chooseOfferedClass(calls, COMPANY_B)

    // B asked for itself, and that answer is still out: A's families are not offered.
    expect(classReads(calls, CLASS_FLAT).map((call) => call.companyId)).toEqual([COMPANY_A, COMPANY_B])
    expect(within(familySelect()).queryByRole('option', { name: 'A-ONLY aile' })).toBeNull()
    expect(screen.getByText(CHECKING_FIELD)).toBeTruthy()
  })

  it('a 403 on the scoped read goes to the capability boundary, not to a local failure note', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onOptions: optionsFor(() => json({ code: 'forbidden' }, 403)),
    })
    renderProductApp(PATH)
    await formVisible()
    await chooseOfferedClass(calls)

    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Yeni ürün' })).toBeNull())
    expect(screen.queryByText(FAILED_FIELD)).toBeNull()
    expect(posts(calls)).toHaveLength(0)
  })
})
