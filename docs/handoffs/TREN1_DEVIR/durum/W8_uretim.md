# tren-1 W8 üretim

| Dilim | Durum | Commit | Sonuç ve kalan |
|---|---|---|---|
| URT-S5 AT044 | DONE — kaynak/derleme; entegrasyon bekliyor | `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937` | URT-D37/URT-D111: Slitting ilk kayıt, önizleme ve revizyonunda HamKg etiketin tüm pozisyonlarındaki güncel fiziksel toplamını aşamaz. Taslakta serbest miktar ve tek pozisyon şartı uygulanmaz; onaydaki Inventory ayırma kontrolü korunur. |
| URT-SC33 manuel sıra | PARTIAL — uygulama başlıyor | — | URT-D110: mevcut elle sıra termin/puan değişiminde korunur; yeni üye ilk daha geç otomatik anahtarın önüne girer. B20→A10 ve C15 sonucu C→B→A. Kalıcı sıra, komut, UI, migration ve test kaynakları hazırlanacak. |

## Doğrulama

- `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937`: Manufacturing.UnitTests derlemesi **exit 0, 0 uyarı / 0 hata**. Tek MSBuild node, shared compilation ve OpenAPI build üretimi kapalı.
- AT044 için unit, iki gerçek SQL HTTP fact ve frontend ret/düzeltme test kaynakları yazıldı. **Test runtime, gerçek SQL, HTTP runtime, Vitest ve browser NOT_RUN.** Yeni SQL HTTP kaynaklarının derlemesi ve frontend TSC henüz NOT_RUN.
- AT044 migration, THROW, izin, grant, rol veya pin değişikliği içermez.
- W7 merkezi adayına bu commit dahil değildir; sonraki dalgada lider entegrasyonu beklenir. Bu rapor yalnız üretim ekip dalındadır.
