# Codex İlk Görev Promptu

Aşağıdaki metni yeni `GoymenERP` repository’sinde Codex’e ver:

---

Göymen ERP için `ERP-000A + ERP-000B` görevlerini uygula.

Önce repository kökündeki `AGENTS.md`, ardından `docs/decisions/KARAR_SICILI.md`, `docs/architecture/MIMARI_KARARLAR.md` ve `docs/organization/SIRKET_AYRISTIRMA_SOZLESMESI.md` dosyalarını oku. Açık işletme kararlarını tahmin ederek hard-code etme.

## Amaç

Yeni bağımsız repository’de production-quality başlangıç iskeleti ve çok şirket çekirdeği oluştur.

## Kapsam

1. .NET 10 solution ve modüler monolit proje yapısı
2. React + TypeScript web uygulaması
3. `Platform.Organization`, `Platform.Identity`, `Platform.Audit`, `Platform.Documents`, `Platform.Messaging` modül sınırları
4. SQL Server EF Core başlangıcı; connection string secret/env üzerinden
5. `Company`, `Site`, `Location`, `WorkCenter` temel varlıkları
6. Request-scoped `CompanyContext`
7. Bütün şirket kapsamlı kayıtlar için zorunlu `CompanyId`
8. Backend authorization filtresi ve şirket değiştirme sözleşmesi
9. Osmaniye/Gaziantep/harici depo seed dosyasını okuyabilen seed mekanizması; hukuki şirketler placeholder kalabilir
10. Unit ve integration testleri: başka CompanyId verisine query/command erişimi reddedilir
11. Docker/dev ortamı, `.env.example`, build/test komutları ve README
12. CI için ilk build/test workflow tanımı

## Kapsam dışı

- Satış, stok, üretim, finans iş tabloları
- Gerçek SSO entegrasyonu
- Gerçek AI model çağrısı
- Gerçek e-fatura/Mikro entegrasyonu
- Açık politika değerlerini sabitleme

## Kalite

- Her command CompanyId, actor, correlation ve idempotency sözleşmesine hazır olmalı.
- Kod isimleri İngilizce, UI metinleri Türkçe/localizable.
- Magic CompanyId/role/tolerance yok.
- Testler temiz ortamda çalışmalı.
- Yapılan mimari seçimleri `docs/adr/ADR-001-repository-structure.md` içinde kaydet.

## Teslim

Değişen dosyaları, çalıştırılan testleri, açık riskleri ve bir sonraki küçük görev önerisini özetle. Büyük bir sonraki modülü başlatma.

---
