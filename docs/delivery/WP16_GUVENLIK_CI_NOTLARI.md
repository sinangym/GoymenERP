# WP16 — Deterministik güvenlik taraması ve AI testlerinin CI'a eklenmesi: notlar

Paket: WP16 (`WORK_AI_TAMAMLAMA_PLANI_v1.md`), DALGA_PLANI_v2 1.18, plan D1 ve D12. `.github/**` değişikliği K3'tür:
iki bağımsız exact-head review; yalnız GitHub-hosted runner; secret/environment/deploy/cache yüklemesi yok; artifact
≤ 7 gün (`SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md`). Bu paket yeni workflow, yeni job, artifact veya izin eklemez.

## Eklenenler

| Dosya | Ne yapar |
|---|---|
| `tools/security/credential_scan.py` | Git'te izlenen dosyalarda yüksek güvenli sır kalıpları (özel anahtar bloğu, AWS/GitHub/Slack/Google/Stripe/Anthropic/OpenAI/npm anahtarı, Azure depolama anahtarı) ve hiç commit edilmemesi gereken dosya adları (`.env`, `.pfx`/`.p12`, `id_rsa`…). Ağ, saat, ortam kullanmaz. Rapor değeri değil yalnız SHA-256 izini yazar. |
| `contracts/security/secret-scan-allowlist-v1.json` | İncelenmiş test değerleri için kural + yol + SHA-256 listesi (bugün boş). Kullanılmayan girdi taramayı kırar. |
| `tools/security/audit_action_pins.py` | Etkin workflow'lardaki her `uses:` referansını sınıflar; tam 40 karakterlik SHA dışındaki her referans yalnız tabanda varsa geçer. `docker://` ve dal adı yasak. |
| `contracts/security/action-pin-baseline-v1.json` | Yalnız küçülebilen taban: bugünkü 6 resmî `actions/*@v<n>` etiketi. Yeni action veya yeni etiket SHA ister; SHA'ya taşınan girdi silinmezse denetim kırılır. |
| `.github/dependabot.yml` | github-actions, nuget, npm ×2, docker ×2. **Etkin değil:** her ekosistemde `open-pull-requests-limit: 0`. |
| `tests/security_scan/` | Üç aracın davranış testleri ve depo üzerinde gerçek koşu. |
| `ci.yml` → `backend-build` | İki adım: "AI knowledge base integrity preflight" (`tests/ai_decision_index`, `tests/ai_eval_set`, `evaluate_ai_eval_set.py`) ve "Deterministic security scan". |

## Yol filtresi değişikliği (CIH-D5 eşliği)

`tools/generate_ai_decision_index.py` `docs/decisions/` altındaki bütün karar belgelerini okur ve
`tests/ai_decision_index` dizinin güncel olduğunu denetler. `docs/decisions/**/*.md` CI'ı başlatmayan düzyazı
listesinde kaldıkça yalnız karar belgesi değişen bir PR CI'ı başlatmaz ve dizin sessizce bayatlar (D1). Bu yüzden
`docs/decisions` atlanan düzyazı kökü olmaktan çıkarıldı (`ci.yml`, `cari-c02-link-acceptance.yml`,
`test_workflow_path_filters.py`). Diğer üç kök (`docs/delivery`, `docs/handoffs`, `docs/operations`) aynen kalır.

**AGENTS.md Faz 0 (c) bloğu bu pakette değiştirilmedi** (ortak kurallar: paket oturumu AGENTS.md'ye dokunmaz).
`test_workflow_path_filters.py`'deki iki Faz 0 testi blok güncellenene kadar kırmızıdır. Ana oturum/Sinan aşağıdaki
yamayı uygulayınca `tests/ci_safe_trx_summary` 103/103 geçer (yerelde doğrulandı):

```diff
-SKIPPED=(':(top,exclude,glob)docs/decisions/**/*.md' ':(top,exclude,glob)docs/delivery/**/*.md' \
-  ':(top,exclude,glob)docs/handoffs/**/*.md' ':(top,exclude,glob)docs/operations/**/*.md')
-READ_BY_CI=(':(top)docs/decisions/KARAR_SICILI.md' ':(top)docs/decisions/ACIK_KARARLAR.md' \
-  ':(top)docs/decisions/SINAN_KARARLARI_2026-08-31.md' ':(top)docs/delivery/DECISION_PACK.md')
+SKIPPED=(':(top,exclude,glob)docs/delivery/**/*.md' ':(top,exclude,glob)docs/handoffs/**/*.md' \
+  ':(top,exclude,glob)docs/operations/**/*.md')
+READ_BY_CI=(':(top)docs/delivery/DECISION_PACK.md')
```

Etkisi: `main` yalnız `docs/decisions` belgesinde ilerlediğinde Faz 0 (c) artık uygulanmaz; (a)/(b) kademesine geçilir.
Bu, karar değişikliğinin AI dizinini bayatlatabildiği gerçeğinin süreç karşılığıdır (GOV-D5 listeleri filtreyle aynı
kalır).

## Tren head'inde üretilecek pin'ler (bu paket üretmedi)

- `PlatformTenantQueryFilterBypassGuardTests.ReviewedContinuousIntegrationWorkflowFingerprint` (ci.yml değişti).
- Release manifest (`ci.yml` bir release girdisidir).
- `contracts/ai/decision-index-v1.json` (tren-2'de zaten bayat; `tests/ai_decision_index` bu yüzden şu an kırmızı).

Python tarafındaki iki ci.yml izi (`test_summarize_trx.py`, `test_validate_dotnet_bootstrap_integrity.py`
`backend-build`) ve `local-ci-bootstrap.yml.disabled` aynası bu pakette güncellendi.

## İnsan kapıları (Settings; ajan yapmaz)

1. Settings → Code security: Dependabot alerts ve security updates; secret scanning ve push protection.
2. Sürüm güncelleme PR'larını açmak için `dependabot.yml`'de ilgili ekosistemin `open-pull-requests-limit` değerini
   yükseltmek (iki incelemeli `.github` değişikliği).
3. Tabandaki 6 etiketin tam SHA'ya taşınması ayrı bir `.github` PR'ıdır; mevcut workflow testleri `@v7`/`@v8`
   metinlerini birebir sabitlediği için o testlerle birlikte yapılmalıdır.
