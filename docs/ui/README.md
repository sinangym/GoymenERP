# Göymen ERP UI referans kataloğu

Bu dizin, daha önce kararlaştırılmış Göymen ERP ekranlarını yeniden tasarlamak için değil; dağınık görsel, prototip ve karar kaynaklarını izlenebilir bir geliştirme girdisine dönüştürmek için oluşturulmuştur.

Katalog `main` dalındaki `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` dosyasını değiştirmez. Görsel veya HTML prototip; yetki, CompanyId izolasyonu, hesap, formül, audit, onay ya da iş kuralı kaynağı değildir. Çelişki halinde repo karar hiyerarşisi uygulanır.

## Başlangıç durumu

- Katalog tabanı: `main@908bbfd5083debfabbe660d45b7ec99fa6d1b9d6`
- Envanter tarihi: 27 Ağustos 2026
- Repo görünürlüğü doğrulaması: `PRIVATE`
- Kod kapsamı: yok; bu paket yalnız `docs/ui` içeriğidir.
- ERP-000E ve ERP-000F kapsamı: bu dala karıştırılmamıştır.

## Klasörler

| Yol | Amaç |
|---|---|
| `UI_DESIGN_SYSTEM.md` | Ortak kabuk, renk, yoğunluk, AI, responsive ve durum standardı |
| `SCREEN_CATALOG.md` | İnsan tarafından okunabilir ekran ve kanıt özeti |
| `SCREEN_CATALOG.csv` | Kaynak ekran–kanonik ekran uzlaştırma kayıtları |
| `CANONICAL_SCREEN_STATUS.csv` | Repo Master’daki 159 kanonik ekranın referans kapsama durumu |
| `SOURCE_RECONCILIATION.md` | Kimlik, sürüm, kilit ve kaynak çatışmaları |
| `SOURCE_MANIFEST.csv` | Alınan veya bilinçli olarak alınmayan ana kaynakların hash kaydı |
| `ASSET_MANIFEST.csv` | Repo içine taşınan her görsel, prototip ve kaynak dosyanın hash kaydı |
| `screens/` | Kaynak ekran başına makine-okunur ekran kartı |
| `assets/original/` | Dönüştürülmeden alınmış ekran görselleri |
| `prototypes/` | Salt referans olarak saklanan interaktif HTML’ler |
| `sources/` | UI’ye doğrudan dayanak olan seçilmiş PDF, DOCX ve Markdown kaynakları |

## Statü sözlüğü

| Statü | Anlamı |
|---|---|
| `LOCKED_VISUAL` | Kaynak belgesinde kilitli olduğu açıkça yazan ve görseli bulunan ekran |
| `DESIGNED_FINAL_QA` | Tasarlanmış; küçük alan/kolon düzeltmeleri için kullanıcı kabulü gereken ekran |
| `DESIGNED_NOT_LOCKED` | Görsel veya prototip mevcut; kaynak statüsü henüz kilit değil |
| `DECISION_ONLY_VISUAL_MISSING` | İşlev/UI kararları var; özgün ekran görseli bulunamadı |
| `PROTOTYPE_REFERENCE` | HTML’de çalışan referans var; production uygulaması değildir |
| `HISTORICAL_REFERENCE` | Daha güncel kaynağı geçersiz kılamayan tarihsel referans |
| `UNMAPPED_LEGACY` | Kaynak kimliği var; kanonik kimliğe güvenli eşleme henüz yok |

Eşleme statüleri `EXACT`, `SPLIT`, `MERGE`, `EMBEDDED_COMPONENT`, `UNRESOLVED` ve bunların henüz onaylanmamış `CANDIDATE_*` biçimleridir. `CANDIDATE_*` kayıtları kodlama yetkisi vermez.

## Kaynak kullanma kuralı

Bir ekran uygulanmadan önce aşağıdaki zincir birlikte okunmalıdır:

1. Repo Master’daki kanonik ekran ve değişmez ilkeler.
2. İlgili DEC/ADR/feature/acceptance/formül kaynağı.
3. Bu katalogdaki ekran kartı ve uzlaştırma durumu.
4. Hash’i kayıtlı özgün görsel veya prototip.

Kimlik veya iş kuralı çelişkisi varsa tahmin yapılmaz; `UNRESOLVED` bırakılır ve Karar Sicili/ADR ile çözülür.

## Prototip güvenlik sınırı

HTML prototipleri çevrimdışı ve self-contained olsa da gerçek backend, kimlik, yetki, CompanyId, audit, AI servisi veya kalıcı kayıt içermez. Bazı prototiplerde `innerHTML`, inline event handler ve sentetik şirket/iletişim/veri örnekleri vardır. Bunlar üretim React koduna kopyalanmaz; yalnız bilgi mimarisi ve etkileşim referansı olarak kullanılır.

Bu varlıklar “Şirket İçi Kullanım” içeriği taşıdığı için repo private kaldığı sürece korunur. Repo görünürlüğü public yapılmadan önce ayrı veri sınıflandırma ve anonimleştirme kontrolü zorunludur.

## Değişiklik yönetimi

- Özgün varlıklar yerinde düzenlenmez; yeni sürüm yeni dosya ve yeni hash ile eklenir.
- Bir kaynak başka bir kaynağın yerini alıyorsa karar/supersedes kaydı olmadan “daha yeni” diye üstün sayılmaz.
- Ekran kartındaki kanonik eşleme değişikliği review gerektirir.
- Eksik görsel ancak mevcut kararlarla oluşturulup kullanıcı onayı aldıktan sonra `LOCKED_VISUAL` olabilir.
- Kodlama PR’ı, referans dosyasını üretim davranışı sanmamalıdır; acceptance ve güvenlik kapıları ayrıca geçilir.
