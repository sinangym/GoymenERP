/**
 * The shell's navigation definitions: which mounted capabilities the sidebar names, in what order,
 * and under which label. `AppShell` decides what the server granted and draws it; nothing here
 * grants, hides or routes anything. An entry still renders only where the snapshot carries its
 * capability (DEC-GOV-007).
 *
 * Both tables were moved here unchanged — every id, label, position and comment — from
 * `AppShell.tsx` (the order) and `capabilities.ts` (the labels). Both files still export them, so
 * their existing importers are untouched; `navEntryPointInvariant.test.ts` pins that those exports
 * are these very objects, not copies.
 *
 * Why the keys are the ids themselves rather than `CAPABILITY.…`: `capabilities.ts` re-exports
 * `CAPABILITY_NAV_LABEL` from this file, so a VALUE import of `CAPABILITY` back would be a module
 * cycle, and whichever side loaded first would build this table before `CAPABILITY` existed. The
 * type import below is erased at build time, and `CapabilityId` keeps the compiler as the check: an
 * unknown or renamed id fails `tsc` in either table, and a missing one fails it in the label table.
 */

import type { CapabilityId } from './capabilities'

/** Every capability the frontend can actually mount, in nav order. */
/** Exported for `navEntryPointInvariant.test.ts`: the nav list and the label table must agree. */
export const NAV_CAPABILITIES: readonly CapabilityId[] = [
  'cari.parties.list',
  /**
   * `cariPartiesCreate` is deliberately ABSENT, and it is the one removal here that is a decision
   * rather than a layout fact: K5 (Sinan, 20 Eylül 2026, #66 5748258299) makes creation a single
   * authorised "Yeni …" action INSIDE the relevant list and names cari among the modules whose
   * parallel sidebar-create must not be duplicated. C-01 carries that action from this change on,
   * so the route keeps exactly one entry point — the invariant `TEKNIK_TUZAKLAR §8` states, whose
   * CHOICE clause K5 narrowed.
   *
   * The two moves are in ONE change on purpose: `navEntryPointInvariant.test.ts` warns that
   * removing an entry without adding its replacement produces a screen reachable from nowhere,
   * which is the MFG-02 condition that file exists to catch.
   */
  'cari.accounts.link',
  /**
   * K5 gives Product exactly one sidebar entry: the list. Create remains a mounted,
   * separately authorised route reached through the list's single create action; the
   * 360 and editor likewise stay route-only. Product capabilities still come from the
   * global Product authority, never from a company permission.
   */
  'product.items.list',
  'finance.manual-bank-payments.list',
  'finance.allocations.list',
  /**
   * Listed together with the route that serves it, on purpose: a nav entry whose route
   * is not mounted is a link to the not-found block. It still renders only where the
   * server granted the capability, so until Backend #246 publishes `organization.*` it
   * does not appear. The card is deliberately absent — it is reached from a location
   * row, never from the sidebar.
   */
  'organization.sites.list',
  /**
   * ORG-03. The sidebar is not one option among several here, it is the ONLY one: the
   * route stands on `organization.work-centers.read` alone, so a holder of that and not
   * `organization.locations.read` never opens ORG-01 and could never be linked from it.
   * Nothing else renders only where the server granted it, as ever.
   */
  'organization.work-centers.list',
  /** S-02 is the single Sales work-list entry. Create lives inside that list; detail opens from a row. */
  'sales.drafts.list',
  /**
   * MFG-02, listed with the route that serves it (MFG-ORD). It renders only where the server's
   * snapshot carries the capability; the order detail is reached from a list row, never the sidebar.
   *
   * It was MISSING until now, and the gap was invisible: the capability, its route and its label all
   * existed and `manufacturingMountDrift` asserted the label — but nothing asserted that any nav entry
   * NAMED it, and nothing in the tree links to `/uretim/emirler`. So a delivered screen could only be
   * opened by typing the URL, and the order detail sat behind a list nobody could reach (#363 review,
   * P3-3). The lesson is in the case beside this file: assert the LINK, not the label it would use.
   */
  'manufacturing.orders.list',
  /**
   * Listed together with the routes that serve them (FIN-UI-1b / FIN-T06). Each renders only where
   * the server's snapshot carries the capability; the new draft, the detail and the propose screen
   * have none. The reversal checker queue is a separate work list and therefore has its own entry.
   */
  'finance.receipts.list',
  'finance.customer-instruments.list',
  'finance.official-invoice-import.upload',
  'finance.official-invoice-import.approve',
  'finance.receipts.reverse.approve',
  'finance.bank-accounts.list',
  /**
   * Listed together with the routes that serve them (ACC-V0b). Each renders only where the server's
   * snapshot carries the capability — staged until the acc01 acceptance — and the propose screen and
   * the journal detail are reached from their own list, never from the sidebar.
   */
  'accounting.periods.list',
  'accounting.journals.list',
  'accounting.posting-queues',
  'inventory.opening-stock.record',
  'inventory.stock-positions.list',
  /**
   * Listed together with the routes that serve them (LOG-4b). Each renders only where the server's
   * snapshot carries the capability (staged until the l04 suite); the detail is reached from a row.
   */
  // The L-03 creation screen is NOT here: the L-02 list carries its own primary link, and the rule
  // is exactly one entry point per creation route (coordinator, 19 September 2026). Two links with
  // the same accessible name to one route is what l04's strict-mode violation found, and a screen
  // reader announces it twice.
  'logistics.instructions.list',
  // AT-025: the operations work list's two separate sections, each shown only to an actor holding its own grant.
  'logistics.operations-work.sales',
  'logistics.operations-work.purchase',
  /**
   * P-01 and P-03 are Purchasing's two list entries. Their create actions live inside their lists
   * under K5, and each detail is reached from a row. Submit and approve are controls inside their
   * respective details, never routes or navigation items.
   */
  'purchasing.requests.list',
  'purchasing.orders.list',
  /**
   * SY-01 read subset, listed with the route that serves it. It renders only where the server granted
   * `identity.assignments.read` company-wide; the role card is reached from a row.
   */
  'identity.assignments.list',
]

