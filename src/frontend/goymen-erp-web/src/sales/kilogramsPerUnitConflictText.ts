/**
 * SAT-D474 / SAT-D477: Turkish copy for the kg/m table conflict warning shown on an approved piece line whose kg/unit was taken
 * from the card's ENTERED direction (the table holds the profile measures in both directions with different values).
 */
export const KILOGRAMS_PER_UNIT_CONFLICT_TEXT = {
  heading: 'Ağırlık tablosu çelişkisi',
  explanation: (selected: string, other: string, difference: string) =>
    `Belgeye kartta girilen yönün değeri yazıldı: ${selected} kg/adet. Ters yönün değeri ${other} kg/adet (fark ${difference} kg/adet).`,
  kgPerMetre: (selected: string, other: string) => `Tablo kg/m: girilen yön ${selected}, ters yön ${other}.`,
  correction: 'Ağırlık tablosunun düzeltilmesi gerekir. Onaylanmış belge tablo düzeltilse de değişmez.',
} as const
