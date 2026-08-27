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
