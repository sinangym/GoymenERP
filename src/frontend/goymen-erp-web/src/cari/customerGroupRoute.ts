/**
 * SAT-D57 — where the company's customer group list is managed.
 *
 * Mounted under the Cari list capability (`cari.parties.list`, i.e. `counterparty.parties.read` company-wide — exactly
 * the list read's own permission) and reached from the Cari list and the Cari card. A static segment declared ahead of
 * `/cari/:partyId`, so it is never read as a party id. Drawing it under the Ayarlar sidebar group needs its own server
 * shell capability (a Cari owner-contract change), which is a separate step; listing the path here grants nothing.
 */
export const CUSTOMER_GROUPS_ROUTE = '/cari/musteri-gruplari'
