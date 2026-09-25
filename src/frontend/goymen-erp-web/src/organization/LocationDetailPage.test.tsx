/**
 * ORG-02 component/DOM tests — the reasoned state change and the fresh read.
 *
 * Fixtures through the real shell, gate and fence. NOT real-API, real-SQL or browser
 * acceptance, and never reported as such.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { attemptStoreKey, recallAttempt } from './unresolvedAttemptStore'
import { ORGANIZATION_CAPABILITY, workCenterListPath } from './capabilities'
import { WORK_CENTER_LOCATION_TEXT } from './workCenterLocationFilter'
import {
  COMPANY_A,
  COMPANY_B,
  IDENTITY,
  LOC_H0,
  READ_CAPABILITIES,
  SITE_OSM,
  installFetch,
  json,
  location,
  noContent,
  problem,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'

const PATH = `/ayarlar/organizasyon/lokasyon/${LOC_H0}`
const REASON = 'Bakım için pasife alma'

interface Scenario {
  readonly recorded: RecordedRequest[]
  /** Detail payloads served in order; the last one repeats. */
  readonly detail: Array<() => Response>
  readonly command: Array<() => Response>
}

function stub(
  options: {
    detail?: Array<() => Response>
    command?: Array<() => Response>
    capabilities?: readonly string[]
    generations?: readonly string[]
  } = {},
): Scenario {
  const detail = options.detail ?? [() => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'))]
  const command = options.command ?? [() => noContent()]
  let detailIndex = 0
  let commandIndex = 0

  const recorded = installFetch({
    capabilities: options.capabilities,
    generations: options.generations,
    respond: (url) => {
      if (url.includes('/active-state')) {
        const respond = command[Math.min(commandIndex, command.length - 1)]
        commandIndex += 1
        return respond()
      }
      if (url.includes('/organization/locations/')) {
        const respond = detail[Math.min(detailIndex, detail.length - 1)]
        detailIndex += 1
        return respond()
      }
      return undefined
    },
  })
  return { recorded, detail, command }
}

function commandCalls(recorded: readonly RecordedRequest[]) {
  return requestsFor(recorded, '/active-state')
}

function detailReads(recorded: readonly RecordedRequest[]) {
  return requestsFor(recorded, '/organization/locations/').filter(
    (request) => !request.url.includes('/active-state'),
  )
}

const ACTION = /Pasife al|Aktifleştir/

/** Opens the confirmation modal from the card. */
async function openDialog() {
  await userEvent.click(await screen.findByRole('button', { name: ACTION }))
  return screen.getByRole('dialog')
}

async function fillAndSubmit(reason = REASON) {
  const dialog = await openDialog()
  const field = within(dialog).getByLabelText('Gerekçe')
  await userEvent.clear(field)
  if (reason.length > 0) await userEvent.type(field, reason)
  // The submit lives INSIDE the dialog; the card's trigger shares its label.
  await userEvent.click(within(dialog).getByRole('button', { name: ACTION }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('the card', () => {
  it('shows the location read from the server', async () => {
    stub()
    renderAt(PATH)

    expect(await screen.findByRole('heading', { name: 'Lokasyon' })).toBeTruthy()
    expect(await screen.findByText('OSM-H0')).toBeTruthy()
    expect(screen.getByText('Hol-0')).toBeTruthy()
    expect(screen.getByText('Aktif')).toBeTruthy()
  })

  it('scopes the read to the committed company', async () => {
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByText('OSM-H0')
    expect(detailReads(recorded)[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows the neutral not-found block for a 404', async () => {
    stub({ detail: [() => problem(404)] })
    renderAt(PATH)
    expect(await screen.findByText('Bulunamadı — lokasyon')).toBeTruthy()
  })

  it('fails closed on a version it cannot vouch for', async () => {
    stub({
      detail: [
        () => json({ ...location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'), version: 9007199254740994 }),
      ],
    })
    renderAt(PATH)
    expect(await screen.findByText('Beklenmeyen yanıt — lokasyon')).toBeTruthy()
  })
})

describe('the back destination', () => {
  it('uses the canonical location list for a direct entry', async () => {
    stub()
    renderAt(PATH)

    await screen.findByText('OSM-H0')
    const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', {
      name: /Organizasyon/,
    })
    expect(back.getAttribute('href')).toBe('/ayarlar/organizasyon')
  })

  it('ignores an untrusted origin from another context', async () => {
    stub()
    const { router } = renderAt(PATH)
    await screen.findByText('OSM-H0')

    await router.navigate(PATH, {
      state: {
        ns: 'another-company::generation',
        from: 'javascript:alert(1)',
      },
    })

    await waitFor(() => {
      const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', {
        name: /Organizasyon/,
      })
      expect(back.getAttribute('href')).toBe('/ayarlar/organizasyon')
    })
  })
})

describe('who may change the state', () => {
  it('hides the write surface from a read-only holder', async () => {
    // The server would refuse anyway; offering a form that can only fail is not a
    // capability the frontend gets to imply.
    stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)

    await screen.findByText('OSM-H0')
    expect(screen.queryByRole('button', { name: ACTION })).toBeNull()
    expect(screen.queryByLabelText('Gerekçe')).toBeNull()
  })

  it('offers it to a holder of the status capability', async () => {
    stub()
    renderAt(PATH)
    expect(await screen.findByRole('button', { name: 'Pasife al' })).toBeTruthy()
    const dialog = await openDialog()
    expect(within(dialog).getByLabelText('Gerekçe')).toBeTruthy()
  })
})

describe('the justification is mandatory', () => {
  it('refuses an empty reason and mints NO key', async () => {
    const { recorded } = stub()
    renderAt(PATH)
    await fillAndSubmit('')

    expect(await screen.findByText(/Gerekçe zorunludur/)).toBeTruthy()
    // Nothing was attempted, so there is nothing to be uncertain about.
    expect(commandCalls(recorded)).toHaveLength(0)
  })

  it('refuses a whitespace-only reason, matching the server’s trim rule', async () => {
    const { recorded } = stub()
    renderAt(PATH)
    await fillAndSubmit('    ')

    expect(await screen.findByText(/Gerekçe zorunludur/)).toBeTruthy()
    expect(commandCalls(recorded)).toHaveLength(0)
  })
})

describe('a successful change', () => {
  it('sends the contract body and then RE-READS the record', async () => {
    const { recorded } = stub({
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { isActive: false, version: 4 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()

    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(1))
    const command = commandCalls(recorded)[0]
    expect(command.method).toBe('PUT')
    expect(command.body).toEqual({
      isActive: false,
      expectedVersion: '3',
      reason: REASON,
    })
    expect(command.headers['Idempotency-Key']).toBeTruthy()
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)

    // A 204 carries no state. The card must come from a fresh GET.
    await waitFor(() => expect(detailReads(recorded).length).toBeGreaterThan(1))
    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
    expect(await screen.findByText('Pasif')).toBeTruthy()
  })

  it('never predicts the next version — the card follows the server', async () => {
    // A same-state command records a NoChange audit and leaves the version alone,
    // so version+1 is not something a client may assume.
    const { recorded } = stub({
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 7 })),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 7 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()

    await waitFor(() => expect(detailReads(recorded).length).toBeGreaterThan(1))
    // Still active, still version 7 — and the screen says so rather than claiming
    // the change it asked for.
    expect(await screen.findByText('Aktif')).toBeTruthy()
  })

  it('reports a REPLAY without claiming a resulting state', async () => {
    /**
     * A replayed 204 is the server repeating an earlier command's terminal answer,
     * not a statement about the record now. The notice says so and points at the
     * fresh view.
     */
    const { recorded } = stub({
      command: [() => noContent({ 'Idempotency-Replayed': 'true' })],
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText('Aynı işlem zaten kaydedilmişti')).toBeTruthy()
    await waitFor(() => expect(detailReads(recorded).length).toBeGreaterThan(1))
  })
})

describe('the two different 409s get different recoveries', () => {
  it('shows a version conflict with both numbers', async () => {
    stub({
      command: [
        () => json({ conflict: { expectedVersion: 3, currentVersion: 5 } }, 409),
      ],
      detail: [() => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 }))],
    })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText('Kayıt bu sırada değişti')).toBeTruthy()
    expect(screen.getByText(/Beklediğiniz sürüm 3, kaydın güncel sürümü 5\./)).toBeTruthy()
  })

  it('does NOT call a replayed conflict’s number current', async () => {
    /**
     * Verified against PR #241: the server replays a stored 409 verbatim, so its
     * `currentVersion` can be several versions stale. Calling it "güncel" would put a
     * wrong number in front of the user.
     */
    stub({
      command: [
        () =>
          json({ conflict: { expectedVersion: 3, currentVersion: 4 } }, 409, {
            'Idempotency-Replayed': 'true',
          }),
      ],
      detail: [() => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 }))],
    })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText('Kayıt bu sırada değişti')).toBeTruthy()
    expect(screen.getByText(/daha önceki aynı işlemin tekrarı/)).toBeTruthy()
    expect(screen.queryByText(/kaydın güncel sürümü/)).toBeNull()
  })

  it('distinguishes an idempotency conflict, which no re-send can fix', async () => {
    stub({
      command: [() => json({ title: 'Idempotency-Key çakışması.', status: 409 }, 409)],
    })
    renderAt(PATH)
    await fillAndSubmit()

    expect(
      await screen.findByText('Bu işlem anahtarı farklı bir istekle kullanılmış'),
    ).toBeTruthy()
    // No same-key retry is offered for this one: it cannot succeed.
    expect(screen.queryByRole('button', { name: /Aynı işlem anahtarıyla/ })).toBeNull()
    expect(screen.getByRole('button', { name: 'Güncel kaydı yükle' })).toBeTruthy()
  })
})

