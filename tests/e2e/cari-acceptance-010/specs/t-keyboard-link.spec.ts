/**
 * ME-005 (keyboard part): the FULL link journey with the keyboard in a real Chromium
 * — Tab order reaches the search box, the candidate radio, the role checkboxes and the
 * submit button, every stop has a visible focus, Space selects, Enter submits, the
 * success is announced exactly once. Error focus (UX-AC-001 for the link form): an invalid
 * keyboard submit moves focus to the validation summary (role alert) and the radio is
 * linked to its error text; the journey then continues from there by keyboard only.
 *
 * Measurement boundary: after the route opens, focus is reset ONCE with a pointer click on
 * <body> so the Tab sequence is measured from the document start; that click is labeled in
 * the facts and is not part of the keyboard journey. A pointer-free opening, screen-reader
 * speech and visual contrast are NOT measured here.
 */
import { expect, test } from '../src/test'
import { B_ID, PARTY_2, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, json } from '../src/harness'
import { openLink, focused, selection, submit, type FocusStop } from '../src/ui'

const P2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const isRadio = (stop: FocusStop, code: string) => stop.inputType === 'radio' && new RegExp(code).test(stop.name ?? '')
const isCheckbox = (stop: FocusStop, label: string) => stop.inputType === 'checkbox' && (stop.name ?? '').includes(label)
const isSubmit = (stop: FocusStop) => stop.tag === 'button' && stop.text === 'Şirkete bağla'

async function tabUntil(page: Parameters<typeof focused>[0], predicate: (stop: FocusStop) => boolean, key: 'Tab' | 'Shift+Tab', stops: FocusStop[], limit = 40) {
  for (let i = 0; i < limit; i += 1) {
    await page.keyboard.press(key)
    const stop = await focused(page)
    stops.push(stop)
    if (predicate(stop)) return stop
  }
  throw new Error(`no focus stop matched within ${limit} presses of ${key}`)
}

const INITIAL_FOCUS_RESET = 'one pointer click on <body> after the route opened, before the first Tab; not part of the keyboard journey'

test('ME-005 Link keyboard journey after an initial pointer focus reset: Tab reaches search, candidate radio, roles and submit with visible focus; Space selects, Enter links; success announced once', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'keyboard-link-journey', company: B_ID,
    candidates: () => json(candidatePage([candidate(), P2])),
    link: ({ roles }) => json(linkedView(roles ?? 2), 201),
  })
  await openLink(page)
  // Initial focus reset (pointer): see INITIAL_FOCUS_RESET. Everything below is keyboard only.
  await page.locator('body').click({ position: { x: 1, y: 1 } })
  const stops: FocusStop[] = []
  const radio = await tabUntil(page, (s) => isRadio(s, 'CAR-000001'), 'Tab', stops)
  expect(radio.inputType).toBe('radio')
  const searchReached = stops.some((s) => s.inputType === 'search' || s.inputType === 'text' || (s.name ?? '').includes('Aday ara'))
  expect(searchReached, 'the candidate search box is a Tab stop before the radios').toBe(true)
  await page.keyboard.press('Space')
  await expect(page.getByRole('radio', { name: /CAR-000001/ })).toBeChecked()
  await expect.poll(() => selection(page)).toBe('fresh')
  const supplier = await tabUntil(page, (s) => isCheckbox(s, 'Tedarikçi'), 'Tab', stops)
  expect(supplier.inputType).toBe('checkbox')
  await page.keyboard.press('Space')
  await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).toBeChecked()
  const button = await tabUntil(page, isSubmit, 'Tab', stops)
  expect(button.tag).toBe('button')
  expect(stops.every((s) => s.focusVisible), 'every keyboard stop shows a visible focus').toBe(true)
  await page.keyboard.press('Enter')
  await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
  expect(lane.of('link')).toHaveLength(1)
  expect(lane.of('link')[0].roles).toBe(2)
  const status = page.locator('[role="status"]').getByText('Cari CAR-000001 hedef şirkete bağlandı.', { exact: true })
  await expect(status).toHaveCount(1)
  await expect(page.getByText('Cari CAR-000001 hedef şirkete bağlandı.', { exact: true })).toHaveCount(1)
  expect(lane.externalBlocked).toBe(0)
  await lane.finish(info, {
    me: ['ME-005'], initialFocusReset: INITIAL_FOCUS_RESET, keyboardOnlyAfterInitialReset: true, pointerUsedAfterInitialReset: false,
    stops: stops.map((s) => ({ tag: s.tag, id: s.id, inputType: s.inputType, role: s.role, name: s.name, text: s.text, focusVisible: s.focusVisible })),
    announcementsInStatusRegion: 1, screenReaderSpeechMeasured: false, visualContrastMeasured: false,
  })
})

