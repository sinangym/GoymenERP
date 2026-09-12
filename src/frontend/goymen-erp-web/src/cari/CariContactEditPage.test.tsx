/**
 * C-06 — the operational contact/address editor, through the real shell: provider,
 * memory router, capability gate, the real transport and the real patch builder.
 *
 * What these cases are about is the PAYLOAD and the ORDER, because that is where this
 * screen can do damage: a row it did not send must not be deleted, a field it did not
 * touch must not be re-asserted, an extension must not be stranded, and nothing on
 * screen may claim to be the record's new state until a fresh read says so.
 *
 * Fixture shape and example values come from the pinned Backend handoff
 * (`CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` at `9df7f40`). This is a component
 * test against a stubbed transport: it is NOT an API acceptance, and no live
 * environment is involved.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  A_ID,
  B_ID,
  CHAIN,
  PARTY,
  contactAddress,
  contactPerson,
  contactView,
  held,
  installFetch,
  json,
  jsonWithoutAck,
  problem,
  renderAt,
  settle,
} from './commandPageHarness'
import { resetContractReadiness } from './clientContract'
import { PROBLEM_CODE } from './contract'

const EDIT_PATH = `/cari/${PARTY}/bilgiler`
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

function control(id: string): HTMLInputElement | HTMLSelectElement {
  const node = document.getElementById(id)
  if (node === null) throw new Error(`no control with id "${id}"`)
  return node as HTMLInputElement | HTMLSelectElement
}

function saveButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /Bilgileri kaydet|Gönderiliyor/ }) as HTMLButtonElement
}

async function openEditor() {
  renderAt(EDIT_PATH)
  // The phone control only exists once the read has landed and seeded the draft.
  await waitFor(() => control('cari-contact-phone'), CHAIN)
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
  resetContractReadiness()
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('C-06 read and the closed Save', () => {
  it('shows what the account holds and offers no save until something changes', async () => {
    const fetchLog = installFetch({
      contact: () =>
        json(
          contactView({
            phone: '0212 555 00 00',
            addresses: [contactAddress()],
            contacts: [contactPerson()],
          }),
        ),
    })
    await openEditor()

    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('0212 555 00 00')
    expect((control('cari-address-0-line1') as HTMLInputElement).value).toBe('Merkez Cadde 1')
    expect((control('cari-address-0-purpose') as HTMLSelectElement).value).toBe('billing')
    expect((control('cari-contact-row-0-displayName') as HTMLInputElement).value).toBe('Ayşe Yılmaz')
    expect((control('cari-contact-row-0-channel-0-value') as HTMLInputElement).value).toBe('0532 000 00 00')

    // Nothing changed, so there is nothing to save: a save that sends an empty patch
    // spends a key and an audit record to say nothing.
    expect(saveButton().disabled).toBe(true)
    expect(screen.getByText('Kaydedilecek değişiklik yok.')).toBeTruthy()
    expect(fetchLog.contactUpdates()).toHaveLength(0)
    // And the whole read went out under ONE company header.
    expect(fetchLog.contactReads()[0].companyId).toBe(A_ID)
  })

  it('closes the kind of a channel the server already holds', async () => {
    installFetch({ contact: () => json(contactView({ contacts: [contactPerson()] })) })
    await openEditor()
    // Immutable for an existing UUID, so the control is closed rather than offering an
    // edit the contract refuses.
    expect((control('cari-contact-row-0-channel-0-kind') as HTMLSelectElement).disabled).toBe(true)
  })
})

describe('C-06 save — only what changed, then a fresh read', () => {
  it('sends the corrected field alone, fenced on the account version, and re-reads after 204', async () => {
    let current = contactView({
      phone: '0212 555 00 00',
      addresses: [contactAddress()],
      contacts: [contactPerson()],
    })
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          current = contactView(
            {
              phone: '+90 212 555 00 01',
              addresses: [contactAddress()],
              contacts: [contactPerson()],
            },
            { version: '2' },
          )
          return new Response(null, {
            status: 204,
            headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1' },
          })
        }
        return json(current)
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.clear(control('cari-contact-phone'))
    await user.type(control('cari-contact-phone'), '+90 212 555 00 01')
    expect(saveButton().disabled).toBe(false)
    await user.click(saveButton())

    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)
    const put = fetchLog.contactUpdates()[0]
    // Exactly the changed scalar. Every untouched field and BOTH collections are absent,
    // and absence is what retains them.
    expect(put.body).toEqual({ expectedVersion: '1', changes: { phone: '+90 212 555 00 01' } })
    expect(put.companyId).toBe(A_ID)
    expect(put.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/contact-information`)

    // The 204 carries no body, so the screen says nothing about the record until the
    // fresh read lands — and then it shows what the SERVER holds.
    await waitFor(() => {
      expect(screen.getByText('Bilgiler kaydedildi')).toBeTruthy()
    }, CHAIN)
    expect(fetchLog.contactReads().length).toBeGreaterThanOrEqual(2)
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('+90 212 555 00 01')
    expect(saveButton().disabled).toBe(true)
    expect(screen.getByTestId('cari-contact-announcement').textContent).toContain('kaydedildi')
  })

  it('adds an address and a contact with a channel, each under a client-minted id', async () => {
    const fetchLog = installFetch({
      contact: (call) =>
        call.method === 'PUT'
          ? new Response(null, {
              status: 204,
              headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1' },
            })
          : json(contactView()),
    })
    const user = userEvent.setup()
    await openEditor()

    await user.click(screen.getByRole('button', { name: 'Adres ekle' }))
    await user.selectOptions(control('cari-address-0-purpose'), 'delivery')
    await user.type(control('cari-address-0-countryCode'), 'TR')
    await user.type(control('cari-address-0-line1'), 'Depo Yolu 2')
    await user.type(control('cari-address-0-city'), 'Kocaeli')

    await user.click(screen.getByRole('button', { name: 'İrtibat kişisi ekle' }))
    await user.type(control('cari-contact-row-0-displayName'), 'Ayşe Yılmaz')
    await user.type(control('cari-contact-row-0-purpose'), 'Operasyon')
    await user.click(screen.getByRole('button', { name: 'Kanal ekle' }))
    await user.selectOptions(control('cari-contact-row-0-channel-0-kind'), 'phone')
    await user.type(control('cari-contact-row-0-channel-0-value'), '0532 000 00 00')

    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)

    const sent = fetchLog.contactUpdates()[0].body as {
      expectedVersion: string
      changes: {
        addresses: { id: string; operation: string; [k: string]: unknown }[]
        contacts: { id: string; operation: string; channels: { id: string; operation: string }[] }[]
      }
    }
    expect(sent.expectedVersion).toBe('1')
    expect(sent.changes.addresses).toHaveLength(1)
    // A v4 UUID the FORM minted when the row appeared — not at submit time, so a
    // same-key retry reproduces this exact payload.
    expect(sent.changes.addresses[0].id).toMatch(UUID_RE)
    expect(sent.changes.addresses[0]).toMatchObject({
      operation: 'upsert',
      purpose: 'delivery',
      countryCode: 'TR',
      line1: 'Depo Yolu 2',
      city: 'Kocaeli',
    })
    // The empty optional fields are absent, not null: absent means unset for a row that
    // never had a value, and null is a clear.
    expect(Object.hasOwn(sent.changes.addresses[0], 'postalCode')).toBe(false)
    expect(sent.changes.contacts[0]).toMatchObject({ operation: 'upsert', displayName: 'Ayşe Yılmaz' })
    expect(sent.changes.contacts[0].channels[0]).toMatchObject({ operation: 'upsert', kind: 'phone' })
    expect(sent.changes.contacts[0].channels[0].id).toMatch(UUID_RE)
    expect(sent.changes.contacts[0].channels[0].id).not.toBe(sent.changes.contacts[0].id)
  })

  it('deactivating a row sends the identity and the operation, and nothing else', async () => {
    const fetchLog = installFetch({
      contact: (call) =>
        call.method === 'PUT'
          ? new Response(null, {
              status: 204,
              headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1' },
            })
          : json(
              contactView({
                addresses: [contactAddress(), contactAddress({ id: 'a2222222-2222-4222-8222-222222222222', purpose: 'delivery' })],
              }),
            ),
    })
    const user = userEvent.setup()
    await openEditor()

    // The FIRST row is deactivated; the second is not spoken about at all.
    await user.click(screen.getAllByRole('button', { name: 'Pasife al' })[0])
    expect(screen.getByText('Kaydedildiğinde pasife alınacak.')).toBeTruthy()
    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)

    expect(fetchLog.contactUpdates()[0].body).toEqual({
      expectedVersion: '1',
      changes: {
        addresses: [{ id: 'a1111111-1111-4111-8111-111111111111', operation: 'deactivate' }],
      },
    })
  })
})

describe('C-06 refusals — the draft survives every one of them', () => {
  it('an invalid e-mail stops the save, marks the field and sends nothing', async () => {
    const fetchLog = installFetch({ contact: () => json(contactView()) })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.type(control('cari-contact-email'), 'operasyon@')
    await user.click(saveButton())

    expect(await screen.findByText(/işaretli alanları düzeltin/, {}, CHAIN)).toBeTruthy()
    expect(control('cari-contact-email').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText(/E-posta biçimi geçersiz/)).toBeTruthy()
    // Nothing left the client and no key was minted.
    expect(fetchLog.contactUpdates()).toHaveLength(0)
    // The form is intact: a refused save never costs the user their typing.
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('0212 555 00 00')
    expect((control('cari-contact-email') as HTMLInputElement).value).toBe('operasyon@')
  })

  it('clearing a phone refuses to leave its extension stranded', async () => {
    /**
     * The contract is explicit that a phone-only change must never silently destroy a
     * retained extension. So the refusal is VISIBLE and the user clears it themselves:
     * the client does not delete a value on their behalf, and the payload then carries
     * both nulls.
     */
    const fetchLog = installFetch({
      contact: (call) =>
        call.method === 'PUT'
          ? new Response(null, {
              status: 204,
              headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1' },
            })
          : json(contactView({ phone: '0212 555 00 00', phoneExtension: '123' })),
    })
    const user = userEvent.setup()
    await openEditor()

    await user.clear(control('cari-contact-phone'))
    await user.click(saveButton())
    expect(await screen.findByText(/Dahili numara yalnız bir telefonla/, {}, CHAIN)).toBeTruthy()
    expect(fetchLog.contactUpdates()).toHaveLength(0)

    await user.clear(control('cari-contact-phoneExtension'))
    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[0].body).toEqual({
      expectedVersion: '1',
      changes: { phone: null, phoneExtension: null },
    })
  })

  it('a version conflict is definite: no resend, and the draft is kept for a fresh read', async () => {
    const fetchLog = installFetch({
      contact: (call) =>
        call.method === 'PUT'
          ? problem(409, PROBLEM_CODE.versionConflict)
          : json(contactView({ phone: '0212 555 00 00' })),
    })
    const user = userEvent.setup()
    await openEditor()

    await user.clear(control('cari-contact-phone'))
    await user.type(control('cari-contact-phone'), '+90 212 555 00 01')
    await user.click(saveButton())

    const notice = await waitFor(() => {
      const node = document.querySelector('[data-failure="version-conflict"]')
      if (node === null) throw new Error('no version-conflict notice')
      return node
    }, CHAIN)
    expect(notice.textContent).toContain('Kayıt siz düzenlerken değişti')
    // One attempt, and no automatic retry: the kept snapshot carries the fence that is
    // already stale, so a resend would be refused for exactly the same reason.
    expect(fetchLog.contactUpdates()).toHaveLength(1)
    // The user's edit is still on screen, ready to be re-entered against a fresh read.
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('+90 212 555 00 01')
    expect(screen.getByRole('button', { name: 'Kaydı yenile' })).toBeTruthy()
  })

  it('a lost reply leaves the outcome unknown, with the same-key retry and no success', async () => {
    let puts = 0
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method !== 'PUT') return json(contactView({ phone: '0212 555 00 00' }))
        puts += 1
        throw new TypeError('Failed to fetch')
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-email'), 'operasyon@example.test')
    await user.click(saveButton())

    const notice = await waitFor(() => {
      const node = document.querySelector('[data-attempt="unknown"]')
      if (node === null) throw new Error('no unknown notice')
      return node
    }, CHAIN)
    // The record may have been written: the screen must not say it was not.
    expect(notice.textContent).toContain('İşlem sonucu belirsiz')
    expect(screen.queryByText('Bilgiler kaydedildi')).toBeNull()
    expect(puts).toBe(1)

    // The retry re-checks the ground it stands on first, then re-sends the SAME key.
    const firstKey = fetchLog.contactUpdates()[0].idempotencyKey
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => {
      expect(puts).toBe(2)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[1].idempotencyKey).toBe(firstKey)
    expect(fetchLog.contactUpdates()[1].body).toEqual(fetchLog.contactUpdates()[0].body)
  })

  it('a non-disclosing 404 is a state block, never an empty contact card', async () => {
    // No visible account relation in the selected company. An empty editor would be a
    // claim that the counterparty has no address; this screen makes no such claim.
    installFetch({ contact: () => problem(404, PROBLEM_CODE.notFound) })
    renderAt(EDIT_PATH)
    await waitFor(() => {
      const node = document.querySelector('[data-state="not-found"]')
      if (node === null) throw new Error('no not-found block')
      return node
    }, CHAIN)
    expect(document.getElementById('cari-contact-phone')).toBeNull()
  })
})

