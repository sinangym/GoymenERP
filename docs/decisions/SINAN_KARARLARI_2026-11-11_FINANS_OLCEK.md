# Sinan Kararları — 2026-11-11 (Finans ödeme/makbuz dağıtımı tutar ölçeği)

Bu belge önceki karar dosyalarını silmez; FNS-D16'yı ekler. FNS-D1…FNS-D15 geçerlidir. FNS-D16, ödeme dağıtımı (API-20, F3a)
ve makbuz dağıtımı (F-03) tutarının ondalık ölçeğini sınırlar; yeni bir para/formül politikası seçmez.

**Kaynak:** Sinan'ın 11 Kasım 2026'da koordinatör oturumunda (`AskUserQuestion`) verdiği cevap; Issue #66 yorumu
`6794310745` (`SINAN-KARARI`, madde 2), Finans şeridinin `NEEDS-SINAN` sorusuna (`6794251775`) karşılık. Soru: ödeme
dağıtımında dağıtım tutarı, ödemenin veya açık kalemin bakiyesinden daha fazla ondalık basamak taşıyabilir mi (ör. 2 ondalık
bakiyeye `100.000`)? Önerilen (soranın işareti): reddet; makbuz yolunda aynı kural.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Dağıtım tutarı bakiyelerden fazla ondalık basamak taşıyabilir mi? Şu an kabul ediliyor ve saklanan bakiye metinlerinin ölçeği büyüyebiliyor. | Metin (`SINAN-KARARI`): "ödeme ve makbuz yolu aynı kuralı uygular; dağıtım tutarı kaynağın (ödeme/makbuz) kendi ondalık ölçeğinden fazla olamaz. Ayrıntı (kalem ölçeğinin büyümesi, max/min) Finans şeridince karar dosyasına yazılıp ayrı PR'da uygulanır; belirsiz nokta kalırsa NEEDS-SINAN." | Kabul edilmeyen davranış: fazla ondalıklı tutar kabul edilip bakiye ölçeği büyümesi. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D16 | Dağıtım tutarı ölçeği | Ödeme (API-20) ve makbuz dağıtımı **aynı kuralı** uygular: dağıtım tutarının ondalık basamak sayısı kaynağın (ödeme/makbuz) **özgün tutar metnindeki** basamak sayısını aşamaz; aşarsa `finance_allocation_amount_invalid` (alan `amount`). Yuvarlama yoktur; tutar reddedilir. | `FinanceAllocationPolicy.CreatePayment` / `Create`; Finans dağıtım API'leri (API-20) |
| FNS-D16a | Kalem ölçeği büyümesi (uygulama ayrıntısı, geri alınabilir) | Sinan'ın "kalem ölçeğinin büyümesi" ayrıntısını Finans şeridine bıraktığı noktada, **saklanan hiçbir bakiye metninin ölçeği büyümez**: tutar ayrıca açık kalemin kalan tutar metnindeki basamak sayısını da aşamazsa kabul edilir (`max` değil, kalem ölçeği üst sınırdır). Hata kodu aynıdır. Bu yalnız reddeder, kabul alanını daraltır; geri alınması kuralı gevşetir ve veri değiştirmez. | Aynı politika; mevcut `Apply`/`Create` |
| FNS-D16b | Sıra | Ölçek kontrolü **aşım kontrollerinden sonra** çalışır: kaynağı veya kalemi aşan tutar kendi özel kodunu (`finance_allocation_exceeds_source` / `_open_item`) korur. Mevcut kayıtlar ve bakiyeler değişmez; kural yalnız yeni dağıtımlara uygulanır, serbest bırakma (release) etkilenmez. | Politika sırası; iptal serbest bırakması |

## Sınırlar

- Mevcut saklı bakiye metinlerinin yeniden biçimlendirilmesi, geriye dönük düzeltme veya migration bu kararın kapsamı
  değildir; gerekirse ayrı `NEEDS-SINAN`.
- Para birimi ölçeği (ör. TL için 2 ondalık) dayatması bu kararda yoktur; yalnız dağıtım tutarı kaynağın kendi ölçeğiyle
  sınırlanır.
- Deployment, environment, secret, Settings'te etkinleştirme, grant/rol/Critical, production aktivasyonu ve `OPEN-*`
  kapatma kapıları değişmez.
