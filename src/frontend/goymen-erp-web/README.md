# Göymen ERP Web

ERP-000A+B kapsamındaki React/TypeScript istemcisi. Bu aşamada yalnız yetkili şirket
seçici ve Organization özet iskeleti vardır; ana ERP sayfaları sonraki görevlerdir.

```powershell
npm ci
npm run lint
npm test
npm run build
npm run dev
```

Vite geliştirme sunucusu `/api` isteklerini `http://localhost:5080` adresine yönlendirir.
Development bootstrap kullanıcısı `VITE_DEV_USER` ile değiştirilebilir.

Yerel hesap girişi (ID-1F): `VITE_AUTH_MODE=LocalAccount` ile derlenen istemci, sunucu 401
döndüğünde giriş ekranını ve araç çubuğunda "Çıkış yap" denetimini gösterir; bu kipte
`X-Development-User` başlığı hiç gönderilmez. Değişken verilmezse eski davranış sürer
(development başlığı ve "Oturum sonlandı" durumu). Bayrak yalnız istemcinin ne sunduğunu
belirler; hangi kimlik doğrulamanın kabul edildiğine sunucu karar verir.
