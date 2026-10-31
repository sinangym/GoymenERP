# Sinan Kararları — 2026-10-31 (Satış: e-belge kapsamı, senaryo kodları, fatura bekleme, şirkete özel bağ)

Bu belge önceki karar dosyalarını silmez; SAT-D119…SAT-D122'yi ekler. PR #860 (SAT-D102 Cari e-belge),
PR #857 (SAT-D103/D114 revizyonda fatura) ve PR #852 (iskonto limit bağı) uygulamalarının açtığı NEEDS-SINAN
sorularını cevaplar. SAT-D102, SAT-D114, SAT-D66 ve SAT-D116 geçerlidir.

**Kaynak:** Sinan'ın 31 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; dört soruda da önerilen seçenek seçildi.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Müşterinin e-belge durumu bir şirkette kontrol edilip kaydedilince, diğer 2 şirketin cari kartında da görünsün mü? (VKN'ye bağlı, GİB'de tek) | 3 şirkette ortak | "Müşterinin VKN'si için bir kez kontrol edilir, üç şirkette de aynı görünür. GİB sorgusu tek; gerçeğe uygun." (seçilmeyen: "Her şirket ayrı" — "Her şirketin cari kartı kendi e-belge durumunu ayrı tutar; bir şirketteki kontrol diğerine yansımaz (şu anki kod).") |
| e-Fatura senaryo kodları listesi Ayarlar'da nasıl başlasın? | Standart set gelsin | "Standart GİB/UBL-TR kodları (TEMELFATURA, TİCARİFATURA, e-Arşiv, İHRACAT) hazır gelir; sen düzenleyebilirsin. Özellik hemen kullanılabilir olur." (seçilmeyen: "Boş kalsın, ben gireyim") |
| Onaylı siparişte YALNIZ miktar veya YALNIZ KDV/tevkifat değişip müşteri kabulü beklenirken, daha önce sevk edilmiş malın faturası ne olsun? | O da beklesin | "Her revizyon türü (miktar, KDV/tevkifat) için müşteri kabulü gelene kadar fatura bekler; fiyat kuralıyla (SAT-D114) aynı, tutarlı ve güvenli." (seçilmeyen: "Yalnız fiyatta beklesin") |
| Şirkete özel iskonto limit bağı ne zaman açılsın? | Şimdilik ortak kalsın | "Üç şirkette ortak varsayılan sürer; şirkete özel bağ sonra, ihtiyaç olunca açılır. Daha az karmaşıklık." (seçilmeyen: "Şimdi açılsın") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D119 | e-belge durumunun şirket kapsamı | Müşterinin e-belge durumu (e-Fatura mükellefi / e-Arşiv) müşterinin **VKN'sine bağlı tek bir olgudur**; bir kez kontrol edilip kaydedilince aynı VKN'li müşterinin **üç şirketteki** cari kartında da görünür. Kayıt mimari değişmezleri korur: VKN düzeyinde ortak referans olarak tutulur, bir şirketin özel satırı diğerine sızdırılmaz; modül sınırı ve CompanyId filtresi bozulmaz. Senaryo override'ı ve kontrol zamanı/kişisi de aynı VKN için ortaktır (senaryo override'ının VKN-ortak olması SAT-D102'deki "müşteri bazında" ifadesinin okunuşu; kontrol zamanı/kişisinin ortaklığı öneri — ikisi de geri alınabilir yorum). Bu karar #860'ın bugünkü `CompanyPartyAccount` (şirket-hesap) bazlı modelinin yerine geçer | Cari e-belge (PR #860), SAT-D102, AT-009 |
| SAT-D120 | e-Fatura senaryo kod listesi | Ayarlar'daki e-Fatura senaryo kod listesi standart GİB/UBL-TR kodlarıyla başlar (TEMELFATURA, TİCARİFATURA, e-Arşiv senaryosu, İHRACAT); liste efektif tarihli ve **düzenlenebilir** (gözden geçirilmiş katalog sürümü). Senaryo **varsayılan değeri** boş kalır ve Sinan tarafından girilir (SAT-D102) | Ayarlar senaryo kod listesi (PR #860), SAT-D102 |
| SAT-D121 | Müşteri kabulü beklenirken fatura (tüm revizyon türleri) | SAT-D114'teki "müşteri kabulü beklenirken fatura bekler" kuralı yalnız fiyat revizyonunda değil, **miktar (SAT-D104/D111) ve KDV/tevkifat (SAT-D105) revizyonlarında da** geçerlidir. Müşteri kabulü gereken siparişte, yeni revizyon müşteri kabulü beklerken daha önce sevk edilmiş mal dahil o siparişin faturası bekler | Sipariş durum ekseni (`invoicing.gate`), PR #857, S5 API-32 |
| SAT-D122 | Şirkete özel iskonto limit bağı | İskonto limit grubu kişi bağları **şimdilik üç şirkette ortaktır** (SAT-D66 teyidi); şirkete özel bağ (bir kişiye şirket bazında farklı grup) bu dalgada açılmaz, ihtiyaç olunca ayrı kararla açılır. SAT-D116'nın "üye olunan şirketlere yazılır" kuralı geçerlidir | Ayarlar iskonto limit bağı (PR #852) |

## Açık kalanlar

- **SAT-D119 paylaşım anahtarı:** Aynı VKN birden çok Cari kartında olabildiğinden (AT-009, uyarı — ret değil), paylaşılan
  olgunun anahtarının VKN → hangi taraf/hesap kümesini kapsadığı ve **TCKN'li veya vergi numarasız** (nihai tüketici, e-Arşiv)
  müşterilerde durumun nasıl tutulacağı uygulama PR'ında (K3) belirlenir. Belirlenene kadar kontrol bugünkü gibi şirket/hesap
  bazında kalır (fail-closed, geri alınabilir).
- Paylaşılan e-belge referansının müşterinin VKN'si değişirse (`OPEN-030`) ne olacağı sorulmadı; bu durum uygulama PR'ında ele alınır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- SAT-D119 uygulaması şirket izolasyonu ve modül sınırı değişmezlerini koruyarak tasarlanır; VKN düzeyinde ortak referansın
  sahibi ve okuma yüzeyi uygulama PR'ında (K3) belirlenir.
- e-belge sağlayıcı/GİB entegrasyonu (`OPEN-016`) ve `counterparty.accounts.e-document.update` izninin personaya verilmesi
  insan kapısı olarak açık kalır.
