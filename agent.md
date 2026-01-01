# Agent Notları ve Hatalar

## Önemli Notlar

### String Interpolation Hatası

**Hata:** C# string interpolation (`$""`) kullanımı eski .NET Framework sürümlerinde (4.0-4.5) desteklenmez.

**Hata Mesajı:**
```
error CS1056: Beklenmeyen karakter '$'
```

**Çözüm:** 
- String interpolation yerine `String.Format()` kullanılmalıdır.
- Örnek:
  ```csharp
  // YANLIŞ (eski .NET Framework'te çalışmaz):
  debugInfo.AppendLine($"Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
  
  // DOĞRU:
  debugInfo.AppendLine(string.Format("Zaman: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
  ```

**Not:** Bu proje .NET Framework 4.0+ gerektirir, bu yüzden string interpolation kullanılmamalıdır.

### Form/Pencere Düzeltmeleri - İki State Kontrolü

**Kural:** QuickActionForm gibi birden fazla state'e sahip formlarda düzeltme yaparken, **her iki state için de kontrol et**.

**Örnekler:**
- Form yüksekliği değiştirilirken: `isShifted ? height1 : height2` - her ikisini de kontrol et
- Buton konumu değiştirilirken: Her iki state'deki butonları kontrol et
- Padding/margin değiştirilirken: Her iki state'de de uygulanmalı
- Yeni özellik eklenirken: Her iki state'de de çalışmalı

**Kontrol Listesi:**
- [ ] Normal state (isShifted = false) kontrol edildi
- [ ] İleri alınmış state (isShifted = true) kontrol edildi
- [ ] Her iki state'de de görsel tutarlılık sağlandı

### Private Metod/Property Erişim Hatası

**Hata:** Başka bir class'tan (örneğin QuickActionForm) TimeShifter'ın private metodlarına veya property'lerine erişmeye çalışırken erişim hatası.

**Hata Mesajı:**
```
error CS0122: 'TimeShifter.MethodName', koruma düzeyi nedeniyle erişilemez
```

**Çözüm:** 
- Başka class'lardan erişilmesi gereken metodları ve property'leri `public` yap.
- Örnek: 
  - `private void OnResetTime()` → `public void OnResetTime()`
  - `private bool isShifted` → `public bool IsShifted { get; }` (property olarak)
- **Kural:** Yeni bir class oluştururken, o class'ın erişmesi gereken tüm metodları ve property'leri baştan `public` yap veya erişim hatası alındığında `private`'dan `public`'e çevir.

