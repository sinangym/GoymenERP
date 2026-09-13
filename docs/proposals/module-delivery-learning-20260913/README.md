# Modül tesliminden kalıcı öğrenmeye

Status: PROPOSAL — insan incelemesine sunulan çalışma rehberi; yeni merge/CI politikası değildir.
Owner: mevcut danışman görevi; Business ve implementation sahipliği devralınmaz.
Tarih: 2026-09-13. İncelenen main: `60e584d3255aa034060538f4413dc4aafa051a7c`.

## İstek ve kapsam

Sinan'ın isteği: Cari, Ürün ve Organizasyon geliştirmelerindeki deneyimleri sonraki modüllerde tekrar kullanılabilir hale getirmek; kısa ön inceleme, erken entegrasyon ve uygun doğrulamayla daha hızlı, doğru ve eksiksiz teslim etmek; dört rolün başlangıç promptlarını hazırlamak.

Bu paket model eğitimi veya hatasızlık garantisi değildir. Öğrenme, yeni ajanın bulabildiği örnek kaynak + hata nedeni + onu yakalayan kontrol + doğru kullanım sınırıdır. Kodların tümünü kopyalamak yerine ortak bileşen/servis kullanılır; yalnız benzerlik nedeniyle yeni genel framework çıkarılmaz.

İnceleme kapsamı bu görevde erişilebilir konuşma metni ve aktarılan raporlar, aşağıdaki depo kaynakları ve seçilmiş gerçek kod/test örnekleridir. Eski her ekran görüntüsü, ek, tüm ajan oturumları ve tüm SQL/trace artefaktları yeniden denetlenmedi. Paket tam konuşma arşivi, bağımsız ürün güvenlik denetimi veya yeniden acceptance sonucu değildir. Uzun konuşma/compaction nedeniyle görünmeyen içerik için tamlık iddiası yoktur.

## Ne hazırlandı?

- [Dersler ve örnek kaynaklar](LESSONS.md): kalıcılaşmış testlerle açık/tarihsel dersleri ayırır.
- [Başlangıç ve teslim akışı](WORKFLOW.md): bir küçük kullanıcı akışı için uygulanır; yeni kapı sistemi kurmaz.
- [Dört rolün promptları](PROMPTS.md): aynı Orchestrator ve Business, ayrı yazarlık, mevcut proje.
- [Sipariş / Stok / Finans / Üretim başlangıç ekleri](MODULE_BRIEFS.md): iş spec'i veya uygulama yetkisi değil, sonraki yetkili işe hazırlık.

## Kaynak ve yetki

Son açık kullanıcı kararı > tarihli kullanıcı kararları > [karar sicili](../../decisions/KARAR_SICILI.md) > [Master v3](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md) > ilgili kabul edilmiş spec/ADR. [AGENTS](../../../AGENTS.md), [Hızlı Teslim v3](../../governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md) ve [v2 güvenlik sınırları](../../governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md) yürürlüktedir. Tarihsel belge içi status/sayılar canlı durumun yerine geçmez.

K1/K2 için mevcut tek bağımsız review; yalnız AGENTS'te tanımlı K3 için iki review uygulanır. Zorunlu CI, migration/para/yetki/contract ve ilgili gerçek SQL/browser kapıları azaltılmaz. Governance, CI, karar sicili, backlog ve mevcut acceptance durumları bu pakette değiştirilmez. Bu öneri otomatik merge edilmez.

Aktif hedef Ürün U01/U02 teslimi ardından Cari L04 kapanışıdır. Yeni modüllerin promptlarının hazırlanması onları başlatmaz. L04'ün incelenen son kaydında gerçek koşum başarısız, düzeltme çalışması aktiftir; paket onu tamamlandı ilan etmez. B otomatik-account önerisi, #229 ve production insan sınırları korunur. Mobilin toplu değerlendirmesini sona alma kullanıcı tercihi, mevcut masaüstü/klavye ve sözleşmede gerekli erişilebilirlik kontrollerini kaldırmaz; mobil tamamlandı denmez.

## Kullanıma alma

1. Öneri paketi insan tarafından incelenir; normatif dosyalara sessizce taşınmaz.
2. Koordinatör mevcut L04 işini kesmez. Sonraki yetkili dilimde PROMPTS ve ilgili MODULE_BRIEFS bölümünü kullanır; görev adı, kaynaklar ve yazılabilir yolları kendisi güncel kayıttan doldurur.
3. Ajanlar başlangıç notunda kullanılacak örnekleri ve modüle özgü farkı belirtir. Sadece "okudum" makbuzu üretmez.
4. Kapanışta gerçekten yeni ders varsa LESSONS'a aynı sahip/normal review yoluyla kısa ek önerilir. Her başarılı turda yeni belge, commit veya yeni hatırlama sistemi üretilmez.

Bu paket mevcut [bilgi aktarım alanının](../../knowledge/README.md) yerine geçen bir RAG/registry değildir. Kalıcı ve ajanlar arası erişilebilir kaynak Git'teki bu öneridir; özel sohbet hafızası veya belirli modelin belleği değildir.

## Paket doğrulaması

Yalnız beş yeni öneri dosyası. 54 yerel Markdown bağlantısı gerçek dosyalara çözümlendi; kırık bağlantı yok. Örnek semboller ve kritik davranışlar kaynakta incelendi; bu işlem tam kod incelemesi değildir. Markdown/diff ve yazılabilir yüzey kontrolü yapıldı. Ürün kodu değişmediği için ürün testleri, SQL, browser, ortam ve geçmiş acceptance tekrar çalıştırılmadı. Uzak CI ve insan review sonucu bu yerel kontrollerden türetilmez.
