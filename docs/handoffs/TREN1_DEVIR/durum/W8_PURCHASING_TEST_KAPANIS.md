# W8 Purchasing ilk birim koşusu düzeltmeleri

Durum: SOURCE_DONE; birleşik kaynak derlemesi ve test tekrarı bekliyor.
Taban `0197aca77a08c34bd1a7b77a20de93534fc83f27`; GOV-D9.
İlk gerçek TRX `a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8` üzerinde 12 başarısız
Purchasing örneği gösterdi. İlk kanıt değiştirilmedi; yeni dilim/dalga açılmadı.

- İzinli kapanış/iptal/onay/geri çekme alanı değişikliklerinde eski testler erişilemez
  SQL bağlantısının iç hatasını DbException sanıyordu; Windows iç hata Win32Exception'dır.
  Bu ağ hatasını yeni başarı koşulu yapmak yerine yalnız bu test context'lerine isteğe
  bağlı SavingChanges interceptor sentinel'i eklenir. Guard'ın izinli değişikliği kabul
  edip EF save girişine ulaştığı, DB bağlantısı kurulmadan sentinel ile ölçülür. Ardından
  yasak alan değişikliklerinin mevcut InvalidOperationException ile sentinel'den önce
  reddedilmesi korunur. Bu testler SQL kabulünü kanıtlamaz; SQL başarısı iddia edilmez.
- Kaydedilmiş fiyat fixture'ında UnitPrice vardı ama PriceUnitId yoktu. Geçerli kayıtlı
  fiyatı temsil etmesi için birim kimliği tamamlandı. Mevcut gerçek supplier/price revision
  guard'ının geçmişsiz fiyat değişikliğini DB öncesinde reddetmesi açık mesajla doğrulanır.
  Currency ve diğer kayıt değişmezliği kontrolleri korunur; üretim guard'ı değiştirilmedi.
- Kabul participant sözleşmesinin mevcut ResolveReceiptTargetAsync okuması ve StageAsync
  yazması tekil ad, dönüş ve argüman türleriyle doğrulanır; lifecycle gevşetilmez.
- Fiyatsız gelecek tedarik sözleşmesine mevcut DefinitiveReturnedText eklenir; yasak
  fiyat/tutar/vergi/maliyet alanları kontrolü korunur. Satır pay kontrolü mevcut
  DefinitiveReturned payını da kapsayan tam üst sınıra hizalanır; constraint eşliği kalır.
  ALS-D92 kaynak yorumundaki yeniden açılma sonrası tarihsel kapanış/iptal birlikteliği
  korunur; iki payın negatif olmaması ve tam toplam üst sınırı birlikte doğrulanır.
- Maker/checker SQL kontrolü, mevcut binary karşılaştırmalı
  COALESCE(CurrentPreparedBy, CreatedBy) hazırlayan kaynağıyla eşleştirilir.
- Shell owner sürümü mevcut 1.15'e hizalanır; staging, ekran, route, izin ve üst kontrol
  ilişkileri kaldırılmaz. S7/S8 canonical ekran/route düzeltmesi kaynaktan korunur.
- Kabul ters kaydında gerçek participant'ın döndürdüğü prefiksli
  PurchasingStoreCodes.SameKeyPayloadMismatch beklenir; replay ve çift yazmama kontrolleri kalır.
- Bildirim alıcısı testinde şirket drift'i yalnız adversarial ICompanyContext ile taklit
  edilir. Üretim CompanyContext.Set değişmezliği gevşetilmez; DB öncesi ret beklentisi kalır.

Yalnız beş Purchasing.Unit test dosyası ve bu rapor değişti; backend/model/migration,
contract, guard/pin, grant/rol veya işletme politikası değişikliği yapılmadı.
`git diff --check`: exit 0. Bu head'de derleme/test, gerçek SQL, Node, HTTP, tarayıcı ve
CI NOT_RUN; ağır işlem sırası entegrasyon sahibinde. Skip/silme/karantina yok.
Bu yazar desteğidir; bağımsız exact-head/K3 onayı değildir.
