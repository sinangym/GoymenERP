# Sinan Kararları — 2026-11-22 (sistem içi yapay zekâ: zamanlama, sürekli öğrenme, onaycı yardımı, kişisel asistan ve zamanlı görevler)

Bu belge önceki AI kararlarını silmez; AI-D10–AI-D15 ekler. DEC-AI-001..DEC-AI-009 (`KARAR_SICILI.md`), AI-D1..AI-D6
(`SINAN_KARARLARI_2026-11-03_AI.md`), AI-D7, AI-D8, AI-D9, 2026-10-21 kararlarının 156/157/159 numaraları ve E9
geçerliliğini korur: yapay zekâ isteğe bağlıdır, kendini onaylayamaz, yetki veremez, ham SQL kullanamaz; her yazma önizleme
ve kullanıcı onayı ister; hassas veri yalnız takma adla, kişisel ve "çok gizli" veri hiç dışarı çıkmaz; sağlayıcı veriyi
saklamaz ve eğitmez; anahtar ekleme, üretimde açma, veri türü izinleri ve rol/izin atamaları insan kapısıdır.

**Kaynak:** Sinan, 2026-11-22, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 56, ajan aktarımı: Issue
#1189 yorumu 7078140665). Sorulardan önce sistemdeki yapay zekânın kod durumu ve ERP satıcılarının (SAP, Oracle Fusion ve
NetSuite, Microsoft Dynamics 365, Workday, Infor, Odoo, Epicor, IFS, Sage, Acumatica) 2025–2026 yapay zekâ/ajan
özellikleri araştırılıp özetlendi. "(Önerilen)" işaretleri soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması | Seçilmeyen seçenekler |
|---|---|---|---|---|
| 1 | Sistem yapay zekâsı (bilgi tabanı, araçlar, onaycı yardımı, öğrenme) ne zaman yapılsın? | **Bu tren bitince ayrı trende (Önerilen)** | Mevcut tren K3 kabulüne ve genel değerlendirmeye gider; yapay zekâ ayrı plan, kararlar ve dilimlerle sonraki trende yapılır. | Paralel başlansın. |
| 2 | Yapay zekâ ilk aşamada hangi modeli kullansın? | **Serbest cevap:** "En faydalı hangisi ise, öğrenimi yapıp kendisini sürekli geliştirsin sürekli öğrensin, birşeyi aynı şeyi sürekli yeniden öğrenmesin ama öğrendiği değişirse bunu güncellesin ve üzerine ekleyerek öğrenmesini genişletsin yeni şeyleride sürekli öğrensin" | Model seçimi faydaya göre (AI-D11); öğrenme sürekli, tekrarsız, değişince güncellenen ve üzerine eklenen bilgi olarak (AI-D12). | Dış model, takma adla (Önerilen); Önce yerel model; İkisi birden. |
| 3 | Onaylarda yapay zekâ ilk aşamada ne kadar ileri gitsin? | **Özet + risk + gerekçeli öneri (Önerilen)** | Onaylayan her zaman insan; yapay zekâ özeti, kural kontrolünü ve öneriyi hazırlar. Otomatik onay sonra, her onay türü için ayrı kararla. | Düşük riskte otomatik onay da. |
| 4 | İlk yapılacak yapay zekâ yetenekleri hangileri olsun? | **Sistemi ve kararları bilen asistan; Onay kutusu yardımcısı; E-posta/PDF'ten taslak; Doğal dille sorgu ve rapor** ve serbest ek: "Ve sistem içi daha fazla, her modül için daha detaylı düşün ve modüller arası daha detaylı düşün, ayrıca her kullanıcının kendi ai si olsun veya bunun yerine sen ne önerirsen, birde her kullanıcının openai chatgpt deki dot gibi sürekli zamanlı görev ve araştırma takibi yapabilen bir özellikte olsun ve kullanıcılar bunu hem sistemin her modülünde hemde sistem dışı chatgpt sohbeti gibi sistem dışı sektörel işlerinde kullanabilsin" | Dört yetenek seçildi; modül ve modüller arası ayrıntılı yetenek planı, kişisel asistan ve zamanlı görev/araştırma takibi istendi (AI-D13–AI-D15). | — |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| AI-D10 | Zamanlama | Sistem yapay zekâsı mevcut tren (PR #1201, adet/kg dahil) kabul edilip ana dala girdikten sonra **ayrı bir trende** yapılır; plan `docs/delivery/AI_TREN_PLANI_v2.md`. | Yol haritası |
| AI-D11 | Model seçimi | Model **faydaya göre** seçilir: sabit bir sağlayıcıya bağlanmayan, mevcut OpenAI/Anthropic adaptörlerini kullanan model-bağımsız ağ geçidi; aday modeller şirketin gerçek görev setiyle (değerlendirme testleri) karşılaştırılıp en iyi sonuç veren seçilir ve görev türüne göre farklı model kullanılabilir. AI-D3/D4/D5 veri kuralları ve AI-D6/D7 insan kapıları (anahtar, üretimde açma) aynen sürer; yerel model OPEN-014/OPEN-020 ile açık kalır. | AI ağ geçidi, sağlayıcı yapılandırması |
| AI-D12 | Sürekli öğrenme | Yapay zekâ **sürekli öğrenir**: kararlar, kurallar, ekran/iş akışı bilgisi, onay/red gerekçeleri, düzeltmeler ve kullanıcı geri bildirimleri sürümlü bir şirket bilgi tabanına işlenir. Aynı bilgi tekrar öğrenilmez (kimlik ve kaynakla tekilleştirilir); bilgi değişirse eski sürüm "yerine geçildi" diye işaretlenip yenisi geçerli olur; yeni bilgi mevcut bilginin üzerine eklenir. Her bilginin kaynağı ve geçerlilik tarihi tutulur. Şirket kuralı niteliğindeki öğrenimler DEC-AI-006'ya göre öneri → yönetici onayı → sürümlü politika yolundan geçer; yapay zekâ politikayı kendisi yayımlamaz. Model, şirket verisiyle eğitilmez (AI-D5). | AI bilgi tabanı, geri bildirim döngüsü |
| AI-D13 | Onaycı yardımı (ilk aşama) | Onay ekranlarında yapay zekâ **özet, risk işaretleri ve gerekçeli öneri** hazırlar; onaylayan her zaman insandır; kural kontrolü sunucudaki deterministik kurallardan okunur. Politika sınırlı otomatik onay ilk aşamada yoktur; her onay türü için ayrı kararla açılabilir. | Onay kutusu, onay ekranları |
| AI-D14 | İlk yetenekler | İlk trende: (1) sistemi ve kararları bilen asistan, (2) bütün onay türlerini toplayan onay kutusu yardımcısı, (3) e-posta/PDF'ten satış siparişi, tedarikçi teyidi ve gelen fatura taslağı (kaydetmeyi insan onaylar, DEC-AI-008), (4) yetki sınırında doğal dille sorgu ve rapor; ayrıca her modül ve modüller arası ayrıntılı yetenek planı (`AI_TREN_PLANI_v2.md`). | AI yetenek kataloğu |
| AI-D15 | Kişisel asistan ve zamanlı görevler | Her kullanıcının **kendi asistanı** olur: tek bir sistem yapay zekâsı, kullanıcının yetkisiyle, kullanıcıya ait hafıza ve tercihlerle çalışır (ayrı model değil). Kullanıcı **zamanlı görev ve araştırma takibi** kurabilir (ör. her sabah açık onay özeti, geciken tedarikçi teslimlerini izleme, haftalık marj kaybı raporu, sektör/emtia fiyatı araştırması); görevler sistem içi modüllerde ve sistem dışı genel/sektörel sohbette kullanılabilir. Sistem dışı sohbet ve web araştırması AI-D1 (genel sorulara cevap) kapsamındadır; şirket verisi dışarı çıkacaksa AI-D3/D4 kuralları uygulanır; zamanlı görev yazma işlemi yapamaz, yalnız okur, özetler ve bildirir. | Kişisel asistan, zamanlı görevler, bildirimler |

## Sınırlar

- Bu belge yapay zekâyı üretimde açmaz, anahtar eklemez, rol/izin atamaz; bunlar insan kapısıdır (AI-D6/D7).
- Yerel model ve GPU (OPEN-014), sağlayıcı/KVKK/aktivasyon (OPEN-020) açık kalır; `OPEN-*` kapatılmaz.
- Politika sınırlı otomatik onay, eşikleri ve hangi onay türlerinde açılacağı ayrı karardır.
- Sistem dışı web araştırmasının hangi sağlayıcıyla yapılacağı ve şirket verisiyle birleştirme sınırları ayrı karardır.
- Kişisel asistan hafızasının saklama süresi mevcut 90 gün / 1 yıl / 2 yıl kararlarıyla (159, 157, E9) uyumlu ayrıca netleştirilir.
- `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez.
