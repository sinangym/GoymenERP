# AiKnowledge modülü (WP-A4)

Yapay zekâ bilgi paketlerinin (karar dizini, yayındaki sürümün davranış bilgisi) **şirket bazında insan onayı**
(AI-D32 "onaylı karar dizini", DALGA_PLANI_v2 A.4). Paketlerin kendisi API içeriğidir (`contracts/ai/*.json`); bu modül
yalnız "bu paket sürümü bu şirkette onaylı mı" kaydını tutar.

| Konu | Değer |
|---|---|
| Şema | `aiknowledge` (`PackageApprovals`, `CommandAttempts`); her satırda `CompanyId` + şirket sorgu filtresi |
| İzin | `ai.knowledge.approve` — şirket çapı, Critical değil, **hiçbir role verilmez** (grant insan kapısı) |
| Komutlar | `ai-knowledge.package.approve.v1` (201), `ai-knowledge.approval.withdraw.v1` (200, expected version) |
| Uç noktalar | `GET /api/v1/ai/knowledge/packages`, `POST /api/v1/ai/knowledge/approvals`, `POST /api/v1/ai/knowledge/approvals/{id}/withdrawal` |
| Audit | `ai-knowledge.package.approved.v1`, `ai-knowledge.approval.withdrawn.v1` |
| Migration | `20261122202208_InitialAiKnowledgeApprovals`; Down onay ya da deneme varken `THROW 52290` (forward fix) |

Kurallar: onay, onaylayanın gördüğü paket baytlarının SHA-256'sına bağlıdır; paketlenen sürüm farklıysa 422
`ai_knowledge_package_changed`. Aynı sürümün ikinci etkin onayı kayıtlı 409 `ai_knowledge_already_approved`. Onay
düzenlenmez/silinmez; geri alma bir sürüm adımıdır (gerekçe zorunlu) ve aynı baytların yeni onayı yeni kayıttır. Araçlar
`IAiKnowledgeApprovalReaderV1` ile okur; okunamazsa `Unavailable` (fail-closed). Modül kurulmamış bir host'ta varsayılan okuyucu
da `Unavailable` döner.
