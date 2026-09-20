# Teknik tuzaklar — tekrarlayanlar ve doğru desen

Bu dosya, **birden fazla şeridi birden fazla kez ısırmış** teknik tuzakları ve her birinin doğru
desenini tutar. Ölçüt dar: bir tuzak buraya ancak farklı şeritlerde tekrarladığında girer; tek
seferlik bir hata kendi PR'ının yorumunda kalır.

Not: `#253` (reusable module delivery lessons) merge edilirse bu içerik oraya taşınabilir; o zamana
kadar tekrar eden tuzakların tek adresi burasıdır.

---

## 1. Koleksiyon üyeli `record`'larda eşitlik referansla karşılaştırır

**Belirti.** `Assert.Equal` iki bildirim/görünüm için "Collections differ" diyor, ama yazdırdığı
beklenen ve gerçek satırlar **birebir aynı görünüyor**. Saatler, olmayan bir farkı aramakla geçiyor.

**Sebep.** C# `record`'unun sentezlenmiş `Equals`'ı her üyeyi o üyenin kendi `Equals`'ıyla
karşılaştırır. `IReadOnlyCollection<string>` / `IReadOnlyList<T>` gibi bir üye için bu **referans**
karşılaştırmasıdır. Farklı kaynaklardan üretilmiş yapısal olarak özdeş iki değer asla eşit olmaz.
Bu bir hata değil, dilin varsayılanı; ve bir wire sözleşmesine yalnız testler için özel `Equals`
yazılmaz.

**Bu projede ısırdığı yerler (bir gecede üç kez, 19 Eylül 2026):**

| Yer | Üye |
|---|---|
| `#355` MFG-BAL kütle dengesi görünümü | `MissingParameters` |
| `#360` MFG-ORD emir listesi karşılaştırması | sıra + koleksiyon |
| `#362` ACC-V0b `acc-staged` profil testi | `ShellCapabilityDeclaration.RequiredPermissions` |

**Doğru desen.** Bütün record'u karşılaştırma. Her bildirimi **tek bir karşılaştırılabilir değere**
indirge — bütün üyeler artı koleksiyon **sırasıyla** birleştirilmiş — ve projeksiyonları koleksiyon
olarak karşılaştır:

```csharp
private static string Comparable(ShellCapabilityDeclaration declaration) => string.Join(
    '\u001f',
    declaration.CapabilityId, declaration.RouteId, declaration.RoutePath, declaration.ScreenId,
    declaration.RoutePrecedence.ToString(), declaration.ContextRequirement.ToString(),
    declaration.ScopeRequirement.ToString(),
    declaration.ImplementationAvailable ? "available" : "staged",
    declaration.GlobalProductOperation?.ToString() ?? "<no-global-operation>",
    declaration.ParentCapabilityId ?? "<no-parent>",
    string.Join(',', declaration.RequiredPermissions));

Assert.Equal(expected.Select(Comparable).ToArray(), actual.Select(Comparable).ToArray());
```

**Ne YAPMA.** İddiayı `Contains`'e veya sayıya indirme. Bu testlerin varlık sebebi genelde
"sahibinin bildirdiklerini açar ve **başka hiçbir şey** yapmaz" cümlesidir; `Contains` tam da o
cümleyi kaybeder. Projeksiyon zayıflatma değildir: her üye hâlâ iddia edilir, eklenen/düşen/değişen
bir bildirim hâlâ testi kırar.

**Testin içine sebebini yaz.** Bir sonraki okuyucu deseni değil tuzağı kopyalamasın diye,
karşılaştırmanın neden projeksiyonla yapıldığı iki satırla orada dursun.

**Öneri (ORG, #360 turunda).** Her şeridin bunu yeniden keşfetmesi yerine tek bir paylaşılan
assertion yardımcısı — ya da bu tipteki `record` karşılaştırmalarını yakalayan bir analyzer kuralı —
konulabilir. Bu bir karar değil, öneridir: paylaşılan yardımcı merkezi bir dosyadır, yeri ve sahibi
koordinatörün dağıtımına bağlıdır.

---

## 2. Suite seed çıktıları yalnız GUID veya aktör kimliği olabilir

**Belirti.** Tarayıcı koşusu **seed adımında** ölür; hiçbir ekran açılmaz.

**Sebep.** `tools/validate_browser_acceptance.py` içindeki `seed_export`, kapalı listedeki her
çıktının değerini kontrol eder: sıfır olmayan bir GUID, kanonik bir aktör ya da
`<suite>-<slug>` kalıbı. Serbest metin (bir müşteri adı, bir adres etiketi, bir para birimi kodu)
reddedilir.

**Bu projede:** `l04` (`customerName`) ve `s04` (`deliveryAddressText`, `deliveryContactName`,
`currencyCode`) — ikisi de 19 Eylül 2026'da, tarayıcı koşusundan önce yakalandı.

**Doğru desen.** Seed yalnız **kimlik** taşısın; spec, deterministik metni kendi sabiti olarak
iddia etsin. Satırı yazdıktan sonra doğrulamanın kendi kuralıyla sına — tahmin etme:

```python
import importlib.util, sys
spec = importlib.util.spec_from_file_location('b', 'tools/validate_browser_acceptance.py')
b = importlib.util.module_from_spec(spec); sys.modules['b'] = b; spec.loader.exec_module(b)
b.seed_export('l04', {...})   # kabul etmiyorsa satır yanlış, koşu değil
```

---

## 3. Bir sabiti doğrulatırken kendi sonucunu söyleme

**Sebep.** Bağımsız kontrol, kontrolcü sayıyı önce görmüşse bağımsız değildir (anchoring). Sonuç
yine doğru olabilir — ama artık kanıt değildir.

**Doğru desen.** Kontrolcüye **girdileri** (sağlayıcı bildirimleri, kaynak dosya ve head) ve
**kalibrasyon sabitlerini** (ağacın zaten servis ettiği, başkasının doğruladığı değerler) ver;
kendi ölçtüğün değeri **verme**. Kontrolcü önce kalibrasyonu yeniden üretmeli, sonra yeni değeri
hesaplamalı. Ölçümü yapan ve satırı yazan kişi kontrolcü olamaz.

**Bu projede:** ACC-V0b host sabiti — ilk kontrol anchored kaldı (değer daha önce bir mesajda
geçmişti) ve kayda böyle geçti; kapıyı, sayıyı hiç görmemiş bir şeridin kör türetmesi kapattı.
Koordinatör 19 Eylül 2026'da bunu her cross-check için kural yaptı.
