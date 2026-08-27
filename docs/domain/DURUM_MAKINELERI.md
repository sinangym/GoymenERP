# Durum Makineleri Sözleşmesi

Durum geçişleri backend’de tanımlanır; UI renkleri kural değildir.

## Sipariş ayrı eksenleri

- Yaşam döngüsü: Taslak / Aktif / Kapalı / İptal
- İç onay: Hazırlanıyor / Onay bekliyor / Onaylı / Reddedildi
- Kaynak: Kaynaksız / Kısmi / Tam kaynaklı
- Gerçekleşme: Başlamadı / Kısmi / Tamamlandı
- Faturalama: Yok / Kısmi / Tam
- Finansal kapanış: Açık / Kısmi / Kapalı
- Müşteri onayı: Bekleniyor / Alındı / Vazgeçildi

## Diğer yaşam döngüleri

- Tahsis: Öneri → Taslak → Aktif → Kısmi Kullanıldı → Kullanıldı/Serbest/İptal
- Üretim planı: Taslak → Kontrolde → Onaylı → Kuyrukta → Üretimde → Kısmi/Tamamlandı → İptal/Ters
- Yükleme: Planlandı → Hazırlık → Yükleniyor → Tamamlandı → İptal
- Belge sürümü: Yüklendi → Sınıflandırıldı → Kontrolde → Onaylı → Geçersiz/Yeni Sürüm
- Finansal kaynak: Taslak → Kontrolde → Onaylı → Gerçekleşti → Mutabık → Ters
- Dağıtım: Öneri → Taslak → Uygulandı → Kısmi Geri Alındı → Geri Alındı

Her geçişte önkoşul, yetki, etkiler, ters alma ve audit şeması bulunur.
