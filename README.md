# Lyra

Windows için yerel müzik arşivi oynatıcısı. Mevcut sürüm: **0.10.39**.

Windows medya paneliyle bütünleşir: parça adı, sanatçı, albüm kapağı ve oynatma kontrolleri Windows'a iletilir. Kilit ekranındaki görünürlük Windows sürümüne ve sistem ayarlarına bağlıdır. Medya tuşlarının hangi uygulamaya yönleneceğini Windows yönetir. Windows 10 sürüm 1809 veya üzeri gerekir.

Üst çubukta geri düğmesi, üzerine gelindiğinde veya tıklandığında genişleyen arama ve sağ paneli açma/kapatma düğmesi bulunur. Sağ panel gizliyken çalan parçanın adı ve sanatçısı çubuğun ortasında görünür. Saydam üst çubuk, albüm veya çalma listesi arka planını kesintisiz gösterir.

Lyra kendi bilgisayarındaki müzik dosyalarını çalar. Arşiv taraması, kapaklar ve görselleştirme yerel olarak çalışır. Çevrimiçi şarkı sözü, sanatçı fotoğrafı ve Wikipedia özeti aramaları internet bağlantısı kullanır. Türkçe ve İngilizce arayüz bulunur.

## Özellikler

- Albüm ve sanatçı görünümleri, arama, çalma kuyruğu ve çalma listeleri.
- Sanatçı profillerinde arayüz diline uygun Wikipedia özeti, kaynak bağlantısı ve çevrimdışı önbellek.
- Favoriler, beş yıldızlı puanlama ve arşiv filtreleri.
- FLAC, WavPack ve diğer desteklenen ses biçimleri; ayrıntılı etiket/teknik bilgi görünümü.
- Ses aygıtı seçimi, WASAPI paylaşımlı/özel çıkış seçenekleri ve ReplayGain.
- Osiloskop, spektrum, VU metre ve canlı spektrogram.
- Yerel veya isteğe bağlı çevrimiçi şarkı sözleri; mevcutsa zamanlı söz takibi ve satıra tıklayarak o zamana atlama.
- Tam ekran ve mini oynatıcı, multimedya tuşları, görev çubuğu kontrolleri.
- Özelleştirilebilir arayüz, tema ve OLED siyah tema.

## Windows sürümünü kurma

1. [Sürümler](https://github.com/berkcankara/LYRA-Lossless-Music-Player/releases) sayfasından `Windows-x64-Setup.exe` ile biten dosyayı indir ve aç.
2. Kurulum klasörünü seç.
3. Başlat menüsü ve masaüstü kısayolu seçeneklerini belirle; ikisi de başlangıçta işaretlidir.
4. Kurulumu tamamla ve Lyra'yı aç.
5. **Ayarlar → Müzik arşivi → Klasör ekle** yolundan müzik klasörünü seç.

Kurulum varsayılan olarak kendi kullanıcı hesabındaki `AppData\Local\Programs\Lyra` klasörüne yapılır ve yönetici izni istemez. Başka bir konum seçersen hesabının o klasöre yazabilmesi gerekir. Ayrı .NET kurulumu gerekmez. Güncellemeler ve kaldırma işlemi uygulamanın oluşturduğu `data` klasörünü korur. Portable sürümden geçerken Lyra kapalıyken mevcut `data` klasörünü yeni Lyra klasörüne kopyalayabilirsin.

Depo gizliyse indirme bağlantılarına yalnızca depoya erişimi olan GitHub hesapları ulaşabilir.

## Kurulumsuz kullanım

1. Windows x64 Portable ZIP paketini indir.
2. ZIP'e sağ tıklayıp **Tümünü ayıkla** seçeneğini kullan.
3. Çıkan `Lyra` klasörünü yazma iznin olan bir konuma yerleştir ve içindeki `Lyra.exe` dosyasını aç.
4. **Ayarlar → Müzik arşivi → Klasör ekle** yolundan müzik klasörünü seç.

ZIP'in içinden çalıştırma. Yalnızca EXE'yi ayırma; yanındaki dosya ve klasörleri birlikte tut. Gerekli .NET çalışma ortamı taşınabilir pakette bulunur. İlk açılışta oluşturulan `data` klasörü arşiv kayıtları, tercihler ve önbellekleri saklar; güncellemeden önce bu klasörü yedekle ve koru.

Mevcut paket **Windows x64** içindir. Windows 10 üzerinde doğrudan uyumluluk testi henüz yapılmamıştır. macOS sürümü bu depoda henüz bulunmaz.

## Kaynaktan derleme

Windows üzerinde .NET 8 SDK ve PowerShell gerekir:

```powershell
./Source/build.ps1
```

Çıktı `App` klasörüne yazılır. Projenin eski dosya adı `Source/Ahenk.csproj` ve iç ad alanı `Ahenk` olarak korunur; oluşturulan uygulamanın adı `Lyra`dır.

Kurulum EXE'sini ve Portable ZIP'i hazırlamak için Inno Setup 7 derleyicisiyle:

```powershell
./Installer/build-installer.ps1 -CompilerPath 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

Paketler `.artifacts` klasörüne yazılır. Derleme yalnızca uygulama dosyalarını alır; kişisel `data` klasörü, arşiv veritabanları ve hata ayıklama dosyaları pakete eklenmez. Güncelleme notları [CHANGELOG.md](CHANGELOG.md) dosyasındadır.

Çalışma sırasında kullanılan ayrı komut satırı araçları `App/tools` içinde olmalıdır:

- `ffmpeg.exe`
- `ffprobe.exe`
- `wvunpack.exe`

Bu araçların çalıştırılabilir dosyaları kaynak deposuna eklenmez. Kullanılan sürüm, derleme bilgisi ve lisanslar için [üçüncü taraf bildirimlerini](THIRD-PARTY-NOTICES.md) incele. Bir dağıtım hazırlarken ilgili lisans metinleri ve gerekli kaynak erişimini de sağlamalısın.

## Depo kapsamı

Kaynak kodu `Source`, ikon kaynakları `Icon`, üçüncü taraf lisans metinleri `licenses` klasöründedir. Kurulu uygulama, kişisel müzik dosyaları, arşiv veritabanı, kullanıcı tercihleri ve önbellekler Git'e eklenmez. İndirilebilir büyük ZIP paketleri kaynak geçmişi yerine GitHub Releases üzerinden paylaşılmalıdır.

## Yayın ve lisans durumu

Uygulama henüz dijital olarak imzalanmamıştır. Mevcut taşınabilir paket bir geliştirme dağıtımıdır; halka açık ikili dağıtım için üçüncü taraf bileşenlerin karşılık gelen kaynak erişimi ve lisans bildirim işlemleri henüz tamamlanmamıştır. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) ve [licenses](licenses) bu durumu açıklar.

Lyra'nın kendi kaynak kodu için ayrıca bir açık kaynak lisansı seçilmemiştir. Üçüncü taraf bileşenler kendi lisanslarına tabidir; bu lisanslar kendiliğinden Lyra'nın tüm kaynak koduna uygulanmış sayılmaz.