describe('an unknown outcome', () => {
  it('says the change MAY have been applied, and never that it was not', async () => {
    stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    expect(screen.getByText(/uygulanmış olabilir/)).toBeTruthy()
  })

  it('re-sends under the SAME key so the server replays instead of writing twice', async () => {
    const { recorded } = stub({ command: [() => problem(503), () => noContent()] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    await userEvent.click(screen.getByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }))

    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(2))
    const [first, second] = commandCalls(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('does not re-send on its own', async () => {
    const { recorded } = stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    // Give any stray retry a chance to appear.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(commandCalls(recorded)).toHaveLength(1)
  })
})

describe('definite refusals', () => {
  it('reports a validation refusal with an empty body', async () => {
    stub({ command: [() => new Response(null, { status: 400 })] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText('İşlem kabul edilmedi')).toBeTruthy()
  })

  it('reports a 404 without claiming anything about other records', async () => {
    stub({ command: [() => problem(404)] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText('Bulunamadı')).toBeTruthy()
  })
})

describe('a command refused by authorization reaches the SHARED boundary', () => {
  it.each([
    [401, 'Oturum sonlandı — oturum'],
    [403, 'Bulunamadı — sayfa'],
  ])('routes a command %i through the central boundary, not just the modal', async (status, text) => {
    /**
     * A command rejected by authorization is the same security fact as a rejected read.
     * Before this, a PUT 401 printed "Oturum sonlandı" inside the modal while the dead
     * session kept its company card, capabilities and submit surface on screen behind
     * it. Now it is reported to the shell boundary, which ends the session (401) or
     * drops the capability generation and fails every gated mount closed (403).
     */
    stub({ command: [() => problem(status)] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText(text)).toBeTruthy()
    // The write surface must not survive behind the boundary's verdict.
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('an unresolved attempt cannot become a second command', () => {
  async function reachUnknown() {
    const scenario = stub({ command: [() => problem(503), () => noContent()] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    return scenario
  }

  it('freezes the justification while the outcome is unknown', async () => {
    // Editing it would change the payload fingerprint, and a changed fingerprint is
    // what mints a new key for a command that may already be recorded.
    const { recorded } = await reachUnknown()
    const field = screen.getByLabelText('Gerekçe') as HTMLTextAreaElement
    expect(field.readOnly).toBe(true)

    await userEvent.type(field, ' ek metin')
    expect(field.value).toBe(REASON)
    expect(commandCalls(recorded)).toHaveLength(1)
  })

  it('offers no ordinary submit while unresolved — only the same-key resend', async () => {
    await reachUnknown()
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).queryByRole('button', { name: ACTION })).toBeNull()
    expect(
      within(dialog).getByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    ).toBeTruthy()
  })

  it('refreshing the record does NOT discard the unresolved attempt', async () => {
    /**
     * Seeing the target state in a GET is not a receipt for the command — the record
     * could have reached it another way. Dropping the key here would abandon an
     * unresolved command while telling the user nothing about what became of it.
     */
    const { recorded } = await reachUnknown()
    const before = detailReads(recorded).length

    await userEvent.click(screen.getByRole('button', { name: 'Güncel kaydı yükle' }))

    await waitFor(() => expect(detailReads(recorded).length).toBeGreaterThan(before))
    // Still unresolved, still no second command.
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    expect(commandCalls(recorded)).toHaveLength(1)

    // And the ONE way forward still uses the original key.
    await userEvent.click(
      screen.getByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    )
    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(2))
    const [first, second] = commandCalls(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('keeps the uncertainty when a retry is refused, and never says it was not applied', async () => {
    const { recorded } = stub({ command: [() => problem(503), () => problem(400)] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()

    await userEvent.click(
      screen.getByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    )

    // The refusal is shown...
    expect(await screen.findByText(/Son deneme sunucu tarafından reddedildi/)).toBeTruthy()
    // ...but the first request's uncertainty still owns the verdict.
    expect(screen.getByText(/sonuç bilinmiyor/)).toBeTruthy()
    expect(screen.queryByText(/Değişiklik uygulanmadı/)).toBeNull()
    // And the key is still the same one, so a further resend is still a replay.
    expect(commandCalls(recorded)[1].headers['Idempotency-Key']).toBe(
      commandCalls(recorded)[0].headers['Idempotency-Key'],
    )
  })

  it('resolves the attempt when the retry REPLAYS the original terminal answer', async () => {
    const { recorded } = stub({
      command: [
        () => problem(503),
        () => problem(404, {}, { 'Idempotency-Replayed': 'true' }),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()
    await userEvent.click(
      await screen.findByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    )

    expect(await screen.findByText('Bulunamadı')).toBeTruthy()
    expect(screen.queryByText(/sonuç bilinmiyor/)).toBeNull()
    expect(commandCalls(recorded)).toHaveLength(2)
  })
})

describe('the justification survives the recommended recovery', () => {
  it('keeps the typed reason after a version conflict reload', async () => {
    /**
     * The recommended next step after a 409 is "look at the fresh record and decide
     * again". Wiping the draft as a side effect of taking that step is the opposite of
     * helping (AT-006 / Business).
     */
    stub({
      command: [() => json({ conflict: { expectedVersion: 3, currentVersion: 5 } }, 409)],
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 5 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText('Kayıt bu sırada değişti')).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: 'Güncel kaydı yükle' }))

    const field = (await screen.findByLabelText('Gerekçe')) as HTMLTextAreaElement
    expect(field.value).toBe(REASON)
    // The attempt is closed, so a fresh submit is available again.
    expect(field.readOnly).toBe(false)
    expect(within(screen.getByRole('dialog')).getByRole('button', { name: ACTION })).toBeTruthy()
  })
})

describe('focus stays inside the modal', () => {
  it('cycles Tab and Shift+Tab within the dialog', async () => {
    /**
     * `aria-modal` is a promise to assistive technology, not something the browser
     * implements for a plain div. Without containment Tab reaches the trigger, the
     * breadcrumb and the company picker underneath — which is also a keyboard route to
     * acting on the page behind an unresolved attempt.
     */
    stub()
    renderAt(PATH)
    const dialog = await openDialog()

    const focusable = [...dialog.querySelectorAll<HTMLElement>('button, textarea, a[href]')]
    expect(focusable.length).toBeGreaterThan(1)
    const last = focusable[focusable.length - 1]

    for (let i = 0; i < focusable.length + 2; i += 1) {
      await userEvent.tab()
      expect(dialog.contains(document.activeElement)).toBe(true)
    }

    last.focus()
    await userEvent.tab({ shift: true })
    expect(dialog.contains(document.activeElement)).toBe(true)
  })
})

describe('modal semantics', () => {
  it('puts focus in the justification field when the dialog opens', async () => {
    stub()
    renderAt(PATH)
    const dialog = await openDialog()
    expect(document.activeElement).toBe(within(dialog).getByLabelText('Gerekçe'))
  })

  it('states the target as a fact before it is committed', async () => {
    stub()
    renderAt(PATH)
    const dialog = await openDialog()
    expect(
      within(dialog).getByText(/OSM-H0 lokasyonu AKTİF durumdan PASİF duruma alınacak\./),
    ).toBeTruthy()
  })

  it('closes on Escape and returns focus to the trigger', async () => {
    stub()
    renderAt(PATH)
    const trigger = await screen.findByRole('button', { name: ACTION })
    await openDialog()

    await userEvent.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(document.activeElement).toBe(trigger)
  })

  it('keeps the dialog open on Escape while an outcome is UNRESOLVED', async () => {
    // Dismissing here would hide the fact that the write may have landed.
    stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    await userEvent.keyboard('{Escape}')
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('closes the dialog once the command is accepted', async () => {
    stub()
    renderAt(PATH)
    await fillAndSubmit()

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
  })
})

describe('unsent draft during a company switch', () => {
  it('warns before discarding a typed justification', async () => {
    stub()
    renderAt(PATH)
    const dialog = await openDialog()
    await userEvent.type(within(dialog).getByLabelText('Gerekçe'), 'Yarım kalmış gerekçe')

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    // The shared dirty-scope gate, not a local flag: nothing is switched until the
    // user answers, so the draft survives "Kal".
    expect(await screen.findByText('Kaydedilmemiş değişiklikler var')).toBeTruthy()
  })

  it('warns for an UNRESOLVED attempt even with no text typed', async () => {
    // An unresolved key is dirty work of its own: leaving the context would strand a
    // command whose outcome nobody has established.
    stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    expect(await screen.findByText('Kaydedilmemiş değişiklikler var')).toBeTruthy()
  })

  it('does not warn when nothing has been typed', async () => {
    stub()
    renderAt(PATH)
    await openDialog()

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    await waitFor(() => expect(screen.queryByText('Kaydedilmemiş değişiklikler var')).toBeNull())
  })
})

describe('the list behind the card', () => {
  it('drops the cached list page so the change is visible there too', async () => {
    /**
     * Business requires the new state to be visible "on the same list page". Under the
     * production 30 s staleTime a revisit would otherwise paint the pre-change rows
     * from cache, so the accepted command invalidates them.
     */
    const { recorded } = stub({
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { isActive: false, version: 4 })),
      ],
    })
    const { queryClient } = renderAt(PATH, { retainCache: true })

    // Seed a list page into the cache the way a visit to ORG-01 would.
    await screen.findByText('OSM-H0')
    const namespace = queryClient
      .getQueryCache()
      .getAll()
      .map((entry) => entry.queryKey)
      .find((key) => key[2] === 'organization-location')?.[0]
    expect(namespace).toBeTruthy()
    queryClient.setQueryData([namespace, 1, 'organization-locations', 1], {
      type: 'ok-single',
      data: { items: [], page: 1, pageSize: 25, totalCount: 0 },
    })

    await fillAndSubmit()
    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(1))

    await waitFor(() => {
      const listQuery = queryClient
        .getQueryCache()
        .getAll()
        .find((entry) => entry.queryKey[2] === 'organization-locations')
      expect(listQuery?.state.isInvalidated).toBe(true)
    })
  })
})

describe('the unresolved attempt survives the component lifecycle', () => {
  const STORE_KEY = attemptStoreKey(0, IDENTITY.userId, COMPANY_A, LOC_H0)

  it('survives a FAILED re-read that unmounts the card, and comes back on recovery', async () => {
    /**
     * The gap this closes: "Güncel kaydı yükle" on an unknown outcome refreshes the
     * record; if that GET fails, the card renders a state block instead of the action
     * section, the section unmounts, and its `useReducer` — the only copy of the
     * Idempotency-Key — went with it. A later successful read then mounted a fresh
     * `idle` section, so a command that MAY ALREADY BE RECORDED had silently lost its
     * key, and the next submit would mint a second one.
     */
    const { recorded } = stub({
      command: [() => problem(503), () => noContent()],
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => problem(503), // the refresh fails
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: 'Güncel kaydı yükle' }))

    // The card is gone, but the outstanding command is NOT silent.
    expect(await screen.findByText('Sonucu bilinmeyen bir işleminiz var')).toBeTruthy()
    expect(recallAttempt(STORE_KEY)?.snapshot.key).toBeTruthy()

    // Recover the read; the attempt must come back, not restart idle.
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    const resend = await screen.findByRole('button', {
      name: /Aynı işlem anahtarıyla tekrar gönder/,
    })
    await userEvent.click(resend)

    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(2))
    const [first, second] = commandCalls(recorded)
    // The ORIGINAL key, not a new one.
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('restores the justification it was sent with, not an empty box', async () => {
    stub({
      command: [() => problem(503)],
      detail: [
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
        () => problem(503),
        () => json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0', { version: 3 })),
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()
    await userEvent.click(await screen.findByRole('button', { name: 'Güncel kaydı yükle' }))
    await screen.findByText('Sonucu bilinmeyen bir işleminiz var')
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))

    const field = (await screen.findByLabelText('Gerekçe')) as HTMLTextAreaElement
    expect(field.value).toBe(REASON)
    // Still frozen: the attempt is still unresolved.
    expect(field.readOnly).toBe(true)
  })

  it('survives the auth boundary closing the mount on a retry 403', async () => {
    /**
     * Same loss, arriving the other way: the same-key retry is refused 403, which is
     * reported to the shared boundary, which drops the capability and unmounts every
     * gated screen. The safe close is correct and stays — what must not happen is the
     * outstanding key disappearing with it.
     */
    const { recorded } = stub({ command: [() => problem(503), () => problem(403)] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    const originalKey = commandCalls(recorded)[0].headers['Idempotency-Key']

    await userEvent.click(
      screen.getByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    )

    // The shell closed the feature — that part is the boundary doing its job.
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    // ...and the outstanding command is still on record for this exact identity.
    const stored = recallAttempt(STORE_KEY)
    expect(stored?.snapshot.key).toBe(originalKey)
    expect(stored?.snapshot.payload.reason).toBe(REASON)
  })

  it('does not offer a surviving attempt to a different company', async () => {
    // The store key is actor + company + location, so a switch cannot surface it.
    stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()

    expect(recallAttempt(STORE_KEY)).not.toBeNull()
    expect(recallAttempt(attemptStoreKey(0, IDENTITY.userId, COMPANY_B, LOC_H0))).toBeNull()
  })

  it('forgets the attempt once it is resolved', async () => {
    stub({ command: [() => noContent()] })
    renderAt(PATH)
    await fillAndSubmit()

    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
    expect(recallAttempt(STORE_KEY)).toBeNull()
  })
})

describe('focus cannot escape backwards from a notice', () => {
  it('keeps Shift+Tab inside the dialog when focus sits on the outcome notice', async () => {
    /**
     * The outcome notices are `tabIndex={-1}` so an arriving answer can be focused
     * programmatically. From there the trap's first/last tests did not match and the
     * element was inside the root, so the guard fell through and Shift+Tab walked out
     * to the breadcrumb behind the modal.
     */
    stub({ command: [() => problem(503)] })
    renderAt(PATH)
    await fillAndSubmit()

    const notice = await screen.findByRole('alert')
    await waitFor(() => expect(document.activeElement).toBe(notice))
    const dialog = screen.getByRole('dialog')

    await userEvent.tab({ shift: true })
    expect(dialog.contains(document.activeElement)).toBe(true)

    notice.focus()
    await userEvent.tab()
    expect(dialog.contains(document.activeElement)).toBe(true)
  })
})

describe('the key is reserved BEFORE the request leaves', () => {
  const STORE_KEY = attemptStoreKey(0, IDENTITY.userId, COMPANY_A, LOC_H0)

  it('has the attempt on record at the moment the request is issued', async () => {
    /**
     * The probe has to run INSIDE the transport, not after it.
     *
     * `send()` executes in the same tick as the dispatch, while the mirroring effect
     * runs only after the render commit — so asserting from the test body cannot tell
     * the two apart: by then the effect has flushed either way. Reading the store from
     * the fetch responder is the only place that observes the actual ordering, and it
     * is the ordering that matters: an unmount between the request going out and the
     * effect running used to lose a key that was already on the wire.
     */
    let storedAtRequestTime: string | null | undefined
    const { recorded } = stub({
      command: [
        () => {
          storedAtRequestTime = recallAttempt(STORE_KEY)?.snapshot.key ?? null
          return noContent()
        },
      ],
    })
    renderAt(PATH)
    await fillAndSubmit()

    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(1))
    expect(storedAtRequestTime).toBe(commandCalls(recorded)[0].headers['Idempotency-Key'])
  })

  it('keeps the reservation when the mount is torn down mid-flight', async () => {
    // Survival across leaving the screen entirely while the command is unresolved.
    let release: () => void = () => {}
    const pending = new Promise<Response>((resolve) => {
      release = () => resolve(noContent())
    })
    const { recorded } = stub({ command: [() => pending as unknown as Response] })
    const { router } = renderAt(PATH)
    await fillAndSubmit()
    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(1))

    await router.navigate('/ayarlar/organizasyon')

    expect(recallAttempt(STORE_KEY)?.snapshot.key).toBe(
      commandCalls(recorded)[0].headers['Idempotency-Key'],
    )
    release()
  })
})

describe('recovery after a fresh capability generation', () => {
  const STORE_KEY = attemptStoreKey(0, IDENTITY.userId, COMPANY_A, LOC_H0)

  it('restores the attempt in the SAME session even though the namespace changed', async () => {
    /**
     * A refence is followed by a re-verification that returns a NEW capability
     * generation, which changes the shell context namespace. The attempt must still come
     * back, because it is bound to session + actor + company + location — not to the
     * namespace, whose generation is precisely what a refence changes.
     */
    const { recorded } = stub({
      command: [() => problem(503), () => noContent()],
      generations: ['org-cap-gen-1', 'org-cap-gen-2'],
    })
    const { router } = renderAt(PATH)
    await fillAndSubmit()
    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    const originalKey = commandCalls(recorded)[0].headers['Idempotency-Key']

    // Leave and re-enter: the route entry re-verifies and lands on generation 2.
    await router.navigate('/ayarlar/organizasyon')
    await router.navigate(PATH)

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    await userEvent.click(
      await screen.findByRole('button', { name: /Aynı işlem anahtarıyla tekrar gönder/ }),
    )

    await waitFor(() => expect(commandCalls(recorded)).toHaveLength(2))
    expect(commandCalls(recorded)[1].headers['Idempotency-Key']).toBe(originalKey)
    expect(recallAttempt(STORE_KEY)).toBeNull() // resolved by the 204
  })
})


describe('HYBRID-05 location-bound work-center action', () => {
  it('offers the exact canonical filter only with the existing work-center capability', async () => {
    stub({ capabilities: [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.workCentersList] })
    renderAt(PATH)
    const link = await screen.findByRole('link', { name: WORK_CENTER_LOCATION_TEXT.action })
    expect(link.getAttribute('href')).toBe(workCenterListPath(LOC_H0))
  })

  it('keeps the card readable without that capability and never probes work centers', async () => {
    const { recorded } = stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)
    await screen.findByText('OSM-H0')
    expect(screen.queryByRole('link', { name: WORK_CENTER_LOCATION_TEXT.action })).toBeNull()
    expect(requestsFor(recorded, '/organization/work-centers')).toEqual([])
  })

  it('refuses a valid-shaped response describing a different location', async () => {
    stub({
      capabilities: [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.workCentersList],
      detail: [() => json(location('44444444-4444-4444-4444-444444444444', SITE_OSM, 'OTHER', 'Other'))],
    })
    renderAt(PATH)
    await screen.findByText('Beklenmeyen yanıt — lokasyon')
    expect(screen.queryByText('Other')).toBeNull()
    expect(screen.queryByRole('link', { name: WORK_CENTER_LOCATION_TEXT.action })).toBeNull()
  })

  it('restores only the same location filter when returning from a filtered row', async () => {
    stub({ capabilities: [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.workCentersList] })
    const { router, queryClient } = renderAt(PATH)
    await screen.findByText('OSM-H0')
    const ns = queryClient.getQueryCache().getAll()
      .find((entry) => entry.queryKey[2] === 'organization-location')?.queryKey[0]
    expect(typeof ns).toBe('string')
    await router.navigate(PATH, { state: { ns, from: 'work-centers', locationId: LOC_H0 } })
    await waitFor(() => expect(within(screen.getByRole('navigation', { name: 'Geri' }))
      .getByRole('link', { name: /İş Merkezleri/ }).getAttribute('href')).toBe(workCenterListPath(LOC_H0)))
  })

  it('drops the old-company action when the next company cannot read that location', async () => {
    installFetch({
      capabilities: [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.workCentersList],
      respond: (url, init) => url.includes('/organization/locations/')
        ? new Headers(init?.headers).get('X-Company-Id') === COMPANY_A
          ? json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0')) : problem(404)
        : undefined,
    })
    renderAt(PATH)
    await screen.findByRole('link', { name: WORK_CENTER_LOCATION_TEXT.action })
    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)
    await screen.findByText('Bulunamadı — lokasyon')
    expect(screen.queryByRole('link', { name: WORK_CENTER_LOCATION_TEXT.action })).toBeNull()
    expect(screen.queryByText('OSM-H0')).toBeNull()
  })
})
