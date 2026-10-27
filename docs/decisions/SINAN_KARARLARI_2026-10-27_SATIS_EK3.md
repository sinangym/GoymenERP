# Sinan Kararları — 2026-10-27 (Satış: talep/teklif, sözleşme, sorumlu, gerçekleşme toleransı)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md`, `SINAN_KARARLARI_2026-10-26_SATIS_EK.md` ve
`SINAN_KARARLARI_2026-10-27_SATIS_EK2.md` dosyalarını silmez; onlara SAT-D22…SAT-D29'u ekler.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum
`session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru cevapları. Sorular Issue #66'daki `NEEDS-SINAN`
satırlarıydı ([6434928541](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6434928541) S4
talep/teklif, [6434190983](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6434190983) satış
sorumlusu, [6433544263](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6433544263) sözleşme,
[6435560545](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6435560545) referans okuması) ve
API-08 (PR #832) uygulamasında çıkan iki soru. Cevaplar (seçilen seçenek veya Sinan'ın kendi metni):

| Soru | Cevap |
|---|---|
| Satış talebinin onayı kaç kişi gerektirsin? (No 160) | "Tek onaycı ama, birden fazla kişiye onay yetkisi verilebilir, hazırlayan kişi onaylayan kişi aynı olabilir bu o kişinin yetki durumuna bağlı" |
| No 161 satış taleplerine de uygulansın mı? | Evet, uygulansın |
| Onaylanmamış (bekleyen) sipariş talebin reddini engellesin mi? | Evet, engellesin |
| Teklifin geçerlilik süresi nasıl işlesin? | Süresi geçene yönetici onayı |
| Kayıp/fırsat nedenleri nasıl girilsin? | Kapalı liste + açıklama |
| Teklife fiyat yazmak ayrıca `sales.orders.price.write` istesin mi? | Hayır, teklif yazma yetkisi yeterli |
| Satış sorumlusu seçicisinde kimler listelensin? | Sipariş oluşturma yetkisi olanlar |
| Yalnız talep/teklif yetkisi olan kişi müşteri/ürün/para birimi listelerini okuyabilsin mi? | Evet, okuyabilsin |
| Satış sözleşmesi nasıl tutulsun? (S-11) | Satış'ta ayrı kayıt |
| S-11 belge kategorileri kesin mi? | Ayarlar'dan yönetilen liste |
| Fazla teslim %1–%2 (uyarı bandı) gerçekleşme ekseninde ne görünsün? | Tamamlandı + uyarı işareti |
| Teslim toleransı hangi tarihteki politikaya göre hesaplansın? | Siparişin tarihi |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D22 | Satış talebi onayı (No 160) | Talep **tek onaycı** ile onaylanır. Onay yetkisi (`sales.requests.approve`) birden fazla kişiye verilebilir. Talebi hazırlayan kişi, onay yetkisine sahipse kendi talebini onaylayabilir; ayrıca "hazırlayan ≠ onaylayan" kısıtı yoktur. Yetkinin kime verileceği grant/rol kararıdır ve insan kapısında kalır | API-21 (#824) |
| SAT-D23 | No 161'in satış talebine uygulanması ve bekleyen sipariş | No 161 satış taleplerine de uygulanır: talepten (onu kapsayan teklif üzerinden) doğmuş **iptal edilmemiş her sipariş** — onaylanmamış/bekleyen, onaylı, revizyonla hazırlığa dönmüş veya zorla kapatılmış — talebin reddini engeller; talep ancak sipariş iptal edildikten sonra reddedilebilir | API-21/API-23 (#824, #828) |
| SAT-D24 | Teklif geçerlilik süresi | Teklifin geçerlilik tarihi geçmişse "Siparişe dönüştür" **yönetici onayı** olmadan yapılamaz. Yönetici onayı ayrı bir onay adımıdır (yeni izin adı permission manifest'e eklenebilir, hiçbir role atanmaz); geçerlilik içindeki teklif bugünkü gibi dönüştürülür. Varsayılan geçerlilik süresi bir sayı olarak seçilmemiştir: Ayarlar'da yapılandırılabilir, kayıt yoksa varsayılan uygulanmaz | API-22/API-23 (takip işi) |
| SAT-D25 | Kayıp/fırsat nedenleri | Nedenler **Ayarlar'dan yönetilen kapalı bir listeden** seçilir; "Diğer" seçilirse açıklama zorunludur. Liste içeriği (başlangıç nedenleri) Ayarlar kaydıdır; kayıt yoksa kayıp kaydı fail-closed reddedilir | API-24 (takip işi) |
| SAT-D26 | Teklif fiyatı yetkisi | Teklife fiyat yazmak için `sales.quotes.write` yeterlidir; ayrıca `sales.orders.price.write` aranmaz. Siparişe dönüştürme bugünkü gibi sipariş oluşturma ve fiyat yazma yetkilerini ister. Fiyatları kendisine gizlenen (fiyat görme yetkisi olmayan) kişinin teklif revizyonu/fiyatlı oluşturması veri kaybını önlemek için reddedilmeye devam eder | API-22 (#828) |
| SAT-D27 | Satış sorumlusu seçicisi ve referans okuması | (1) Satış sorumlusu seçicisi şirkette aktif olan ve **sipariş oluşturma yetkisine (`sales.drafts.create`) sahip** kişileri listeler. (2) Yalnız talep/teklif yazma yetkisi olan kişi (`sales.requests.write` / `sales.quotes.write`) de müşteri/ürün/para birimi seçim listelerini (`/sales/references/*`) okuyabilir. İkisi de mevcut izin adlarıyla uygulanır; yeni rol/grant açılmaz | MOD-08/API-04, UI-20 (takip işleri) |
| SAT-D28 | Satış sözleşmesi ve belge kategorileri (S-11) | (1) Satış sözleşmesi **Satış modülünde ayrı bir kayıttır** (numara, sürüm, durum, geçerlilik, ödeme planı, iskonto ve maddeler; PDF'i Belge kasasında kayda bağlı durur). Ödeme planı/iskonto gibi alanların değer ve kuralları ayrıca politika kararıdır ve bu karar onları seçmez. (2) Belge kategorileri **Ayarlar'dan yönetilen bir listedir**; S-11 görselindeki 8 kategori başlangıç önerisidir, sonradan eklenip pasifleştirilebilir | UI-71 devamı, yeni Satış sözleşme dilimi, Belgeler kategori alanı (takip işleri) |
| SAT-D29 | Gerçekleşme ekseninde tolerans gösterimi ve tarihi (SAT-D5'in uygulaması) | (1) Fazla teslim **uyarı bandındaysa** (%1–%2 aralığı, Ayarlar politikasındaki eşiklerle) satır gerçekleşme ekseninde **"Tamamlandı"** sayılır ve yanında **tolerans uyarısı işareti** görünür. (2) Tolerans, **siparişin iş tarihinde** geçerli olan Ayarlar politikasıyla hesaplanır; sonradan değişen politika eski siparişi etkilemez | API-08 (#832), UI-15 |

## Açık kalanlar (bu belgeyle karar verilmedi)

- Onay bandındaki fazla teslimin onaydan sonra "Tamamlandı" sayılıp sayılmayacağı; zorla kapatılmış
  siparişte uyarı bandında eksik teslimin gösterimi; yaşam döngüsünde "Aktif" türetimi ve tüm eksenler
  bitince otomatik kapanış (API-08 / #832 NEEDS-SINAN 1, 2, 4). Karar gelene kadar fail-closed kalır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol
  veya grant açılmaz; yeni izin adları gerekiyorsa permission manifest'e eklenir, role atanmaz.
- Sayısal değerler (varsayılan teklif süresi, kayıp nedeni listesi, tolerans eşikleri) Ayarlar
  kaydıdır; kayıt yoksa ilgili işlem fail-closed kalır.