/** Nav label per capability. Only rendered when the server granted the capability. */
export const CAPABILITY_NAV_LABEL: Record<CapabilityId, string> = {
  'cari.parties.list': 'Cari Kartlar',
  // No nav entry, and this one is a DECISION rather than the usual "reached from a row": K5 (Sinan,
  // 20 Eylül 2026, #66 5748258299) puts creation inside the relevant list as a single authorised
  // "Yeni …" action and names cari among the modules whose parallel sidebar-create must not be
  // duplicated. C-01 carries that action now. The entry is removed and the list's link is added in
  // the SAME change, because removing one without the other is the MFG-02 condition
  // `navEntryPointInvariant.test.ts` exists to catch.
  'cari.parties.create': '',
  'cari.accounts.link': 'Cariyi Şirkete Bağla',
  // No nav entry: the detail is reached from a row, never from the sidebar.
  'cari.parties.detail': '',
  // Nor this one: it is reached from the card of the record it edits.
  'cari.accounts.contact.update': '',
  'product.items.list': 'Ürünler',
  // K5: create is reached through the list's single action, never from the sidebar.
  'product.items.create': '',
  // The 360 is reached from a row and the editor from the card it edits. Therefore
  // `NAV_CAPABILITIES` gains exactly one Product entry when the routes are mounted.
  'product.items.detail': '',
  'product.items.edit': '',
  'organization.sites.list': 'Organizasyon',
  // Nor this one: the card is reached from a location row, never from the sidebar. So
  // `NAV_CAPABILITIES` gains exactly ONE Organization entry when the routes are mounted.
  'organization.locations.detail': '',
  // ORG-03 DOES get one: its only entry point is the sidebar, because a holder of
  // work-centers.read without locations.read never reaches ORG-01 to be linked from it.
  // So Organization's sidebar goes from one entry to two — a list each, no card, no control.
  'organization.work-centers.list': 'İş Merkezleri',
  // ORG-04: reached from a work-centre row, never from the sidebar; Organization keeps two entries.
  'organization.work-centers.detail': '',
  'sales.drafts.list': 'Sipariş Taslakları',
  // K5: creation is reached through the list's authorised action, never a parallel sidebar entry.
  'sales.drafts.create': '',
  // Nor this one: the 360 is reached from a list row, never from the sidebar.
  'sales.drafts.detail': '',
  'manufacturing.orders.list': 'Üretim emirleri',
  // Nor this one: an order is opened from the list or by its id, never from the sidebar. So
  // `NAV_CAPABILITIES` gains exactly ONE Manufacturing entry.
  'manufacturing.orders.detail': '',
  // K5: creation is reached from the order list; detail is reached from the saved plan result.
  'manufacturing.plans.create': '',
  'manufacturing.plans.detail': '',
  // MFG-PLAN-LIST: reached from the order list and plan detail, so the sidebar keeps ONE Manufacturing entry.
  'manufacturing.plans.list': '',
  'finance.receipts.list': 'Tahsilat Taslakları',
  'finance.customer-instruments.list': 'Müşteri Çekleri ve Senetleri',
  'finance.official-invoice-import.upload': 'Onaylı fatura taksit kaynağı yükle',
  'finance.official-invoice-import.approve': 'Resmî fatura aktarım onayları',
  'finance.allocations.list': 'Finansal Dağıtımlar',
  'finance.receipts.reverse.approve': 'Ters Kayıt Onayları',
  'finance.bank-accounts.list': 'Banka Hesapları',
  // None of these three: the new draft is entered from a Cari account, the detail from a list row and
  // the propose screen from the F-04 list, never from the sidebar.
  'finance.receipts.draft': '',
  'finance.receipts.detail': '',
  'finance.bank-accounts.propose': '',
  'finance.manual-bank-payments.list': 'Giden Ödemeler',
  'finance.manual-bank-payments.create': '',
  'finance.manual-bank-payments.detail': '',
  'accounting.periods.list': 'Muhasebe Dönemleri',
  'accounting.journals.list': 'Muhasebe Fişleri',
  'accounting.posting-queues': 'Muhasebe Kontrol',
  // Staged until the actual M-06 browser acceptance; no sidebar publication yet.
  'accounting.trial-balance': '',
  'inventory.opening-stock.record': 'Stok Açılışı',
  'inventory.stock-positions.list': 'Stok / Malzeme Pozisyonu',
  'identity.assignments.list': 'Kullanıcılar ve yetkiler',
  // The role card is opened from a row of the access list, never from the sidebar.
  'identity.roles.detail': '',
  // Not these two: a period is proposed from the M-07 list and a journal opened from a row.
  'accounting.periods.propose': '',
  'accounting.journals.detail': '',
  'accounting.normal-journal-drafts.create': '',
  'accounting.normal-journal-drafts.detail': '',
  'accounting.normal-journal-drafts.approve': '',
  'logistics.instructions.list': 'Yükleme Talimatları',
  // Neither of these two: the new-instruction screen is reached from the L-02 list's own primary
  // link (one entry point per creation route, the Finance and Accounting precedent), and an
  // instruction is opened from a list row — never from the sidebar.
  'logistics.instructions.create': '',
  'logistics.instructions.detail': '',
  // AT-025: one sidebar entry per section, because each section is its own grant.
  'logistics.operations-work.sales': 'Operasyon işleri — Satış',
  'logistics.operations-work.purchase': 'Operasyon işleri — Satın alma',
  'purchasing.requests.list': 'Satın alma talepleri',
  // K5: create is reached through the list's single action; detail is reached from a row.
  'purchasing.requests.create': '',
  'purchasing.requests.detail': '',
  'purchasing.orders.list': 'Satın alma siparişleri',
  // P-04 is staged and, once published, is reached from the list. The detail is reached from a row.
  'purchasing.orders.create': '',
  'purchasing.orders.detail': '',
}
