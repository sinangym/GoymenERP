// B-05 / M-03 (Muhasebe > Cari Hesaplar): the open-account list and one account's open items. Every user-facing text
// of these panels lives here.

export const ACCOUNTS_TITLE = 'Açık kalemi olan cari hesaplar'
export const ACCOUNTS_NOTICE =
  'Her cari hesabın kapanmamış alacak ve borç kalemlerinin kalanı, para birimi ayrı. Bu bakiye değildir: bakiye için '
  + 'cari hesabı açıp bakiye özetine bakın.'
export const ACCOUNTS_LOADING = 'Açık cari hesaplar yükleniyor...'
export const ACCOUNTS_SUBJECT = 'açık cari hesap listesi'
export const ACCOUNTS_EMPTY = 'Açık alacak veya borç kalemi olan cari hesap yok.'
export const ACCOUNTS_FORBIDDEN = 'Açık cari tutarlarını görme yetkiniz yok.'
export const ACCOUNTS_TOO_MANY =
  'Şirketin açık kalem sayısı bir listenin okuyabileceğinden fazla; toplamlar eksik kalemle hesaplanmaz. Cari hesabı '
  + 'seçerek açın.'
export const ACCOUNTS_COLUMN_ACCOUNT = 'Cari hesap'
export const ACCOUNTS_COLUMN_CURRENCY = 'Para birimi'
export const ACCOUNTS_COLUMN_RECEIVABLE_COUNT = 'Açık alacak kalemi'
export const ACCOUNTS_COLUMN_RECEIVABLE = 'Açık alacak'
export const ACCOUNTS_COLUMN_PAYABLE_COUNT = 'Açık borç kalemi'
export const ACCOUNTS_COLUMN_PAYABLE = 'Açık borç'
export const ACCOUNTS_OPEN = 'Ekstreyi aç'

export const ITEMS_TITLE = 'Açık kalemler'
export const ITEMS_NOTICE = 'Bu cari hesabın kapanmamış kalemleri, vadeye göre; vadesiz kalemler sonda.'
export const ITEMS_LOADING = 'Açık kalemler yükleniyor...'
export const ITEMS_SUBJECT = 'açık kalem listesi'
export const ITEMS_EMPTY = 'Bu cari hesabın açık kalemi yok.'
export const ITEMS_FORBIDDEN = 'Bu cari hesabı görme yetkiniz yok.'
export const ITEMS_REDACTED = 'Açık kalemler bu yetkiyle gösterilmiyor.'
export const ITEMS_WITHHELD = 'Açık kalemler şu anda alınamadı.'
export const ITEMS_TOO_MANY_OPEN =
  'Bu cari hesapta bir listenin gösterebileceğinden fazla açık kalem var; liste eksik gösterilmez.'
export const ITEMS_TOO_MANY_FACTS =
  'Bu cari hesapta tek bir özetin okuyabileceğinden fazla kayıt var; açık kalemler eksik özetle gösterilmez.'
export const ITEMS_COLUMN_DIRECTION = 'Tür'
export const ITEMS_COLUMN_DUE = 'Vade'
export const ITEMS_COLUMN_CURRENCY = 'Para birimi'
export const ITEMS_COLUMN_ORIGINAL = 'İlk tutar'
export const ITEMS_COLUMN_OUTSTANDING = 'Kalan'
export const ITEMS_COLUMN_SOURCE = 'Kaynak'
export const ITEMS_NO_DUE = 'Vadesiz'
export const DIRECTION_LABEL: Readonly<Record<'Receivable' | 'Payable', string>> = {
  Receivable: 'Alacak (satış)',
  Payable: 'Borç (alış)',
}
