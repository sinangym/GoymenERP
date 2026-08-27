# ERP-000C Uygulama Teslim Raporu

- Tarih: 2026-08-27
- Dal: `codex/erp-000c-identity-authorization`
- Taban: `main` / `518052c1fbad0486ee3aaea08589bca6099c1011`
- Durum: Kabul edildi ve `main`e squash merge edildi
- Merge commit: `fe87fc4f995a4dc4fb2e38b1399e96c95a2d09cd`

## Tamamlanan kapsam

- Identity Domain/Application/Infrastructure modül sınırları.
- SQL Server `identity` şeması ve ayrı EF migrations history.
- Yerel kullanıcı, dış `(issuer, subject)` bağlantısı, rol, permission, süreli atama ve
  Company/Site/Location/WorkCenter kapsamı.
- Sağlayıcıdan bağımsız OIDC/JWT bearer yapılandırması.
- Yalnız Development/Testing ortamında çalışabilen bootstrap kimliği.
- Dış claim enjeksiyonuna karşı yerel ERP claim temizleme ve yeniden üretme.
- Backend permission policy’leri ve varsayılan deny.
- Organization liste, toplam, doğrudan ID ve command yollarında ABAC filtreleri.
- Kritik yetki değişikliği için ikinci kişi onayı/görev ayrılığı domain modeli.
- `/api/v1/identity/me` ile yerel kimlik ve yetkili şirket görünümü.
- Identity migration script üretimini CI backend kapısına ekleme.

## Güvenlik kabulleri

- Anonim istek reddedilir.
- Kimliği doğrulanmış ancak ERP’de provision edilmemiş subject reddedilir.
- Token içinden gönderilen sahte `goymen:user_id/company_access` claim’leri yetki vermez.
- A şirketi kullanıcısı B şirketini header, URL, ID veya toplam üzerinden öğrenemez.
- Tesis kapsamlı kullanıcı yalnız o tesisin 8 lokasyonunu ve 5 iş merkezini görür.
- Salt-okuma rolü location active-state command’ını çalıştıramaz.
- Bilinmeyen permission hiçbir koşulda yetki üretmez.
- Kritik yetki talebini talep eden veya hedef kullanıcı onaylayamaz.
- SQL Server, yanlış şirketi `Company` scope olarak eklemeyi check constraint ile reddeder.

## Doğrulama sonucu

- Debug build: 0 uyarı, 0 hata.
- Organization unit: 10/10 geçti.
- Identity unit: 6/6 geçti.
- HTTP/claims integration: 15/15 geçti.
- Gerçek Docker SQL Server relational acceptance: 1/1 geçti.
- Identity migration + seed, SQL scope constraint, composite tenant FK, HTTP 404/403,
  kritik yetki self-approval constraint’i, RBAC write reddi ve ABAC liste filtresi aynı
  relational testte doğrulandı.
- NuGet ve npm vulnerability taraması: bilinen açık yok.
- Frontend lint 0 hata; frontend test 2/2; production build geçti.
- Docker Compose model doğrulaması ile API/web image build geçti.

Tüm yerel kapılar `Release` yapılandırmasıyla alındı. Uzak GitHub CI aynı testleri temiz
Linux runner ve service SQL Server üzerinde tekrar çalıştırmalıdır.

## Bilinçli kapsam dışı

- Belirli production IdP, tenant/app registration, MFA ve dış erişim: `OPEN-013`.
- Kullanıcı/rol yönetim UI ve public mutation endpoint’leri.
- AI ile yetki taslağı ve onay akışının uygulama katmanı.
- Kalıcı audit/correlation: ERP-000D.
- Kalıcı idempotency/concurrency: ERP-000E.
- Outbox/inbox: ERP-000F.

Bu ayrım nedeniyle ERP-000C’de yeni kritik public write endpoint’i açılmadı; görev
ayrılığı talep modeli ve tabloları sonraki idempotent command katmanı için hazırlandı.
