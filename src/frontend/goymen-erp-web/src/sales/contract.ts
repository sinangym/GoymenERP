/**
 * The Sales order draft contract, as far as it is known — and no further.
 *
 * STATUS: `LOGICAL_SPEC`. Every wire name below comes from the logical example in
 * `docs/features/SALES_ORDER_DRAFT_JIT_SPEC_v1.md` §3 (#254 @ 290624d), which says
 * in so many words that it is "mantıksal contract girdisi; mevcut endpoint/DTO
 * iddiası değildir". The Sales Backend pins route, DTO, status/error map, version
 * and permission names; when its examples land, THIS is the one file whose names
 * change. Screens and the form never spell a wire name themselves.
 *
 * What is intentionally absent until the Backend pins it:
 *  - response DTOs (list, detail, create result) and their decoders;
 *  - the replay indicator, the problem codes and the success status;
 *  - line identity: §2 leaves "client/server tek sahibi" to the Backend, and the
 *    Cari child-UUID rule is explicitly not carried over;
 *  - optional create fields §2 names but §3's example does not carry (internal note,
 *    header due date, delivery address, contact, sales owner, payment term, price).
 */

export const SALES_CONTRACT_STATUS = 'LOGICAL_SPEC' as const

/** §2: the first command carries an explicit Order intent; the server makes it a Draft. */
export const ORDER_KIND = 'Order' as const

export interface OrderDraftLineRequest {
  readonly productId: string
  /** Canonical positive decimal string (see ./wirePrimitives.ts). */
  readonly quantity: string
  readonly orderUnitId: string
  /**
   * `YYYY-MM-DD`, or null when unknown. §2: "bilinmeyen termin null, bugüne/vadeye
   * dönüştürülmez". Whether the DTO wants null or omission is a Backend pin.
   */
  readonly requestedDeliveryDate: string | null
}

/**
 * The create body. Carries NO company, actor, version, status, number or revision:
 * §2 puts the company in the verified request context and every one of the others
 * on the server.
 */
export interface OrderDraftCreateRequest {
  readonly kind: typeof ORDER_KIND
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly currencyId: string
  readonly lines: readonly OrderDraftLineRequest[]
}
