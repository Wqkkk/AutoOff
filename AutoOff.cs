// AutoOff · 定时关机/睡眠小工具（WinForms 单文件，用系统自带 .NET Framework 4，免安装、零依赖、不落盘）
// 功能：倒计时(时:分:秒)到点执行 关机 或 睡眠；「熄屏但电脑不睡眠」立即熄屏、机器继续跑；
//       倒计时/保持唤醒期间用 SetThreadExecutionState 挡住系统闲置睡眠，点「取消」随时解除；
//       单实例锁：已在后台运行时再双击只提示一次。
// 命令行：--dry-run 自检（电源动作只写日志不执行，结果落 %TEMP%\autooff-selftest.log，退出码 0/1）
//         --preview 界面预演（电源动作同样只写日志，给构建后截图验收用，标题带「预演」字样）
// 编译：powershell -File build.ps1 [-Desktop]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();   // 不做的话 125%/150% 缩放上被系统拉伸发虚

    [STAThread]
    static int Main(string[] argv)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        if (argv.Length > 0 && argv[0] == "--dry-run") return SelfTest.Run();
        bool preview = argv.Length > 0 && argv[0] == "--preview";
        if (!preview)
        {
            // 只在真跑时抢锁；自检/预演放行，不然测试机和正主互相顶
            using (var gate = new System.Threading.Mutex(false, "AutoOff-single-instance"))
            {
                if (!gate.WaitOne(0)) { MessageBox.Show("AutoOff 已经在后台运行了，双击只应该一次。", "AutoOff"); return 0; }
                return RunUi(preview);
            }
        }
        return RunUi(preview);
    }

    static int RunUi(bool preview)
    {
        var power = new PowerOps { DryRun = preview };
        Application.Run(new MainForm(power, preview));
        return 0;
    }
}

// 系统电源动作集中在这一个类：dry-run 时全部降级为写日志，测试永远碰不到真机器
sealed class PowerOps
{
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1;
    const uint WM_SYSCOMMAND = 0x112, SC_MONITORPOWER = 0xF170, SMTO_ABORTIFHUNG = 0x2;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);

    public bool DryRun;
    public event Action<string> Log;

    public void KeepAwake(bool on)
    {
        // 连续调用置在当前线程上，直到再调一次裸 ES_CONTINUOUS 才解除
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
        Say(on ? "保持唤醒：开" : "保持唤醒：关");
    }

    public void ScreenOff()
    {
        if (!DryRun)
        {
            IntPtr r;   // 广播 SC_MONITORPOWER(2=熄屏)；用带超时的广播，防某个卡死的窗口挂住调用
            SendMessageTimeout((IntPtr)0xFFFF, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2, SMTO_ABORTIFHUNG, 500, out r);
        }
        Say("熄屏");
    }

    public void Shutdown()
    {
        if (!DryRun) Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = true });
        Say("关机");
    }

    public void Sleep()
    {
        if (!DryRun) Application.SetSuspendState(PowerState.Suspend, false, false);
        Say("睡眠");
    }

    void Say(string s) { if (Log != null) Log(s); }
}

sealed class MainForm : Form
{
    public enum Mode { Shutdown, Sleep }
    public enum State { Idle, Counting, Holding }   // Holding = 熄屏保持唤醒中

    readonly PowerOps power;
    readonly double unit;
    NumericUpDown nH, nM, nS;
    Button go, sleepBtn, cancelBtn, screenBtn;
    Label status, caption;
    System.Windows.Forms.Timer tick;
    State state = State.Idle;
    Mode mode;
    DateTime deadline;

    public State Now { get { return state; } }
    public event Action<Mode> Fired;                // 自检挂它确认到点触发过

    int S(int v) { return (int)Math.Round(v * unit); }   // 96DPI 设计像素 → 当前物理像素