describe('C-06 gating', () => {
  it('mounts nothing without the editor capability, and calls no contact API', async () => {
    /**
     * A capability is UI visibility, not authorization — the API re-authorizes the read
     * and the basic update on every request. What the gate buys is that the user is
     * never offered an editor whose Save they are not permitted to use, and that an
     * ungranted route is indistinguishable from an unknown one.
     */
    const fetchLog = installFetch({
      capabilities: ['cari.parties.list', 'cari.parties.detail'],
      contact: () => json(contactView()),
    })
    renderAt(EDIT_PATH)
    await waitFor(() => {
      const node = document.querySelector('[data-state="not-found"]')
      if (node === null) throw new Error('no not-found block')
      return node
    }, CHAIN)
    expect(fetchLog.contactReads()).toHaveLength(0)
    expect(fetchLog.contactUpdates()).toHaveLength(0)
  })
})

/**
 * The behaviours the first non-author review found missing, each with its own case, plus
 * the narrow controlled additions Business asked for in the same batch.
 *
 * CONTROLLED, and labelled as such: the transport is stubbed. None of these is an API
 * acceptance, and the session cases are a context change in this client — no
 * identity-provider session was revoked anywhere.
 */

/** A real-shaped 204: no body, and the acknowledgment a compatible node sends. */
function ack204(headers: Record<string, string> = {}): Response {
  return new Response(null, {
    status: 204,
    headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1', ...headers },
  })
}

