// TimeShifter - System Tray Saat Değiştirici
// Derlemek için: csc /target:winexe /win32icon:icon.ico TimeShifter.cs
// Veya doğrudan çalıştır: dotnet script TimeShifter.cs

using System;
using System.Drawing;
using System.Reflection;
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

    // NotifyIcon için Icon handle cleanup (GDI leak önleme)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

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

    // QuickActionForm için public property'ler
    public bool IsShifted { get { return isShifted; } }
    public int ShiftAmount { get { return shiftAmount; } }
    public int RemainingMinutes { get { return remainingMinutes; } set { remainingMinutes = value; } }
    public bool UntilEndOfDay { get { return untilEndOfDay; } }
    public bool WarningShown { get { return warningShown; } set { warningShown = value; } }

    // Renkler
    private readonly Color normalColor = Color.FromArgb(107, 114, 128); // Gri
    private readonly Color shiftedColor = Color.FromArgb(239, 68, 68);  // Kırmızı
    private readonly Color warningColor = Color.FromArgb(251, 191, 36); // Sarı

    public TimeShifter()
    {
        // Form'u gizle (ana form arka planda çalışır)
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Load += (s, e) => {
            this.Visible = false;
            // Başlangıçta QuickActionForm'u göster
            ShowQuickActionForm();
        };

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
        trayMenu.Items.Add("İleri Al: 1 yıl", null, (s, e) => OnShiftForward(12));
        trayMenu.Items.Add("İleri Al: 3 ay", null, (s, e) => OnShiftForward(3));
        trayMenu.Items.Add("İleri Al: 1 ay", null, (s, e) => OnShiftForward(1));
        trayMenu.Items.Add(new ToolStripSeparator());
        
        // Reset süresi seçenekleri
        trayMenu.Items.Add("Sıfırlama süresi: 10 dk", null, null).Enabled = false;
        trayMenu.Items.Add("   10 dakika", null, (s, e) => SetDuration(10, false));
        trayMenu.Items.Add("   30 dakika", null, (s, e) => SetDuration(30, false));
        trayMenu.Items.Add("   2 saat", null, (s, e) => SetDuration(120, false));
        trayMenu.Items.Add("   Gün sonuna kadar", null, (s, e) => SetDuration(0, true));
        trayMenu.Items.Add(new ToolStripSeparator());
        
        trayMenu.Items.Add("Saati Geri Al", null, OnResetTime);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Çıkış", null, OnExit);

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

        trayIcon.MouseClick += (s, e) =>
        {
            // Sadece sol tık ile hızlı erişim penceresini aç (sağ tık context menüyü açacak)
            if (e.Button == MouseButtons.Left)
            {
                ShowQuickActionForm();
            }
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

    public void SetDuration(int minutes, bool untilEndOfDayMode)
    {
        defaultMinutes = minutes;
        untilEndOfDay = untilEndOfDayMode;
        
        string durationText = untilEndOfDayMode ? "Gün sonuna kadar" : string.Format("{0} dk", minutes);
        ((ToolStripMenuItem)trayMenu.Items[4]).Text = string.Format("Sıfırlama süresi: {0}", durationText);
        
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

    public void OnShiftForward(int months)
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

        // Tamamlandı bildirimi
        string shiftText = months == 12 ? "1 yıl" : months == 3 ? "3 ay" : "1 ay";
        string resetText = untilEndOfDay ? "Gün sonuna kadar" : string.Format("{0} dakika", remainingMinutes);
        ShowNotification(
            string.Format("Saat {0} ileri alındı\nOtomatik geri alma: {1}", shiftText, resetText),
            ToolTipIcon.Info);
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

        // 1 dakika kala uyarı
        if (remainingMinutes == 1 && !warningShown)
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
        trayIcon.Icon = CreateIcon(warningColor, "1");
        
        string extendPreview = untilEndOfDay ? "gün sonuna kadar" : string.Format("+{0} dakika", defaultMinutes);
        var result = MessageBox.Show(
            string.Format("Saat 1 dakika içinde geri alınacak.\n\nSüreyi uzatmak ister misiniz? ({0})", extendPreview),
            "TimeShifter - Süre Bitiyor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);

        if (result == DialogResult.Yes)
        {
            // ADDITIVE: Süreyi mevcut kalan süreye EKLE
            if (untilEndOfDay)
            {
                DateTime now = DateTime.Now;
                DateTime endOfDay = now.Date.AddDays(1).AddSeconds(-1);
                remainingMinutes = (int)(endOfDay - now).TotalMinutes;
            }
            else
            {
                remainingMinutes += defaultMinutes; // Toplama işlemi
            }
            warningShown = false;
            UpdateTrayIcon();
            
            string extendText = untilEndOfDay ? "gün sonuna kadar" : string.Format("+{0} dakika", defaultMinutes);
            MessageBox.Show(
                string.Format("Süreye {0} eklendi.\nYeni kalan süre: {1} dakika", extendText, remainingMinutes),
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

    public void OnResetTime(object sender, EventArgs e)
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
        
        // Tamamlandı bildirimi
        ShowNotification("Saat geri alındı ve senkronize edildi.", ToolTipIcon.Info);
    }

    public void UpdateTrayIcon()
    {
        if (isShifted)
        {
            string text = remainingMinutes > 0 ? remainingMinutes.ToString() : "!";
            Color color = remainingMinutes <= 1 ? warningColor : shiftedColor;
            
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

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using (Icon temp = Icon.FromHandle(hIcon))
                {
                    return (Icon)temp.Clone(); // Clone edip handle bağımlılığını kopar
                }
            }
            finally
            {
                try { DestroyIcon(hIcon); } catch { }
            }
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

    public void OnExit(object sender, EventArgs e)
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

    private void ShowQuickActionForm()
    {
        using (QuickActionForm form = new QuickActionForm(this))
        {
            form.ShowDialog();
        }
    }

    private void ShowNotification(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (trayIcon != null)
        {
            trayIcon.BalloonTipTitle = "TimeShifter";
            trayIcon.BalloonTipText = message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(3500); // 3.5 saniye
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

// Hızlı Erişim Formu
public class QuickActionForm : Form
{
    private TimeShifter parent;
    private RadioButton rb1Month, rb3Months, rb1Year;
    private RadioButton rb10Min, rb30Min, rb2Hours, rbUntilEndOfDay;
    private Button btnPrimary, btnSecondary, btnCancel;
    private Label lblStatusPrimary, lblStatusSecondary;
    private GroupBox gbShift, gbDuration;
    private Label lblDurationHelper;
    private System.Windows.Forms.Timer uiTimer;
    private bool isShifted;
    private Panel statusPanel;

    // UI Colors - Windows 11 Fluent Design (simplified)
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);        // Primary blue
    private static readonly Color AccentHoverColor = Color.FromArgb(0, 103, 192);   // Darker blue
    private static readonly Color NeutralColor = Color.FromArgb(107, 114, 128);     // Gray
    private static readonly Color IdleFormBg = Color.FromArgb(252, 252, 252);       // Form background
    private static readonly Color StatusPanelBg = Color.FromArgb(248, 250, 252);    // Status panel background
    private static readonly Color BorderColor = Color.FromArgb(229, 231, 235);      // Soft border

    public QuickActionForm(TimeShifter parent)
    {
        this.parent = parent;
        this.isShifted = parent.IsShifted;
        InitializeForm();
    }

    private void InitializeForm()
    {
        this.Text = isShifted ? "TimeShifter — Aktif" : "TimeShifter";
        // Form yüksekliği: Butonlar için yeterli alan bırak
        this.Size = new Size(380, isShifted ? 380 : 355);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.AutoScaleMode = AutoScaleMode.Dpi; // DPI-aware scaling
        this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        this.Padding = new Padding(16); // Increased padding for modern feel

        // Task 3: Simplified - same background for both states
        this.BackColor = IdleFormBg;

        // Header
        Label lblTitle = new Label
        {
            Text = "TimeShifter",
            AutoSize = true,
            // Task 3: Consistent font, no color change
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(this.Padding.Left, this.Padding.Top),
            ForeColor = SystemColors.ControlText // Neutral color for both states
        };

        Label lblSubtitle = new Label
        {
            Text = isShifted ? "Sistem saati ileri alınmış durumda" : "Sistem saatini ileri alma",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(this.Padding.Left, this.Padding.Top + 24)
        };

        this.Controls.Add(lblTitle);
        this.Controls.Add(lblSubtitle);

        int startY = this.Padding.Top + 50;

        // Status Panel (sadece ileri alınmışken) - Task 3: Simplified, no progress bar
        if (isShifted)
        {
            statusPanel = new Panel
            {
                Location = new Point(this.Padding.Left, startY),
                Size = new Size(this.ClientSize.Width - this.Padding.Horizontal, 64), // Reduced height
                BackColor = StatusPanelBg,
                BorderStyle = BorderStyle.None
            };

            // Task 3: Simple border only, no accent strip
            statusPanel.Paint += (s, e) =>
            {
                try
                {
                    var g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var r = statusPanel.ClientRectangle;

                    // Draw soft border with rounded corners (4px radius) - low contrast
                    using (var pen = new Pen(Color.FromArgb(77, BorderColor), 1f)) // 30% opacity
                    {
                        int radius = 4;
                        DrawRoundedRectangle(g, pen, r.X, r.Y, r.Width - 1, r.Height - 1, radius);
                    }
                    // No accent bar - removed per Task 3
                }
                catch { }
            };

            // Icon aligned with first line text baseline
            PictureBox pbIcon = new PictureBox
            {
                Location = new Point(14, 14),
                Size = new Size(24, 24),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = CreateClockBitmapMDL2(24, NeutralColor), // Native Windows Clock icon
                BackColor = Color.Transparent
            };

            // Primary text - semibold but not too bold
            lblStatusPrimary = new Label
            {
                Text = GetStatusPrimaryText(),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(48, 14), // Aligned with icon
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(55, 65, 81) // Dark gray
            };

            // Secondary text
            lblStatusSecondary = new Label
            {
                Text = GetStatusSecondaryText(),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(48, 36),
                BackColor = Color.Transparent,
                ForeColor = SystemColors.GrayText
            };

            // No progress bar - removed per Task 3

            statusPanel.Controls.Add(pbIcon);
            statusPanel.Controls.Add(lblStatusPrimary);
            statusPanel.Controls.Add(lblStatusSecondary);
            this.Controls.Add(statusPanel);

            startY += statusPanel.Height + 16;

            // Status canlı güncelle
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += (s, e) =>
            {
                if (lblStatusPrimary != null) lblStatusPrimary.Text = GetStatusPrimaryText();
                if (lblStatusSecondary != null) lblStatusSecondary.Text = GetStatusSecondaryText();
            };
            uiTimer.Start();
        }

        // İleri alma seçenekleri (sadece normal durumda) - Task 4: Fixed alignment
        if (!isShifted)
        {
            gbShift = CreateStyledGroupBox("İleri alma miktarı", startY, 70);

            // Task 4: Manual layout for perfect alignment
            int radioY = 24;
            int col1X = 16;
            int col2X = 120;
            int col3X = 224;

            rb1Month = CreateStyledRadioButton("1 ay", col1X, radioY, true, 0);
            rb3Months = CreateStyledRadioButton("3 ay", col2X, radioY, false, 1);
            rb1Year = CreateStyledRadioButton("1 yıl", col3X, radioY, false, 2);

            gbShift.Controls.Add(rb1Month);
            gbShift.Controls.Add(rb3Months);
            gbShift.Controls.Add(rb1Year);

            this.Controls.Add(gbShift);
            startY += 80;
        }

        // Reset/Uzatma süresi seçenekleri - Task 4 & 5: Fixed layout
        // Task 5: Fixed height to prevent layout jump (includes space for helper text)
        int durationHeight = isShifted ? 120 : 110;
        gbDuration = CreateStyledGroupBox(isShifted ? "Süreyi uzat" : "Sıfırlama süresi", startY, durationHeight);

        // Task 4: Manual layout for perfect alignment
        int baseY = isShifted ? 18 : 24;
        int durCol1X = 16;
        int durCol2X = 175;
        int rowSpacing = 28;

        // Subtitle for active state - ADDITIVE davranışı açıkça belirt
        if (isShifted)
        {
            var lblDurationSubtitle = new Label
            {
                Text = "Seçilen süre kalan süreye eklenir",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(durCol1X, baseY),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point)
            };
            gbDuration.Controls.Add(lblDurationSubtitle);
            baseY += 22;
        }

        // Radio buttons with fixed positions (Task 4)
        // isShifted durumunda "+" prefix ile ADDITIVE davranışı göster
        string prefix = isShifted ? "+" : "";
        rb10Min = CreateStyledRadioButton(prefix + "10 dakika", durCol1X, baseY, !isShifted, 3);
        rb2Hours = CreateStyledRadioButton(prefix + "2 saat", durCol2X, baseY, false, 5);
        rb30Min = CreateStyledRadioButton(prefix + "30 dakika", durCol1X, baseY + rowSpacing, isShifted, 4);
        rbUntilEndOfDay = CreateStyledRadioButton("Gün sonuna kadar", durCol2X, baseY + rowSpacing, false, 6);

        gbDuration.Controls.Add(rb10Min);
        gbDuration.Controls.Add(rb2Hours);
        gbDuration.Controls.Add(rb30Min);
        gbDuration.Controls.Add(rbUntilEndOfDay);

        // Task 5: Helper text with reserved space (always present, visibility controlled)
        lblDurationHelper = new Label
        {
            Text = "",
            AutoSize = false,
            Size = new Size(gbDuration.Width - 32, 18),
            Location = new Point(durCol1X, baseY + rowSpacing * 2 + 4),
            ForeColor = Color.FromArgb(107, 114, 128),
            Font = new Font("Segoe UI", 8F, FontStyle.Italic, GraphicsUnit.Point)
        };
        gbDuration.Controls.Add(lblDurationHelper);

        rbUntilEndOfDay.CheckedChanged += (s, e) =>
        {
            if (lblDurationHelper != null)
                lblDurationHelper.Text = rbUntilEndOfDay.Checked ? "Sıfırlama bugün 23:59'da yapılır." : "";
        };
        // İlk render
        if (lblDurationHelper != null)
            lblDurationHelper.Text = rbUntilEndOfDay.Checked ? "Sıfırlama bugün 23:59'da yapılır." : "";

        this.Controls.Add(gbDuration);

        // Butonlar (bottom-right) - Task 3: Styled buttons with hierarchy
        FlowLayoutPanel buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(this.Padding.Left, startY + durationHeight + 16),
            Width = this.ClientSize.Width - this.Padding.Horizontal,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        // Tertiary button: İptal - lowest visual weight
        btnCancel = CreateStyledButton("İptal", ButtonStyle.Neutral, 9);
        btnCancel.Size = new Size(80, 32); // Smaller for tertiary
        btnCancel.DialogResult = DialogResult.Cancel;

        if (isShifted)
        {
            // Task 3: Primary button (accent blue)
            btnPrimary = CreateStyledButton("Uzat", ButtonStyle.Primary, 7);
            btnPrimary.Click += (s, e) => { this.Hide(); ExtendTime(); this.Close(); };
            btnPrimary.Image = CreateAddBitmap(16, Color.White); // Native Windows Add icon
            btnPrimary.ImageAlign = ContentAlignment.MiddleLeft;
            btnPrimary.TextImageRelation = TextImageRelation.ImageBeforeText;

            // Task 3: Secondary button - neutral style (not danger)
            btnSecondary = CreateStyledButton("Geri Al", ButtonStyle.Neutral, 8);
            btnSecondary.Click += (s, e) =>
            {
                // UX: Onay al
                var result = MessageBox.Show(
                    "Sistem saati normale dönecek. Devam etmek istiyor musunuz?",
                    "Saat geri alınacak",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (result != DialogResult.Yes)
                    return;

                this.Hide();
                parent.OnResetTime(null, null);
                this.Close();
            };
            btnSecondary.Image = CreateUndoBitmap(16, NeutralColor); // Native Windows undo icon
            btnSecondary.ImageAlign = ContentAlignment.MiddleLeft;
            btnSecondary.TextImageRelation = TextImageRelation.ImageBeforeText;

            buttonRow.Controls.Add(btnCancel);
            buttonRow.Controls.Add(btnSecondary);
            buttonRow.Controls.Add(btnPrimary);

            // Enter → primary (Uzat)
            this.AcceptButton = btnPrimary;
            this.CancelButton = btnCancel;
        }
        else
        {
            // Task 3: Primary button (accent blue)
            btnPrimary = CreateStyledButton("İleri Al", ButtonStyle.Primary, 7);
            btnPrimary.Click += (s, e) => { this.Hide(); ShiftForward(); this.Close(); };
            btnPrimary.Image = CreateForwardBitmap(16, Color.White); // Native Windows Forward icon
            btnPrimary.ImageAlign = ContentAlignment.MiddleLeft;
            btnPrimary.TextImageRelation = TextImageRelation.ImageBeforeText;

            buttonRow.Controls.Add(btnCancel);
            buttonRow.Controls.Add(btnPrimary);

            this.AcceptButton = btnPrimary;
            this.CancelButton = btnCancel;
        }

        this.Controls.Add(buttonRow);

        // Focus ve Enter tuşu desteği
        this.Shown += (s, e) =>
        {
            this.Activate();
            if (btnPrimary != null) btnPrimary.Focus();
        };

        // Enter ve Esc tuşları için
        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        };
    }

    // Button style enum
    private enum ButtonStyle { Primary, Neutral }

    // Create styled button with consistent height and appearance
    private Button CreateStyledButton(string text, ButtonStyle style, int tabIndex)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(110, 32), // Consistent height
            TabIndex = tabIndex,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        btn.Padding = new Padding(12, 0, 12, 0);
        btn.TextAlign = ContentAlignment.MiddleCenter;
        btn.Margin = new Padding(8, 0, 0, 0);

        switch (style)
        {
            case ButtonStyle.Primary:
                btn.BackColor = AccentColor;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = AccentHoverColor;
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 90, 158);
                break;

            case ButtonStyle.Neutral:
                btn.BackColor = Color.White;
                btn.ForeColor = Color.FromArgb(55, 65, 81);
                btn.FlatAppearance.BorderColor = BorderColor;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(249, 250, 251);
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(243, 244, 246);
                break;
        }

        // Task 9: Focus ring (handled via Paint for custom appearance)
        btn.GotFocus += (s, e) => btn.Invalidate();
        btn.LostFocus += (s, e) => btn.Invalidate();
        btn.Paint += (s, e) =>
        {
            if (btn.Focused)
            {
                var rect = btn.ClientRectangle;
                rect.Inflate(-2, -2);
                using (var pen = new Pen(AccentColor, 2f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        };

        return btn;
    }

    // Task 4: Create styled radio button with fixed position
    private RadioButton CreateStyledRadioButton(string text, int x, int y, bool isChecked, int tabIndex)
    {
        return new RadioButton
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y),
            Checked = isChecked,
            TabIndex = tabIndex,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    // Task 8: Create styled GroupBox with rounded corners
    private GroupBox CreateStyledGroupBox(string text, int y, int height)
    {
        var gb = new GroupBox
        {
            Text = text,
            Location = new Point(this.Padding.Left, y),
            Size = new Size(this.ClientSize.Width - this.Padding.Horizontal, height),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(55, 65, 81)
        };
        return gb;
    }

    // Draw rounded rectangle
    private static void DrawRoundedRectangle(Graphics g, Pen pen, int x, int y, int width, int height, int radius)
    {
        using (var path = CreateRoundedRectPath(new Rectangle(x, y, width, height), radius))
        {
            g.DrawPath(pen, path);
        }
    }

    // Helper: Create rounded rectangle path
    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    // Native Windows icon using Segoe MDL2 Assets font (Windows 10/11)
    private static Bitmap CreateIconFromMDL2(int size, Color color, string glyphChar)
    {
        Bitmap bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Segoe MDL2 Assets is the Windows 10/11 icon font
            using (Font iconFont = new Font("Segoe MDL2 Assets", size * 0.7f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(glyphChar, iconFont, brush, new RectangleF(0, 0, size, size), sf);
            }
        }
        return bmp;
    }

    // MDL2 Icon codes: https://docs.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font
    private static Bitmap CreateUndoBitmap(int size, Color color)
    {
        return CreateIconFromMDL2(size, color, "\uE7A7"); // Undo icon
    }

    private static Bitmap CreateAddBitmap(int size, Color color)
    {
        return CreateIconFromMDL2(size, color, "\uE710"); // Add/Plus icon
    }

    private static Bitmap CreateForwardBitmap(int size, Color color)
    {
        return CreateIconFromMDL2(size, color, "\uE72A"); // Forward arrow icon
    }

    private static Bitmap CreateClockBitmapMDL2(int size, Color color)
    {
        return CreateIconFromMDL2(size, color, "\uE823"); // Clock icon
    }

    private string GetStatusText()
    {
        if (!isShifted) return "";
        string shiftText = parent.ShiftAmount == 12 ? "1 yıl" : parent.ShiftAmount == 3 ? "3 ay" : "1 ay";
        string timeText = parent.UntilEndOfDay ? "Gün sonuna kadar" : string.Format("{0} dakika kaldı", parent.RemainingMinutes);
        return string.Format("Durum: Saat {0} ileri\n{1}", shiftText, timeText);
    }

    private string GetStatusPrimaryText()
    {
        if (!isShifted) return "";
        string shiftText = parent.ShiftAmount == 12 ? "1 yıl" : parent.ShiftAmount == 3 ? "3 ay" : "1 ay";
        return string.Format("Saat {0} ileri alındı", shiftText);
    }

    private string GetStatusSecondaryText()
    {
        if (!isShifted) return "";
        if (parent.UntilEndOfDay)
        {
            return "Sıfırlama bugün 23:59’da yapılır.";
        }
        return string.Format("Sıfırlamaya {0} dakika kaldı", parent.RemainingMinutes);
    }

    private void ShiftForward()
    {
        int months = rb1Year.Checked ? 12 : (rb3Months.Checked ? 3 : 1);
        int minutes = rbUntilEndOfDay.Checked ? 0 : (rb2Hours.Checked ? 120 : (rb30Min.Checked ? 30 : 10));
        bool untilEnd = rbUntilEndOfDay.Checked;

        // Reset süresini ayarla
        parent.SetDuration(minutes, untilEnd);
        
        // İleri al
        parent.OnShiftForward(months);
    }

    private void ExtendTime()
    {
        int minutes = rbUntilEndOfDay.Checked ? 0 : (rb2Hours.Checked ? 120 : (rb30Min.Checked ? 30 : 10));
        bool untilEnd = rbUntilEndOfDay.Checked;

        // Reset süresini ayarla
        parent.SetDuration(minutes, untilEnd);
        
        // ADDITIVE: Seçilen süreyi mevcut kalan süreye EKLE
        if (untilEnd)
        {
            parent.RemainingMinutes = (int)(DateTime.Now.Date.AddDays(1).AddSeconds(-1) - DateTime.Now).TotalMinutes;
        }
        else
        {
            parent.RemainingMinutes += minutes; // Toplama işlemi
        }
        parent.WarningShown = false;
        parent.UpdateTrayIcon();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            if (uiTimer != null)
            {
                uiTimer.Stop();
                uiTimer.Dispose();
                uiTimer = null;
            }
        }
        catch { }
        base.OnFormClosed(e);
    }
}