    public MainForm(PowerOps power, bool preview)
    {
        this.power = power;
        using (var dc = Graphics.FromHwnd(IntPtr.Zero)) unit = Math.Max(1.0, dc.DpiX / 96.0);
        Text = "AutoOff · 定时关机" + (preview ? "（预演）" : "");
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;          // 几何全部走 S() 手动缩放，WinForms 自动缩放在这套布局上不可靠
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(S(296), S(206));
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);   // 打开居中
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);   // 用嵌入 exe 的图标（标题栏/任务栏）

        int y = S(10);
        Controls.Add(new Label { Text = "时长", Left = S(12), Top = y + S(4), Width = S(38), Height = S(20), TextAlign = ContentAlignment.MiddleLeft });
        nH = Spin(S(52), y, 0, 23);  Lab("时", S(100)); nM = Spin(S(118), y, 0, 59); Lab("分", S(166)); nS = Spin(S(184), y, 0, 59); Lab("秒", S(232));
        nM.Value = 30;                                // 默认 30 分钟，最常见的"睡前一档"

        y = S(42);
        Preset(S(52), "15分", 0, 15, 0); Preset(S(106), "30分", 0, 30, 0); Preset(S(160), "1时", 1, 0, 0); Preset(S(214), "2时", 2, 0, 0);

        y = S(72);
        status = new Label { Left = S(12), Top = y, Width = S(272), Height = S(30), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold) };
        caption = new Label { Left = S(12), Top = y + S(32), Width = S(272), Height = S(18), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray };
        Controls.Add(status); Controls.Add(caption);

        y = S(124);
        go = Btn("关机", Color.FromArgb(198, 57, 57), Color.White, S(12), y, S(84));
        go.Click += delegate { StartFromSpinners(Mode.Shutdown); };
        sleepBtn = Btn("睡眠", Color.FromArgb(64, 120, 192), Color.White, S(102), y, S(84));
        sleepBtn.Click += delegate { StartFromSpinners(Mode.Sleep); };
        cancelBtn = Btn("取消", Color.FromArgb(228, 228, 228), Color.Black, S(192), y, S(92));
        cancelBtn.Click += delegate { CancelAll(); };
        AcceptButton = go;                            // 回车＝按当前时长直接开始倒计时关机（文案里写明了）

        y = S(164);
        screenBtn = Btn("熄屏但电脑不睡眠", Color.FromArgb(76, 175, 80), Color.White, S(12), y, S(272));
        screenBtn.Click += delegate { HoldScreen(); };

        tick = new System.Windows.Forms.Timer { Interval = 200 };
        tick.Tick += delegate { OnTick(); };

        FormClosed += delegate { tick.Stop(); power.KeepAwake(false); };   // 无论怎么退出，别把「保持唤醒」留在系统里
        SetIdle();
    }

    void Lab(string t, int x) { Controls.Add(new Label { Text = t, Left = x, Top = S(14), Width = S(16), Height = S(20), TextAlign = ContentAlignment.MiddleLeft }); }

    NumericUpDown Spin(int x, int y, int min, int max)
    {
        var n = new NumericUpDown { Left = x, Top = y, Width = S(46), Height = S(24), Minimum = min, Maximum = max, TextAlign = HorizontalAlignment.Center };
        n.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartFromSpinners(Mode.Shutdown); } };
        Controls.Add(n);
        return n;
    }

    void Preset(int x, string t, int h, int m, int s)
    {
        var b = new Button { Text = t, Left = x, Top = S(42), Width = S(48), Height = S(22), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(240, 240, 240) };
        b.FlatAppearance.BorderSize = 0;
        b.Click += delegate { nH.Value = h; nM.Value = m; nS.Value = s; };
        Controls.Add(b);
    }

    Button Btn(string t, Color back, Color fore, int x, int y, int w)
    {
        var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = S(34), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore };
        b.FlatAppearance.BorderSize = 0;
        Controls.Add(b);
        return b;
    }

    void StartFromSpinners(Mode m)
    {
        int total = (int)nH.Value * 3600 + (int)nM.Value * 60 + (int)nS.Value;
        if (total == 0) { Flash("先设时长，别从 0 开始", S(1400)); return; }
        StartCountdown(m, TimeSpan.FromSeconds(total));
    }

    // 核心：进入倒计时。自检传短时长直接调它；到点触发 Fired，然后回 Idle
    public void StartCountdown(Mode m, TimeSpan d)
    {
        mode = m;
        deadline = DateTime.UtcNow + d;               // UtcNow，不怕中途跨夏令时/手动改时间把倒计时拉飞
        state = State.Counting;
        TopMost = true;                               // 倒计时中别被别的窗盖住，用户回来能一眼看到
        cancelBtn.BackColor = Color.FromArgb(255, 171, 64);
        power.KeepAwake(true);                        // 倒计时期间挡住闲置睡眠
        tick.Start();
        OnTick();
    }

    // 立即熄屏 + 保持唤醒，直到「取消」
    public void HoldScreen()
    {
        state = State.Holding;
        TopMost = true;
        cancelBtn.BackColor = Color.FromArgb(255, 171, 64);
        power.KeepAwake(true);
        power.ScreenOff();
        status.Text = "已熄屏 · 电脑保持清醒";
        status.ForeColor = Color.FromArgb(46, 125, 50);
        caption.Text = "点「取消」恢复";
    }

    public void CancelAll()
    {
        if (state == State.Idle) return;
        tick.Stop();
        power.KeepAwake(false);
        state = State.Idle;
        TopMost = false;
        cancelBtn.BackColor = Color.FromArgb(228, 228, 228);
        SetIdle();
    }

    void OnTick()
    {
        if (state != State.Counting) return;
        TimeSpan left = deadline - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            Mode m = mode;
            CancelAll();                              // 复位窗口状态；动作本身在复位后执行
            power.KeepAwake(false);
            if (m == Mode.Shutdown) power.Shutdown(); else power.Sleep();
            if (Fired != null) Fired(m);
            return;
        }
        status.Text = Fmt(left) + (mode == Mode.Shutdown ? " 后关机" : " 后睡眠");
        status.ForeColor = Color.FromArgb(183, 63, 40);
        caption.Text = "倒计时中 · 保持唤醒 · 点「取消」解除";
    }

    static string Fmt(TimeSpan t)
    {
        return t.Hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", t.Hours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", t.Minutes + t.Hours * 60, t.Seconds);
    }

    void SetIdle()
    {
        status.Text = "设好时长 → 点「关机」或「睡眠」";
        status.ForeColor = Color.Gray;
        caption.Text = "回车＝按当前时长开始倒计时关机";
    }

    void Flash(string msg, int ms)                   // 状态行临时提示，之后自动回到闲置文案
    {
        status.Text = msg;
        status.ForeColor = Color.FromArgb(183, 63, 40);
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += delegate { t.Stop(); t.Dispose(); if (state == State.Idle) SetIdle(); };
        t.Start();
    }
}