test('ME-005 Link error focus after an initial pointer focus reset: an invalid keyboard submit focuses the validation summary (role alert), links the radio to the selection error, and the journey continues by keyboard', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'keyboard-link-error-focus', company: B_ID,
    candidates: () => json(candidatePage([candidate(), P2])),
    link: ({ roles }) => json(linkedView(roles ?? 2), 201),
  })
  await openLink(page)
  // Initial focus reset (pointer): see INITIAL_FOCUS_RESET. Everything below is keyboard only.
  await page.locator('body').click({ position: { x: 1, y: 1 } })
  const stops: FocusStop[] = []
  await tabUntil(page, isSubmit, 'Tab', stops)
  await page.keyboard.press('Enter')
  const summary = page.locator('#cari-link-summary')
  await expect(summary).toBeVisible()
  const afterInvalid = await focused(page)
  expect(afterInvalid.id, 'focus moves to the validation summary').toBe('cari-link-summary')
  expect(afterInvalid.role).toBe('alert')
  await expect(page.locator('#cari-link-selection-error')).toHaveText('Güncel aday listesinden bir cari seçin.')
  const summaryItems = await summary.locator('li').count()
  expect(summaryItems).toBeGreaterThanOrEqual(1)
  const firstRadio = page.getByRole('radio', { name: /CAR-000001/ })
  await expect(firstRadio).toHaveAttribute('aria-invalid', 'true')
  await expect(firstRadio).toHaveAttribute('aria-describedby', /cari-link-selection-error/)
  expect(lane.of('link'), 'a client-invalid submit sends nothing').toHaveLength(0)
  // Continue from the summary with the keyboard only: back to the radio group, select, roles, submit.
  // Backward Tab enters an unchecked radio group on its LAST radio; the arrow keys move
  // (and check) inside the group, exactly as a keyboard user would reach CAR-000001.
  const group = await tabUntil(page, (s) => s.inputType === 'radio', 'Shift+Tab', stops)
  expect(group.inputType).toBe('radio')
  let arrows = 0
  while (!isRadio(await focused(page), 'CAR-000001') && arrows < 5) { await page.keyboard.press('ArrowUp'); arrows += 1; stops.push(await focused(page)) }
  expect(isRadio(await focused(page), 'CAR-000001')).toBe(true)
  await page.keyboard.press('Space')
  await expect(page.getByRole('radio', { name: /CAR-000001/ })).toBeChecked()
  await expect.poll(() => selection(page)).toBe('fresh')
  await tabUntil(page, (s) => isCheckbox(s, 'Tedarikçi'), 'Tab', stops)
  await page.keyboard.press('Space')
  await tabUntil(page, isSubmit, 'Tab', stops)
  await page.keyboard.press('Enter')
  await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
  expect(lane.of('link')).toHaveLength(1)
  expect(stops.every((s) => s.focusVisible), 'every keyboard stop shows a visible focus').toBe(true)
  await lane.finish(info, {
    me: ['ME-005'], invalidSubmitFocus: { id: afterInvalid.id, role: afterInvalid.role, focusVisible: afterInvalid.focusVisible },
    summaryItems, initialFocusReset: INITIAL_FOCUS_RESET, keyboardOnlyAfterInitialReset: true, pointerUsedAfterInitialReset: false,
    screenReaderSpeechMeasured: false, visualContrastMeasured: false,
    stops: stops.map((s) => ({ tag: s.tag, id: s.id, inputType: s.inputType, role: s.role, name: s.name, text: s.text, focusVisible: s.focusVisible })),
  })
  void submit
})
