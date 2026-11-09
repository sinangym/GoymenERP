# Sinan Kararları — 2026-11-07 (sevkiyatçı bilinmezken fazla teslim onayı)

Bu belge önceki karar dosyalarını silmez; SAT-D222'yi ekler. SAT-D30 (onay bandındaki fazla teslimin onayı) uygulamasında
(PR #963) açık kalan soruyu cevaplar. SAT-D30, SAT-D168, EK28 SAT-D187/D188/D190, EK35 SAT-D209 ve EK37 SAT-D213/SAT-D216
geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevap: bir tur, bir soru. "(Önerilen)" işareti soranındır. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | "Fazla teslim onayı (SAT-D30): senin kararınla sevkiyatı yapan kişi kendi fazla teslimini onaylayamıyor (SAT-D216). Ama Lojistik bugün sevkiyatı kimin yaptığını Satış'a söylemiyor. Modül sırası kararın (SAT-D209) yüzünden Lojistik'e yeni bağlantı da ekleyemiyoruz. Lojistik bu bilgiyi verene kadar ne olsun?" | Kapalı kalsın (Önerilen) | "Lojistik sevkiyatı yapanı bildirene kadar hiçbir fazla teslim onaylanamaz; satır 'onay bekliyor' kalır. Güvenli ama özellik o zamana kadar fiilen kullanılamaz." (seçilmeyenler: "Kontrolsüz açılsın" — "Şimdilik 'sevkiyatı yapan onaylayamaz' kontrolü yapılmadan onay verilebilsin; Lojistik bilgiyi verince kontrol devreye girsin. Özellik hemen çalışır ama geçiş döneminde aynı kişi onaylayabilir."; "Kontrolsüz, iz bırakarak" — "Onay verilebilsin ama kayıtta 'sevkiyatçı doğrulanamadı' işareti dursun; sonradan raporla incelenebilsin.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D222 | Sevkiyatçı bilinmezken fazla teslim onayı | Lojistik sevkiyatı yapan kişiyi Satış'a bildirene kadar **hiçbir fazla teslim onaylanamaz**; onay bandındaki satır "onay bekliyor" kalır (soru 1). SAT-D216 kontrolsüz ya da "sevkiyatçı doğrulanamadı" işaretiyle uygulanmaz. Uygulama ayrıntısı (geri alınabilir): onay komutu bu durumda anahtarın kayıtlı 409 `sales_over_delivery_shipper_unverified` cevabını verir; Lojistik sevk edeni bildirdiğinde SAT-D216 kontrolü kod değişmeden devreye girer | PR #963 (SAT-D30 onay komutu), Satış README "Over-delivery approval" |

## Bilinçli değişiklikler

- SAT-D30 / SAT-D168 ile verilen fazla teslim onayı yetkisi, Lojistik sevk edeni bildirene kadar **fiilen kullanılamaz**. Bu,
  SAT-D216'nın (sevkiyatı yapan kendi fazla teslimini onaylayamaz) güvenli uygulanması için bilerek kabul edilmiştir; "onaylı
  fazla teslim" işareti bu sürede hiçbir satırda oluşmaz.

## Sınırlar

- Lojistik'e yeni port, tablo veya ekran **eklenmedi** (SAT-D209 aynen geçerli).
- `ShippedBy`, Satış'a ait mevcut AT-024 teslim cevabı kaydında (`SalesOrderLineDeliveryV1`) **isteğe bağlı** bir alandır;
  Lojistik onu doldurana kadar `null` kalır. Lojistik'in bu alanı ne zaman ve nasıl dolduracağı Lojistik geliştirmesi başlayınca
  incelenir ("ertelendi: Lojistik başlayınca").
- Bu karar bir `OPEN-*` kapatma **değildir**.
- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. `sales.orders.over-delivery.approve` hiçbir role atanmamıştır;
  gerçek grant ataması insan kapısıdır.
