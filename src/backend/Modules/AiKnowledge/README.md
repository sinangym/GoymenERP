# AiKnowledge modülü (WP-A4)

Yapay zekâ bilgi paketlerinin (karar dizini, yayındaki sürümün davranış bilgisi) **grup genelinde tek insan onayı**
(AI-D32 "onaylı karar dizini", AI-D46, DALGA_PLANI_v2 A.4). Paketlerin kendisi API içeriğidir (`contracts/ai/*.json`); bu modül
yalnız "bu paket sürümü bu şirkette onaylı mı" kaydını tutar.

| Konu | Değer |
|---|---|
| Şema | `aiknowledge`: `PackageApprovals` grup genelinde (şirket filtresi yok; `OriginCompanyId`/`WithdrawnOriginCompanyId` doğrulanmış istek şirketi, yazma yalnız o şirket bağlamından; Product grup ortak ana veri deseni), `CommandAttempts` istek şirketinde (`CompanyId` + filtre) |
| İzin | `ai.knowledge.approve` — herhangi bir şirkette şirket çapı tutulur, Critical değil (AI-D46), **hiçbir role verilmez** |
| Komutlar | `ai-knowledge.package.approve.v1` (201), `ai-knowledge.approval.withdraw.v1` (200, expected version) |
| Uç noktalar | `GET /api/v1/ai/knowledge/packages`, `POST /api/v1/ai/knowledge/approvals`, `POST /api/v1/ai/knowledge/approvals/{id}/withdrawal` |
| Audit | `ai-knowledge.package.approved.v1`, `ai-knowledge.approval.withdrawn.v1`; varlık `AiKnowledge.GroupPackageApproval`, yükte `scope: group`, olay işlemi yapanın şirketinde |
| Migration | `20261122202208_InitialAiKnowledgeApprovals`; Down onay ya da deneme varken `THROW 52290` (forward fix) |

Kurallar: onay, onaylayanın gördüğü paket baytlarının SHA-256'sına bağlıdır; paketlenen sürüm farklıysa 422
`ai_knowledge_package_changed`. Aynı sürümün ikinci etkin onayı kayıtlı 409 `ai_knowledge_already_approved`. Onay
düzenlenmez/silinmez; geri alma bir sürüm adımıdır (gerekçe zorunlu) ve aynı baytların yeni onayı yeni kayıttır. Araçlar
`IAiKnowledgeApprovalReaderV1` ile okur; okunamazsa `Unavailable` (fail-closed). Modül kurulmamış bir host'ta varsayılan okuyucu
da `Unavailable` döner.
