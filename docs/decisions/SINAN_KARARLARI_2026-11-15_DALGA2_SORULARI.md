# Sinan Kararları — 2026-11-15 Dalga 2 soruları (ortalama maliyet toplamı, sevk planı, iade taksiti, masraf para profili, sevk riski, kredi limiti onayı, belge erişimi, unvan değişikliği, ebat kapsamı, kesit türleri)

Bu belge önceki karar dosyalarını silmez; `KLN-D55`, `SAT-D353`, `SAT-D354`, `FNS-D54`–`FNS-D56`, `KLN-D56`,
`ALS-D98`, `URN-D10` ve `URN-D11` kararlarını ekler. `SINAN_KARARLARI_2026-11-15_MALIYET_VE_RISK.md` (`KLN-D53`,
`URT-D67`, `FNS-D53`, `KLN-D54`), `SINAN_KARARLARI_2026-11-12_URUN.md`, `SINAN_KARARLARI_2026-11-12_URUN_EK1.md`
(`URN-D1`–`URN-D9`) ve `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` (BS-9) aynen geçerlidir; bu belge BS-9'u
aşağıda yazılan iki noktada netleştirir.

**Kaynak:** Sinan, 2026-11-15, Claude şeridi oturumunda soru-cevap aracıyla. Sorular tren-1 dalga 1 takım
raporlarındaki ve takımlar arası mesajlardaki NEEDS-SINAN maddelerindendir. "(Önerilen)" işaretleri soranındır.
Masraf ödemesi sorusunun ilk hâline Sinan'ın cevabı kelimesi kelimesine: **"Bunu anlamadım bunu mantıklı
araştırırsan aynı olması gerekmiyormu ben mi yanlış anladım"**; soru açıklanarak yeniden soruldu ve aşağıdaki seçim
yapıldı. Kullanılabilir artık (BS-5) etiketinin bölünme/birleşme mirası sorusu soruldu, cevap gelmedi; açık kalır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Stok (STK-S35): ürünün şirket geneli ortalama maliyeti depoların ortalamasından hesaplanırken bir depo okunamıyorsa veya o depoda kayıt yoksa ne yapılsın? | Kg ağırlıklı, okunamazsa yok (Önerilen) | Depoların ortalaması eldeki kg ile ağırlıklandırılır. Stoğu ve kaydı olmayan depo hesaba girmez. Tek bir depo okunamazsa şirket geneli değer "okunamadı" gösterilir, eksik hesap yapılmaz. Seçilmeyenler: "Kg ağırlıklı, okunamayanı atla" (okunamayan depolar çıkarılır, "eksik veri" uyarısı); "Şirket geneli olmasın" (yalnız depo bazında, STK-S35 iptal). |
| 2 | Satış (SAT-D313): siparişte "sevk planı miktarı" Lojistik'te hangi kayıttan okunsun? | Yükleme talimatı payı (Önerilen) | Siparişin yükleme talimatlarına ayrılmış miktarı plan miktarı sayılır. Lojistik'te bu kayıt zaten var; yeni tablo gerekmez. Plan üstü sevk engellenir. Seçilmeyenler: "Ayrı sevk planı kaydı" (yeni Lojistik belgesi, tablo, migration, ekran); "Şimdilik plan yok" (kalan = onaylı − kesin sevk sürer). |
| 3 | Finans (FIN-S10): faturanın birden çok taksiti açıkken KISMİ müşteri iadesi kabul edilince iade tutarı hangi taksitten düşülsün? | En yakın vadeden (Önerilen) | İade tutarı önce vadesi en yakın açık taksitten düşülür, artarsa sıradakine geçer. Seçilmeyenler: "En uzak vadeden"; "Oransal dağıt" (açık taksitlere kalan tutarları oranında). |
| 4 | Masraf ödemesi: para profili bir işlemde hangi para birimi ve kaç ondalık girilebileceğini söyler (TL, 2 ondalık); değerler banka ödemesiyle aynı olmalı. Soru yalnız Ayarlar listesinde ayrı satır olup olmayacağı (Alış siparişinde ALS-D69 ile aynı değerlerle ayrı satır açılmıştı). | Ayrı satır, aynı değer (Önerilen) | Ayarlar'da "masraf ödemesi" satırı açılır, değerleri banka ödemesiyle aynıdır (TL, 2 ondalık). İleride biri değişirse diğerini etkilemez; Alış/Satış'taki düzenle aynı. Seçilmeyen: "Tek satır, banka ödemesininki" (masraf kalıcı olarak manuel banka ödemesinin satırını kullanır). |
| 5 | Satış/Finans risk: onaylı irsaliyesi henüz olmayan ama kesinleşmiş sevk, müşteri riskinde "faturalanmamış sevk" tutarına sayılsın mı? | Evet, kesin sevk sayılır (Önerilen) | Mal çıktığı an risk doğar; irsaliye onayı beklenmeden kesin sevk miktarı × fiyat riske eklenir. Seçilmeyen: "Hayır, irsaliye onayından sonra". |
| 6 | Finans (OPEN-009): müşteri kredi limiti belli bir tutarın üzerindeyse ikinci bir kişinin onayı gereksin mi? | Şimdilik tek onay (Önerilen) | Yetkili kişi limiti tek adımda belirler; her değişiklik denetim kaydına girer. İkinci onay ve eşik sonra eklenebilir. Seçilmeyenler: "Eşik üstü ikinci onay" (şirket bazında eşik, ikinci yetkili); "Her limit ikinci onay". |
| 7 | Belgeler (B-26): bir dosya birden çok kayıtla bağlantılıysa erişim kuralı ne olsun? | Önerilen kural (Önerilen) | Görme: bağlı kayıtlardan en az birini görebilen dosyayı görür. Yeni sürüm yüklemek: tüm bağlı kayıtları görebilmeli. Başka kayda bağlamak: kaynak ve hedef kaydı görebilmeli. İlk bağlantı birincil sayılır. Seçilmeyenler: "Daha sıkı: hepsini görmeli"; "Yalnız birincil kayıt". |
| 8 | Cari (CARI-DEC-004): carinin resmi/hukuki unvanı değiştirilirken hazırlayan-onaylayan düzeni nasıl olsun? | Tek kişi, mevcut yetki | Cari düzenleme yetkisi olan kişi unvanı doğrudan değiştirir; değişiklik denetim kaydına girer, onay yok. Seçilmeyenler: "İki yeni yetki, iki kişi (Önerilen)"; "İki kişi ve Critical". |
| 9 | Ürün (BS-9): ürün kartındaki ebatlar üç şirkette ortak mı, her şirkete ayrı mı? | Ortak, tek satır (Önerilen) | Ürün kartı şirketler arası ortak olduğu için ebatlar da tek; yetkisi olan herhangi bir şirketten düzenlenir ve her değişiklik denetim kaydına girer. Seçilmeyen: "Şirket bazında". |
| 10 | Ürün (BS-9): kesit türleri Sac = kalınlık/en/boy, Rulo = kalınlık/en, Yuvarlak boru = dış çap/et kalınlığı (kg/m isteğe bağlı), Profil = en/yükseklik/et kalınlığı (kg/m zorunlu). Yeterli mi? | Şimdilik bu dördü (Önerilen) | Dört tür ve ölçüler aynen kalır; yeni tür gerektiğinde ayrı dilimle eklenir. Seçilmeyen: "Köşebent/lama/dolu da ekle". |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D55 | Şirket geneli ortalama maliyetin toplanması | Şirket geneli ortalama maliyet, depo ortalamalarının **eldeki kg ile ağırlıklı** ortalamasıdır. Stoğu ve kaynağı olmayan depo hesaba girmez. Bir depo bile okunamazsa sonuç "okunamadı" olur; eksik depolarla hesap yapılmaz (fail-closed). Hiç kaynak yoksa sonuç "kaynak yok"tur. Depo ortalaması KLN-D53'teki hareket anı hareketli ortalamadır. | Stok STK-S35 (şirket geneli okuma portu), Satış marj okuması |
| SAT-D353 | Sevk planı miktarının kaynağı (SAT-D313) | Siparişin sevk planı miktarı, Lojistik'teki **yükleme talimatlarına ayrılmış miktardır**; yeni plan kaydı açılmaz. Plan üstü sevk engellenir. Okuma, Lojistik'in sürümlü portu üzerinden yapılır (Satış Lojistik tablosuna erişmez). | Satış SAT-D313 kalan hesabı, Lojistik okuma portu |
| SAT-D354 | Kesin sevkin riske girmesi (plan S0(c)) | Onaylı irsaliyesi olmayan **kesin sevk**, müşteri riskinde "faturalanmamış sevk" tutarına sayılır (kesin sevk miktarı × fiyat). | Satış risk okuması, Finans kredi riski |
| FNS-D54 | Kısmi iadede taksit sırası | Birden çok açık taksitte kısmi müşteri iadesi, **vadesi en yakın açık taksitten** başlayarak düşülür; artan tutar sıradaki vadeye geçer. `finance_customer_return_installment_order_not_decided` reddi bu kuralla yer değiştirir. | Finans FIN-S10 (iade kabulü) |
| FNS-D55 | Masraf ödemesinin para profili | Ayarlar para profili kataloğuna **masraf ödemesi için ayrı satır** eklenir; değerleri manuel banka ödemesiyle aynıdır (GDC/GSP/GIN şirket profilleri ayrı, TRY 2 ondalık). Geçici `finance.manual-bank-payment.v1` bağlaması bu satıra taşınır; saklı satırlar profil kimliği ve sürümünü taşıdığından geçmiş kayıtlar değişmez. Anahtar adı uygulama ayrıntısıdır (geri alınabilir). | Ayarlar para kataloğu, Finans `ExpensePaymentBinding` |
| FNS-D56 | Kredi limitinde ikinci onay (OPEN-009) | **Şimdilik tek onay**: yetkili kişi kredi limitini tek adımda belirler veya kaldırır; her değişiklik denetim kaydına girer. Eşik ve ikinci onay sonra ayrı kararla eklenebilir. OPEN-009 bu kararla cevaplanmıştır; açık kararlar listesindeki durum güncellemesi ayrı belge değişikliğidir. | Finans FIN-S1/FIN-S15 kredi limiti |
| KLN-D56 | Çok bağlantılı belge erişimi (B-26, DEC-DOC-002) | Görme: bağlı kayıtlardan **en az birini** görebilen dosyayı görür. Yeni sürüm: **tüm** bağlı kayıtlar görülebilmeli. Bağlama: kaynak dosya en az bir bağlantısından görülebilmeli ve hedef kayıt görülebilmeli. İlk bağlantı birincildir. | Belgeler PLT-S16 |
| ALS-D98 | Cari resmi unvan değişikliği (CARI-DEC-004) | Unvan değişikliği **tek kişiyle, mevcut cari düzenleme yetkisiyle** yapılır; onay adımı ve yeni yetki yoktur. Değişiklik eski ve yeni değerle denetim kaydına girer; geçmiş belgelerdeki unvan değişmez. | Cari (Counterparty) |
| URN-D10 | Ebatların kapsamı (BS-9) | Ürün kartındaki ebatlar **şirketler arası ortaktır, tek satırdır**; yetkisi olan herhangi bir şirketten düzenlenir ve her değişiklik denetim kaydına girer. | Ürün ebat paneli |
| URN-D11 | Kesit türleri (BS-9) | Kesit türleri **şimdilik dört**: Sac (kalınlık/en/boy), Rulo (kalınlık/en), Yuvarlak boru (dış çap/et kalınlığı, kg/m isteğe bağlı), Profil (en/yükseklik/et kalınlığı, kg/m zorunlu; KLN-D54 tablosu). Yeni tür ayrı dilimle eklenir. | Ürün STK-S37 |

## Sınırlar

- Açık kalanlar (NEEDS-SINAN): kullanılabilir artık (BS-5) etiketi bölünür veya birleştirilirse çocuk etiketlerin
  durumu miras alıp almayacağı (soruldu, cevap gelmedi); yeni yetkilerin hangi rol/persona'ya verileceği
  (`product.dimension.manage`, `documents.files.link`, `settings.sales-delivery-tolerances.manage`,
  `settings.sales-approval-thresholds.manage`, URT-D66 termin izni); satış politikası baseline aktarımının üretimde
  şirket başına sırası ve yetkilisi.
- Bu belge yetki vermez. İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı
  kararı ister.