async function findByState(state: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = document.querySelector<HTMLElement>(`[data-state="${state}"]`)
    if (node === null) throw new Error(`no [data-state="${state}"]`)
    return node
  }, CHAIN)
}

async function findByFailure(failure: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = document.querySelector<HTMLElement>(`[data-failure="${failure}"]`)
    if (node === null) throw new Error(`no [data-failure="${failure}"]`)
    return node
  }, CHAIN)
}

describe('C-06 after a save — the editor stays usable', () => {
  it('lets the user save AGAIN once the first save has been confirmed', async () => {
    /**
     * A completed attempt can begin no new one, so with the attempt left completed the
     * screen re-seeded, called itself clean, and then refused every further edit: Save
     * enabled, submit silently blocked, nothing said. The attempt is released once the
     * fresh read it depends on has landed.
     */
    let version = 1
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          version += 1
          return ack204()
        }
        return json(
          contactView({ phone: version > 1 ? '+90 212 555 00 01' : null }, { version: String(version) }),
        )
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '+90 212 555 00 01')
    await user.click(saveButton())
    await waitFor(() => {
      expect(screen.getByText('Bilgiler kaydedildi')).toBeTruthy()
    }, CHAIN)
    expect(saveButton().disabled).toBe(true)

    // A second, independent edit on the SAME screen.
    await user.type(control('cari-contact-email'), 'operasyon@example.test')
    expect(saveButton().disabled).toBe(false)
    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(2)
    }, CHAIN)

    const second = fetchLog.contactUpdates()[1]
    // Fenced on what the record became, under a key of its own.
    expect(second.body).toEqual({ expectedVersion: '2', changes: { email: 'operasyon@example.test' } })
    expect(second.idempotencyKey).not.toBe(fetchLog.contactUpdates()[0].idempotencyKey)
  })

  it('offers a read-only recovery when the read after a save fails, and sends no second write', async () => {
    let puts = 0
    let gets = 0
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          puts += 1
          return ack204()
        }
        gets += 1
        // The read the screen needs in order to say anything at all.
        if (gets === 2) throw new TypeError('Failed to fetch')
        return json(contactView({ phone: puts > 0 ? '+90 212 555 00 01' : null }, { version: puts > 0 ? '2' : '1' }))
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '+90 212 555 00 01')
    await user.click(saveButton())

    const recovery = await findByState('read-recovery')
    expect(recovery.textContent).toContain('kaydın güncel hâli okunamadı')
    // Nothing is claimed about the record, and no second write may go out on top of a
    // state nobody has confirmed.
    expect(screen.queryByText('Bilgiler kaydedildi')).toBeNull()
    expect(saveButton().disabled).toBe(true)
    expect(puts).toBe(1)

    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => {
      expect(screen.getByText('Bilgiler kaydedildi')).toBeTruthy()
    }, CHAIN)
    // The recovery cost no extra write.
    expect(puts).toBe(1)
    expect(fetchLog.contactUpdates()).toHaveLength(1)
  })
})

