using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QRZLogExport
{
    static class Program
    {
        public const string Version = "1.1";
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    // ---------- ADIF ----------
    static class Adif
    {
        public static string Ascii(string s)
        {
            s = s.Replace("Ø", "O").Replace("ø", "o").Replace("Æ", "AE").Replace("æ", "ae").Replace("ß", "ss")
                 .Replace("Ł", "L").Replace("ł", "l").Replace("Đ", "D").Replace("đ", "d").Replace("Œ", "OE").Replace("œ", "oe")
                 .Replace("Ð", "D").Replace("ð", "d").Replace("Þ", "Th").Replace("þ", "th").Replace("’", "'").Replace("–", "-");
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in d) { if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; sb.Append(ch < 128 ? ch : '?'); }
            return sb.ToString();
        }
        public static string Field(string name, string val)
        {
            val = Ascii(val);
            return "<" + name + ":" + val.Length + ">" + val;
        }
        public static string Write(List<Dictionary<string, string>> qsos)
        {
            var sb = new StringBuilder();
            sb.Append("Exporte depuis QRZ.com Logbook par QRZLogExport " + Program.Version + "\r\n");
            sb.Append(Field("ADIF_VER", "3.1.4") + "\r\n" + Field("PROGRAMID", "QRZLogExport") + "\r\n" + Field("PROGRAMVERSION", Program.Version) + "\r\n<EOH>\r\n\r\n");
            foreach (var q in qsos)
            {
                foreach (var kv in q) if (!string.IsNullOrEmpty(kv.Value) && !kv.Key.StartsWith("_")) sb.Append(Field(kv.Key, kv.Value));
                sb.Append("<EOR>\r\n");
            }
            return sb.ToString();
        }
        public static List<Dictionary<string, string>> Parse(string s)
        {
            var res = new List<Dictionary<string, string>>();
            int p = s.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase);
            p = p < 0 ? 0 : p + 5;
            var cur = new Dictionary<string, string>();
            var re = new Regex(@"<(\w+)(?::(\d+))?(?::\w+)?>", RegexOptions.Compiled);
            while (p < s.Length)
            {
                var m = re.Match(s, p);
                if (!m.Success) break;
                string n = m.Groups[1].Value.ToUpperInvariant();
                p = m.Index + m.Length;
                if (n == "EOR") { if (cur.Count > 0) res.Add(cur); cur = new Dictionary<string, string>(); continue; }
                if (!m.Groups[2].Success) continue;
                int len = int.Parse(m.Groups[2].Value);
                if (p + len > s.Length) len = s.Length - p;
                cur[n] = s.Substring(p, len);
                p += len;
            }
            if (cur.Count > 0) res.Add(cur);
            return res;
        }
        public static void Sort(List<Dictionary<string, string>> l)
        {
            Func<Dictionary<string, string>, string> key = q =>
            {
                string d, t; q.TryGetValue("QSO_DATE", out d); q.TryGetValue("TIME_ON", out t);
                return (d ?? "") + (t ?? "").PadRight(6, '0');
            };
            var s = l.Select((q, i) => new { q, i, k = key(q) }).OrderBy(x => x.k, StringComparer.Ordinal).ThenBy(x => x.i).Select(x => x.q).ToList();
            l.Clear(); l.AddRange(s);
        }
        static readonly HashSet<string> PhoneModes = new HashSet<string> { "SSB", "USB", "LSB", "AM", "FM", "FREEDV", "DSTAR", "C4FM", "DMR", "DIGITALVOICE", "DV", "PHONE" };
        static readonly HashSet<string> CwModes = new HashSet<string> { "CW" };
        static readonly HashSet<string> FtModes = new HashSet<string> { "FT8", "FT4", "FT2" };
        // Rapports par defaut (seulement si le contact n'en a pas). Retourne les modes sans regle -> nombre de contacts.
        public static Dictionary<string, int> ApplyReports(List<Dictionary<string, string>> qsos)
        {
            var unknown = new Dictionary<string, int>();
            foreach (var q in qsos)
            {
                if (q.ContainsKey("RST_SENT") || q.ContainsKey("RST_RCVD")) continue;
                string m; q.TryGetValue("MODE", out m); m = (m ?? "").ToUpperInvariant();
                string r = PhoneModes.Contains(m) ? "59" : CwModes.Contains(m) ? "599" : FtModes.Contains(m) ? "-01" : null;
                if (r == null) { string k = m == "" ? "(vide)" : m; int n; unknown.TryGetValue(k, out n); unknown[k] = n + 1; continue; }
                q["RST_SENT"] = r; q["RST_RCVD"] = r;
            }
            return unknown;
        }
        public static string DedupKey(Dictionary<string, string> q)
        {
            string id; if (q.TryGetValue("_ID", out id) && id != "") return "id" + id;
            string[] ks = { "CALL", "QSO_DATE", "TIME_ON", "BAND", "MODE", "FREQ" };
            return string.Join("|", ks.Select(k => { string v; return q.TryGetValue(k, out v) ? v : ""; }).ToArray());
        }
        static readonly double[][] Bands = {
            new[]{1.8,2.0}, new[]{3.5,4.0}, new[]{5.06,5.45}, new[]{7.0,7.3}, new[]{10.1,10.15}, new[]{14.0,14.35}, new[]{18.068,18.168},
            new[]{21.0,21.45}, new[]{24.89,24.99}, new[]{28.0,29.7}, new[]{50.0,54.0}, new[]{70.0,71.0}, new[]{144.0,148.0}, new[]{420.0,450.0}, new[]{1240.0,1300.0} };
        static readonly string[] BandNames = { "160M","80M","60M","40M","30M","20M","17M","15M","12M","10M","6M","4M","2M","70CM","23CM" };
        public static string BandFromFreq(double mhz)
        {
            for (int i = 0; i < Bands.Length; i++) if (mhz >= Bands[i][0] && mhz <= Bands[i][1]) return BandNames[i];
            return "";
        }
        public static string NormDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var m = Regex.Match(s, @"(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})");
            if (m.Success) return m.Groups[1].Value + m.Groups[2].Value.PadLeft(2, '0') + m.Groups[3].Value.PadLeft(2, '0');
            m = Regex.Match(s, @"\b(\d{8})\b");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(s, @"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})");
            if (m.Success) return m.Groups[3].Value + m.Groups[2].Value.PadLeft(2, '0') + m.Groups[1].Value.PadLeft(2, '0');
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d.ToString("yyyyMMdd");
            if (DateTime.TryParse(s, new CultureInfo("fr-FR"), DateTimeStyles.None, out d)) return d.ToString("yyyyMMdd");
            return "";
        }
        public static string NormTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var m = Regex.Match(s, @"(\d{1,2}):(\d{2})(?::(\d{2}))?");
            if (m.Success) return m.Groups[1].Value.PadLeft(2, '0') + m.Groups[2].Value + (m.Groups[3].Success ? m.Groups[3].Value : "");
            m = Regex.Match(s, @"\b(\d{4}|\d{6})\b");
            return m.Success ? m.Groups[1].Value : "";
        }
    }

    // ---------- Edge piloté par CDP ----------
    class Cdp : IDisposable
    {
        ClientWebSocket ws; int id;
        readonly JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public void Connect(string url)
        {
            ws = new ClientWebSocket();
            ws.ConnectAsync(new Uri(url), CancellationToken.None).Wait();
        }
        public string Eval(string js)
        {
            int my = ++id;
            var req = ser.Serialize(new Dictionary<string, object> {
                { "id", my }, { "method", "Runtime.evaluate" },
                { "params", new Dictionary<string, object> { { "expression", js }, { "returnByValue", true }, { "awaitPromise", true } } } });
            var b = Encoding.UTF8.GetBytes(req);
            ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            var buf = new byte[65536];
            while (true)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                    if (r.MessageType == WebSocketMessageType.Close) throw new Exception("Edge a ferme la connexion.");
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                var d = ser.DeserializeObject(Encoding.UTF8.GetString(ms.ToArray())) as Dictionary<string, object>;
                if (d == null || !d.ContainsKey("id") || Convert.ToInt32(d["id"]) != my) continue;
                if (d.ContainsKey("error")) throw new Exception("CDP : " + ser.Serialize(d["error"]));
                var res = d["result"] as Dictionary<string, object>;
                if (res.ContainsKey("exceptionDetails")) throw new Exception("Erreur JavaScript dans la page : " + ser.Serialize(res["exceptionDetails"]));
                var rr = res["result"] as Dictionary<string, object>;
                object v; return rr.TryGetValue("value", out v) && v != null ? v.ToString() : "";
            }
        }
        // Commande CDP simple (sans retour utile)
        public void Call(string method, Dictionary<string, object> prm)
        {
            int my = ++id;
            var req = ser.Serialize(new Dictionary<string, object> { { "id", my }, { "method", method }, { "params", prm ?? new Dictionary<string, object>() } });
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(req)), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            var buf = new byte[65536];
            for (int guard = 0; guard < 200; guard++)
            {
                var ms = new MemoryStream(); WebSocketReceiveResult r;
                do { r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                var d = ser.DeserializeObject(Encoding.UTF8.GetString(ms.ToArray())) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("id") && Convert.ToInt32(d["id"]) == my) return;
            }
        }
        public void Dispose() { if (ws != null) { try { ws.Dispose(); } catch { } } }
    }

    static class Js
    {
        public const string Extract = @"(function(){
function tx(e){return (e.innerText||e.textContent||'').replace(/\s+/g,' ').trim();}
var best=null,bs=0,ts=document.querySelectorAll('table');
for(var i=0;i<ts.length;i++){var t=ts[i],rs=t.querySelectorAll('tr'),n=0,mc=0;
 for(var j=0;j<rs.length;j++){var c=rs[j].querySelectorAll('td').length;if(c>=4){n++;if(c>mc)mc=c;}}
 if(n*mc>bs){bs=n*mc;best=t;}}
var o={url:location.href,title:document.title,headers:[],rows:[],pages:0,login:Array.prototype.some.call(document.querySelectorAll('input[type=password]'),function(e){return e.offsetParent!==null;}),table:!!best,ipage:(document.getElementById('ipage')?document.getElementById('ipage').value:'')};
var bt=document.body?document.body.innerText:'';
var m=bt.match(/Book\s*#\s*\d+\s+of\s+(\d+)/i);if(m){m=[m[0],'1',m[1]];}
if(!m)m=bt.match(/(?:page|pg)\s*(\d+)\s*(?:of|\/|sur)\s*(\d+)/i)||bt.match(/(\d+)\s*(?:of|sur)\s*(\d+)\s*pages?/i);
if(m){o.pages=parseInt(m[2]);}
else{var as=document.querySelectorAll('a'),mx=0;
 for(var k=0;k<as.length;k++){var tt=tx(as[k]);
  if(/^\d{1,5}$/.test(tt)){var p=as[k].parentNode,g=p?p.parentNode:null;
   var ok=/page|pg=|p=/i.test(as[k].getAttribute('href')||'')||(p&&/pag/i.test(p.className||''))||(g&&/pag/i.test(g.className||''));
   var v=parseInt(tt);if(ok&&v>mx)mx=v;}}
 o.pages=mx;}
if(best){var trs=best.querySelectorAll('tr'),hr=null;
 for(var a=0;a<trs.length;a++){if(trs[a].querySelectorAll('th').length>=3){hr=trs[a];break;}}
 if(!hr){for(var a2=0;a2<trs.length;a2++){if(trs[a2].children.length>=4){hr=trs[a2];break;}}}
 if(hr){var hc=hr.children;for(var h=0;h<hc.length;h++)o.headers.push(tx(hc[h]));}
 var fr=null;for(var a4=0;a4<trs.length;a4++){if(trs[a4]!==hr&&trs[a4].querySelectorAll('td').length>=4){fr=trs[a4];break;}}
 if(fr){var cl=[];var ft=fr.querySelectorAll('td');for(var z=0;z<ft.length;z++)cl.push(ft[z].className||'');cl.push('rownum');o.cols=cl;}
 for(var a3=0;a3<trs.length;a3++){if(trs[a3]===hr)continue;var tds=trs[a3].querySelectorAll('td');
  if(tds.length<4)continue;var row=[];for(var q=0;q<tds.length;q++){var cc=tds[q].className||'',tt=tds[q].getAttribute('title');row.push((/td_(date|time|freq|call|grid)/.test(cc)&&tt)?tt:tx(tds[q]));}row.push(trs[a3].getAttribute('data-rownum')||'');o.rows.push(row);}}
return JSON.stringify(o);})()";

        public const string Next = @"(function(n){
