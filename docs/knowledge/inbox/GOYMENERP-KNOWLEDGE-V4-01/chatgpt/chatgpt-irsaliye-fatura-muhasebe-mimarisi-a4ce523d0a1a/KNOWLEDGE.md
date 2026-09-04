# Göymen ERP Knowledge Export — chatgpt-irsaliye-fatura-muhasebe-mimarisi-a4ce523d0a1a

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Provider: chatgpt
Conversation-ID: chatgpt-irsaliye-fatura-muhasebe-mimarisi-5365b941
Conversation-Title: İrsaliye Fatura Muhasebe Mimarisi
Coverage: visible_only
Normative-Status: NON_NORMATIVE_CANDIDATE
Repository-Base: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97

## Kaynak özeti

Bu paket, bu ChatGPT konuşmasında görünür olan Göymen ERP proje bilgisini ve yalnız doğrulama amacıyla okunan güncel canonical repository kanıtlarını normalize eder. Operasyon talimatı olan evrensel aktarım promptu kaynak gereksinim olarak içe alınmamıştır. Referans verilen önceki “Şirket içi ChatGPT kullanımı” konuşmasının ham/tam içeriği bu oturumda doğrudan görünür değildir; bu konuşmada aktarılan anlam ve canonical repo kararları korunmuştur.

## Ana anlam

- Satış ve Satın Alma altında ayrı irsaliye/fatura menüleri yerine, kendi modül sınırlarında sekmeli **İrsaliye & Fatura** çalışma alanları kullanılacaktır.
- İade/red/iptal/düzeltme ayrı ana menü değil, ilgili belge zincirinin yaşam döngüsü/durum görünümüdür.
- Satış ve Alış operasyon ekranları Muhasebe tarafından kopyalanmaz. Muhasebe belge doğruluğu, eşleme, cari hesap, fiş, hesap planı, mizan ve dönem/vergi kontrolüne odaklanır.
- Kullanıcının açık kararıyla **Muhasebe ve Finans ayrı ana modüllerdir**. Ana **Cari** modülü de ayrı kalır.
- **Muhasebe > Cari Hesaplar**, ikinci cari masterı oluşturmaz; aynı Cari/CariHesap kaydının muhasebe ekstresi, ticari ekstresi, açık kalem, fatura, tahsilat/ödeme ve mutabakat görünümüdür.
- Finans; nakit, banka, tahsilat/ödeme, ödeme araçları, kredi/finansman, teminat/limit ve nakit akışı odağındadır; detay muhasebe ekstresinin sahibi değildir.
- Bir olay/belge bir kez kaydedilir; farklı modüller aynı kaydı farklı iş bağlamlarında gösterir.

## Canonical repo doğrulaması

Güncel main `188c792900e35d3e2ed6a9ba4734dc9ad596fc97` üzerinde karar ve hedef dokümanları bu konuşmanın nihai yönüyle uyumludur: `DEC-SAL-007` iade/red/düzeltmeyi ayrı ana menü olmaktan çıkarır; Master Rapor v3 Satış/Satın Alma İrsaliye & Fatura, ayrı Muhasebe ve Finans, ayrıca Muhasebe > Cari Hesaplar mimarisini açıkça tanımlar. DELIVERY_BACKLOG v1 de W2-IRSALIYE, W2-ACIKKALEM, W4-MUHASEBE ve W4-FINANS dilimlerini ayırır. Bu doğrulama uygulamanın kodlanmış/çalışıyor olduğu anlamına gelmez.

## Sürüm ve prototip notu

Konuşmanın ilk tasarım prototipinde ayrı “İade / Red” alt menüsü vardı; sonraki değerlendirme ve canonical karar bunu supersede eder. Konuşmada üretilen HTML/PNG/MD/ZIP prototipleri `PROTOTYPE_ONLY` olarak değerlendirilir; production frontend/backend kanıtı değildir.
