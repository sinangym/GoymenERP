# Sinan Kararları — 2026-11-16 (Dalga 5 ek 26: W5-5 cevapları ve tren-1 devri)

Bu belge önceki karar dosyalarını silmez; ALS-D117, FNS-D110, SAT-D420 ve GOV-D11'i ekler.
ALS-D117, EK25'teki ALS-D116'nın Critical sınıfı sorusunu; FNS-D110, FNS-D108'in düzeltme
izinlerini; SAT-D420, SAT-D419'un eşleme ayarı yetkisini tamamlar. GOV-D11, kalan kodun aynı
trende devrini kaydeder. Bu noktalara değinilmeyen önceki kararlar aynen geçerlidir.

**Kaynak:** Sinan'ın 16 Kasım 2026 tarihli `SINAN-CEVAP W5-5` kaydı:
https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6920278065 .
Kaynak, cevapların oturum içindeki soru penceresiyle verildiğini söyler. "(Önerilen)"
işaretleri soranındır. Aşağıdaki cevap ve açıklamalar kaynak yorumdan aynen aktarılmıştır.
Seçilmeyen seçenekler kaynakta verilen adlarıyla korunmuştur; kaynakta bulunmayan seçenek
açıklamaları eklenmemiştir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| W5-5.1 | ALS-D116 fazla teslim istisnası onay izni Critical mi | "Critical değil (Önerilen)" | diğer 12 izinle aynı muamele; biri girer başka biri onaylar. (Diğer: Critical.) |
| W5-5.2 | FNS-D108 kapalı dönem sonrası açılış kredisi düzeltmesinin giriş/onay izinleri | "Mevcut kredi izinleri (Önerilen)" | girişi finance.credits.propose, onayı finance.credits.approve sahibi yapar (giren onaylayamaz); yeni izin açılmaz. (Diğer: ayrı düzeltme izinleri.) |
| W5-5.3 | SAT-D419 SMM hesap eşleme ayarını kim değiştirir | "Mevcut finans eşleme izni (Önerilen)" | diğer finans hesap eşleme ayarlarını yöneten mevcut izin sahibi; değişiklik denetim kaydına yazılır. (Diğer: yeni ayrı izin; yalnız yönetici.) |
| W5-5.4 | Süreç: Claude haftalık kullanım sınırı nedeniyle kalan kod işini Codex devralır | "Aynı tren: claude/tren-1 (Önerilen)" | Codex aynı entegrasyon dalında devam eder; main'e en sonda tek K3 PR. (Diğer: ayrı dal codex/tren-2.) Rol/grant ataması yapılmadı; insan kapıları değişmez. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| ALS-D117 | Fazla teslim istisnası onay izninin sınıfı (W5-5.1; ALS-D116'yı tamamlar) | ALS-D116'daki yeni ayrı izin **Critical değildir**. Biri girer, başka biri onaylar. İzin sipariş onayından bağımsız kalır; grant veya rol ataması yapılmaz. | Alış fazla teslim onayı, izin kaydı |
| FNS-D110 | Kapalı dönem sonrası açılış kredisi düzeltme izinleri (W5-5.2; FNS-D108'i tamamlar) | Giriş **`finance.credits.propose`**, onay **`finance.credits.approve`** izni sahibince yapılır. **Giren onaylayamaz. Yeni izin açılmaz.** FNS-D108'in kapalı dönemi açmadan açık dönemde ters kayıt + doğru kayıt yolu ve FNS-D109'un approve Critical / propose Critical değil sınıflaması aynen geçerlidir. | Açılış kredisi düzeltmesi, Finans kredi komutları |
| SAT-D420 | SMM hesap eşleme ayarının yetkisi (W5-5.3; SAT-D419'u tamamlar) | Eşlemeyi diğer Finans hesap eşleme ayarlarını yöneten **mevcut izin sahibi** değiştirir. Değişiklik **denetim kaydına** yazılır. Yeni ayrı izin veya rol seçilmez. SAT-D419'un ticari mal 621 / mamul 620 varsayılanı aynen geçerlidir. | Finans eşleme ayarı, SMM farkı fiş önerisi |
| GOV-D11 | Tren-1 kod geliştirme devri (W5-5.4) | Claude şeridinin kalan kod işini **Codex aynı `claude/tren-1` entegrasyon dalında** sürdürür. **Main'e en sonda tek K3 PR** açılır. Rol/grant ataması yapılmaz; insan kapıları değişmez. | Tren-1 geliştirme ve kapanış akışı |

## Sınırlar

- Bu belge yalnız kaynak yorumdaki dört cevabı kaydeder. Başka açık soruya cevap vermez.
- ALS-D116'nın kesin izin adının uygulama ayrıntısı olması değişmez. Yeni izne grant veya
  rol ataması bu belgeyle yapılmaz.
- SAT-D420, mevcut Finans eşleme izninin teknik adını kaynakta bulunmadığı için uydurmaz.
  Kodlayan şerit mevcut sözleşmeyi doğrular; karşılığı yoksa `NEEDS-SINAN` yazar.
- Kredi alanı spec taslağının onayı FNS-D106 uyarınca Sinan'dadır. Bu belge kredi gruplarının
  alanlarını veya hareket politikasını seçmez; Finance STOP sınırını değiştirmez.
- GOV-D11 son K3 PR'ını açma akışını kaydeder; kendi kendini inceleme veya merge yetkisi
  vermez. K3'ün bağımsız exact-head review ve kabul kapıları korunur.
- Hiçbir `OPEN-*` kaydı kapanmaz. Deployment, environment, secret, Settings etkinleştirmesi,
  grant/rol, yeni Critical kararı, production aktivasyonu ve `.claude/settings.json` insan
  kapılarında kalır.
