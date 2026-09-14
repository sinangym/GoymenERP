# WORKCENTER_SHELL_MOUNT_HANDOFF — Organizasyon şeridi → ortak shell sahibi

**Kaynak şerit:** `CLAUDE-ORGANIZASYON` (backend + frontend) · branch `claude/goymen-org-workcenter-flow-1lb4f3` · base `main@eacbfe720aa426f4d6a71d5806603c461b30d9e2`
**Tek yazar alanım:** `src/frontend/goymen-erp-web/src/organization/**` + `src/backend/Modules/Organization/**`
**İstenen tek iş:** ORG-03 iş merkezi listesine **kenar çubuğu girdisi**. Başka hiçbir şey.

Bu paketi ben uygulamadım. `src/shell/**` içinde tek satır değiştirmedim; `SHARED_MOUNT_HANDOFF.md` ile aynı sıra ve aynı sınır geçerli.

---

## 1. Neden bir delta gerekiyor — ve neden yalnız nav girdisi

Route **zaten mount oluyor**: `shell/router.tsx` `...organizationRoutes` spread ediyor ve ORG-03 o dizinin üçüncü elemanı. `CapabilityRoute` diğer ikisiyle aynı şekilde kapatıyor. Yani backend `organization.work-centers.list` yayımladığı an ekran **URL ile erişilebilir**.

Eksik olan tek şey **nav girdisi**, ve bu kozmetik değil:

> ORG-01'in route'u `organization.locations.read` üzerinde duruyor. Bu dilimin bütün amacı `work-centers.read` tutup `locations.read` **tutmayan** kullanıcı. O kullanıcı için ORG-01 içine konmuş bir bağlantı hiç yoktur. Bu yüzden iş merkezleri kenar çubuğundan açılır; lokasyon kartı ise satırdan açılır.

## 2. `src/shell/capabilities.ts` — üç girdi

`CAPABILITY` içine (organization girdilerinin yanına):

```ts
  /**
   * ORG-03 Ayarlar > Organizasyon > İş Merkezleri — yetkili iş merkezi listesi.
   * Route `/ayarlar/organizasyon/is-merkezleri`, owner CLAUDE-ORGANIZASYON.
   *
   * Taşıyıcı izin: YALNIZ `organization.work-centers.read`. ORG-01'in
   * `organization.locations.read`'i bu route'un şartı DEĞİLDİR ve tersi de doğrudur:
   * lokasyon ekranlarını tutmak buraya giriş sağlamaz. Backend registry'si bunu tam
   * olarak böyle yayımlıyor (`OrganizationShellCapabilityProvider`, ORG-03, 1.1).
   */
  organizationWorkCentersList: 'organization.work-centers.list',
```

`CAPABILITY_ROUTE` içine:

```ts
  // Statik kardeş. `/ayarlar/organizasyon` ile aynı segment sayısında değil, kartla da
  // değil; hiçbir url ikisine birden uyamaz (#172 P1-2 kuralı).
  [CAPABILITY.organizationWorkCentersList]: '/ayarlar/organizasyon/is-merkezleri',
```

`CAPABILITY_NAV_LABEL` içine:

```ts
  [CAPABILITY.organizationWorkCentersList]: 'İş Merkezleri',
```

## 3. `src/shell/AppShell.tsx` — `NAV_CAPABILITIES`'e tek girdi

```ts
  CAPABILITY.organizationWorkCentersList,
```

`organizationSitesList`'in hemen ardına. Sunucu grant etmeden görünmez; ORG-02 kartı girmez.

## 4. Benim dosyamda taşınması gereken TEK satır — aynı commit'te

`src/organization/capabilities.ts` içinde `organization.work-centers.list` girdisi şu an `ORGANIZATION_ROUTE_AWAITING_SHELL` tablosunda duruyor, `ORGANIZATION_ROUTE`'ta değil. Sebebi mekanik:

`shell/organizationMountDrift.test.ts` **sizin dosyanız** ve `Object.keys(ORGANIZATION_ROUTE)` ile `shell/capabilities.ts`'teki `organization.*` id'leri arasında **küme eşitliği** iddia ediyor. O test doğru ve iki yarıyı birlikte inmeye zorlayan mekanizma tam olarak o. Yani:

- girdiyi bugün `ORGANIZATION_ROUTE`'a koysaydım, sizin testiniz **kırmızı** olurdu;
- siz §2'yi uygulayıp benim satırımı taşımasaydınız, yine kırmızı olurdu.

İkisi aynı commit'te olmalı. Uygulama anında yapılacaklar:

1. `ORGANIZATION_ROUTE_AWAITING_SHELL`'deki satırı `ORGANIZATION_ROUTE`'a taşıyın (tablo boş kalır).
2. `ORGANIZATION_NAV_LABEL_AWAITING_SHELL`'deki satırı `ORGANIZATION_NAV_LABEL`'a taşıyın.
3. `src/organization/organizationMount.test.ts` içindeki `has exactly one route still waiting on the shell — ORG-03` beklentisini boş diziye çevirin. O satır bilerek oradadır: taşımayı sessizce unutmayı imkânsız kılar.

`organizationRoutes.tsx`'teki geçici `as CapabilityId` cast'ine **dokunmayın** — sizin girdiniz merge olduktan sonra gereksizleşir ve onu ben kaldırırım (runtime'da hiçbir şeyi genişletmiyor; `hasCapability` her hâlükârda string'i sunucu snapshot'ına karşı okuyor).

## 5. Ortak transport / generated-client ihtiyacı — yok

`src/organization/api.ts` kendi ince fetch'ini yapıyor (`fetchWorkCenters`). Sizden **hiçbir export istemiyorum**. Shell'den tükettiklerim değişmedi: `useShellState`, `contextKey`/`contextKeyString`/`hasCapability`, `useAuthBoundary`, `uiState`, `StateBlock`, `CapabilityRoute`.

Yeni bir `DIRTY_SET` scope'u yok — ORG-03 salt okuma, hiçbir taslak tutmuyor.

## 6. Backend tarafı bende, bilgi olarak

`OrganizationShellCapabilityProvider` 1.0 → **1.1**: dört mevcut declaration byte-aynı, beşinci olarak ORG-03 route'u eklendi. Kabul edilen registry revision'ı bu yüzden değişti (`ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11`). Sizden bir şey gerekmiyor; API host'taki o sabit, kendi modülümün declaration'larının türevi ve host'un açılışta reddetmemesi için zorunlu.

## 7. Delta uygulanmadan durum

PR'ım yeşil ve güvenli. Sunucu ORG-03'ü yayımladıktan sonra ekran URL ile çalışır, kenar çubuğunda **görünmez**. Bunu "çalışıyor" diye raporlamıyorum: `work-centers.read`-only kullanıcı için keşfedilebilir bir giriş noktası olmadan dilim tamamlanmış sayılmaz.

Sizden ping beklemiyorum; **uygulama zamanını siz seçin.**
