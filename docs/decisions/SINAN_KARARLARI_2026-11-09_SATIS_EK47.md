# Sinan Kararları — 2026-11-09 (Satış: fatura dışa aktarma biçimleri ve kullanıcısı, tam sıfır son TL taksidi)

Bu belge önceki karar dosyalarını silmez; SAT-D236 ve SAT-D237'yi ekler. Satış faturası dışa aktarmasının (API-34)
biçimini ve kullanıcısını kayda geçirir ve EK46 "Sınırlar"da `NEEDS-SINAN` bırakılan tam sıfır son TL taksidi
sorusunu kapatır. SAT-D7, SAT-D85, SAT-D91 (EK13), SAT-D227 (EK43), SAT-D233 (EK46), DEC-FIN-003 ve DEC-INT-001
geçerlidir; bu belge onların metnini değiştirmez (SAT-D233'ün kapsamının genişlemesi için "Bilinçli değişiklikler"e
bakın).

**Kaynak:** Sinan'ın 9 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`)
Satış (Claude) şeridine sohbette verdiği iki cevap. Makbuzlar Issue #66'dadır:
[yorum 6741147691](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6741147691) (API-34) ve
[yorum 6741478944](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6741478944) (tam sıfır son taksit;
API-34'ün yeniden teyidi). Bu belgeyi yazan ajana Sinan'ın sohbet metinlerinin yalnız makbuzlardaki özeti ve
koordinatör özeti iletildi; aşağıda aynen alıntılanan tek ifade ikinci sorudaki devretme cümlesidir. Seçilmeyen
seçeneklerin adları ve açıklamaları iletilmediği için tabloda yazılmadı (uydurulmadı).

## Soru ve cevaplar

| # | Soru | Cevap (makbuzdaki biçimiyle) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Satış fatura dışa aktarması (API-34) hangi biçimlerde olsun, kim kullanacak? | "Satış fatura dışa aktarma her iki formatta olacak: Excel ve e-fatura XML (UBL-TR); kullanıcı muhasebe." (#66 yorum 6741147691; yorum 6741478944'te yeniden teyit) | API-34 iki biçim üretir: **Excel** ve **e-fatura XML (UBL-TR)**. Dışa aktarmayı kullanan/tüketen **Muhasebe**'dir. |
| 2 | Taksitli dövizli faturada son taksidin TL tutarı TL yuvarlaması yüzünden **tam sıfır** çıkarsa ne olsun? (EK46 "Sınırlar" `NEEDS-SINAN`) | Sinan seçimi Claude şeridine bıraktı: "Bu sorunun cevabını sen bul". Claude şeridinin seçimi: "0 da reddedilir, eksi taksitle aynı kod (422 sales_invoice_instalment_functional_invalid; SAT-D233 genişletmesi)" (#66 yorum 6741478944) | Tam sıfır son TL taksidi eksi taksitle aynı biçimde reddedilir. Bu seçim Sinan'ın açıkça devrettiği yetkiyle Claude şeridince yapılmıştır; gerekçesi SAT-D237 satırındadır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D236 | Satış fatura dışa aktarma biçimleri ve kullanıcısı (API-34) | (1) API-34 fatura dışa aktarması **iki biçimde** üretilir: **Excel** ve **e-fatura XML (UBL-TR)**. Bu, SAT-D91'in ilk iki biçimini API-34'ün uygulama kapsamı olarak teyit eder; SAT-D91'in UBL-TR sürüm notu (araştırmaya göre 1.2.1) ve Mikro içe aktarma şablonu maddesi (örnek şablon dosyası gelene kadar üretilmez) aynen geçerlidir. (2) Dışa aktarmanın kullanıcısı/tüketicisi **Muhasebe**'dir: dosya, resmî faturanın SAT-D7'ye göre dış programda (Mikro / e-Mikro portal) kesilmesi için Muhasebe'ye verilir. (3) Dışa aktarma **yalnız dosya üretir**; resmî bir dış bağlantı (GİB, entegratör, Mikro'ya doğrudan yazma) değildir. DEC-INT-001 ve DEC-FIN-003 aynen geçerlidir; e-belgenin gönderilmesi/kesilmesi ERP dışında ve insan elindedir. (4) Uygulama backend şeridindedir (Codex). | API-34 (`POST invoices/{id}/export`), `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-SAL-006 |
| SAT-D237 | Tam sıfır son TL taksidi (SAT-D233 genişletmesi) | SAT-D227'ye göre son taksite eklenen fark son taksidin TL tutarını **tam 0**'a düşürürse faturanın kesinleştirilmesi, eksi taksitte olduğu gibi **reddedilir**: aynı 422 `sales_invoice_instalment_functional_invalid`, hiçbir şey yazılmaz, Finans'a devir olmaz. Sıfır tutarlı taksit Finans'a taşınmaz, taksit sessizce düşürülmez veya önceki taksitle birleştirilmez. **Seçimi yapan:** Sinan'ın sohbette açıkça devrettiği yetkiyle ("Bu sorunun cevabını sen bul") Claude şeridi. **Gerekçe:** (a) taksit sayısı sipariş/fatura vade modelinin parçasıdır ve sessizce değişmemelidir; (b) Finans açık kalem kabulü yalnız **pozitif** tutar alır (SAT-D85: her taksit için bir açık kalem); (c) belirsiz durumda fail-closed kalınır; (d) yaygın ERP uygulamasında yuvarlama farkı son taksite yüklenir ve sıfır ya da eksi son taksit **geçersiz ödeme planı** sayılır. SAT-D227'nin TL hesaplama kuralı değişmez | Fatura kesinleştirme (API-36), `SalesInvoiceInstalmentCalculator`; DEC-SAL-008 |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D236'nın kapsam ayrıntıları kararlaştırılmamıştır ve uygulamada **yapılandırılabilir** bırakılır: hangi
  faturaların dışa aktarılabileceği (ör. yalnız onaylı taslak mı, kesinleşmiş fatura da mı), tarih aralığı veya
  toplu dışa aktarma, Excel sütunları ve sırası, dosya adlandırması. Bu noktalar tahminle kilitlenmez
  (aşağıda "Sınırlar").
- SAT-D237'de karşılaştırma TL kuruşa yuvarlanmış son taksit tutarı üzerinden yapılır (`<= 0` reddedilir); hata kodu
  ve Türkçe açıklama eksi taksitle aynıdır, ayrı bir kod açılmaz.

## Kapatılan açık sorular

- EK46 (`SINAN_KARARLARI_2026-11-09_SATIS_EK46.md`) "Sınırlar" — "Son taksidin TL tutarının **tam sıfır** çıkması …
  (`NEEDS-SINAN`)" (SAT-D237).
- API-34'ün biçim ve kullanıcı sorusu (SAT-D236). API-34'ün kapsam ayrıntıları açık kalır.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; yukarıdaki soruların `OPEN-NNN` kimliği yoktur.

## Bilinçli değişiklikler

- **SAT-D233'ün kapsamı:** EK46'da yalnız **eksi** son taksidi kapsayan red kuralı SAT-D237 ile **sıfır** son taksidi
  de kapsar. EK46'nın metni yerinde değiştirilmez.
- **SAT-D91:** metni değişmez; SAT-D236 onun Excel ve UBL-TR XML biçimlerini API-34'ün uygulama kapsamı olarak teyit
  eder ve kullanıcıyı (Muhasebe) ekler.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama takip listesi

| Karar | Açtığı kod/doküman işi | Şerit |
|---|---|---|
| SAT-D236 | API-34 `POST invoices/{id}/export`: Excel ve UBL-TR XML dosya üretimi, `sales.invoices.export` izninin manifest'e eklenmesi (role atanmaz), OpenAPI ve sözleşme testleri; kapsam ayrıntılarının (fatura durumu, tarih aralığı, Excel sütunları) yapılandırılabilir tutulması. Dosyanın Belge kasasına yazılması `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırındaki plana göre. Frontend tüketimi sonraki dilim. | Codex (backend); Claude (frontend, sonra) |
| SAT-D237 | `SalesInvoiceInstalmentCalculator`'da red koşulunun `< 0`'dan `<= 0`'a genişletilmesi (bugün sıfırı reddetmez); birim + gerçek-SQL kanıtı (son taksit tam 0 → 422, hiçbir şey yazılmaz); Sales README "SAT-D227 deviation" satırının SAT-D233/SAT-D237 ile güncellenmesi. Para/formül değişikliği: K3. | Satış (backend) |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration veya Ayarlar verisi değiştirmez; yukarıdaki
  takip işleri ayrı PR'lardır.
- SAT-D236'da hangi faturaların dışa aktarılacağı (ör. yalnız kesinleşmiş mi), tarih aralığı ve Excel sütunları
  Sinan'ın cevabında yoktur: yapılandırılabilir kalır, `NEEDS-SINAN`.
- SAT-D236 "kullanıcı muhasebe" ifadesi dışa aktarmanın tüketicisini belirtir; `sales.invoices.export` izninin hangi
  role verileceği ve grant'ın açılması **insan kapısıdır**; bu belge grant açmaz.
- SAT-D236 resmî dış bağlantı (GİB, entegratör, Mikro'ya doğrudan aktarım) açmaz; e-belgenin gönderilmesi insan
  kapısında ve ERP dışında kalır (DEC-INT-001, DEC-FIN-003).
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
