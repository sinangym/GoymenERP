/**
 * EK-46 / BULGU-42 — the shared list and form standard every list and edit screen is accepted against (R03.2, R07.2).
 *
 * This is a reviewer's checklist, not runtime code: nothing in the app imports it. Each item names how it is proved:
 * the static audit (./listFormAudit.ts with its shrinking baseline), the token contrast test, a Vitest page test, the
 * browser cases (EK46-* in tests/e2e/organization-org03/specs/org03-work-centre-list.spec.ts) or a human review. The texts
 * are Turkish because reviewers read them; they deliberately avoid tag and API spellings so the audit never reads
 * this file as markup.
 */

import type { ListFormRule } from './listFormAudit'

export type ListFormEvidence = 'static-audit' | 'contrast-test' | 'vitest' | 'browser-pack' | 'review'

export interface ListFormStandardItem {
  readonly id: string
  readonly title: string
  readonly requirement: string
  readonly evidence: readonly ListFormEvidence[]
  /** The static rule that enforces the item, when one exists. */
  readonly rule: ListFormRule | null
}

export const LIST_FORM_STANDARD: readonly ListFormStandardItem[] = [
  {
    id: 'LF-01',
    title: 'Sunucu tarafı sıralama, filtre ve sayfalama',
    requirement:
      'Sıralama, filtre ve sayfa durumu sunucuya parametre olarak gider; istemci yalnız gelen sayfayı gösterir, kısmi ' +
      'bir sayfayı kendi başına sıralamaz, süzmez veya toplamaz. Toplam ve sayılar da yetkiye tabidir (B15).',
    evidence: ['vitest', 'review'],
    rule: null,
  },
  {
    id: 'LF-02',
    title: 'Kolon düzeni',
    requirement:
      'Liste ortak DataTable ile çizilir: her satırda kolon başına bir hücre, kimlik kolonu başta sabit, ana miktar ve ' +
      'eylemler sonda sabit, sayılar sağa hizalı ve tablo kendi kaydırma alanında.',
    evidence: ['review', 'browser-pack'],
    rule: null,
  },
  {
    id: 'LF-03',
    title: 'Tablonun erişilebilir adı',
    requirement: 'Her tablo bir başlık (caption), aria-label veya aria-labelledby ile adlandırılır.',
    evidence: ['static-audit'],
    rule: 'table-name',
  },
  {
    id: 'LF-04',
    title: 'Başlık hücresi kapsamı',
    requirement: 'Her başlık hücresi kolon veya satır kapsamını (scope) açıkça taşır.',
    evidence: ['static-audit'],
    rule: 'th-scope',
  },
  {
    id: 'LF-05',
    title: 'Tek ana içerik bölgesi',
    requirement:
      'Ana içerik bölgesi yalnız uygulama kabuğundadır (#main-content); sayfalar ikinci bir ana bölge açmaz, kendi ' +
      'bölümlerini section ve başlıkla kurar.',
    evidence: ['static-audit'],
    rule: 'nested-main',
  },
  {
    id: 'LF-06',
    title: 'Klavye ve odak',
    requirement:
      'Her eylem gerçek bir düğme veya bağlantıdır ve düğme türü açıkça yazılır; Tab sırası görsel sırayı izler, odak ' +
      'her zaman görünürdür ve hata bildirimi açıldığında odak bildirime taşınır.',
    evidence: ['static-audit', 'vitest', 'browser-pack'],
    rule: 'button-type',
  },
  {
    id: 'LF-07',
    title: 'Kontrast',
    requirement:
      'Metin ile zemini en az 4,5:1, odak göstergesi en az 3:1 kontrast taşır. 3:1 altındaki çizgi ve nokta tokenları ' +
      'yalnız süs veya metinle tanınan düğmenin kenarı olarak kullanılır; bir giriş alanının tek sınırı olamaz.',
    evidence: ['contrast-test', 'review'],
    rule: null,
  },
  {
    id: 'LF-08',
    title: '320 px genişlik',
    requirement:
      'Sayfa 320 px genişlikte yatay kaydırma üretmez; geniş tablo yalnız kendi kaydırma alanında kayar, form alanları ' +
      'alt alta dizilir.',
    evidence: ['browser-pack'],
    rule: null,
  },
  {
    id: 'LF-09',
    title: 'Ondalık giriş',
    requirement:
      'Miktar, para ve sürüm sayı türünde bir giriş alanıyla alınmaz; metin olarak alınır, ondalık klavye ipucu verilir ' +
      've değer sunucuya decimal metin olarak gider.',
    evidence: ['static-audit'],
    rule: 'number-input',
  },
  {
    id: 'LF-10',
    title: 'Tam durum kontrolü',
    requirement: 'Başarı sözleşmedeki tam durum koduyla anlaşılır; yanıtın genel başarı bayrağına bakılmaz (F04).',
    evidence: ['static-audit'],
    rule: 'response-ok',
  },
  {
    id: 'LF-11',
    title: 'Kalıcı tarayıcı deposu',
    requirement:
      'Kalıcı tarayıcı deposu (local storage) yalnız gözden geçirilmiş dosyalarda ve oturum kimliğine bağlı olarak ' +
      'kullanılır (F08); yeni kullanım baseline\'a eklenmeden kabul edilmez.',
    evidence: ['static-audit', 'review'],
    rule: 'local-storage',
  },
  {
    id: 'LF-12',
    title: 'Eşzamanlı değişiklik (AT-005)',
    requirement:
      'Sürüm çakışmasında ikinci kullanıcıya çakışma bildirilir; güncel kayıt yüklendiğinde girdiği değer ile güncel ' +
      'değer yan yana gösterilir, girdisi korunur ve yeniden kaydetme güncel sürümle yeni bir işlem anahtarı kullanır.',
    evidence: ['vitest', 'browser-pack'],
    rule: null,
  },
  {
    id: 'LF-13',
    title: 'Başarısız kaydetmede veri koruma (AT-006)',
    requirement:
      'Ağ, sunucu, doğrulama veya çakışma sonrasında form değerleri silinmez. Sonucu bilinmeyen işlemde alanlar ' +
      'kilitlenir ve yalnız aynı işlem anahtarıyla tekrar gönderilir (F09, B16).',
    evidence: ['vitest', 'browser-pack'],
    rule: null,
  },
  {
    id: 'LF-14',
    title: 'Şirket değişimi (AT-008)',
    requirement: 'Kirli bir form şirket değiştirilirken sessizce taşınmaz; kullanıcıya onay sorulur.',
    evidence: ['vitest'],
    rule: null,
  },
  {
    id: 'LF-15',
    title: 'Ekran durumları',
    requirement:
      'Boş, yükleniyor, hata, çatışma ve yetkisiz durumları ortak durum bloğuyla gösterilir; eski şirketin verisi yeni ' +
      'şirkette görünmez (F05).',
    evidence: ['vitest', 'review'],
    rule: null,
  },
]
