# TimeShifter ⏰

Windows sistem saatini geçici olarak ileri alan system tray uygulaması.

## Özellikler

- 🟢 **Yeşil ikon**: Saat normal
- 🔴 **Kırmızı ikon**: Saat ileri alınmış (kalan dakika gösterir)
- 🟡 **Sarı ikon**: 5 dakika kaldı, uyarı

### Kullanım

1. **Çift tık**: Saati ileri al / geri al (toggle - varsayılan: 1 yıl)
2. **Sağ tık menüsü**:
   - **İleri Alma Seçenekleri**:
     - 1 Yıl
     - 3 Ay
     - 1 Ay
   - **Reset Süresi Seçenekleri**:
     - 10 dakika (varsayılan)
     - 30 dakika
     - 2 saat
     - Gün sonuna kadar
   - Saati geri al
   - Çıkış

### Akıllı Geri Alma

- Varsayılan reset süresi: 10 dakika
- 5 dakika kala uyarı penceresi çıkar
- "Evet" → süre uzatılır
- "Hayır" → sayaç devam eder, süre dolunca saat geri alınır
- Uyarıya yanıt verilmeden saat geri alınmaz (kullanıcı AFK olabilir)
- "Gün sonuna kadar" seçeneği ile günün sonuna kadar otomatik geri alma

### Güvenlik

- Çıkışta saat hâlâ ilerideyse uyarı verir
- Windows Time servisi otomatik yönetilir
- Admin yetkisi gerektirir (otomatik UAC)

## Kurulum

### Yöntem 1: Derle
```batch
build.bat
```

### Yöntem 2: Manuel derleme
```batch
csc /target:winexe /out:TimeShifter.exe TimeShifter.cs
```

### Yöntem 3: Visual Studio
Yeni Windows Forms projesi oluştur, kodu yapıştır, derle.

## Gereksinimler

- Windows 10/11
- .NET Framework 4.0+
- Admin yetkisi

## Registry Temizleme

Registry kayıtlarını temizlemek için `clean-registry.bat` dosyasını çalıştırın. Bu script:
- `HKEY_CURRENT_USER\Software\TimeShifter` kaydını siler
- `HKEY_CURRENT_USER\Control Panel\NotifyIconSettings` altındaki TimeShifter kayıtlarını temizler

## Not

Test ve geliştirme amaçlıdır. Üretim ortamında dikkatli kullanın.
