# Sinan Kararları — 2026-10-22 (geliştirme dalgaları ve ağır doğrulama)

Bu belge 21 Ekim kararlarını silmez veya yeniden yorumlamaz. Aşağıdaki üç daha yeni süreç
kararının kanonik ekidir:

- [Issue #66 comment 6340316262 — koordinatör merge yetkisi](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6340316262)
- [Issue #66 comment 6342711040 — bütün dalgalar açık](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342711040)
- [Issue #66 comment 6342843156 — gece slotu kaldırıldı](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342843156)

Kaynakların üçü de `sinangym` hesabından 22 Ekim 2026 tarihinde yayımlanmıştır.

## 0. Governance ve karar PR'ı merge sınırı

- `AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `.claude/**` ve `.github/**` kalite
  kapısı değişikliklerini koordinatör merge edebilir; Sinan'ın tıklaması veya `MERGE-OK`
  makbuzu aranmaz. Bu yetki otomatik merge değildir.
- **Dar istisna — `.claude/settings.json`:** 2026-10-22 doğrudan kullanıcı eki r2 §0/E7
  uyarınca bu dosyayı değiştiren PR, aşağıdaki review kapılarına ek olarak Sinan'ın Issue
  #66'da vereceği açık tek satırlık onayı gerektirir. Genel `.claude/**` koordinatör merge
  yetkisi bu özel dosya için insan onayının yerine geçmez.
- Yazar koordinatör olamaz. Aday exact head'de iki bağımsız review ve açık P0/P1 bulunmaması
  zorunludur.
- Yalnız doküman/governance/karar/ayar dosyası değiştiren PR'da CI koşumu zorunlu değildir;
  çalışmamış veya eksik CI PASS sayılmaz.
- `.github/**` değişikliğinde iki review ayrıca bütün `runs-on` değerlerinin self-hosted
  kaldığını; hosted fallback ile artifact/cache/Packages/secret/environment/deploy yüzeyi
  açılmadığını doğrular.
- Merge sonrası Issue #66'ya PR, merge SHA ve iki review bağlantısını içeren makbuzu
  koordinatör yazar. Sinan bu yetkiyi tek satırla geri alabilir.

## 1. Geliştirme kapsamı

1. İlk sürümün mevcut 0–G geliştirme dalgalarının tamamı açıktır. Bağımlılık sırası, tek
   yazar/dosya sahipliği, source-first hazırlık ve kabul kapıları yürürlükte kalır.
2. Finance STOP geliştirme engeli kaldırılmıştır. Dalga D; F07–F10 ve EK-39…42 için gerçek
   SQL, API, OpenAPI ve kullanıcı ekranı geliştirme kapsamını açar. Bu açılış herhangi bir
   finans formülü, tutar, eşik, provider veya production davranışı uydurma yetkisi vermez.
3. AI geliştirme sırası W05.1 → W05.2 → W06 olarak açıktır. AI yalnız dar, şemalı backend
   araçlarını kullanır; ham SQL/DB erişimi, kendi kendine yetki/onay, yeni model veya sağlayıcı
   aktivasyonu bu karardan çıkmaz.
4. `DEC-GOV-007` ile ertelenen modüller ve bütün `OPEN-*` alt kararları kendi durumlarını
   korur. “Bütün dalgalar açık” ifadesi, ilk sürüm dışı kapsamı ilk sürüme eklemez.

## 2. Ağır doğrulama çalışma kuralı

- SQL, browser, full ve container doğrulamaları saat bağımsız yürütülebilir; 22:00–07:00
  bekleme penceresi yoktur.
- Aynı anda yalnız mevcut tek ağır-iş kilidi kullanılır. Her denemeden hemen önce en az
  2 GiB boş RAM yeniden ölçülür; eşik yoksa koşu başlamaz ve ret nedeni makbuza yazılır.
- Aynı koordinasyon döngüsünde en fazla üç sınırlı deneme yapılır. Denemeler arasında kalıcı
  watcher, scheduler, servis, config veya yeni oturum kurulmaz. Üçüncü ret/başarısızlıktan
  sonra sonuç dürüstçe BLOCKED/NOT_RUN kalır ve sonraki mevcut koordinatör döngüsüne devredilir.
- Kaynak, test seçimi ve kapılar denemeler arasında sessizce değiştirilmez; başarısız veya
  başlamamış koşu PASS sayılmaz.

## 3. Değişmeyen insan kapıları

Production aktivasyonu, deployment, environment, secret, gerçek provider, gerçek grant/rol,
`Critical` sınıflaması, Settings → Actions workflow aktivasyonu, bütçe/ücretli hizmet ve
`OPEN-*` kapatma somut kullanıcı kararında kalır. `.claude/settings.json` PR'ı
Sinan'ın Issue #66'da vereceği açık tek satırlık onayı gerektirir; `config.toml`, Power modu
ve Settings → Actions aktivasyonu da kullanıcı kapısında kalır. Bu belge permission adı, persona, model, tool,
işletme formülü, tolerans, tutar veya saklama politikası üretmez.

## 4. Sicil ve açık karar etkisi

- Yeni `DEC-*` kimliği açılmaz. Bu iki süreç kararı mevcut `DEC-GOV-004` ve `DEC-TST-002`
  kayıtlarına kaynak/kabul eki olarak bağlanır.
- `KARAR_SICILI.md` ile `KARAR_SICILI.csv` aynı kimlikleri ve aynı satır sayısını korur.
- `ACIK_KARARLAR.md` içindeki hiçbir durum bu belgeyle değişmez. Finance ve AI geliştirme
  dalgalarının açılması, açık işletme kararlarının kapandığı veya runtime'ın hazır olduğu
  anlamına gelmez.

## 5. Kanıt sınırı

Bu doküman kaynak kanıtıdır. SQL/API/UI/AI runtime, test, browser, CI, provider veya
production kabulü değildir. Her geliştirme dilimi kendi source, test, gerçek SQL/browser,
CI ve bağımsız review kanıtıyla kapanır.
