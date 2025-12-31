// TimeShifter - System Tray Saat Değiştirici
// Derlemek için: csc /target:winexe /win32icon:icon.ico TimeShifter.cs
// Veya doğrudan çalıştır: dotnet script TimeShifter.cs

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;

public class TimeShifter : Form
{
    // Win32 API for setting system time
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemTime(ref SYSTEMTIME st);

    [DllImport("kernel32.dll")]
    private static extern void GetSystemTime(ref SYSTEMTIME st);

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
    private int defaultMinutes = 30;
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
        trayMenu.Items.Add("⏩ Saati 1 Yıl İleri Al", null, OnShiftForward);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("⏱️ Süre: 30 dk", null, null).Enabled = false;
        trayMenu.Items.Add("   15 dakika", null, (s, e) => SetDuration(15));
        trayMenu.Items.Add("   30 dakika", null, (s, e) => SetDuration(30));
        trayMenu.Items.Add("   60 dakika", null, (s, e) => SetDuration(60));
        trayMenu.Items.Add("   120 dakika", null, (s, e) => SetDuration(120));
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

        trayIcon.DoubleClick += (s, e) =>
        {
            if (isShifted)
                OnResetTime(s, e);
            else
                OnShiftForward(s, e);
        };
    }

    private void InitializeTimer()
    {
        countdownTimer = new System.Windows.Forms.Timer();
        countdownTimer.Interval = 60000; // 1 dakika
        countdownTimer.Tick += OnTimerTick;
    }

    private void SetDuration(int minutes)
    {
        defaultMinutes = minutes;
        ((ToolStripMenuItem)trayMenu.Items[2]).Text = string.Format("⏱️ Süre: {0} dk", minutes);
        
        // Tick işareti güncelle
        for (int i = 3; i <= 6; i++)
        {
            var item = (ToolStripMenuItem)trayMenu.Items[i];
            item.Checked = item.Text.Contains(minutes.ToString());
        }
    }

    private void OnShiftForward(object sender, EventArgs e)
    {
        if (isShifted)
        {
            MessageBox.Show("Saat zaten ileri alınmış!", "TimeShifter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // İşlem popup'ı göster
        Form progressForm = ShowProgressForm("Saat ileri alınıyor...\nLütfen bekleyin.");
        Application.DoEvents();

        try
        {
            // Windows Time servisini durdur
            StopTimeService();
            Application.DoEvents();

            // Saati kaydet ve 1 yıl ileri al
            originalTime = DateTime.UtcNow;
            
            SYSTEMTIME st = new SYSTEMTIME();
            GetSystemTime(ref st);
            st.wYear = (ushort)(st.wYear + 1);
            SetSystemTime(ref st);
            Application.DoEvents();

            shiftedTime = DateTime.UtcNow;
            isShifted = true;
            warningShown = false;
            remainingMinutes = defaultMinutes;

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
        MessageBox.Show(
            string.Format("Saat 1 yıl ileri alındı.\nOtomatik geri alma: {0} dakika", remainingMinutes),
            "TimeShifter - İşlem Tamamlandı",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnTimerTick(object sender, EventArgs e)
    {
        if (!isShifted) return;

        remainingMinutes--;
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
            remainingMinutes = defaultMinutes;
            warningShown = false;
            UpdateTrayIcon();
            
            MessageBox.Show(
                string.Format("Süre {0} dakika uzatıldı.", defaultMinutes),
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

            // Windows Time servisini başlat
            StartTimeService();
            Application.DoEvents();

            isShifted = false;
            originalTime = null;
            shiftedTime = null;
            warningShown = false;

            UpdateTrayIcon();
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
            trayIcon.Text = string.Format("TimeShifter - {0} dk kaldı\nSaat 1 yıl ileri", remainingMinutes);
            
            ((ToolStripMenuItem)trayMenu.Items[0]).Enabled = false;
            ((ToolStripMenuItem)trayMenu.Items[8]).Enabled = true;
        }
        else
        {
            trayIcon.Icon = CreateIcon(normalColor, "");
            trayIcon.Text = "TimeShifter - Hazır";
            
            ((ToolStripMenuItem)trayMenu.Items[0]).Enabled = true;
            ((ToolStripMenuItem)trayMenu.Items[8]).Enabled = false;
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

            psi.FileName = "w32tm";
            psi.Arguments = "/resync /force";
            process = Process.Start(psi);
            if (process != null)
                process.WaitForExit();
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
