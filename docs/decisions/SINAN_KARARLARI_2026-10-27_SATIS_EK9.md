# Sinan Kararları — 2026-10-27 (Satış: sipariş PDF'i ve fiyat kademe zinciri)

Bu belge önceki karar dosyalarını silmez; SAT-D56…SAT-D60'ı ekler. SAT-D9 (fiyat kademe zinciri: anlaşma > cari özel >
cari grubu > şirket) ve SAT-D28'in (Satış'ta ayrı sözleşme kaydı) uygulamasıdır. İki yerde onları değiştirir: SAT-D58
SAT-D28'in alan listesine ürün fiyat satırlarını ekler; SAT-D60 SAT-D9'daki elle fiyat koşulunun yerine geçer.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sorular MOD-07 sipariş PDF'i (PR [#842](https://github.com/sinangym/GoymenERP/pull/842)) ve MOD-02 fiyat kademe
zinciri (PR [#843](https://github.com/sinangym/GoymenERP/pull/843)) uygulamalarındaki NEEDS-SINAN maddeleriydi (#843'ün 1–4.
maddeleri SAT-D57…D60 ile cevaplandı; 5. madde, paket standardı U-07, açık kalır). Sinan'a
seçeneğin adı **ve açıklaması** birlikte gösterildi; bir soruda Sinan kendi metnini yazdı, o metin aynen verilir.

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Onaylanmamış satış siparişinin (hazırlanıyor / onay bekliyor / reddedildi) PDF'i indirilebilsin mi? | Evet, ama 'TASLAK' filigranıyla | "Onaylanmamış siparişin PDF'i alınır ama her sayfada büyük 'TASLAK' yazısı olur." (seçilmeyenler: "Evet, durum yazılı olarak", "Hayır, yalnız onaylı") |
| Fiyat kademesinde 'cari grubu' (örn. bayi, toptancı) fiyat listesi için müşteri grubu tanımı nerede tutulsun? | Kendi metni: "Cari kartında bence ve bizde genellikle toptancı, perakendeci, imalatçı gibi ayrılır" | — (seçenekler: "Cari kartında grup alanı" — "Her cari karta bir 'fiyat grubu' seçilir; gruplar Ayarlar'dan yönetilen listedir."; "Şimdilik grup yok") |
| Anlaşma (sözleşme) fiyatı nerede tutulsun? | Sözleşme kaydında | "Satış sözleşmesi kaydına (SAT-D28) ürün fiyatları yazılır; zincir oradan okur." (seçilmeyen: "Ürün fiyat listesinde") |
| Aynı kademede (örn. aynı cariye ait) birden fazla geçerli fiyat listesi varsa hangisi kullanılsın? | Kullanıcı seçsin | "Adaylar gösterilir, siparişi giren kişi seçer (şu anki uygulama)." (seçilmeyenler: "En son başlayan", "En düşük fiyat", "Çakışmaya izin verilmesin") |
| Hiçbir fiyat listesi bulunamazsa elle fiyat yazılması nasıl olsun? | Liste fiyatsız satır kuralı yeter | "Elle fiyat yazılabilir; satır SAT-D43 gereği yönetici onayına gider. Ayrı yetki/neden alanı eklenmez." (seçilmeyen: "Ayrı yetki + zorunlu neden") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D56 | Onaylanmamış siparişin PDF'i | Her durumdaki siparişin PDF'i indirilebilir; **onaylı revizyon dışındaki** her durumda (hazırlanıyor, onay bekliyor, reddedildi vb.) her sayfada büyük **"TASLAK"** filigranı bulunur. Onaylı revizyonun PDF'i filigransızdır; onaylandıktan sonra iptal edilmiş veya zorla kapatılmış sipariş de filigransız basılır ve durumunu yazar (soru yalnız onaylanmamış durumları sordu) | MOD-07 (#842) |
| SAT-D57 | Cari grubu (fiyat grubu) | Müşteri grubu **cari kartında** bir alan olarak tutulur (Cari/Counterparty sahibi); atama **şirket kapsamlıdır** (şirketin cari hesabında, `CompanyPartyAccount`), şirketler arasında paylaşılan bir taraf alanı değildir. Sinan'ın verdiği tipik gruplar: **toptancı, perakendeci, imalatçı**; bunlar başlangıç listesidir. Grup listesinin Ayarlar'dan yönetilmesi Sinan'ın açıkça seçtiği değil, ona gösterilen seçenek açıklamasından alınan **yorumdur** (geri alınabilir UX seçimi); grup değerleri tahmin edilmez, cari kartına grup atanmamışsa grup kademesi o cari için atlanır | MOD-02 (#843), Cari kartı |
| SAT-D58 | Anlaşma fiyatı | Anlaşma kademesinin fiyatı **Satış sözleşmesi kaydında** (SAT-D28) tutulur. Ürün fiyat listesinde "anlaşma" kapsamı açılmaz. Bağımlılık yönü gereği anlaşma kademesini **Satış kendisi** çözer, alt kademeler için Ürün'ün fiyat okumasına sorar; SAT-D9'daki sıra korunur (modüller arası sözleşme, K3). Anlaşma veya grup kaydı var olduktan sonra okunamazsa üst kademe **atlanmaz**, sonuç "okunamadı" olur (fail-closed); kayıt hiç yokken (bugün) kademe "kaynak yok" olarak geçilir | MOD-02 (#843), SAT-D28 sözleşme dilimi |
| SAT-D59 | Aynı kademede birden fazla liste | Aynı kademede birden fazla geçerli liste varsa **adaylar gösterilir, siparişi giren kişi seçer**; sistem otomatik seçmez | MOD-02 (#843), UI-14 |
| SAT-D60 | Liste bulunamazsa elle fiyat | Hiçbir kademe eşleşmezse fiyat **elle yazılabilir**; satır SAT-D43 gereği yönetici onayına gider (SAT-D45: yetkisi olan fiyatı yazan da onaylayabilir). Elle fiyat için ayrı yetki veya zorunlu neden alanı **eklenmez**. Bu karar SAT-D9'daki "manuel fiyat için yetki, sebep ve audit" koşulunun **yerine geçer**: yetki, sebep ve audit SAT-D43 onayında karşılanır (yetki = SAT-D37 onay izni; sebep = onaylayanın girdiği zorunlu onay nedeni; audit = onay kaydı). Sebebi fiyatı yazan değil onaylayan girer; SAT-D45 gereği bu aynı kişi olabilir. Onay nedeninin zorunlu kalması bu kararın şartıdır | MOD-02 (#843), API-12 (#834) |

## Açık kalanlar

- **Sözleşmeden fiyatlanan satır:** SAT-D58'e göre fiyatı sözleşmeden gelen satırın ürün fiyat listesi referansı olmaz;
  bu satırın SAT-D43 onayına gidip gitmeyeceği ve iskontonun (SAT-D35) neye göre ölçüleceği sorulmadı. Karar verilene kadar
  böyle satırlar onaya gider (fail-closed). Sözleşme dilimi başlamadan NEEDS-SINAN olarak sorulur; bugün sözleşme kaydı
  olmadığı için etkisi yoktur.

- Paket standardı masterı (U-07) yokken paket standardı snapshot'ı "kaynak yok" kalır.
- SAT-D57 grup listesinin ilk değerleri dışında (ör. grup başına başka kural) bir şey seçilmedi.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz.
