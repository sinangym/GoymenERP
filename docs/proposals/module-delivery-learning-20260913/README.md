# Modül tesliminden kalıcı öğrenmeye

Status: PROPOSAL — insan incelemesine sunulan çalışma rehberi; yeni merge/CI politikası değildir.
Owner: mevcut danışman görevi; Business ve implementation sahipliği devralınmaz.
Tarih: 2026-09-13. İlk kod örneği taraması main `60e584d3255aa034060538f4413dc4aafa051a7c`; teslim durumu güncellemesi main `f6fa07d026403649d6c815b84fd850e7ceeaf092`.

## İstek ve kapsam

Sinan'ın isteği: Cari, Ürün ve Organizasyon geliştirmelerindeki deneyimleri sonraki modüllerde tekrar kullanılabilir hale getirmek; kısa ön inceleme, erken entegrasyon ve uygun doğrulamayla daha hızlı, doğru ve eksiksiz teslim etmek; dört rolün başlangıç promptlarını hazırlamak.

Bu paket model eğitimi veya hatasızlık garantisi değildir. Öğrenme, yeni ajanın bulabildiği örnek kaynak + hata nedeni + onu yakalayan kontrol + doğru kullanım sınırıdır. Kodların tümünü kopyalamak yerine ortak bileşen/servis kullanılır; yalnız benzerlik nedeniyle yeni genel framework çıkarılmaz.

İnceleme kapsamı bu görevde erişilebilir konuşma metni ve aktarılan raporlar, aşağıdaki depo kaynakları ve seçilmiş gerçek kod/test örnekleridir. Eski her ekran görüntüsü, ek, tüm ajan oturumları ve tüm SQL/trace artefaktları yeniden denetlenmedi. Paket tam konuşma arşivi, bağımsız ürün güvenlik denetimi veya yeniden acceptance sonucu değildir. Uzun konuşma/compaction nedeniyle görünmeyen içerik için tamlık iddiası yoktur.

## Ne hazırlandı?

- [Dersler ve örnek kaynaklar](LESSONS.md): kalıcılaşmış testlerle açık/tarihsel dersleri ayırır.
- [Başlangıç ve teslim akışı](WORKFLOW.md): bir küçük kullanıcı akışı için uygulanır; yeni kapı sistemi kurmaz.
- [Dört rolün promptları](PROMPTS.md): aynı Orchestrator ve Business, ayrı yazarlık, mevcut proje.
- [Sipariş / Stok / Finans / Üretim başlangıç ekleri](MODULE_BRIEFS.md): iş spec'i veya uygulama yetkisi değil, sonraki yetkili işe hazırlık.
- [Teslim durumu ve düşük tüketimli yol haritası](ROADMAP.md): üç çiftin dar teslimi, bir aktif çift önerisi ve bağımlılık kontrolü.
- [Oturum/model ve paralel teslim planı](SESSION_MODEL_PLAN.md): mevcut/yeni konuşma seçimi, Spark/Astra iş dağılımı ve erken Backend katılımı. Son kullanıcı tercihi: mevcut Business/koordinatör Astra, yeni Backend'ler Spark.
- [Sipariş ve Finans gerçek başlangıcı](START_ORDERS_FINANCE.md): açık kullanıcı talimatıyla yeni iki çift, ayrı worktree'ler, altı görev promptu ve mevcut sahiplerdeki somut öncül ihtiyaçları. Ortam hazırlığı runtime veya ürün kabulü değildir.

## Kaynak ve yetki

Son açık kullanıcı kararı > tarihli kullanıcı kararları > [karar sicili](../../decisions/KARAR_SICILI.md) > [Master v3](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md) > ilgili kabul edilmiş spec/ADR. [AGENTS](../../../AGENTS.md), [Hızlı Teslim v3](../../governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md) ve [v2 güvenlik sınırları](../../governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md) yürürlüktedir. Tarihsel belge içi status/sayılar canlı durumun yerine geçmez.

K1/K2 için mevcut tek bağımsız review; yalnız AGENTS'te tanımlı K3 için iki review uygulanır. Zorunlu CI, migration/para/yetki/contract ve ilgili gerçek SQL/browser kapıları azaltılmaz. Governance, CI, karar sicili, backlog ve mevcut acceptance durumları bu pakette değiştirilmez. Bu öneri otomatik merge edilmez.

Son durum kontrolünde Ürün U01/U02 ve Cari L04 dar işleri birleşmiş, gerçek kabul ve sahipli temizlik teslim edilmiştir. Eski failure'lar tarihsel kalır. Son main CI sonucu ayrıca kontrol edilir; hedefi yalnız kendi koordinatörü kapatır. Genel prompt hazırlığı başlatma yetkisi değildir; Sipariş/Finans ise sonraki açık kullanıcı talimatıyla gerçekten başlatılmıştır, kesin durum başlangıç kaydındadır. B otomatik-account önerisi, #229 ve production insan sınırları korunur. Mobilin toplu değerlendirmesini sona alma tercihi mevcut masaüstü/klavye ve gerekli erişilebilirlik kontrollerini kaldırmaz.

## Kullanıma alma

1. Öneri paketi insan tarafından incelenir; normatif dosyalara sessizce taşınmaz.
2. Koordinatör tamamlanmış L04/Ürün/Organizasyon işlerini tekrar açmaz. ROADMAP'teki kısa bağımlılık kontrolüyle sonraki yetkili dilimde PROMPTS ve ilgili MODULE_BRIEFS bölümünü kullanır; görev adı, kaynaklar ve yazılabilir yolları kendisi güncel kayıttan doldurur.
3. Ajanlar başlangıç notunda kullanılacak örnekleri ve modüle özgü farkı belirtir. Sadece "okudum" makbuzu üretmez.
4. Kapanışta gerçekten yeni ders varsa LESSONS'a aynı sahip/normal review yoluyla kısa ek önerilir. Her başarılı turda yeni belge, commit veya yeni hatırlama sistemi üretilmez.

Bu paket mevcut [bilgi aktarım alanının](../../knowledge/README.md) yerine geçen bir RAG/registry değildir. Kalıcı ve ajanlar arası erişilebilir kaynak Git'teki bu öneridir; özel sohbet hafızası veya belirli modelin belleği değildir.

## Paket doğrulaması

Önceki `1bb9fed` sürümü altı öneri dosyası ve 56 doğrulanmış yerel bağlantı içerir. Sipariş/Finans başlangıç güncellemesiyle yerelde 15 Markdown dosyası, 77 yerel bağlantı (kırık 0), dengeli kod blokları ve diff/yazılabilir yüzey doğrulandı. İlk gönderilen tam promptlar yerel artefaktlarda, Git'teki tekrar kullanılabilir kopya ortak bölümü tekilleştirerek saklanır. Örnek sembollerin kaynak incelemesi tam kod denetimi değildir. Ürün kodu değişmediği için ürün testleri, SQL, browser ve geçmiş acceptance elle tekrar çalıştırılmadı. Uzak CI/insan review bu yerel kontrolden türetilmez; eski commit'in CI sonucu yeni değişikliğe taşınmaz.
