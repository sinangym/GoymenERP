/**
 * MFG-PLAN-EDIT screen text: the Draft edit form, its refusals and the plan history.
 *
 * Every refusal is explained in the user's terms and names what to do next. The rules themselves are the server's
 * (`ProductionPlan.Revise`): only a Draft that opened no order is edited; the process never changes; a plan with a
 * slitting spec waits for the AT-044 business decision; a change of nothing is not recorded.
 */

import type { PlanRevision } from './planRevisionContract'
import { TARGET_BASIS_LABEL } from './manufacturingText'

export const PLAN_EDIT_TEXT = {
  editButton: 'Planı düzenle',
  formHeading: 'Taslak planı düzenle',
  save: 'Değişikliği kaydet',
  saving: 'Kaydediliyor…',
  cancel: 'Vazgeç',
  reasonLabel: 'Düzenleme gerekçesi',
  reasonHelp: 'Gerekçe plan geçmişinde değişiklikle birlikte saklanır (en fazla 256 karakter).',
  processNote: (process: string) =>
    `Planın süreci (${process}) değişmez: yalnız aynı süreci yürüten bir iş merkezi seçilebilir; farklı süreç sunucuda reddedilir.`,
  inputsNote:
    'Ana rulo listesinin tamamı yeni içeriktir. Tutulan her rulonun etiket sürümü ve kg değeri kayıtta Inventory ile yeniden doğrulanır.',
  legacyLabel: 'Etiket sürümü yok — ruloyu kaldırıp adaylardan yeniden ekleyin.',
  approvedNote:
    'Onaylı plan düzenlenmez: onay bir karar kaydetti ve plandan üretim emri açılmış olabilir. Onay sonrası revizyon ayrı bir karardır.',
  specNote: 'Dilme ayrıntısı taşıyan plan düzenlenmez; ana ruloyla dengesi iş kararı bekliyor (AT-044).',
  historyNotReady: 'Plan geçmişi okunamadığı için düzenleme şu an açılamıyor.',
  historyStale: 'Plan geçmişi planın güncel sürümünü göstermiyor; güncel planı yeniden okuyun.',
  formRequired: 'İş merkezi, en az bir ana rulo ve her rulo için sıfırdan büyük planlanan kg zorunludur.',
  targetPair: 'Hedef kullanılacaksa türü ve sıfırdan büyük kg değeri birlikte girilmelidir.',
  reasonRequired: 'Düzenleme gerekçesi zorunludur (en fazla 256 karakter).',
  legacyRequired: 'Etiket sürümü olmayan ana rulo yeniden eklenmeden kaydedilemez.',
  noChange: 'Düzenleme planda hiçbir şeyi değiştirmiyor.',
  applied: (version: number) => `Plan güncellendi (sürüm ${version}); güncel kayıt ve geçmiş yeniden okundu.`,
  appliedRefreshPending: 'Plan güncellendi; güncel kayıt henüz yeniden okunamadı.',
  unknown: 'Düzenlemenin sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.',
  historyHeading: 'Plan geçmişi',
  historyEmpty: 'Plan oluşturulduğundan beri düzenlenmedi.',
  created: 'Oluşturma',
} as const

/** M01.3: the shared cut-line editor's labels (create and edit). The user's words: Sıra no, Uzunluk (mm), Adet. */
export const CUT_LINE_TEXT = {
  legend: 'Boy kesme satırları',
  note:
    'Satırlar listedeki sırayla kaydedilir. Sıra no her satır için açıkça girilir (işaretli tam sayı); satırı taşımak yalnız listedeki yerini değiştirir.',
  empty: 'Boy kesme satırı yok.',
  position: 'Satır',
  sequence: 'Sıra no',
  length: 'Uzunluk (mm)',
  count: 'Adet',
  actions: 'İşlem',
  row: (position: number) => `Kesim satırı ${position}`,
  moveUp: 'yukarı taşı',
  moveDown: 'aşağı taşı',
  remove: 'kaldır',
  add: 'Kesim satırı ekle',
  keepOne: 'Kayıtlı boy kesme tanımı en az bir satır korur; tanımı temizleme bu sürümde yok.',
} as const

