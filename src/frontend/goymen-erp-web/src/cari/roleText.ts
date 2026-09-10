/**
 * The Turkish vocabulary for `PartyRole`, and the one rule for a counterparty that
 * carries no commercial role.
 *
 * WHY THIS IS ONE MODULE. The 10 September decision removes the mandatory
 * commercial role from Cari (SINAN_KARARLARI_2026-09-10 §2, ILK_DILIM §1). That
 * makes "no role" an ordinary, valid record — and ILK_DILIM §7 constrains how it
 * may be shown twice over:
 *
 *  - it is NOT "Eksik", "Yetkisiz", "Pasif" or a blocking warning;
 *  - it is NOT an empty cell either. This screen already decided, for the two
 *    active/passive columns, that an absence reads the same as "unknown" — the
 *    same reasoning applies here, so the absence is STATED.
 *
 * "Belirtilmemiş" / "Sınıflandırılmamış" are deliberately not used: both name the
 * record as lacking something it was supposed to have, which is exactly the
 * precondition the decision removed.
 *
 * The label list lived in two screens before this; a role vocabulary that differs
 * between the list and the detail of the SAME account is a defect waiting to
 * happen, so both now read it from here.
 */

import { rolesFromFlags, type PartyRoleName } from './contract'

export const ROLE_LABEL: Record<PartyRoleName, string> = {
  Customer: 'Müşteri',
  Supplier: 'Tedarikçi',
  Tolling: 'Fason',
  Service: 'Hizmet',
}

/**
 * The role flags are LEGACY markers as of the 10 September decision: they record a
 * classification some earlier user chose, and they are not what today's work
 * direction is derived from (ILK_DILIM §4.1, §7). Every place that shows or filters
 * by them says so, so they cannot be read as the current commercial direction.
 */
export const LEGACY_ROLE_HEADING = 'Geçmiş ticari sınıflandırma'

/**
 * The FILTER surface, named differently from the column on purpose: one is a fact
 * about a record, the other is a way to narrow a query. Identical wording put two
 * unrelated controls under one name on the same screen, which is ambiguous to
 * anyone navigating by heading or by text rather than by sight.
 */
export const LEGACY_ROLE_FILTER_HEADING = 'Geçmiş sınıflandırma'

/** A row cell: short, because it sits in a table column. */
export const NO_COMMERCIAL_ROLE_LIST = 'Ticari rol yok'
/** The record's own screen, where there is room to say that this is not a gap. */
export const NO_COMMERCIAL_ROLE_DETAIL = 'Ticari rol yok — bu alan zorunlu değil'

/**
 * The localized role names carried by a flag integer, or `whenNone` when it carries
 * no commercial classification at all.
 *
 * `flags` is the wire value: `0` means "no commercial classification", `1..15` are
 * the legacy bits. Unknown bits never reach here — the decoder rejects them before
 * a view sees the response.
 */
export function rolesText(flags: number, whenNone: string): string {
  const names = rolesFromFlags(flags)
  return names.length === 0 ? whenNone : names.map((name) => ROLE_LABEL[name]).join(', ')
}
