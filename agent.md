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

