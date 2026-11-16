# Identity migration notes


### KIM-S5 company setup (KIM-D18/D20/D21; source PARTIAL)

- Generated migration `20261116185533_AddCompanySetupInboxAndOutboxPosition`: owner inbox/outbox and lifecycle source; Up DDL only. Existing companies remain Active.
- Down first refuses with **51944** while company setup lifecycle/history would lose meaning (forward fix). No database application was performed.
- Only the two exact setup events pass the pending-company dispatcher gate; business events stay held. Dispatcher activation remains human-owned.
- Company opening authority scope is still a decision gate. No opening endpoint, production grant or seed was added; source is not a usable opening workflow.
- Unit/contract and real SQL facts are written; runtime acceptance is NOT_RUN.
