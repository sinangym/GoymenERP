# Sinan Kararları — 22 Kasım 2026 (adet/kg sevk, yapay zekâ okuma araçları, rol taslağı ek soruları)

Bu belge önceki karar dosyalarını silmez; SAT-D462–SAT-D465, AI-D35–AI-D36, ROL-D21–ROL-D23 ve YOL-D22–YOL-D24 ekler. ROL-D21,
SAT-D250 ve SAT-D276 (2)'deki marj görünürlüğünü günceller.

**Kaynak:** Sinan, 22 Kasım 2026, Claude Code oturumunda soru kutusuyla. Sorular şerit raporlarındaki NEEDS-SINAN
maddelerinden gelir: adet/kg AK-4b + AK-7b raporu (dal `claude/adet-kg-tahsis-uretim`), Y-A1 raporu (dal
`claude/ya1-karar-dizini`) ve rol–yetki taslağı v1.1 (dal `claude/w1-rol-taslagi`, §5 Y-01–Y-05); ikinci soru turu adet/kg golden yazarı (PR #1204 N2), iş kutusu W1-A ve komut paleti W1-B raporlarından gelir. "(Önerilen)"
işaretleri soranındır. Bu dosya bir **ajan aktarımıdır**; Issue #1189 kaydı "SINAN-CEVAP — ajan aktarımı" olarak yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Not |
|---|---|---|---|
| E-01 | Stok etiket aramasından seçilen adetli etikette adet bilgisi nasıl gelsin? | **Arama adedi versin (Önerilen)** | Seçilmeyen: "İsteğe bağlı kalsın". |
| E-02 | Adet tutan tahsis başka etikete taşınırken adet zorunlu mu? | **Evet, adet zorunlu (Önerilen)** | Seçilmeyen: "Hayır, adet taşınmaz". |
| E-03 | Kg ile satılan sipariş satırı adetli etiketten nasıl sevk edilsin? | **Sevk eden adedi girer (Önerilen)** | Seçilmeyen: "Sevk edilmesin". |
| E-04 | Yapay zekâ okuma aracında "bekleyen sevkiyat" sayısına hangi kayıtlar girsin? | **Taslak + yayımlanmış talimat (Önerilen)** | Seçilmeyenler: "Yalnız yayımlanmış talimat"; "Talimat + onaysız irsaliye". |
| E-05 | `ai.decisions.read` hangi rollere önerilsin? | **Tüm kullanıcı rolleri (Önerilen)** | Seçilmeyen: "Yalnız müdürler + GM + Sistem Yöneticisi". |
| E-06 | Satış marjını (`sales.orders.margin.read`) kim görsün? | **GM + Satış Müdürü** | Seçilmeyen: "Yalnız Genel Müdür (Önerilen)". |
| E-07 | Kritik politika yayımı (`settings.policies.publish-critical`) kimde olsun? | **Sistem Yöneticisi** | Seçilmeyen: "Genel Müdür (Önerilen)". |
| E-08 | Sistem Yöneticisi'nin acil durum yetkisi nasıl modellensin? | **Süreli, gerekçeli atama (Önerilen)** | Seçilmeyen: "Kalıcı yedek yetki". |
| E-09 | Belge düzeyi kg fiyatında (N2) belge altı iskonto, vade farkı, kg'siz hizmet ve iade satırları nasıl girsin? | **Net mal tutarı / net kg (Önerilen)** | Seçilmeyenler: "Vade farkı da dahil"; "Belge düzeyi gösterilmesin". |
| E-10 | İş kutusu onay süreleri iş gününü sayarken resmî tatilleri nasıl dikkate alsın? | **Ayarlar'da tatil listesi (Önerilen)** | Seçilmeyen: "Yalnız hafta sonu". |
| E-11 | Bir kayıtta birden çok "hazırlayan" varsa onayı kim verebilir? | Serbest metin: "Onayı yetkisi olanlar verebilir" | Seçenekler: "Hazırlayanların hiçbiri (Önerilen)"; "Yalnız son hazırlayan dışlanır". |
| E-12 | Komut paletindeki "son açılan 10 ekran" listesi nerede saklansın? | **Sekme kapanınca unutulsun (Önerilen)** | Seçilmeyen: "Tarayıcıda kalıcı saklansın". |

## Kararlar

| No | Konu | Karar |
|---|---|---|
| SAT-D462 | Etiket aramasında adet | Stok pozisyon/etiket araması adetli etiketin kalan adedini döner; adetli etiketten yapılan seçimde adet zorunludur. Ekran adedi önerilen değerle doldurur, kullanıcı düzeltebilir; adetsiz istek Stok'ta reddedilir. |
| SAT-D463 | Adetli tahsisin taşınması | Adet tutan tahsis başka etikete taşınırken kg ile birlikte adet zorunludur; adetsiz taşıma reddedilir. |
| SAT-D464 | Kg satırının adetli etiketten sevki | Kg ile satılan sipariş satırı adetli etiketten sevk edilebilir; yükleme/sevk sırasında etiketten çıkan adet zorunlu girilir, stok adedi düşer, satış satırı kg olarak kalır. |
| AI-D35 | "Bekleyen sevkiyat" tanımı | Yapay zekâ okuma aracında bekleyen sevkiyat; taslak veya yayımlanmış olup kesin sevke dönmemiş yükleme talimatlarıdır. İrsaliye durumu sayılmaz. `ASSUMPTION-AI-PENDING-SHIPMENT-1` bu kararla kapanır. |
| AI-D36 | `ai.decisions.read` önerisi | İzin bütün kullanıcı rollerine önerilir (ROL-D16 ile tutarlı). Grant ataması insan kapısıdır; kişi adları dizinde maskelenir (AI-D34). |
| ROL-D21 | Satış marjı görünürlüğü | `sales.orders.margin.read` Genel Müdür ve Satış Müdürü'ne önerilir. SAT-D250 ve SAT-D276 (2)'deki "GM + Admin" görünürlüğünün yerine geçer; Sistem Yöneticisi marjı görmez (ROL-D6). |
| ROL-D22 | Kritik politika yayımı | `settings.policies.publish-critical` Sistem Yöneticisi'ne önerilir. |
| ROL-D23 | Sistem Yöneticisi acil erişimi | Acil durum yetkisi kalıcı iş izni değildir: süreli ve gerekçeli erişim ataması olarak modellenir, başka bir yetkili onaylar, denetim kaydına yazılır, süre bitince kalkar. |
| SAT-D465 | Belge düzeyi kg fiyatı (N2) | Belge düzeyi kg fiyatı = net mal tutarı / net kg. Belge altı iskonto mal satırlarına tutarla oransal dağıtılıp düşülür; vade farkı ve kg'siz hizmet satırları dahil edilmez; iade satırlarının tutarı ve kg'si düşülür. SAT-D431 gereği bağımsız golden onaylanmadan gösterim açılmaz. |
| YOL-D22 | Onay süresinde resmî tatil | İş günü hesabı şirket bazında Ayarlar'dan girilen yıllık resmî tatil/arife listesini düşer; liste yoksa yalnız hafta sonu düşülür ve ekranda "tatil takvimi tanımsız" uyarısı görünür. |
| YOL-D23 | Çok hazırlayanlı kayıtta onay | Onay ölçütü onay iznidir: ilgili onay iznine sahip kullanıcı onaylayabilir. Mevcut maker ≠ checker kuralları (ROL-D14; kaydı hazırlayıp onaya gönderen kendi kaydını onaylayamaz) sürer; bunun dışındaki katkı verenler için ek dışlama yoktur. |
| YOL-D24 | Komut paleti son ekranlar | "Son açılan 10 ekran" yalnız sekme oturumunda tutulur, sekme kapanınca unutulur; tarayıcıda kalıcı saklanmaz (LF-11). |

## Taslağa doğrudan yansıyanlar (yeni karar değil)

- Rol taslağı Y-02: `settings.sales-discount-limits.manage` Genel Müdür + Sistem Yöneticisi — ROL-D19'un sonucudur.
- Rol taslağı Y-05: Ayrı Kantar rolü isteğe bağlı rol olarak kalır — ROL-D14'ün sonucudur.
- AK-4b raporundaki "Finans'ın henüz cevaplamadığı kesinleşmiş fatura kg fiyatına girer mi" sorusu SAT-D452 ve SAT-D457
  ile kararlıdır: kesinleşmiş fatura girer, yalnız Finans'ın reddettiği fatura girmez.

## Sınırlar

- Bu dosya rol veya izin atamaz, seed'i değiştirmez; atama ve seed değişikliği insan kapısıdır.
- SAT-D462–SAT-D464 adet/kg golden kapısının (SAT-D431) yerine geçmez; uygulama dilimi kapalı yolları bu kararlara göre
  açar ve testle sabitler.
- İnsan kapıları değişmez: deployment, environment, secret, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma.
