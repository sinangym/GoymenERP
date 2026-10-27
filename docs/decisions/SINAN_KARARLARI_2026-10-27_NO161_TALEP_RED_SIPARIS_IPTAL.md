# Sinan Kararları — 2026-10-27 (No.161 talep reddi ve sipariş kalanı iptali)

Bu belge No.161 için yalnız iki geçiş şeklini karara bağlar. No.160 kaynağını kabul etmez,
No.161 implementasyonunu başlatmaz ve mevcut runtime ya da test kanıtının yerine geçmez.

**Kaynak:** Sinan'ın Main thread'deki doğrudan kullanıcı cevabı. Koordinatör cevabı API'den
`userMessage` olarak okudu; sessizlik, ajan aktarımı veya çıkarım değildir.

- Thread: `01a15955-c417-7300-8184-85f09a9d2aeb`
- Turn: `01a17a00-2f74-7023-9c2a-bcb4b723960f`
- User message: `01a17a04-50e0-7da0-8809-6a9e7be5fcba` (`items[21]`)
- Question item: `["request_user_input_async","call_bbd5992616124457b38ef3ae876f83ad",0]`
- Önceki soru kaydı: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6426595770
- Kayıt tarihi: 27 Ekim 2026, Europe/Istanbul. API nesnesinde bulunmayan kesin cevap saati
  türetilmemiştir.

Soru ve cevap kelimesi kelimesine:

> **Soru:** No161 için şu kapsamı kabul ediyor musunuz: Talep yalnız Submitted veya Approved
> durumunda ve bağlı Approved sipariş kalmadığında reddedilsin; onaylı siparişte kısmi kabul
> yapılmış olsa bile yalnız henüz kabul edilmemiş kalanın tamamı iptal edilsin? Gerekçe/audit,
> kaynak bağının korunması ve fiziksel stok/para olayı üretmeme zaten mevcut karardır. Eksik olan
> bu iki geçiş kapsamıdır.
>
> **Cevap:** Bu kapsamı kabul ediyorum

## Kararlar

1. **Talebin reddi:** Talep yalnız `Submitted` veya `Approved` durumundayken reddedilebilir.
   Talebe bağlı `Approved` satın alma siparişi kaldığı sürece ret işlemi reddedilir; önce bağlı onaylı
   sipariş iptal edilmelidir.
2. **Onaylı siparişin iptali:** Onaylı satın alma siparişinde kısmi kabul yapılmış olsa bile
   yalnız henüz kabul edilmemiş kalanın **tamamı** iptal edilir. Bu karar yeni bir satır bazlı veya
   miktar bazlı kısmi iptal modu seçmez.

## Değişmeyen sınırlar

- Ret ve iptal gerekçe ile audit kaydı ister; orijinal talep/sipariş kaynak bağları korunur.
- İptal edilen kalan yalnız gelecekteki arzdan ve sipariş kalanından çıkarılır. Fiziksel stok veya
  para olayı üretilmez.
- Master `INV-006` alış siparişi kalan formülü değişmez:
  `OnaylıSipariş − İptal − KabulNet − KesinRed/İadeEtkisi`. Bu kayıt yalnız No.161 kapsamında
  hangi kabul edilmemiş kalanın `İptal` etkisine gireceğini sınırlar.
- Bu cevap rol, action, permission veya grant tanımlamaz; yeni politika seçmez, `OPEN-*` kapatmaz
  ve hiçbir özelliği etkinleştirmez.
- No.160 source24 kabul edilmiş `main` kaynağı değildir. No.160/No.161 implementasyonu ile unit,
  gerçek SQL, browser, CI ve kullanıcı kabulü ayrı kapılardır; bu karar bunların hiçbirini geçmiş
  saymaz. Formal kabul sayısı `18/128` olarak değişmeden kalır.
