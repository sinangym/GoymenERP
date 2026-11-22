/**
 * KLN-D119 / SAT-D434 (AK-4b): Turkish texts of the piece side of a physical sales allocation on the ST-02 label detail
 * — the reserved/remaining pieces beside the kilograms, the "pieces held afterwards" field of the change form and the
 * label's piece totals. Every count is the server's; the screen adds, converts or compares nothing but the typed
 * field's own format.
 */
export const ALLOCATION_PIECE_TEXT = {
  pieces: (count: number) => `${count} adet`,
  allocatedRemaining: (allocated: number, remaining: number) => `${allocated} adet / ${remaining} adet`,
  changePieceCount: 'Yeni tahsis adedi (işlemden sonra tutulan)',
  changePieceCountRequiredHint: (held: number) =>
    `Bu tahsis adet tutuyor (şu an ${held} adet). Değişiklikten sonra tahsisin tutacağı adedi girin: tam sayı, en az 1.`,
  changePieceCountMoveHint:
    'Hedef etiket adetle izleniyorsa tahsisin değişiklikten sonra tutacağı adedi girin (tam sayı, en az 1); adetle izlenmiyorsa boş bırakın. Stok kayıt anında doğrular.',
  changePieceCountRequired: 'Bu tahsis adet tutuyor; değişiklikten sonra tutulacak adet zorunludur.',
  changePieceCountFormat: 'Yeni tahsis adedi sıfırdan büyük bir tam sayı olmalıdır.',
  unchangedWithPieces: 'Değişiklik yok: miktar ve adet şu anki tahsisle aynı.',
  releasePieces: (remaining: number) => `Kalan ${remaining} adet de serbest bırakılır.`,
  completionPieces: (remaining: number) => ` (${remaining} adet)`,
  labelPhysicalPieces: 'Fiziksel adet',
  labelFreePieces: 'Serbest adet',
  labelBlockedPieces: 'Bloklu adet',
  positionPieces: 'Adet',
  positionNoPieces: '—',
} as const

/** The four KLN-D119 refusals of the allocation create/change commands, told by their stable code. */
export const ALLOCATION_PIECE_REFUSAL_TEXT: Readonly<Record<string, string>> = {
  inventory_allocation_piece_count_required:
    'Hedef etiketin serbest stoku adetle izleniyor; tahsisin tutacağı adet girilmeden değişiklik kaydedilmez. Adedi girip tekrar deneyin.',
  inventory_allocation_piece_count_not_tracked:
    'Hedef etiketin serbest stoku adetle izlenmiyor; adet gönderilemez. Adet alanını boş bırakıp tekrar deneyin.',
  inventory_allocation_piece_count_invalid:
    'Tahsis adedi geçersiz: sıfırdan büyük bir tam sayı olmalı ve tahsis miktarıyla tutarlı olmalıdır.',
  inventory_allocation_insufficient_free_pieces:
    'Hedef etiketin serbest (tahsis edilmemiş) adedi bu kadar tahsise yetmiyor.',
}