describe('C-06 conflict recovery — the fence moves, the draft does not', () => {
  it('adopts the new version on refresh, keeps the draft, and saves on the new fence', async () => {
    let version = 1
    let gets = 0
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          const expected = (call.body as { expectedVersion?: string } | null)?.expectedVersion
          if (expected !== String(version)) return problem(409, PROBLEM_CODE.versionConflict)
          version += 1
          return ack204()
        }
        gets += 1
        const body = json(contactView({}, { version: String(version) }))
        // Somebody else commits between the editor's seed and its save.
        if (gets === 1) version = 2
        return body
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-email'), 'operasyon@example.test')
    await user.click(saveButton())
    const conflict = await findByFailure('version-conflict')
    expect(conflict.textContent).toContain('Kayıt siz düzenlerken değişti')
    expect(fetchLog.contactUpdates()[0].body).toMatchObject({ expectedVersion: '1' })

    await user.click(screen.getByRole('button', { name: 'Kaydı yenile' }))
    await findByState('refreshed')
    // The draft is NOT overwritten: the user's own words survive for them to review.
    expect((control('cari-contact-email') as HTMLInputElement).value).toBe('operasyon@example.test')

    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(2)
    }, CHAIN)
    // The second attempt is fenced on what the refresh actually read, not on the version
    // the conflict was about — and it carries its own key.
    expect(fetchLog.contactUpdates()[1].body).toEqual({
      expectedVersion: '2',
      changes: { email: 'operasyon@example.test' },
    })
    expect(fetchLog.contactUpdates()[1].idempotencyKey).not.toBe(fetchLog.contactUpdates()[0].idempotencyKey)
    await waitFor(() => {
      expect(screen.getByText('Bilgiler kaydedildi')).toBeTruthy()
    }, CHAIN)
  })

  it('keeps the same-key recovery reachable after an unknown outcome is abandoned', async () => {
    let puts = 0
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method !== 'PUT') return json(contactView())
        puts += 1
        throw new TypeError('Failed to fetch')
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.click(saveButton())
    await waitFor(() => {
      expect(document.querySelector('[data-attempt="unknown"]')).toBeTruthy()
    }, CHAIN)
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))

    /**
     * Abandoned is still UNRESOLVED: the write may have landed. The box has to stay on
     * screen, because without it the unchanged payload was refused silently and the
     * same-key retry — the only way to resolve it — was unreachable.
     */
    const abandoned = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[data-attempt="abandoned"]')
      if (node === null) throw new Error('no abandoned notice')
      return node
    }, CHAIN)
    expect(abandoned.textContent).toContain('Bırakılan işlemin sonucu belirsiz')
    // The unchanged payload mints no new key.
    await user.click(saveButton())
    expect(puts).toBe(1)

    await user.click(screen.getByRole('button', { name: 'Önceki işlemi yeniden dene' }))
    await waitFor(() => {
      expect(puts).toBe(2)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[1].idempotencyKey).toBe(fetchLog.contactUpdates()[0].idempotencyKey)
    expect(fetchLog.contactUpdates()[1].body).toEqual(fetchLog.contactUpdates()[0].body)
  })
})

