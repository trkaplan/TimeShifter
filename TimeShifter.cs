// TimeShifter - System Tray Saat Değiştirici
// Derlemek için: csc /target:winexe /win32icon:icon.ico TimeShifter.cs
// Veya doğrudan çalıştır: dotnet script TimeShifter.cs

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

public class TimeShifter : Form
{
    // Win32 API for setting system time
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemTime(ref SYSTEMTIME st);

    [DllImport("kernel32.dll")]
    private static extern void GetSystemTime(ref SYSTEMTIME st);

    // Windows 11 tray icon "always show" ayarı OS tarafından yönetilir; kodla zorlamak mümkün değil.
    // Ama kullanıcıya sabitleme yönergesini (tek seferlik) gösterebiliriz.
    private const string RegistryKeyPath = @"HKEY_CURRENT_USER\Software\TimeShifter";
    private const string RegistryValueTrayHint = "TrayPinHintShown";

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay;
        public ushort wHour, wMinute, wSecond, wMilliseconds;
    }

    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private System.Windows.Forms.Timer countdownTimer;
    private DateTime? originalTime;
    private DateTime? shiftedTime;
    private int remainingMinutes;
    private int defaultMinutes = 10; // Varsayılan: 10 dakika
    private bool untilEndOfDay = false; // Gün sonuna kadar modu
    private int shiftAmount = 12; // Varsayılan: 1 yıl (12 ay)
    private bool isShifted = false;
    private bool warningShown = false;

    // Renkler
    private readonly Color normalColor = Color.FromArgb(34, 197, 94);   // Yeşil
    private readonly Color shiftedColor = Color.FromArgb(239, 68, 68);  // Kırmızı
    private readonly Color warningColor = Color.FromArgb(251, 191, 36); // Sarı

    public TimeShifter()
    {
        // Form'u gizle
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Load += (s, e) => this.Visible = false;

        // Admin kontrolü
        if (!IsRunAsAdmin())
        {
            // Fail-safe: normalde bu kontrol Main() içinde yapılır.
            RestartAsAdminStatic();
            Environment.Exit(0);
        }

        InitializeTray();
        InitializeTimer();
    }

    private bool IsRunAsAdmin()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdmin()
    {
        RestartAsAdminStatic();
    }

    private static bool IsRunAsAdminStatic()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RestartAsAdminStatic()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Application.ExecutablePath,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            MessageBox.Show("Admin yetkisi gerekli!", "TimeShifter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InitializeTray()
    {
        trayMenu = new ContextMenuStrip();
        
        // İleri alma seçenekleri
        trayMenu.Items.Add("⏩ İleri Al: 1 Yıl", null, (s, e) => OnShiftForward(12));
        trayMenu.Items.Add("⏩ İleri Al: 3 Ay", null, (s, e) => OnShiftForward(3));
        trayMenu.Items.Add("⏩ İleri Al: 1 Ay", null, (s, e) => OnShiftForward(1));
        trayMenu.Items.Add(new ToolStripSeparator());
        
        // Reset süresi seçenekleri
        trayMenu.Items.Add("⏱️ Reset Süresi: 10 dk", null, null).Enabled = false;
        trayMenu.Items.Add("   10 dakika", null, (s, e) => SetDuration(10, false));
        trayMenu.Items.Add("   30 dakika", null, (s, e) => SetDuration(30, false));
        trayMenu.Items.Add("   2 saat", null, (s, e) => SetDuration(120, false));
        trayMenu.Items.Add("   Gün sonuna kadar", null, (s, e) => SetDuration(0, true));
        trayMenu.Items.Add(new ToolStripSeparator());
        
        trayMenu.Items.Add("🔄 Saati Geri Al", null, OnResetTime);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("❌ Çıkış", null, OnExit);

        trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(normalColor, ""),
            Text = "TimeShifter - Hazır",
            ContextMenuStrip = trayMenu,
            Visible = true
        };

        // İkon "ok altında" kalıyorsa bu Windows ayarıdır.
        // Registry hack ile sabitlemeyi dene, olmazsa kullanıcıya ipucu göster.
        if (!AttemptAutoPin())
        {
            ShowTrayPinHintOnce();
        }

        trayIcon.DoubleClick += (s, e) =>
        {
            if (isShifted)
                OnResetTime(s, e);
            else
                OnShiftForward(shiftAmount); // Varsayılan ileri alma miktarı
        };
    }

    // Windows 11 Tray Icon Sabitleme Hack'i
    private bool AttemptAutoPin()
    {
        try
        {
            string path = @"Control Panel\NotifyIconSettings";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
            {
                if (key == null) return false;

                string currentExePath = Application.ExecutablePath;
                if (string.IsNullOrEmpty(currentExePath)) return false;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey subKey = key.OpenSubKey(subKeyName, true))
                        {
                            if (subKey == null) continue;

                            // Sadece kendi executable path'imizi kontrol et - başka uygulamalara dokunma
                            object pathVal = subKey.GetValue("ExecutablePath");
                            if (pathVal == null) continue;

                            string exePath = pathVal.ToString();
                            if (string.IsNullOrEmpty(exePath)) continue;

                            // Sadece tam path eşleşmesi varsa işlem yap
                            if (!exePath.Equals(currentExePath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Sadece kendi kaydımızı değiştir
                            object promotedVal = subKey.GetValue("IsPromoted");
                            // 1 = Sabitlenmiş (Görünür), 0 = Gizli
                            if (promotedVal == null || (int)promotedVal != 1)
                            {
                                subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            }
                            return true; // Kaydı bulduk (zaten sabitli veya biz sabitledik)
                        }
                    }
                    catch
                    {
                        // Bu subkey'de hata oldu, diğerlerine devam et
                        continue;
                    }
                }
            }
        }
        catch
        {
            // Registry erişim hatası - sessizce devam et
        }
        return false; // Kayıt bulunamadı (uygulama ilk kez çalışıyor olabilir)
    }


    private void ShowTrayPinHintOnce()
    {
        try
        {
            object val = Registry.GetValue(RegistryKeyPath, RegistryValueTrayHint, null);
            if (val is int && (int)val == 1)
                return;
        }
        catch { }

        DialogResult result = MessageBox.Show(
            "Windows 11 bazı sistem tepsisi ikonlarını varsayılan olarak gizleyebilir.\n\n" +
            "İkonu saatin yanına sabitlemek için:\n" +
            "1) Sağ alttaki (^) oka tıklayın.\n" +
            "2) TimeShifter ikonunu tutup görev çubuğuna sürükleyin.\n\n" +
            "İsterseniz Ayarlar ekranını açabilirim (Görev çubuğu ayarları).",
            "TimeShifter - İkonu Sabitle",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        try
        {
            Registry.SetValue(RegistryKeyPath, RegistryValueTrayHint, 1, RegistryValueKind.DWord);
        }
        catch { }

        if (result == DialogResult.Yes)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ms-settings:taskbar",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch { }
        }
    }

    private void InitializeTimer()
    {
        countdownTimer = new System.Windows.Forms.Timer();
        countdownTimer.Interval = 60000; // 1 dakika
        countdownTimer.Tick += OnTimerTick;
        
        // Gün sonuna kadar modunda daha sık kontrol et (her 10 saniyede bir)
        // Bu timer'ı dinamik olarak değiştirebiliriz ama şimdilik 1 dakika yeterli
    }

    private void SetDuration(int minutes, bool untilEndOfDayMode)
    {
        defaultMinutes = minutes;
        untilEndOfDay = untilEndOfDayMode;
        
        string durationText = untilEndOfDayMode ? "Gün sonuna kadar" : string.Format("{0} dk", minutes);
        ((ToolStripMenuItem)trayMenu.Items[4]).Text = string.Format("⏱️ Reset Süresi: {0}", durationText);
        
        // Tick işareti güncelle
        for (int i = 5; i <= 8; i++)
        {
            var item = (ToolStripMenuItem)trayMenu.Items[i];
            if (untilEndOfDayMode)
            {
                item.Checked = (i == 8); // Sadece "Gün sonuna kadar" seçili
            }
            else
            {
                item.Checked = item.Text.Contains(minutes.ToString());
            }
        }
    }

    private void OnShiftForward(int months)
    {
        if (isShifted)
        {
            MessageBox.Show("Saat zaten ileri alınmış!", "TimeShifter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        shiftAmount = months; // Seçilen ileri alma miktarını kaydet

        // İşlem popup'ı göster
        Form progressForm = ShowProgressForm("Saat ileri alınıyor...\nLütfen bekleyin.");
        Application.DoEvents();

        try
        {
            // Windows Time servisini durdur
            StopTimeService();
            Application.DoEvents();

            // Saati kaydet ve ileri al
            originalTime = DateTime.UtcNow;
            
            SYSTEMTIME st = new SYSTEMTIME();
            GetSystemTime(ref st);
            
            // Ay ekle
            int newMonth = st.wMonth + months;
            int newYear = st.wYear;
            while (newMonth > 12)
            {
                newMonth -= 12;
                newYear++;
            }
            
            st.wYear = (ushort)newYear;
            st.wMonth = (ushort)newMonth;
            SetSystemTime(ref st);
            Application.DoEvents();

            shiftedTime = DateTime.UtcNow;
            isShifted = true;
            warningShown = false;

            // Reset süresini hesapla
            if (untilEndOfDay)
            {
                DateTime now = DateTime.Now;
                DateTime endOfDay = now.Date.AddDays(1).AddSeconds(-1);
                remainingMinutes = (int)(endOfDay - now).TotalMinutes;
            }
            else
            {
                remainingMinutes = defaultMinutes;
            }

            // UI güncelle
            UpdateTrayIcon();
            countdownTimer.Start();
        }
        finally
        {
            // Popup'ı kapat
            if (progressForm != null)
            {
                progressForm.Close();
                progressForm.Dispose();
            }
        }

        // Tamamlandı mesajı
        string shiftText = months == 12 ? "1 yıl" : months == 3 ? "3 ay" : "1 ay";
        string resetText = untilEndOfDay ? "Gün sonuna kadar" : string.Format("{0} dakika", remainingMinutes);
        MessageBox.Show(
            string.Format("Saat {0} ileri alındı.\nOtomatik geri alma: {1}", shiftText, resetText),
            "TimeShifter - İşlem Tamamlandı",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnTimerTick(object sender, EventArgs e)
    {
        if (!isShifted) return;

        // Gün sonuna kadar modunda, süreyi yeniden hesapla
        if (untilEndOfDay)
        {
            DateTime now = DateTime.Now;
            DateTime endOfDay = now.Date.AddDays(1).AddSeconds(-1);
            remainingMinutes = (int)(endOfDay - now).TotalMinutes;
        }
        else
        {
            remainingMinutes--;
        }

        UpdateTrayIcon();

        // 5 dakika kala uyarı
        if (remainingMinutes == 5 && !warningShown)
        {
            warningShown = true;
            ShowExtensionWarning();
        }

        // Süre bitti ve uyarıya yanıt alındı (warningShown false ise kullanıcı uzatmadı)
        if (remainingMinutes <= 0 && !warningShown)
        {
            OnResetTime(null, null);
        }
    }

    private void ShowExtensionWarning()
    {
        trayIcon.Icon = CreateIcon(warningColor, "5");
        
        var result = MessageBox.Show(
            "Saat 5 dakika içinde geri alınacak.\n\nSüreyi uzatmak ister misiniz?",
            "TimeShifter - Süre Bitiyor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);

        if (result == DialogResult.Yes)
        {
            // Süreyi uzat
            if (untilEndOfDay)
            {
                DateTime now = DateTime.Now;
                DateTime endOfDay = now.Date.AddDays(1).AddSeconds(-1);
                remainingMinutes = (int)(endOfDay - now).TotalMinutes;
            }
            else
            {
                remainingMinutes = defaultMinutes;
            }
            warningShown = false;
            UpdateTrayIcon();
            
            string extendText = untilEndOfDay ? "Gün sonuna kadar" : string.Format("{0} dakika", defaultMinutes);
            MessageBox.Show(
                string.Format("Süre {0} uzatıldı.", extendText),
                "TimeShifter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            // Kullanıcı uzatmadı, sayaç devam etsin
            warningShown = false;
        }
    }

    private void OnResetTime(object sender, EventArgs e)
    {
        if (!isShifted)
        {
            MessageBox.Show("Saat zaten normal durumda.", "TimeShifter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // İşlem popup'ı göster
        Form progressForm = ShowProgressForm("Saat geri alınıyor...\nLütfen bekleyin.");
        Application.DoEvents();

        try
        {
            countdownTimer.Stop();

            // Windows Time servisini durdur (saati manuel ayarlamak için)
            StopTimeService();

            // Eğer originalTime kaydedilmişse, saati ona göre geri al
            if (originalTime.HasValue && shiftedTime.HasValue)
            {
                // Geçen gerçek süreyi hesapla (shiftedTime'dan şimdiye kadar geçen süre)
                TimeSpan elapsed = DateTime.UtcNow - shiftedTime.Value;
                
                // Original time'a geçen süreyi ekle (böylece doğru zamanı buluruz)
                DateTime targetTime = originalTime.Value.Add(elapsed);
                
                SYSTEMTIME st = new SYSTEMTIME();
                
                // SetSystemTime UTC zaman bekliyor
                st.wYear = (ushort)targetTime.Year;
                st.wMonth = (ushort)targetTime.Month;
                st.wDay = (ushort)targetTime.Day;
                st.wDayOfWeek = (ushort)targetTime.DayOfWeek;
                st.wHour = (ushort)targetTime.Hour;
                st.wMinute = (ushort)targetTime.Minute;
                st.wSecond = (ushort)targetTime.Second;
                st.wMilliseconds = (ushort)targetTime.Millisecond;
                
                SetSystemTime(ref st);
            }

            // Windows Time servisini başlat (senkronizasyon arka planda yapılacak)
            StartTimeService();

            isShifted = false;
            originalTime = null;
            shiftedTime = null;
            warningShown = false;
            untilEndOfDay = false;

            UpdateTrayIcon();
        }
        finally
        {
            // Popup'ı kapat (İşlem tamamlandı mesajından önce)
            if (progressForm != null)
            {
                progressForm.Close();
                progressForm.Dispose();
            }
        }
        
        // Senkronizasyonu arka planda başlat (kullanıcıyı bekletmeden)
        System.Threading.ThreadPool.QueueUserWorkItem((state) =>
        {
            System.Threading.Thread.Sleep(500); // Servis başlaması için kısa bekleme
            ForceTimeSync();
        });
        
        // Tamamlandı mesajı
        MessageBox.Show(
            "Saat geri alındı ve senkronize edildi.",
            "TimeShifter - İşlem Tamamlandı",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateTrayIcon()
    {
        if (isShifted)
        {
            string text = remainingMinutes > 0 ? remainingMinutes.ToString() : "!";
            Color color = remainingMinutes <= 5 ? warningColor : shiftedColor;
            
            trayIcon.Icon = CreateIcon(color, text);
            
            string shiftText = shiftAmount == 12 ? "1 yıl" : shiftAmount == 3 ? "3 ay" : "1 ay";
            string timeText = untilEndOfDay ? "Gün sonuna kadar" : string.Format("{0} dk kaldı", remainingMinutes);
            trayIcon.Text = string.Format("TimeShifter - {0}\nSaat {1} ileri", timeText, shiftText);
            
            // Menü öğelerini devre dışı bırak
            for (int i = 0; i < 3; i++)
            {
                ((ToolStripMenuItem)trayMenu.Items[i]).Enabled = false;
            }
            ((ToolStripMenuItem)trayMenu.Items[10]).Enabled = true; // Geri al
        }
        else
        {
            trayIcon.Icon = CreateIcon(normalColor, "");
            trayIcon.Text = "TimeShifter - Hazır";
            
            // Menü öğelerini etkinleştir
            for (int i = 0; i < 3; i++)
            {
                ((ToolStripMenuItem)trayMenu.Items[i]).Enabled = true;
            }
            ((ToolStripMenuItem)trayMenu.Items[10]).Enabled = false; // Geri al
        }
    }

    private Form ShowProgressForm(string message)
    {
        Form form = new Form
        {
            Text = "TimeShifter",
            Width = 350,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            TopMost = true
        };

        Label label = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            AutoSize = false
        };

        form.Controls.Add(label);
        form.Show();
        form.Refresh();
        Application.DoEvents();

        return form;
    }

    private Icon CreateIcon(Color bgColor, string text)
    {
        int size = 16;
        using (Bitmap bitmap = new Bitmap(size, size))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            // Arka plan daire
            using (SolidBrush brush = new SolidBrush(bgColor))
            {
                g.FillEllipse(brush, 0, 0, size - 1, size - 1);
            }

            // Metin
            if (!string.IsNullOrEmpty(text))
            {
                using (Font font = new Font("Arial", 7, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    var textSize = g.MeasureString(text, font);
                    float x = (size - textSize.Width) / 2;
                    float y = (size - textSize.Height) / 2;
                    g.DrawString(text, font, textBrush, x, y);
                }
            }
            else
            {
                // Saat ikonu çiz
                using (Pen pen = new Pen(Color.White, 1.5f))
                {
                    int cx = size / 2, cy = size / 2;
                    g.DrawLine(pen, cx, cy, cx, cy - 4);  // Dakika
                    g.DrawLine(pen, cx, cy, cx + 3, cy);  // Saat
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }
    }

    private void StopTimeService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = "stop w32time",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process != null)
                process.WaitForExit();

            psi.FileName = "sc";
            psi.Arguments = "config w32time start= disabled";
            process = Process.Start(psi);
            if (process != null)
                process.WaitForExit();
        }
        catch { }
    }

    private void StartTimeService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = "config w32time start= auto",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process != null)
                process.WaitForExit();

            psi.FileName = "net";
            psi.Arguments = "start w32time";
            process = Process.Start(psi);
            if (process != null)
                process.WaitForExit();

            // Servis başladıktan sonra biraz bekle
            System.Threading.Thread.Sleep(1000);
        }
        catch { }
    }

    private void ForceTimeSync()
    {
        try
        {
            // Sadece senkronize et (config zaten yapılmış olmalı)
            var psi = new ProcessStartInfo
            {
                FileName = "w32tm",
                Arguments = "/resync /force",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process != null)
                process.WaitForExit(3000); // Maksimum 3 saniye bekle
        }
        catch { }
    }

    private void OnExit(object sender, EventArgs e)
    {
        if (isShifted)
        {
            var result = MessageBox.Show(
                "Saat hâlâ ileri alınmış durumda!\n\nÇıkmadan önce geri almak ister misiniz?",
                "TimeShifter",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
                OnResetTime(null, null);
            else if (result == DialogResult.Cancel)
                return;
        }

        // Tüm kaynakları temizle
        if (countdownTimer != null)
        {
            countdownTimer.Stop();
            countdownTimer.Dispose();
            countdownTimer = null;
        }

        if (trayIcon != null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        if (trayMenu != null)
        {
            trayMenu.Dispose();
            trayMenu = null;
        }

        // Uygulamayı zorla kapat
        Application.ExitThread();
        Environment.Exit(0);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            // Sistem kapatılıyorsa veya başka bir nedenle kapanıyorsa, kaynakları temizle
            CleanupResources();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupResources();
        }
        base.Dispose(disposing);
    }

    private void CleanupResources()
    {
        if (countdownTimer != null)
        {
            countdownTimer.Stop();
            countdownTimer.Dispose();
            countdownTimer = null;
        }

        if (trayIcon != null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        if (trayMenu != null)
        {
            trayMenu.Dispose();
            trayMenu = null;
        }
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Admin değilsek: form/message-loop başlatmadan UAC ile yeniden çalıştır ve çık.
        // Bu, task manager'da "process kaldı" problemini çözer (ilk non-admin proses).
        if (!IsRunAsAdminStatic())
        {
            RestartAsAdminStatic();
            Environment.Exit(0);
            return;
        }

        Application.Run(new TimeShifter());
    }
}
