/** FI04 open-item list and detail: every user-facing string of the two screens. */

/**
 * The source kinds this screen can name. A kind it does not know is shown as the server sent it — never dropped
 * and never given a guessed name.
 */
const SOURCE_KIND_LABEL: Readonly<Record<string, string>> = {
  'official.invoice-installment.v1': 'Onaylı resmî fatura taksiti',
}

export const sourceKindLabel = (sourceKind: string) => SOURCE_KIND_LABEL[sourceKind] ?? sourceKind

/** The end of a record id, as the other lists show a record that has no document number. */
export const shortReference = (id: string) => `…${id.slice(-8)}`

export const OPEN_ITEM_TEXT = {
  heading: 'Açık kalemler',
  subject: 'açık kalemler',
  tableLabel: 'Şirketin açık kalemleri',
  nextPageSubject: 'sonraki açık kalem sayfası',
  order: 'En yeni kayıt en üsttedir.',
  amountsHidden: 'Tutar ve durum bu yetkiyle gösterilmiyor.',
  record: 'Kayıt',
  sourceKind: 'Kaynak türü',
  dueOn: 'Vade',
  noDueOn: 'Vade yok',
  status: 'Durum',
  statusOpen: 'Açık',
  statusClosed: 'Kapalı',
  initialAmount: 'İlk tutar',
  outstandingAmount: 'Kalan tutar',
  currency: 'Para birimi',
  currencyReference: (reference: string) => `ref. ${reference}`,
  createdAt: 'Oluşturuldu',
  redacted: 'Bu yetkiyle gösterilmiyor',
  unavailable: 'Şu anda alınamadı',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  openName: (reference: string, kind: string, dueOn: string) => `Açık kalem ${reference}: ${kind}, ${dueOn}`,
  dueOnName: (dueOn: string) => `vade ${dueOn}`,

  detailHeading: 'Açık kalem ayrıntısı',
  detailSubject: 'açık kalem',
  back: 'Açık kalemlere dön',
  party: 'Cari',
  openParty: 'Cari kartını aç',
  technical: 'Teknik bilgiler',
  technicalNote: 'Bu kayıt cari adı, belge numarası veya para birimi adı taşımaz; aşağıdakiler kaydın kimlikleridir.',
  itemId: 'Kayıt kimliği',
  itemVersion: 'Kayıt sürümü',
  sourceId: 'Kaynak kimliği',
  sourceLineId: 'Kaynak satır kimliği',
  sourceVersion: 'Kaynak sürümü',
  sourceKindValue: 'Kaynak türü kodu',
  partyId: 'Cari kimliği',
  companyAccountId: 'Cari hesap kimliği',
  currencyId: 'Para birimi kimliği',
} as const
