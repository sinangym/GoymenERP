import { describe, expect, it } from 'vitest';

import { OWNER_KIND } from './contract';

describe('document owner contract', () => {
  it('pins the canonical counterparty and inventory material-label identities', () => {
    expect(OWNER_KIND).toEqual({
      counterpartyParty: 'counterparty.party',
      inventoryMaterialLabel: 'inventory.material-label',
    });
  });
});
