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
 * (`CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` at `a6efdc0`). This is a component
 * test against a stubbed transport: it is NOT an API acceptance, and no live
 * environment is involved.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  A_ID,
  CHAIN,
  PARTY,
  contactAddress,
  contactPerson,
  contactView,
  installFetch,
  json,
  problem,
  renderAt,
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
