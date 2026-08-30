# FURSOY Browser — Gerçek Chromium Tabanlı Tarayıcıya Dönüşüm Planı

## Karar: Teknoloji Yığını

Electron **kullanılmayacak**. Electron, Chromium + Node.js'i bir JS uygulaması etrafında paketler; motoru doğrudan kontrol etmez ve Chrome ile birebir aynı performans/güncellik garantisi vermez.

Üç gerçek Chromium seçeneği değerlendirildi:

1. **Chromium'u kaynaktan derleme (tam fork)** — Brave/Vivaldi/Edge'in yaptığı şey. `depot_tools`, `GN`, `Ninja` ile tüm Chromium kaynağını (100GB+) indirip derlemek gerekir. İlk build saatler sürer, C++ uzmanlığı ve ciddi donanım ister. Aşırı özelleştirme (kendi renderer/network stack değişiklikleri) istenmediği sürece gereksiz ağırlıkta.
2. **CEF (Chromium Embedded Framework)** — Chromium'un derlenmiş, hazır dağıtılan binary'lerini bir native uygulamaya gömer. Chrome ile aynı render motoru, aynı hız, aynı web uyumluluğu. Kaynağı kendin derlemezsin; CEF projesinin resmi build'lerini kullanırsın. UI'ı istediğin native dilde (C++, veya CefSharp ile C#) kurarsın.
3. **WebView2** — Windows'ta zaten kurulu olan Edge/Chromium runtime'ını kullanır. En hafif yol ama makine üzerindeki paylaşılan runtime'a bağımlısın, kendi tarayıcı sürümünü/güncelleme takvimini kontrol edemezsin.

**Seçim: CEF, .NET tarafında CefSharp ile.**

Gerekçe: Kullanıcı "Chrome kadar hızlı, minimum özelleştirme yeterli" dedi. CEF gerçek Chromium motorunu kullandığı için hız ve web uyumluluğu Chrome ile birebir aynıdır; kendi Chromium'unu derleme yüküne girmeden bunu elde ederiz. Proje zaten Windows-only (NSIS installer, `.ico` ikon) olduğundan CefSharp (WinForms veya WPF üzerinde) hem üretken hem native bir geliştirme deneyimi sağlar — ham C++ CEF'e kıyasla çok daha az sürtünme.

## Mevcut Durumdan Taşınacaklar

Electron sürümünde (`src/main`, `src/preload`, `src/renderer`) inşa edilen UI/UX mantığı (sekme yönetimi, adres çubuğu, indirilenler, yer imleri, pencere kontrolleri) referans olarak kalacak; native tarafta yeniden yazılacak. Eski Electron kodu, yeni proje stabil hale gelene kadar repoda tutulup ayrı bir klasöre taşınabilir, sonrasında kaldırılır.

## Yeni Proje İskeleti

- `.NET 8` + `WPF` (Windows masaüstü UI için native, XAML tabanlı, CefSharp ile iyi entegre)
- `CefSharp.Wpf` NuGet paketi — Chromium render yüzeyini WPF içine gömer
- Çözüm yapısı (öneri):
  - `src/Fursoy.Browser.App` — WPF uygulaması, pencere/sekme UI'ı
  - `src/Fursoy.Browser.Core` — sekme durumu, geçmiş, yer imleri, indirmeler, ayarlar (UI'dan bağımsız iş mantığı)
  - `src/Fursoy.Browser.Updater` — güncelleme mantığı (bkz. Dağıtım bölümü)

## Aşamalar

### 1. Temel İskelet
- CefSharp.Wpf entegrasyonu, tek sekmeli minimal pencere ile "merhaba dünya" browsing.
- Frameless pencere + özel başlık çubuğu (mevcut Electron UI'daki min/max/close, sekme çubuğu tasarımının native WPF karşılığı).
- Çoklu sekme: her sekme kendi `ChromiumWebBrowser` instance'ı.

### 2. Temel Tarayıcı Özellikleri
- Adres çubuğu (URL/arama ayrımı, mevcut Electron mantığı referans alınabilir).
- Geri/ileri/yenile/ana sayfa, yükleme çubuğu, favicon takibi.
- Sekme sürükle-bırak ile yeniden sıralama / yeni pencereye ayırma.
- Yer imleri ve indirilenler paneli (CEF'in `IDownloadHandler` arayüzü ile indirme yönetimi).
- Geçmiş (History) — ziyaret kayıtları + arama.
- Sayfa içi arama (Ctrl+F) — CEF'in `IFindHandler`/`Find` API'si.
- Zoom kontrolü, kalıcı zoom seviyesi.
- Sağ tık context menüsü (link kopyala, resmi kaydet, kaynağı görüntüle, incele/DevTools — CEF DevTools yerleşik).

### 3. Gizlilik ve Güvenlik
- Gizli (Incognito) mod: ayrı CEF `RequestContext` (izole depolama/çerez).
- Site izinleri yönetimi (kamera/mikrofon/konum/bildirim) — CEF `IPermissionHandler`.
- Reklam/tracker engelleme: CEF `IResourceRequestHandler` üzerinden filtre listesi (EasyList tabanlı) entegrasyonu.
- HTTPS/karışık içerik uyarıları, sertifika hata yönetimi (`IRequestHandler.OnCertificateError`).

### 4. Ayarlar ve Kişiselleştirme
- Ayarlar sayfası: ana sayfa, arama motoru, indirme klasörü, tema (açık/koyu), gizlilik seçenekleri.
- Yeni sekme sayfası: hızlı erişim / en çok ziyaret edilenler.

### 5. Performans
- Arka plan sekmelerinde kaynak kullanımını sınırlama (sekme "uyutma"/discard).
- Çoklu process modeli CEF'te zaten var (her renderer ayrı process) — process sayısını/bellek ayarlarını (`CefSettings`) ihtiyaca göre ayarlama.
- Başlangıç süresi optimizasyonu.

### 6. Dağıtım ve Otomatik Güncelleme
- Kurulum paketi: `Squirrel.Windows` veya mevcut yaklaşıma benzer bir NSIS tabanlı installer.
- Otomatik güncelleme: `Squirrel.Windows` kendi update mekanizmasını sağlar; alternatif olarak GitHub Releases'i kontrol eden basit bir updater yazılabilir.
- CI/CD: `.github/workflows/release.yml` — `v*` tag'i push edildiğinde otomatik build + GitHub Release yayınlar (mevcut workflow Electron'a göre yazıldı, .NET/CefSharp build adımlarına göre güncellenecek — `dotnet publish` + installer paketleme).

## Önerilen Sıralama

1. CefSharp.Wpf ile minimal tek-sekme pencere (motoru çalıştır, temel akışı doğrula)
2. Çoklu sekme + temel navigasyon UI'ı
3. Yer imleri, indirilenler, geçmiş
4. Gizli mod + izin yönetimi
5. Adblock entegrasyonu
6. Ayarlar sayfası + kişiselleştirme
7. Dağıtım/otomatik güncelleme altyapısının yeni yığına uyarlanması
8. Performans cilası

Her aşama ayrı ayrı test edilip commit edilecek. Electron tabanlı eski kod, yeni yığın stabilleşene kadar korunacak, sonra kaldırılacak.

## Notlar

- Bu geçiş dilin JS'den C#'a, framework'ün Electron'dan WPF+CefSharp'a değişmesi anlamına gelir; mevcut `src/main`, `src/preload`, `src/renderer` klasörleri ve `package.json` yeni yığında kullanılmayacak.
- `.github/workflows/release.yml` şu an Electron/`npm run dist` varsayımıyla yazıldı; yeni yığına geçildiğinde build adımları `dotnet publish` + installer üretimine göre güncellenmeli.
