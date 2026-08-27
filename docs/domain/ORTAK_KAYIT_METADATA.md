# Ortak Kayıt Metadata Sözleşmesi

Her kritik kayıt en az şu alanları taşır:

- `Id` — UUID/ULID benzeri değişmez teknik kimlik
- `CompanyId` — zorunlu hukuki şirket kapsamı
- `CreatedAt`, `CreatedBy`
- `UpdatedAt`, `UpdatedBy`
- `Version` / rowversion — optimistic concurrency
- `Status`
- `ApprovalStatus`
- `CorrelationId`, `CausationId`
- `SourceChannel` — web, mobile, import, integration, AI-assisted
- `ReversalOfId` / `SupersedesId`
- `AuditReferenceId`

Miktar/tutar kayıtlarında ayrıca:

- Girilen değer ve birim/para
- Ana birime/yerel paraya çevrilen değer
- Dönüşüm/formül/kur kaynağı ve sürümü
- Yuvarlama politikası
- Teorik/fiilî niteliği

## Şirket kapsamı

`CompanyId` yalnız UI filtresi değildir. Backend authorization, unique index, sorgu kapsamı, event, audit ve export manifestinde yer alır.