/** The server's refusal, in the user's terms. */
export function revisionFailureText(status: number, code: string | null, field: string | null, process: string): string {
  if (status === 401 || status === 403) return 'Plan düzenleme yetkiniz artık geçerli değil.'
  if (status === 404) return 'Plan bu şirket bağlamında bulunamadı.'
  switch (code) {
    case 'manufacturing_version_conflict':
      return 'Plan siz düzenlerken değişti. Güncel plan yeniden okundu; değişikliğinizi güncel sürüm üzerinden yeniden yapın.'
    case 'inventory_label_version_conflict':
      return 'Bir ana rulonun etiketi bu arada değişti. Ruloyu kaldırıp adaylardan yeniden ekleyin.'
    case 'manufacturing_plan_not_draft':
      return 'Plan artık taslak değil (onaylandı ya da emir açtı); onaylı plan düzenlenmez.'
    case 'manufacturing_plan_process_fixed':
      return `Seçilen iş merkezi planın sürecini (${process}) yürütmüyor; planın süreci değişmez.`
    case 'manufacturing_plan_slitting_spec_fixed':
      return PLAN_EDIT_TEXT.specNote
    case 'manufacturing_plan_revision_no_change':
      return PLAN_EDIT_TEXT.noChange
    case 'manufacturing_work_center_not_found':
      return 'Seçilen iş merkezi bu şirkette bulunamadı.'
    case 'manufacturing_mass_scale_exceeded':
      return 'Kg değeri şirketin kg profilinin izin verdiğinden fazla ondalık taşıyor; yuvarlanmaz, düzeltin.'
    case 'manufacturing_duplicate_roll_in_plan':
      return 'Aynı ana rulo planda iki kez yer alamaz.'
    default:
      break
  }
  if (status === 400) {
    return field === 'reason'
      ? PLAN_EDIT_TEXT.reasonRequired
      : `Düzenleme isteği geçersiz (${field ?? code ?? 'request'}).`
  }
  return `Plan düzenlemesi uygulanmadı (${code ?? status}).`
}

function cutLinesText(spec: PlanRevision['cutToLengthSpec']): string {
  return spec === null
    ? 'yok'
    : spec.lines.map((line) => `Sıra no ${line.sequence}: ${line.lengthMm} mm × ${line.count}`).join('; ')
}

function targetText(target: PlanRevision['target']): string {
  return target === null ? 'hedef yok' : `${TARGET_BASIS_LABEL[target.basis] ?? target.basis} ${target.kg} kg`
}

/**
 * What one history entry changed against the previous one, in the user's terms. The history holds complete contents,
 * so the difference is computed here for reading only; nothing is sent back.
 */
export function describeRevisionChanges(previous: PlanRevision, next: PlanRevision): string[] {
  const changes: string[] = []
  if (previous.workCenterId !== next.workCenterId) {
    changes.push(`İş merkezi: ${previous.workCenterId} → ${next.workCenterId}`)
  }
  if (targetText(previous.target) !== targetText(next.target)) {
    changes.push(`Hedef: ${targetText(previous.target)} → ${targetText(next.target)}`)
  }
  const before = new Map(previous.inputs.map((input) => [input.parentRollId, input]))
  const after = new Map(next.inputs.map((input) => [input.parentRollId, input]))
  for (const input of next.inputs) {
    const old = before.get(input.parentRollId)
    if (old === undefined) {
      changes.push(`Eklenen ana rulo ${input.parentRollId}: ${input.plannedKg} kg`)
    } else if (old.plannedKg !== input.plannedKg) {
      changes.push(`Ana rulo ${input.parentRollId}: ${old.plannedKg} kg → ${input.plannedKg} kg`)
    } else if (old.expectedLabelVersion !== input.expectedLabelVersion) {
      changes.push(`Ana rulo ${input.parentRollId}: etiket sürümü ${old.expectedLabelVersion ?? '—'} → ${input.expectedLabelVersion ?? '—'}`)
    }
  }
  for (const input of previous.inputs) {
    if (!after.has(input.parentRollId)) changes.push(`Çıkarılan ana rulo ${input.parentRollId}: ${input.plannedKg} kg`)
  }
  // M01.3: the ordered cut lines are content — list order, Sıra no, length and count are described as stored.
  const cutBefore = cutLinesText(previous.cutToLengthSpec)
  const cutAfter = cutLinesText(next.cutToLengthSpec)
  if (cutBefore !== cutAfter) {
    changes.push(`Boy kesme ayrıntısı: ${cutBefore} → ${cutAfter}`)
  } else if (
    previous.cutToLengthSpec !== null && next.cutToLengthSpec !== null &&
    previous.cutToLengthSpec.lines.some((line, index) =>
      line.lineId.toLowerCase() !== next.cutToLengthSpec?.lines[index].lineId.toLowerCase())
  ) {
    changes.push('Boy kesme ayrıntısı: aynı değerli satırların kimliği değişti.')
  }
  if (changes.length === 0) changes.push('Ana rulo sırası değişti.')
  return changes
}