describe('C-06 refusals that must not cost the user their work', () => {
  it('treats an invalid-only edit as unsaved work, not as a clean form', async () => {
    const fetchLog = installFetch({ contact: () => json(contactView()) })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-email'), 'operasyon@')
    // Dirty: Save is offered and the "nothing to save" hint is gone, so the user can find
    // out what is wrong instead of facing a grey button with no explanation.
    expect(saveButton().disabled).toBe(false)
    expect(screen.queryByText('Kaydedilecek değişiklik yok.')).toBeNull()

    await user.click(saveButton())
    expect(await screen.findByText(/E-posta biçimi geçersiz/, {}, CHAIN)).toBeTruthy()
    expect(fetchLog.contactUpdates()).toHaveLength(0)
    expect((control('cari-contact-email') as HTMLInputElement).value).toBe('operasyon@')
  })

  it('moves focus to the error summary on the first refused submit', async () => {
    installFetch({ contact: () => json(contactView()) })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-email'), 'operasyon@')
    await user.click(saveButton())
    const summary = await findByState('form-invalid')
    // The summary is created by the render this click caused, so focusing it from the
    // handler moved nothing: the focus stayed on the Save button.
    await waitFor(() => {
      expect(document.activeElement).toBe(summary)
    }, CHAIN)
  })

  it('lets the user drop a row they added instead of being held hostage by it', async () => {
    const fetchLog = installFetch({ contact: (call) => (call.method === 'PUT' ? ack204() : json(contactView())) })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.click(screen.getByRole('button', { name: 'Adres ekle' }))
    await user.click(saveButton())
    // The blank row blocks the whole section — which is correct — so there has to be a
    // way out of it that is not "abandon the edit you actually came to make".
    expect(await screen.findByText(/Eklenen satırı doldurun/, {}, CHAIN)).toBeTruthy()
    expect(fetchLog.contactUpdates()).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Satırı kaldır' }))
    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[0].body).toEqual({
      expectedVersion: '1',
      changes: { phone: '0212 555 00 00' },
    })
  })

  it('never lets a status operation swallow a field edit', async () => {
    const fetchLog = installFetch({
      contact: (call) =>
        call.method === 'PUT'
          ? ack204()
          : json(contactView({ addresses: [contactAddress({ city: null, district: null })] })),
    })
    const user = userEvent.setup()
    await openEditor()

    // A status operation carries the identity and NOTHING else, so an edited row that is
    // also marked passive would have had its edit dropped in silence. The screen closes
    // whichever the user did not start, and says why.
    await user.type(control('cari-address-0-city'), 'Kocaeli')
    expect((screen.getByRole('button', { name: 'Pasife al' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText(/durum işlemi ayrı bir kayıttır/)).toBeTruthy()

    await user.clear(control('cari-address-0-city'))
    expect((screen.getByRole('button', { name: 'Pasife al' }) as HTMLButtonElement).disabled).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Pasife al' }))
    expect(control('cari-address-0-line1').disabled).toBe(true)
    expect(screen.getByText(/alanlar gönderilmez/)).toBeTruthy()

    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[0].body).toEqual({
      expectedVersion: '1',
      changes: { addresses: [{ id: 'a1111111-1111-4111-8111-111111111111', operation: 'deactivate' }] },
    })
  })
})

describe('C-06 controlled boundaries — stubbed transport, never an API acceptance', () => {
  it('sends no write when the read that would open the gate was not acknowledged', async () => {
    /**
     * A 200 with a perfectly valid body from a node that never acknowledges is exactly how
     * an old server looks. The editor still renders — the body IS valid — but the write
     * gate never opened, so the refusal happens before a request exists.
     */
    const fetchLog = installFetch({
      contact: (call) => (call.method === 'PUT' ? ack204() : jsonWithoutAck(contactView())),
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.click(saveButton())
    const refusal = await findByFailure('contract-not-ready')
    expect(refusal.textContent).toContain('Uyumlu istemci gerekli')
    expect(fetchLog.contactUpdates()).toHaveLength(0)
  })

  it('keeps the draft after a server 400 and lets the user correct it under a new key', async () => {
    let puts = 0
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method !== 'PUT') return json(contactView())
        puts += 1
        return puts === 1 ? problem(400, PROBLEM_CODE.validationFailed) : ack204()
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.click(saveButton())
    const failed = await findByFailure('validation')
    expect(failed.textContent).toContain('Sunucu doğrulaması başarısız')
    /**
     * The rejection is shown as a summary and NOT parsed into field messages: this 400
     * carries a localized `detail` and no field map, so placing it on an input would be
     * the client inventing a pointer the server never sent.
     */
    expect(document.querySelector('[aria-invalid="true"]')).toBeNull()
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('0212 555 00 00')

    await user.click(screen.getByRole('button', { name: 'Anladım' }))
    await user.type(control('cari-contact-email'), 'operasyon@example.test')
    await user.click(saveButton())
    await waitFor(() => {
      expect(puts).toBe(2)
    }, CHAIN)
    // A definite rejection closed the first attempt, so the corrected submit is a NEW
    // business attempt with a key of its own.
    expect(fetchLog.contactUpdates()[1].idempotencyKey).not.toBe(fetchLog.contactUpdates()[0].idempotencyKey)
  })

  it('applies nothing to the screen when the reply lands after the company changed', async () => {
    /**
     * A CONTEXT change in this client, and nothing more: no identity-provider session was
     * revoked and none can be from here. What is measured is that an outcome bound to the
     * company the attempt was made under does not paint a screen that has moved on.
     */
    const gate = held()
    const fetchLog = installFetch({
      contact: (call) => (call.method === 'PUT' ? gate.promise : json(contactView())),
    })
    const user = userEvent.setup()
    await openEditor()

    await user.type(control('cari-contact-phone'), '0212 555 00 00')
    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(1)
    }, CHAIN)

    // The draft is defended on the way out: a frozen attempt is unsaved work too.
    await user.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
    const dialog = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[role="dialog"]')
      if (node === null) throw new Error('no switch dialog')
      return node
    }, CHAIN)
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))

    gate.release(ack204())
    await settle(150)
    // The late success belongs to an attempt whose screen is gone.
    expect(screen.queryByText('Bilgiler kaydedildi')).toBeNull()
    expect(fetchLog.contactUpdates().filter((c) => c.companyId === B_ID)).toHaveLength(0)
  })
})

