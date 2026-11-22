# Work + AI paket oturumları için ortak kurallar

Bu dosya `WORK_AI_TAMAMLAMA_PLANI_v1.md` paketlerini uygulayan oturumlar içindir. Work şeması ve `ErpPermissions` birden fazla pakette değişebilir; ana oturum birleşimde snapshot'ı modelden yeniden üretir ve kayıt çakışmalarını çözer.


- Taban dal: claude/tren-2 (tren-1 + 22 Kasım kararları + W1-A/B/C/D + Y-A1 birleşik). Kendi dalını bundan aç, yalnız kendi dalına push et. PR açma, merge etme, ready yapma.
- Önce oku: AGENTS.md, docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md (senin paketin + §2 düzeltmeler + §4 çakışma kuralları + THROW aralığın), docs/delivery/DALGA_PLANI_v2.md, docs/decisions/SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md, docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md, docs/decisions/SINAN_KARARLARI_2026-11-22_ROL_YETKI.md, docs/delivery/TEKNIK_TUZAKLAR.md. Skill'ler: .claude/skills altındaki k3-command-skeleton, ef-migration-forward-fix, test-naming-and-groups, permission-capability-registration, frontend-screen-states, architecture-guard-repin.
- Kritik komut iskeleti: yetki, expected version, idempotency, correlation/causation, audit, outbox. Her sorguda CompanyId. Modül başka modülün tablosuna yazmaz/okumaz; yalnız sahip modülün portu. Hesap yalnız backend'de.
- Yeni izin: ErpPermissions + modül manifesti + shell capability; HİÇBİR role verilmez. Yalnız kendi modül bloğuna ekle.
- Yapay zekâ: Ai:Enabled=false ve özellik bayrağı kapalı kalır; gerçek sağlayıcı çağrısı yok, testler Fake sağlayıcıyla. Sağlayıcı/anahtar/secret/canlıda açma/KVKK/bütçe insan kapısı.
- Migration: dotnet-ef ile; Down veri kaybında THROW (yalnız sana ayrılan aralıktan); persistence model testi; guard'a ReviewedMigrationMutationSources girdisi.
- PIN'Lİ DOSYALARI ÜRETME: contracts/openapi/goymen-erp-v1.json, OpenApiContractGenerationTests beklentileri, PlatformTenantQueryFilterBypassGuardTests fingerprint'leri, tools/relational_inventory/*.txt fingerprint, recovery envanteri, release manifest, contracts/ai/decision-index-v1.json. Bunlar tren-2 head'inde tek seferde üretilir. (Yeni SQL test adını relational_inventory dosyasına ordinal sıralı ekleyebilirsin; fingerprint'e dokunma.)
- Test adları yalnız ASCII. Gerçek SQL testi yaz, yerelde koşma (SQL Server yok). Test atlama/silme/iddia gevşetme yasak.
- .NET 10: export PATH=/root/.dotnet-sdk:$PATH DOTNET_ROOT=/root/.dotnet-sdk (yoksa kullanıcı dizinine kur). Python yalnız -I. Çözüm GoymenERP.slnx.
- Frontend: katı decoder, RemoteViewState durumları, Türkçe metinler ayrı dosyada; vitest + npx tsc -b + npm run lint.
- Para/stok/formül/yetki/saklama politikası tahminle seçilmez: fail-closed bırak, raporda NEEDS-SINAN yaz, durma.
- Yasak: karar/AGENTS/workflow/.claude/settings.json değişikliği (WP16 hariç .github), OPEN kapatma, rol/grant atama, production, secret. Yerel SQL/container kurma. Hassas kimlik/yerel yol yazma.
- Sık commit + push. Bitince kısa Türkçe rapor (≤30 satır): dal, head SHA, migration+THROW, yeni izin/uç noktalar, koşan/koşmayan testler, kapalı yollar, NEEDS-SINAN. Raporu ana oturuma send_message ile gönder (session_id "@parent").