// 构建期自检：真开窗真走倒计时/取消/熄屏，但 PowerOps.DryRun=true，任何电源动作都只落日志
static class SelfTest
{
    public static int Run()
    {
        var bad = new List<string>();
        var logs = new List<string>();
        try
        {
            var power = new PowerOps { DryRun = true };
            power.Log += s => logs.Add(s);
            var f = new MainForm(power, preview: true) { Opacity = 0, ShowInTaskbar = false };
            f.Show();
            Pump(300);
            Check("闲置文案非空", f.Controls.Count >= 10 && f.Text.Length > 0, bad);

            // 1) 关机倒计时到点：1.2 秒后必须触发 Fired 且日志里有「关机」，状态回到 Idle
            MainForm.Mode? got = null;
            f.Fired += m => got = m;
            f.StartCountdown(MainForm.Mode.Shutdown, TimeSpan.FromSeconds(1.2));
            Check("倒计时中状态", f.Now == MainForm.State.Counting, bad);
            Pump(4000);
            Check("到点触发关机", got == MainForm.Mode.Shutdown, bad);
            Check("关机动作落日志（dry）", logs.Contains("关机"), bad);
            Check("触发后回闲置", f.Now == MainForm.State.Idle, bad);

            // 2) 睡眠倒计时中途取消：必须不触发
            got = null;
            f.StartCountdown(MainForm.Mode.Sleep, TimeSpan.FromSeconds(1.0));
            Pump(300);
            f.CancelAll();
            Pump(2500);
            Check("取消后不触发", got == null, bad);
            Check("取消后回闲置", f.Now == MainForm.State.Idle, bad);
            Check("取消解除保持唤醒", logs.Contains("保持唤醒：关"), bad);

            // 3) 熄屏保持唤醒：进入 Holding、日志有熄屏、取消后回 Idle
            f.HoldScreen();
            Check("熄屏进入 Holding", f.Now == MainForm.State.Holding, bad);
            Pump(100);
            Check("熄屏动作落日志（dry）", logs.Contains("熄屏"), bad);
            f.CancelAll();
            Check("熄屏可取消", f.Now == MainForm.State.Idle, bad);
        }
        catch (Exception e) { bad.Add("EX " + e.Message); }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "autooff-selftest.log"),
            (bad.Count == 0 ? "PASS" : "FAIL " + string.Join(" | ", bad.ToArray())) + " AutoOff");
        return bad.Count == 0 ? 0 : 1;
    }

    static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); }
    }

    static void Check(string name, bool ok, List<string> bad) { if (!ok) bad.Add(name); }
}