describe('C-06 concurrency — a refresh must not re-assert what the user never touched', () => {
  it('keeps only the user’s own edits and takes every other field from the fresh read', async () => {
    /**
     * The failure this closes: v1 holds phone 111, the user changes ONLY the e-mail,
     * another editor commits phone 222, the save is refused with 409, the user refreshes —
     * and the patch then carried phone 111 as well, wiping 222 on a save the user believed
     * was about their e-mail. The same root cause reached address, contact and channel
     * fields.
     */
    const address = (city: string, version: string) =>
      contactAddress({ city, district: null, version })
    let version = 1
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          const expected = (call.body as { expectedVersion?: string } | null)?.expectedVersion
          if (expected !== String(version)) return problem(409, PROBLEM_CODE.versionConflict)
          version += 1
          return ack204()
        }
        // v1 is what the editor seeds on; v2 is what the OTHER editor left behind.
        return version === 1
          ? json(contactView({ phone: '111', email: null, addresses: [address('Eski', '1')] }, { version: '1' }))
          : json(contactView({ phone: '222', email: null, addresses: [address('Yeni', '2')] }, { version: '2' }))
      },
    })
    const user = userEvent.setup()
    await openEditor()
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('111')
    expect((control('cari-address-0-city') as HTMLInputElement).value).toBe('Eski')

    // The user's entire intent: the e-mail, and nothing else.
    await user.type(control('cari-contact-email'), 'operasyon@example.test')
    // Somebody else commits in the meantime.
    version = 2

    await user.click(saveButton())
    await findByFailure('version-conflict')
    await user.click(screen.getByRole('button', { name: 'Kaydı yenile' }))
    await findByState('refreshed')

    // Their values are now on screen — for the fields this user did not touch.
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('222')
    expect((control('cari-address-0-city') as HTMLInputElement).value).toBe('Yeni')
    // And the user's own work survived.
    expect((control('cari-contact-email') as HTMLInputElement).value).toBe('operasyon@example.test')

    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(2)
    }, CHAIN)
    // The decisive assertion: the patch carries the e-mail and NOTHING else. A rising
    // version number would not have proved this — the old draft's phone would have
    // travelled with it.
    expect(fetchLog.contactUpdates()[1].body).toEqual({
      expectedVersion: '2',
      changes: { email: 'operasyon@example.test' },
    })
  })

  it('keeps a status operation the user marked across the refresh', async () => {
    let version = 1
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          const expected = (call.body as { expectedVersion?: string } | null)?.expectedVersion
          if (expected !== String(version)) return problem(409, PROBLEM_CODE.versionConflict)
          version += 1
          return ack204()
        }
        return json(
          contactView({ phone: version === 1 ? '111' : '222', addresses: [contactAddress({ city: null, district: null })] }, { version: String(version) }),
        )
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.click(screen.getByRole('button', { name: 'Pasife al' }))
    version = 2
    await user.click(saveButton())
    await findByFailure('version-conflict')
    await user.click(screen.getByRole('button', { name: 'Kaydı yenile' }))
    await findByState('refreshed')
    // The marked operation is the user's intent and outlives the refresh; the phone they
    // never touched is now the server's.
    expect(screen.getByText('Kaydedildiğinde pasife alınacak.')).toBeTruthy()
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('222')

    await user.click(saveButton())
    await waitFor(() => {
      expect(fetchLog.contactUpdates()).toHaveLength(2)
    }, CHAIN)
    expect(fetchLog.contactUpdates()[1].body).toEqual({
      expectedVersion: '2',
      changes: { addresses: [{ id: 'a1111111-1111-4111-8111-111111111111', operation: 'deactivate' }] },
    })
  })

  it('closes the form while the pre-retry read is in flight, so no edit is lost to it', async () => {
    /**
     * The retry re-sends a payload that is already frozen. With the form open, an edit made
     * while its licensing read was in flight was not in what went out — and the success
     * that followed re-seeded the draft and erased it.
     */
    let puts = 0
    let gets = 0
    const gate = held()
    const fetchLog = installFetch({
      contact: (call) => {
        if (call.method === 'PUT') {
          puts += 1
          if (puts === 1) throw new TypeError('Failed to fetch')
          return ack204()
        }
        gets += 1
        // The read the retry has to stand on, held open by the test.
        return gets === 2 ? gate.promise : json(contactView({ phone: '0212 555 00 00' }))
      },
    })
    const user = userEvent.setup()
    await openEditor()

    await user.clear(control('cari-contact-phone'))
    await user.type(control('cari-contact-phone'), '+90 212 555 00 01')
    await user.click(saveButton())
    await waitFor(() => {
      expect(document.querySelector('[data-attempt="unknown"]')).toBeTruthy()
    }, CHAIN)
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))
    await user.click(screen.getByRole('button', { name: 'Önceki işlemi yeniden dene' }))

    // While the read is in flight the fields are closed and the screen says why. The
    // group carries the disabled state, which is what actually stops the interaction —
    // an input's own disabled property does not reflect an ancestor fieldset.
    await waitFor(() => {
      expect(control('cari-contact-phone').closest('fieldset')?.disabled).toBe(true)
    }, CHAIN)
    expect(screen.getByText(/kayıt yeniden okunuyor/)).toBeTruthy()
    expect(saveButton().disabled).toBe(true)
    // And an attempt to edit during the window changes nothing.
    await user.type(control('cari-contact-phone'), 'X')
    expect((control('cari-contact-phone') as HTMLInputElement).value).toBe('+90 212 555 00 01')

    gate.release(json(contactView({ phone: '0212 555 00 00' })))
    await waitFor(() => {
      expect(puts).toBe(2)
    }, CHAIN)
    // The re-send is the SAME attempt: same key, same payload, and the payload is the one
    // the user actually submitted.
    expect(fetchLog.contactUpdates()[1].idempotencyKey).toBe(fetchLog.contactUpdates()[0].idempotencyKey)
    expect(fetchLog.contactUpdates()[1].body).toEqual({
      expectedVersion: '1',
      changes: { phone: '+90 212 555 00 01' },
    })
  })
})
