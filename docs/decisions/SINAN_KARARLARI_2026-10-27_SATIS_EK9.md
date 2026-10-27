# Sinan Kararları — 2026-10-27 (Satış: sipariş PDF'i ve fiyat kademe zinciri)

Bu belge önceki karar dosyalarını silmez; SAT-D56…SAT-D60'ı ekler. SAT-D9 (fiyat kademe zinciri: anlaşma > cari özel >
cari grubu > şirket) ve SAT-D28'in (Satış'ta ayrı sözleşme kaydı) uygulamasıdır; onları değiştirmez.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sorular MOD-07 sipariş PDF'i (PR [#842](https://github.com/sinangym/GoymenERP/pull/842)) ve MOD-02 fiyat kademe
zinciri (PR [#843](https://github.com/sinangym/GoymenERP/pull/843)) uygulamalarındaki NEEDS-SINAN maddeleriydi. Sinan'a
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
| SAT-D56 | Onaylanmamış siparişin PDF'i | Her durumdaki siparişin PDF'i indirilebilir; **onaylı revizyon dışındaki** her durumda (hazırlanıyor, onay bekliyor, reddedildi vb.) her sayfada büyük **"TASLAK"** filigranı bulunur. Onaylı revizyonun PDF'i filigransızdır | MOD-07 (#842) |
| SAT-D57 | Cari grubu (fiyat grubu) | Müşteri grubu **cari kartında** bir alan olarak tutulur (Cari/Counterparty sahibi). Sinan'ın verdiği tipik gruplar: **toptancı, perakendeci, imalatçı**; bunlar başlangıç listesidir. Grup listesinin nasıl yönetileceği (Ayarlar'dan liste — Sinan'a gösterilen seçenek açıklaması) uygulama PR'ında bu açıklamaya göre yapılır; grup değerleri tahmin edilmez, cari kartına grup atanmamışsa grup kademesi o cari için atlanır | MOD-02 (#843), Cari kartı |
| SAT-D58 | Anlaşma fiyatı | Anlaşma kademesinin fiyatı **Satış sözleşmesi kaydında** (SAT-D28) tutulur; zincir anlaşma fiyatını oradan okur. Ürün fiyat listesinde "anlaşma" kapsamı açılmaz | MOD-02 (#843), SAT-D28 sözleşme dilimi |
| SAT-D59 | Aynı kademede birden fazla liste | Aynı kademede birden fazla geçerli liste varsa **adaylar gösterilir, siparişi giren kişi seçer**; sistem otomatik seçmez | MOD-02 (#843), UI-14 |
| SAT-D60 | Liste bulunamazsa elle fiyat | Hiçbir kademe eşleşmezse fiyat **elle yazılabilir**; satır SAT-D43 gereği yönetici onayına gider (SAT-D45: yetkisi olan fiyatı yazan da onaylayabilir). Elle fiyat için ayrı yetki veya zorunlu neden alanı **eklenmez**. Bu karar SAT-D9'daki "manuel fiyat için yetki, sebep ve audit" ifadesini şöyle uygular: yetki = SAT-D37 onay izni, sebep/audit = SAT-D43 onay kaydı | MOD-02 (#843), API-12 (#834) |

## Açık kalanlar

- Paket standardı masterı (U-07) yokken paket standardı snapshot'ı "kaynak yok" kalır.
- SAT-D57 grup listesinin ilk değerleri dışında (ör. grup başına başka kural) bir şey seçilmedi.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz.
