# Conflict Candidates — chatgpt-alt-menuleri-tasarla-c813643335af

## C-001 — Risk renk eşiği işletme politikası sanılmamalı

**Kaynak:** `chatgpt-message-012+user-message-013`  
**Konuşma yönü:** analiz/düzeltme paketinde yaklaşık `%80` kullanımda amber, `%100` üzerinde kırmızı risk kartı rengi önerildi.  
**Kanonik repo kanıtı:** `KARAR_SICILI.md` / `ACIK_KARARLAR.md` risk, onay ve teminat oranlarının dinamik politika ile yönetildiğini; gerçek haircut ve aktivasyon politikasının açık/gated olduğunu gösterir.  
**Sınıflandırma:** gerçek uygulama riski taşıyan **çatışma adayı**; renk eşiği yalnız geri alınabilir UX önerisi olarak kalmalıdır, finansal karar veya server-side risk eşiği olarak kullanılmamalıdır.  
**Önerilen çözüm:** renk semantiği server/policy çıktısındaki durum sınıfını render etsin; sayısal eşik frontend'e gömülmesin.

## Kapsam farkı — çelişki değil

Konuşmadaki v3 prototip, Cari Hesap, Siparişler, Stok ve Tahsis, Belgeler/Sicil ve Mutabakat gibi geniş yüzeyleri göstermektedir. Güncel `CARI_UI_VERTICAL_SLICE_v1.json` ilk dikey dilimde yalnız C-01/C-02/C-03 ve şirket bağlama workspace'ini kapsar, daha geniş verilerde mock truth'u yasaklar. Bu, hedef tasarım ile mevcut dilim arasındaki kapsam farkıdır; prototip current implementation kanıtı sayılmamalıdır.

## Entegrasyon zamanlaması — çelişki değil

Konuşmada Cari > Mutabakat altında ERP–Mikro karşılaştırması hedeflenmiştir. Güncel `OPEN-016` ilk sürümde muhasebe/e-belge/banka entegrasyonunun olmadığını ve Mikro mutabakatının daha sonraki öncelik olduğunu belirtir. Hedef ekran korunabilir; gerçek entegrasyon sözleşmesi kapanmadan capability kapalı/spec-only kalmalıdır.
