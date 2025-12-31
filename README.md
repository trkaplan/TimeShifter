# TimeShifter ⏰

Windows sistem saatini geçici olarak 1 yıl ileri alan system tray uygulaması.

## Özellikler

- 🟢 **Yeşil ikon**: Saat normal
- 🔴 **Kırmızı ikon**: Saat 1 yıl ileri alınmış (kalan dakika gösterir)
- 🟡 **Sarı ikon**: 5 dakika kaldı, uyarı

### Kullanım

1. **Çift tık**: Saati ileri al / geri al (toggle)
2. **Sağ tık menüsü**:
   - Saati 1 yıl ileri al
   - Süre seçimi (15/30/60/120 dk)
   - Saati geri al
   - Çıkış

### Akıllı Geri Alma

- Varsayılan süre: 30 dakika
- 5 dakika kala uyarı penceresi çıkar
- "Evet" → süre uzatılır
- "Hayır" → sayaç devam eder, süre dolunca saat geri alınır
- Uyarıya yanıt verilmeden saat geri alınmaz (kullanıcı AFK olabilir)

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

## Kaldırma

Uygulamayı kaldırmak için `uninstall.bat` dosyasını çalıştırın. Bu script:
- Çalışan TimeShifter process'lerini durdurur
- Registry kayıtlarını temizler
- Uygulama dosyasını bulur ve siler
- Kısayolları temizler

**Not:** Yönetici yetkisi gerekebilir.

## Not

Test ve geliştirme amaçlıdır. Üretim ortamında dikkatli kullanın.