function tx(e){return (e.innerText||e.textContent||'').replace(/\s+/g,' ').trim();}
function dis(e){var p=e.parentNode;return !!(e.disabled||/disabled|inactive/i.test((e.className||'')+' '+(p?(p.className||''):''))||e.getAttribute('aria-disabled')=='true');}
function go(e){if(e.tagName=='LI'||e.tagName=='SPAN'){var a=e.querySelector('a,button');if(a)e=a;}if(dis(e))return false;e.click();return true;}
var r=document.querySelector('a[rel=next]');if(r&&!dis(r)){r.click();return 'rel';}
var c=document.querySelectorAll('a,button,li,input[type=button],input[type=submit]');
for(var i=0;i<c.length;i++){var e=c[i],t=(tx(e)||e.value||'').toLowerCase(),al=(e.getAttribute('aria-label')||e.title||'').toLowerCase();
 if(/^next page/.test(t)||/^(next|suivant|next page|page suivante|>|›|»|>>|next »|next >)$/.test(t)||((/next|suivant/.test(al))&&t.length<14)){if(go(e))return 'next';}}
for(var j=0;j<c.length;j++){if(c[j].tagName=='A'&&tx(c[j])==String(n)){c[j].click();return 'num';}}
return '';})(";
    }

    class PageData
    {
        public string url, title; public List<string> headers = new List<string>(); public List<List<string>> rows = new List<List<string>>();
        public string ipage = ""; public List<string> cols = new List<string>(); public int pages; public bool login, table;
    }

    // ---------- Fenêtre ----------
    class MainForm : Form
    {
        TextBox txtOut, txtLog, txtKey, txtCall, txtMyGrid; CheckBox chkRst;
        Button btnOpen, btnGo, btnApi, btnStop, btnDump, btnBrowse;
        Label lblState;
        ProgressBar bar;
        TabControl tabs;
        volatile bool stop; string testLog;
        Thread worker;
        int port; Process edge;
        readonly JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static readonly string ProfileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QRZLogExport", "edge-profile");

        public MainForm(string[] args)
        {
            Text = "QRZLogExport V" + Program.Version + " @F5PBG 2026";
            Width = 780; Height = 640; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(640, 520);
            Font = new Font("Segoe UI", 9.5f);

            tabs = new TabControl { Dock = DockStyle.Top, Height = 210 };
            var t1 = new TabPage("1. Connecté (navigateur Edge)");
            var t2 = new TabPage("2. Clé API QRZ (plus fiable)");
            tabs.TabPages.Add(t1); tabs.TabPages.Add(t2);

            var h1 = new Label { Left = 10, Top = 10, Width = 730, Height = 70, Text =
                "A. Cliquez sur « Ouvrir QRZ dans Edge » et connectez-vous à QRZ (la connexion est retenue pour la prochaine fois).\r\n" +
                "B. Affichez la liste de VOS contacts (logbook.qrz.com), de préférence en 1re page.\r\n" +
                "C. Cliquez sur « Récupérer tout le journal » : toutes les pages sont parcourues toutes seules." };
            btnOpen = new Button { Left = 10, Top = 90, Width = 210, Height = 34, Text = "Ouvrir QRZ dans Edge" };
            btnGo = new Button { Left = 230, Top = 90, Width = 230, Height = 34, Text = "Récupérer tout le journal" };
            btnDump = new Button { Left = 470, Top = 90, Width = 260, Height = 34, Text = "Enregistrer la page (diagnostic)" };
            t1.Controls.AddRange(new Control[] { h1, btnOpen, btnGo, btnDump });

            var h2 = new Label { Left = 10, Top = 10, Width = 730, Height = 50, Text =
                "Dans QRZ : Logbook > Settings > API Access : copiez la clé de votre journal (format XXXX-XXXX-XXXX-XXXX).\r\nRéservé aux comptes QRZ ayant accès à l'API du journal." };
            var l1 = new Label { Left = 10, Top = 70, Width = 120, Text = "Clé API :" };
            txtKey = new TextBox { Left = 130, Top = 66, Width = 300 };
            var l2 = new Label { Left = 10, Top = 104, Width = 120, Text = "Votre indicatif :" };
            txtCall = new TextBox { Left = 130, Top = 100, Width = 140 };
            btnApi = new Button { Left = 290, Top = 98, Width = 240, Height = 30, Text = "Récupérer via l'API" };
            t2.Controls.AddRange(new Control[] { h2, l1, txtKey, l2, txtCall, btnApi });

            var pnl = new Panel { Dock = DockStyle.Top, Height = 112 };
            var lo = new Label { Left = 10, Top = 12, Width = 120, Text = "Fichier ADIF :" };
            txtOut = new TextBox { Left = 130, Top = 8, Width = 480, Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QRZ-journal.adi") };
            btnBrowse = new Button { Left = 620, Top = 6, Width = 110, Height = 28, Text = "Parcourir..." };
            btnStop = new Button { Left = 10, Top = 42, Width = 100, Height = 28, Text = "Arrêter", Enabled = false };
            bar = new ProgressBar { Left = 120, Top = 44, Width = 610, Height = 24 };
            chkRst = new CheckBox { Left = 10, Top = 78, Width = 470, Checked = true, Text = "Ajouter les rapports : 59/59 phonie, 599/599 CW, -01/-01 FT8/FT4/FT2" };
            var lg = new Label { Left = 490, Top = 81, Width = 100, Text = "Mon locator :" };
            txtMyGrid = new TextBox { Left = 590, Top = 77, Width = 140, MaxLength = 10 };
            pnl.Controls.AddRange(new Control[] { lo, txtOut, btnBrowse, btnStop, bar, chkRst, lg, txtMyGrid });
            lblState = new Label { Dock = DockStyle.Top, Height = 24, Text = "Prêt.", Padding = new Padding(10, 3, 0, 0) };
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };

            Controls.Add(txtLog); Controls.Add(lblState); Controls.Add(pnl); Controls.Add(tabs);

            btnBrowse.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "ADIF (*.adi;*.adif)|*.adi;*.adif", FileName = Path.GetFileName(txtOut.Text), DefaultExt = "adi" }) if (d.ShowDialog() == DialogResult.OK) txtOut.Text = d.FileName; };
            btnOpen.Click += (s, e) => { try { OpenEdge("https://logbook.qrz.com/"); } catch (Exception ex) { Log("ERREUR : " + ex.Message); } };
            btnGo.Click += (s, e) => Start(RunWeb);
            btnApi.Click += (s, e) => Start(RunApi);
            btnDump.Click += (s, e) => Start(RunDump);
            btnStop.Click += (s, e) => { stop = true; Log("Arrêt demandé..."); };

            // Mode test sans clic : QRZLogExport.exe --test <url> <sortie.adi>
            if (args.Length >= 3 && args[0] == "--test")
            {
                txtOut.Text = args[2]; testLog = args[2] + ".log";
                Shown += (s, e) => { try { if (args.Length >= 4 && args[3].StartsWith("port=")) port = int.Parse(args[3].Substring(5)); else OpenEdge(args[1]); } catch (Exception ex) { Log("ERREUR : " + ex.Message); return; } Start(() => { WaitForTable(); RunWeb(); Invoke((Action)Close); }); };
            }
            FormClosing += (s, e) => { stop = true; };
        }

        void Log(string s)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Log(s))); } catch { } return; }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n");
            if (testLog != null) { try { File.AppendAllText(testLog, s + "\r\n"); } catch { } }
            lblState.Text = s;
        }
        void Prog(int v, int max)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Prog(v, max))); } catch { } return; }
            bar.Maximum = Math.Max(1, max); bar.Value = Math.Min(Math.Max(0, v), bar.Maximum);
        }
        void Busy(bool b)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Busy(b))); } catch { } return; }
            btnGo.Enabled = btnApi.Enabled = btnDump.Enabled = btnOpen.Enabled = !b; btnStop.Enabled = b;
        }
        void Start(Action a)
        {
            if (worker != null && worker.IsAlive) return;
            stop = false; Busy(true);
            worker = new Thread(() => { try { a(); } catch (Exception ex) { Log("ERREUR : " + ex.Message); } finally { Busy(false); } }) { IsBackground = true };
            worker.Start();
        }

        // --- Edge ---
        static string FindEdge()
        {
            string[] c = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe") };
            foreach (var p in c) if (File.Exists(p)) return p;
            throw new Exception("Microsoft Edge (ou Chrome) est introuvable.");
        }
        string HttpGet(string url)
        {
            using (var w = new WebClient()) { w.Encoding = Encoding.UTF8; return w.DownloadString(url); }
        }
        bool EdgeAlive()
        {
            if (port == 0) return false;
            try { HttpGet("http://127.0.0.1:" + port + "/json/version"); return true; } catch { return false; }
        }
        void OpenEdge(string url)
        {
            if (EdgeAlive()) { Log("Edge est déjà ouvert (fenêtre QRZ)."); return; }
            var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            Directory.CreateDirectory(ProfileDir);
            var psi = new ProcessStartInfo(FindEdge(), "--remote-debugging-port=" + port + " --remote-allow-origins=* --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --no-first-run --user-data-dir=\"" + ProfileDir + "\" \"" + url + "\"") { UseShellExecute = false };
            edge = Process.Start(psi);
            for (int i = 0; i < 40; i++) { if (EdgeAlive()) { Log("Edge ouvert : connectez-vous à QRZ puis affichez votre journal."); return; } Thread.Sleep(250); }
            throw new Exception("Edge n'a pas répondu.");
        }
        Cdp Attach()
        {
            if (!EdgeAlive()) throw new Exception("Ouvrez d'abord QRZ avec le bouton « Ouvrir QRZ dans Edge ».");
            var tabsList = ser.DeserializeObject(HttpGet("http://127.0.0.1:" + port + "/json")) as object[];
            Dictionary<string, object> pick = null;
            foreach (Dictionary<string, object> t in tabsList)
            {
                if ((string)t["type"] != "page" || !t.ContainsKey("webSocketDebuggerUrl")) continue;
                string u = (string)t["url"];
                if (u.IndexOf("logbook.qrz.com", StringComparison.OrdinalIgnoreCase) >= 0) { pick = t; break; }
                if (pick == null && !u.StartsWith("devtools")) pick = t;
            }
            if (pick == null) throw new Exception("Aucun onglet Edge trouvé.");
            var c = new Cdp(); c.Connect((string)pick["webSocketDebuggerUrl"]);
            // la page doit se croire visible et active, sinon Edge ralentit ou suspend ses chargements
            try { c.Call("Page.bringToFront", null); } catch { }
            try { c.Call("Emulation.setFocusEmulationEnabled", new Dictionary<string, object> { { "enabled", true } }); } catch { }
            return c;
        }
        PageData Extract(Cdp c)
        {
            var d = ser.Deserialize<Dictionary<string, object>>(c.Eval(Js.Extract));
            var p = new PageData { url = (string)d["url"], title = (string)d["title"], pages = Convert.ToInt32(d["pages"]), login = (bool)d["login"], table = (bool)d["table"], ipage = Convert.ToString(d["ipage"]) };
            { object cc; if (d.TryGetValue("cols", out cc) && cc != null) foreach (object h in (System.Collections.IEnumerable)cc) p.cols.Add((string)h); }
            foreach (object h in (System.Collections.IEnumerable)d["headers"]) p.headers.Add((string)h);
            foreach (object r in (System.Collections.IEnumerable)d["rows"]) { var row = new List<string>(); foreach (object x in (System.Collections.IEnumerable)r) row.Add((string)x); p.rows.Add(row); }
            return p;
        }

        // --- Correspondance colonnes -> champs ADIF ---
        static string Norm(string s)
        {
            s = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in s) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return Regex.Replace(sb.ToString(), @"[^a-z0-9/ ]", " ").Trim();
        }
        // Colonnes reconnues d'abord par la classe CSS des cellules (td_date, td_call2...), sinon par le titre
        static string[] MapCols(List<string> cols, List<string> hs)
        {
            var byTitle = MapHeaders(hs);
            if (cols == null || cols.Count == 0) return byTitle;
            var r = new string[cols.Count];
            bool anyCall = false;
            for (int i = 0; i < cols.Count; i++)
            {
                string c = cols[i].ToLowerInvariant(), f = "";
                if (c == "rownum") f = "_ID";
                else if (c.Contains("td_date")) f = "QSO_DATE";
                else if (c.Contains("td_time")) f = "TIME_ON";
                else if (c.Contains("td_call")) f = "CALL";
                else if (c.Contains("td_freq")) f = "FREQ";
                else if (c.Contains("td_mode")) f = "MODE";
                else if (c.Contains("td_grid")) f = "GRIDSQUARE";
                else if (c.Contains("td_country")) f = "COUNTRY";
                else if (c.Contains("td_name")) f = "NAME";
                else if (c.Contains("td_band")) f = "BAND";
                else if (c.Contains("td_qth")) f = "QTH";
                else if (c.Contains("td_comment")) f = "COMMENT";
                else if (c.Contains("td_rst") && c.Contains("sent")) f = "RST_SENT";
                else if (c.Contains("td_rst")) f = "RST_RCVD";
                if (f == "CALL") anyCall = true;
                r[i] = f;
            }
            if (!anyCall) return byTitle;
            return r;
        }
        static string[] MapHeaders(List<string> hs)
        {
            var r = new string[hs.Count];
            for (int i = 0; i < hs.Count; i++)
            {
                string h = Regex.Replace(Norm(hs[i]), @"\s+", " ");
                string f = "";
                if (h == "date" || h == "qso date" || h == "date on" || h == "date utc" || h == "jour") f = "QSO_DATE";
                else if (h == "date/time" || h == "date time" || h == "date heure" || h == "datetime" || h == "date/heure" || h == "date utc time") f = "DATETIME";
                else if (h == "time" || h == "utc" || h == "time on" || h == "heure" || h == "start" || h == "time utc" || h == "time on utc") f = "TIME_ON";
                else if (h == "call" || h == "callsign" || h == "worked" || h == "station" || h == "indicatif" || h == "call sign") f = "CALL";
                else if (h == "band" || h == "bande") f = "BAND";
                else if (h == "mode") f = "MODE";
                else if (h == "freq" || h == "frequency" || h == "frequence" || h == "mhz") f = "FREQ";
                else if (h == "rst sent" || h == "rst s" || h == "sent" || h == "rst tx" || h == "snt" || h == "rst envoye") f = "RST_SENT";
                else if (h == "rst rcvd" || h == "rst r" || h == "rcvd" || h == "received" || h == "rst rx" || h == "rst recu") f = "RST_RCVD";
                else if (h == "name" || h == "nom") f = "NAME";
                else if (h == "qth") f = "QTH";
                else if (h == "country" || h == "pays") f = "COUNTRY";
                else if (h == "grid" || h == "gridsquare" || h == "locator" || h == "loc") f = "GRIDSQUARE";
                else if (h == "comment" || h == "comments" || h == "notes" || h == "commentaire" || h == "remarks") f = "COMMENT";
                else if (h == "tx pwr" || h == "power" || h == "puissance") f = "TX_PWR";
                else if (h == "state" || h == "etat") f = "STATE";
                else if (h == "iota") f = "IOTA";
                else if (h == "dxcc") f = "DXCC";
                r[i] = f;
            }
            return r;
        }
        static Dictionary<string, string> RowToQso(string[] map, List<string> row)
        {
            var q = new Dictionary<string, string>();
            for (int i = 0; i < row.Count && i < map.Length; i++)
            {
                string f = map[i], v = row[i].Trim();
                if (f == "" || v == "") continue;
                switch (f)
                {
                    case "DATETIME": { string d = Adif.NormDate(v), t = Adif.NormTime(v); if (d != "") q["QSO_DATE"] = d; if (t != "") q["TIME_ON"] = t; break; }
                    case "QSO_DATE": { string d = Adif.NormDate(v); if (d != "") q["QSO_DATE"] = d; string t = Adif.NormTime(v); if (t != "" && v.Contains(":") && !q.ContainsKey("TIME_ON")) q["TIME_ON"] = t; break; }
                    case "TIME_ON": { string t = Adif.NormTime(v); if (t != "") q["TIME_ON"] = t; break; }
                    case "CALL": q["CALL"] = Regex.Match(v.Replace('Ø', '0'), @"[A-Za-z0-9/]+").Value.ToUpperInvariant(); break;
                    case "BAND": q["BAND"] = v.Replace(" ", "").ToUpperInvariant(); break;
                    case "MODE": q["MODE"] = v.ToUpperInvariant(); break;
                    case "FREQ":
                        {
                            var m = Regex.Match(v.Replace(',', '.'), @"\d+(\.\d+)?");
                            if (m.Success)
                            {
                                double x = double.Parse(m.Value, CultureInfo.InvariantCulture);
                                if (x > 3000) x /= 1000.0; // kHz -> MHz
                                q["FREQ"] = x.ToString("0.######", CultureInfo.InvariantCulture);
                            }
                            break;
                        }
                    default: q[f] = v; break;
                }
            }
            string call; if (!q.TryGetValue("CALL", out call) || call == "") return null;
            string fr; if (!q.ContainsKey("BAND") && q.TryGetValue("FREQ", out fr)) { string b = Adif.BandFromFreq(double.Parse(fr, CultureInfo.InvariantCulture)); if (b != "") q["BAND"] = b; }
            return q;
        }

        // Attend que la page soit completement affichee (meme nombre de lignes et memes lignes sur deux lectures)
        PageData Settle(Cdp c, PageData p)
        {
            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(300);
                PageData q; try { q = Extract(c); } catch { continue; }
                if (q.rows.Count == p.rows.Count && Signature(q) == Signature(p)) return q;
                p = q;
            }
            return p;
        }

        string Signature(PageData p)
        {
            var sb = new StringBuilder(p.url + "#" + p.ipage);
            for (int i = 0; i < Math.Min(3, p.rows.Count); i++) sb.Append("|" + string.Join(";", p.rows[i].ToArray()));
            if (p.rows.Count > 0) sb.Append("|" + string.Join(";", p.rows[p.rows.Count - 1].ToArray()));
            return sb.ToString();
        }

        // Mode test : attend (10 min max) que l'utilisateur soit connecte et affiche un tableau de contacts
        void WaitForTable()
        {
            DateTime t0 = DateTime.Now; string last = "";
            while ((DateTime.Now - t0).TotalMinutes < 10 && !stop)
            {
                try
                {
                    using (var c = Attach())
                    {
                        var p = Extract(c);
                        if (!p.login && p.table && p.rows.Count > 0) { Log("Tableau detecte sur " + p.url); return; }
                        string st = "en attente de connexion/liste : " + p.url + (p.login ? " (mot de passe demande)" : "");
                        if (st != last) { Log(st); last = st; }
                    }
                }
                catch (Exception ex) { Log("attente : " + ex.Message); }
                Thread.Sleep(2000);
            }
        }

        void RunDump()
        {
            using (var c = Attach())
            {
                string html = c.Eval("document.documentElement.outerHTML");
                string f = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(txtOut.Text)), "QRZ-page-diagnostic.html");
                File.WriteAllText(f, html, new UTF8Encoding(false));
                Log("Page enregistrée : " + f);
            }
        }

        void RunWeb()
        {
            using (var c = Attach())
            {
                var qsos = new List<Dictionary<string, string>>();
                var seen = new HashSet<string>();
                var p = Extract(c);
                if (p.login && !p.table) { Log("La page demande un mot de passe : connectez-vous dans Edge, affichez votre journal, puis recommencez."); return; }
                if (!p.table || p.rows.Count == 0) { Log("Aucun tableau de contacts sur cette page (" + p.url + "). Affichez la liste de vos contacts dans Edge. Au besoin, utilisez 'Enregistrer la page (diagnostic)'."); return; }
                int total = p.pages;
                Log("Première page : " + p.rows.Count + " lignes, " + (total > 0 ? total + " pages annoncées." : "nombre de pages inconnu (parcours jusqu'à la dernière).") + "  Colonnes : " + string.Join(" | ", p.headers.ToArray()));
                var map = MapCols(p.cols, p.headers);
                if (!map.Contains("CALL")) { Log("Colonne d'indicatif non reconnue dans : " + string.Join(" | ", p.headers.ToArray()) + ". Utilisez 'Enregistrer la page (diagnostic)'."); return; }
                if (p.ipage != "" && p.ipage != "1")
                {
                    Log("Retour a la premiere page (affichee : " + p.ipage + ")...");
                    c.Eval("goto('first',true)");
                    DateTime t1 = DateTime.Now; PageData fp = null;
                    while ((DateTime.Now - t1).TotalSeconds < 30)
                    {
                        Thread.Sleep(400);
                        try { fp = Extract(c); } catch { continue; }
                        if (fp.ipage == "1" && fp.rows.Count > 0) break;
                    }
                    if (fp == null || fp.ipage != "1")
                    {
                        Log("Page bloquee : rechargement du logbook...");
                        try { c.Eval("location.href='https://logbook.qrz.com/'"); } catch { }
                        t1 = DateTime.Now; fp = null;
                        while ((DateTime.Now - t1).TotalSeconds < 40)
                        {
                            Thread.Sleep(800);
                            try { fp = Extract(c); } catch { continue; }
                            if (fp.rows.Count > 0 && (fp.ipage == "1" || fp.ipage == "")) { fp.ipage = "1"; break; }
                        }
                    }
                    if (fp == null || fp.ipage != "1") { Log("Impossible de revenir a la page 1."); return; }
                    p = Settle(c, fp);
                }
                int page = 1;
                while (!stop)
                {
                    int added = 0;
                    foreach (var r in p.rows)
                    {
                        var q = RowToQso(map, r); if (q == null) continue;
                        if (seen.Add(Adif.DedupKey(q))) { qsos.Add(q); added++; }
                    }
                    Log("Page " + page + (total > 0 ? "/" + total : "") + " : " + added + " nouveaux contacts (total " + qsos.Count + ")");
                    Prog(page, total > 0 ? total : page + 1);
                    if (page % 100 == 0) Save(qsos, true);   // sauvegarde intermediaire
                    if (total > 0 && page >= total) break;
                    if (added == 0 && page > 1) { Log("Plus de nouveaux contacts : fin."); break; }
                    string sig = Signature(p);
                    string clicked = c.Eval(Js.Next + (page + 1) + ")");
                    if (clicked == "") { Log("Pas de page suivante : fin."); break; }
                    PageData np = null; bool changed = false;
                    for (int attempt = 0; attempt < 4 && !changed && !stop; attempt++)
                    {
                        if (attempt > 0)
                        {
                            Log("QRZ n'a pas répondu à la page " + (page + 1) + " : nouvel essai " + attempt + "/3...");
                            if (c.Eval(Js.Next + (page + 1) + ")") == "") break;
                        }
                        DateTime t0 = DateTime.Now;
                        while ((DateTime.Now - t0).TotalSeconds < 45 && !stop)
                        {
                            Thread.Sleep(300);
                            try { np = Extract(c); } catch { continue; }
                            if (np.rows.Count > 0 && Signature(np) != sig && (np.ipage == "" || np.ipage == (page + 1).ToString())) { changed = true; break; }
                        }
                    }
                    if (!changed) { Log(stop ? "Arrêté." : "La page suivante ne s'est pas chargée après 4 essais : fin (le fichier contient les pages déjà lues)."); break; }
                    np = Settle(c, np);
                    p = np; page++;
                    if (total > 0 && page > total + 2) break;
                }
                Save(qsos);
            }
        }

        void Save(List<Dictionary<string, string>> qsos, bool quiet = false)
        {
            if (qsos.Count == 0) { Log("Aucun contact récupéré, fichier non créé."); return; }
            string f = "", myGrid = ""; bool rst = true;
            Invoke((Action)(() => { f = txtOut.Text; myGrid = txtMyGrid.Text.Trim(); rst = chkRst.Checked; }));
            if (myGrid != "") foreach (var q in qsos) q["MY_GRIDSQUARE"] = myGrid;
            if (rst)
            {
                var unk = Adif.ApplyReports(qsos);
                if (!quiet)
                {
                    if (unk.Count == 0) Log("Rapports ajoutés : tous les modes étaient connus.");
                    else Log("MODES SANS RAPPORT (non traités, à me signaler) : " + string.Join(", ", unk.OrderByDescending(k => k.Value).Select(k => k.Key + " (" + k.Value + ")").ToArray()));
                }
            }
            Adif.Sort(qsos);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(f)));
            File.WriteAllText(f, Adif.Write(qsos), new UTF8Encoding(false));
            if (!quiet) Log("TERMINÉ : " + qsos.Count + " contacts, triés par date, dans " + f);
        }

        // --- API QRZ Logbook ---
        void RunApi()
        {
            string key = "", call = "";
            Invoke((Action)(() => { key = txtKey.Text.Trim(); call = txtCall.Text.Trim(); }));
            if (key == "") { Log("Saisissez la clé API."); return; }
            if (call == "") call = "inconnu";
            var qsos = new List<Dictionary<string, string>>(); var seen = new HashSet<string>();
            long after = 0;
            while (!stop)
            {
                string opt = "MAX:250" + (after > 0 ? ",AFTERLOGID:" + after : "");
                string resp;
                using (var w = new WebClient())
                {
                    w.Headers[HttpRequestHeader.UserAgent] = "QRZLogExport/" + Program.Version + " (" + call + ")";
                    w.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    resp = w.UploadString("https://logbook.qrz.com/api", "KEY=" + Uri.EscapeDataString(key) + "&ACTION=FETCH&OPTION=" + Uri.EscapeDataString(opt));
                }
                var kv = new Dictionary<string, string>();
                foreach (var part in resp.Split('&'))
                {
                    int i = part.IndexOf('='); if (i < 0) continue;
                    kv[part.Substring(0, i).ToUpperInvariant()] = Uri.UnescapeDataString(part.Substring(i + 1).Replace('+', ' '));
                }
                string res; kv.TryGetValue("RESULT", out res);
                if (res != "OK")
                {
                    string reason; kv.TryGetValue("REASON", out reason);
                    if (qsos.Count == 0) { Log("Réponse QRZ : " + (reason ?? resp.Substring(0, Math.Min(300, resp.Length)))); return; }
                    break;
                }
                string adif; kv.TryGetValue("ADIF", out adif); adif = adif ?? "";
                if (adif.Contains("&lt;")) adif = adif.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
                var recs = Adif.Parse(adif);
                int added = 0; long mx = 0;
                foreach (var q in recs)
                {
                    string lid; long v; bool hasId = q.TryGetValue("APP_QRZLOG_LOGID", out lid);
                    if (hasId && long.TryParse(lid, out v) && v > mx) mx = v;
                    if (seen.Add(hasId ? lid : Adif.DedupKey(q))) { qsos.Add(q); added++; }
                }
                string ids;
                if (kv.TryGetValue("LOGIDS", out ids)) foreach (var s in ids.Split(',')) { long v; if (long.TryParse(s.Trim(), out v) && v > mx) mx = v; }
                Log("Reçu " + recs.Count + " contacts (total " + qsos.Count + ")");
                Prog(1, 2);
                if (recs.Count == 0 || added == 0 || mx <= after) break;
                after = mx;
            }
            Save(qsos);
        }
    }
}
