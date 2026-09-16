# WORKCENTER_SHELL_MOUNT_HANDOFF — Organizasyon şeridi → ortak shell sahibi

**Kaynak şerit:** `CLAUDE-ORGANIZASYON` (backend + frontend) · branch `claude/goymen-org-workcenter-flow-1lb4f3` · base `main@eacbfe720aa426f4d6a71d5806603c461b30d9e2`
**Tek yazar alanım:** `src/frontend/goymen-erp-web/src/organization/**` + `src/backend/Modules/Organization/**`
**İstenen tek iş:** ORG-03 iş merkezi listesine **kenar çubuğu girdisi**. Başka hiçbir şey.

Bu paketi ben uygulamadım. `src/shell/**` içinde tek satır değiştirmedim; `SHARED_MOUNT_HANDOFF.md` ile aynı sıra ve aynı sınır geçerli.

---

## 1. Neden bir delta gerekiyor — ve neden yalnız nav girdisi

Route **zaten mount oluyor**: `shell/router.tsx` `...organizationRoutes` spread ediyor ve ORG-03 o dizinin **ikinci** elemanı (`organizationRoutes.tsx:38-55`). `CapabilityRoute` diğer ikisiyle aynı şekilde kapatıyor. Yani backend `organization.work-centers.list` yayımladığı an ekran **URL ile erişilebilir**. Bu iddia artık testle korunuyor: `src/organization/shellRouteReachability.test.tsx` gerçek `shellRoutes` üzerinden ORG-03'ü açıyor; spread silinirse kırmızıya döner.

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

## 4. Benim dosyamda taşınması gereken satır — ve neden artık sizin commit'inizde değil

`src/organization/capabilities.ts` içinde `organization.work-centers.list` girdisi şu an `ORGANIZATION_ROUTE_AWAITING_SHELL` tablosunda duruyor. Önceki sürümde sizden, aynı commit'te benim iki dosyamı da değiştirmenizi istemiştim. **Bunu geri alıyorum:** bu dalda tek yazar kuralı var ve o istek şerit sınırını aşıyordu.

Yeni sıra, tek satırlık bir uyarlamayla:

1. **Sizin commit'iniz yalnız `src/shell/**` içinde kalır.** §2 ve §3'ü uygularsınız, ek olarak `shell/organizationMountDrift.test.ts` şeridimin **birleşik** tablolarını okur:
   - `ORGANIZATION_ROUTE` → `ORGANIZATION_MOUNTED_ROUTE`
   - `ORGANIZATION_NAV_LABEL` → `ORGANIZATION_MOUNTED_NAV_LABEL`

   İkisi de `src/organization/capabilities.ts`'te zaten export edilmiş durumda ve girdinin hangi geçiş tablosunda durduğundan bağımsız olarak doğru. Küme eşitliği kontrolü aynen kalır; yalnız karşılaştırdığı taraf değişir. Böylece testiniz sizin commit'inizde yeşil olur ve benim dosyalarıma dokunmanız gerekmez.
2. **Benim takip commit'im** iki tabloyu birleştirir: `ORGANIZATION_ROUTE_AWAITING_SHELL` ve `ORGANIZATION_NAV_LABEL_AWAITING_SHELL` boşalır, `organizationMount.test.ts`'teki "hâlâ shell'i bekleyen tek route" beklentisi boş diziye döner ve `organizationRoutes.tsx`'teki geçici `as CapabilityId` cast'i kalkar. Birleşim tabloları değişmediği için sizin drift testiniz bu commit'te de yeşil kalır.

Sıra bu yüzden katı değil: (1) ve (2) bağımsız olarak inebilir, hangi sırayla inerse insin iki taraf da yeşil kalır. Ölçüldü: bu paket harfiyen uygulanmış bir kopyada `tsc -b` 0 ve `vitest run src/organization src/shell` tamamen yeşil.

`organizationRoutes.tsx`'teki cast'e **dokunmayın** — (2)'de ben kaldırıyorum. Runtime'da hiçbir şeyi genişletmiyor; `hasCapability` her hâlükârda string'i sunucu snapshot'ına karşı okuyor.

## 5. Ortak transport / generated-client ihtiyacı — yok

`src/organization/api.ts` kendi ince fetch'ini yapıyor (`fetchWorkCenters`). Sizden **hiçbir export istemiyorum**. Shell'den tükettiklerim değişmedi: `useShellState`, `contextKey`/`contextKeyString`/`hasCapability`, `useAuthBoundary`, `uiState`, `StateBlock`, `CapabilityRoute`.

Yeni bir `DIRTY_SET` scope'u yok — ORG-03 salt okuma, hiçbir taslak tutmuyor.

## 6. Backend tarafı bende, bilgi olarak

`OrganizationShellCapabilityProvider` 1.0 → **1.1**: dört mevcut declaration byte-aynı, beşinci olarak ORG-03 route'u eklendi. Kabul edilen registry revision'ı bu yüzden değişti (`ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11`). Sizden bir şey gerekmiyor; API host'taki o sabit, kendi modülümün declaration'larının türevi ve host'un açılışta reddetmemesi için zorunlu.

## 7. Delta uygulanmadan durum

PR'ımın kanıtı **yereldir**: bu head'de zorunlu CI hiç koşmadı (Actions `startup_failure`), `[SqlServerFact]` kabul testleri koşulmadı. "Yeşil" dediğim yer yalnız yerel component/DOM testleri ve yerel backend koşusudur.

Sunucu ORG-03'ü yayımladıktan sonra ekran URL ile çalışır, kenar çubuğunda **görünmez**. Bunu "çalışıyor" diye raporlamıyorum: `work-centers.read`-only kullanıcı için keşfedilebilir bir giriş noktası olmadan dilim tamamlanmış sayılmaz.

## 8. Lokasyon bağlantısının görünürlüğü — capability seviyesi (bilgi)

Satırdaki lokasyon bağlantısı **capability seviyesinde** gösterilir: `organization.locations.detail` yoksa ne bağlantı ne de istek vardır. İki izin **farklı kapsamlarda** verildiğinde (ör. iş merkezleri şirket geneli, lokasyonlar tek tesis) bağlantı yine her satırda görünür; kapsam dışındaki bir lokasyona tıklanırsa sunucu **sızıntısız 404** döner — veri yok, kapsam bilgisi yok, yazma yok. Bu, Business matrisinin "lokasyon linki yalnız kendi mevcut izni/capability'siyle" kuralına uygundur. Satır bazında gizleme, hiçbir merged endpoint'in yayımlamadığı satır-kapsam sinyalini gerektirir; ayrı dilim adayıdır ve bu pakette yoktur.

Sizden ping beklemiyorum; **uygulama zamanını siz seçin.**
