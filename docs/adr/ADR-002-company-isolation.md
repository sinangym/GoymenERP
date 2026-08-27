# ADR-002 — CompanyId izolasyonunun zorunlu ve katmanlı uygulanması

- Durum: Kabul
- Tarih: 2026-08-27
- Karar sahibi: Göymen ERP teknik ekip
- İlgili karar ID’leri: DEC-GOV-002, DEC-ORG-001, DEC-SEC-001

## Bağlam

A şirketine yetkili bir kullanıcı; B şirketinin verisini ID, URL, query parametresi,
command, hata içeriği veya toplam sayılar üzerinden öğrenememelidir. UI filtresi bu
güvenlik sınırını tek başına sağlayamaz.

## Seçenekler

1. Şirket filtresini yalnız istemcide uygulamak.
2. Her endpoint’te geliştiricinin elle `CompanyId` koşulu yazması.
3. Sunucu kimliği, request-scoped CompanyContext, EF global filtreleri, bileşik anahtarlar
   ve saldırı senaryosu testlerinden oluşan katmanlı sözleşme.

## Karar

Üçüncü seçenek uygulanır:

- Yetkili şirket listesi sunucu tarafı kimlik claim’lerinden gelir.
- Seçili şirket `X-Company-Id` ile bildirilir, claim kapsamına karşı doğrulanır ve istek
  boyunca değiştirilemeyen `CompanyContext` içine alınır.
- Site, Location ve WorkCenter için `CompanyId` zorunludur.
- EF Core global query filter seçili CompanyId’yi otomatik uygular.
- `(CompanyId, Code)` unique indeksleri ve bileşik foreign key’ler, şirketler arası
  hiyerarşi bağlantısını veritabanı düzeyinde engeller.
- Başka şirketin bilinen ID’sine query/command, aynı genel 404 sonucunu üretir.
- Development/Testing bootstrap kimliği yapılandırmadan gelir; Production’da çalışmaz.
  Gerçek OIDC/SSO, bu claim sözleşmesini ERP-000C’de sağlayacaktır.

## Sonuçlar ve riskler

- Yeni company-scoped varlıklar `TenantEntity` veya eşdeğer zorunlu CompanyId sözleşmesini
  izlemelidir.
- `IgnoreQueryFilters` yalnız seed, migration ve açıkça denetlenen sistem işlerinde
  kullanılmalıdır; HTTP use-case’lerinde yasaktır.
- Grup raporları tek şirket endpoint’ini filtre atlayarak kullanamaz; ayrı ve yetkili bir
  çapraz-şirket read model gerektirir.

## Geri alma/yeniden değerlendirme koşulu

Şirket başına ayrı veritabanı yasal veya operasyonel olarak zorunlu hale gelirse,
mevcut CompanyId sözleşmesi veri paketi ayırma ve taşıma sınırı olarak korunur.

## Test ve kanıt

`CompanyIsolationTests`; selector, header yükseltme, query parametresi, doğrudan URL,
başka şirkete command, hata içeriği ve toplam sayı senaryolarını doğrular.
