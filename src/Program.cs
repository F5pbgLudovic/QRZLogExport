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
        public const string Version = "1.3";
        [STAThread]
        static void Main(string[] args)
        {
            // Utilitaire : QRZLogExport.exe --rapports entree.adi sortie.adi  (ajoute les rapports par defaut a un ADIF existant, sans interroger QRZ)
            if (args.Length >= 3 && args[0] == "--rapports")
            {
                var l = Adif.Parse(File.ReadAllText(args[1], Encoding.UTF8));
                var unk = Adif.ApplyReports(l);
                Adif.Sort(l);
                File.WriteAllText(args[2], Adif.Write(l), new UTF8Encoding(false));
                File.WriteAllText(args[2] + ".log", l.Count + " contacts. Modes sans regle : " + (unk.Count == 0 ? "aucun" : string.Join(", ", unk.OrderByDescending(k => k.Value).Select(k => k.Key + " (" + k.Value + ")").ToArray())) + "\r\n");
                return;
            }
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
                foreach (var kv in q) if (!string.IsNullOrEmpty(kv.Value) && !kv.Key.StartsWith("_") && !(kv.Key == "APP_QRZLOG_LOGID" && q.ContainsKey("_ID"))) sb.Append(Field(kv.Key, kv.Value));
                string lid; if (q.TryGetValue("_ID", out lid) && lid != "") sb.Append(Field("APP_QRZLOG_LOGID", lid));
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
        // ---- Rapports par defaut selon le mode (fichier QRZLogExport-rapports.txt a cote de l'exe, modifiable) ----
        public const string RulesFileName = "QRZLogExport-rapports.txt";
        const string DefaultRules =
            "# Rapports par defaut (emission = reception) ajoutes aux contacts qui n'en ont pas.\r\n" +
            "# Format : MODE=rapport   (une ligne par mode, MODE en majuscules). Lignes commencant par # = commentaires.\r\n" +
            "# Modifiez librement ce fichier, puis relancez l'export. Un mode absent = aucun rapport ajoute.\r\n" +
            "#\r\n" +
            "# --- Phonie : 59 ---\r\n" +
            "SSB=59\r\nUSB=59\r\nLSB=59\r\nAM=59\r\nFM=59\r\nFREEDV=59\r\nDSTAR=59\r\nC4FM=59\r\nDMR=59\r\nDIGITALVOICE=59\r\nDV=59\r\nECHOLINK=59\r\nVOI=59\r\n" +
            "# --- Morse : 599 ---\r\n" +
            "CW=599\r\n" +
            "# --- FT8 / FT4 / FT2 : -01 (valeur choisie par l'utilisateur) ---\r\n" +
            "FT8=-01\r\nFT4=-01\r\nFT2=-01\r\n" +
            "# --- Modes numeriques a reports RST classiques : 599 (valeur courante en RTTY et PSK31) ---\r\n" +
            "RTTY=599\r\nPSK31=599\r\nPSK63=599\r\nPSK125=599\r\nSIM31=599\r\nDATA=599\r\nPKT=599\r\nPACKET=599\r\nMFSK=599\r\nOLIVIA=599\r\nCONTESTIA=599\r\nCONTESTI=599\r\nHELL=599\r\nTHOR=599\r\nDOMINO=599\r\nVARA=599\r\nVARA HF=599\r\n" +
            "# --- Modes faibles signaux a rapport en dB (milieu de la plage habituelle) ---\r\n" +
            "JT65=-15\r\nJT65A=-15\r\nJT9=-15\r\nJT4=-15\r\nQ65=-15\r\nFST4=-15\r\nJS8=-10\r\nFT8CALL=-10\r\nROS=-10\r\n" +
            "# --- Images : RSV 595 (exemple de reference du manuel SSTV) ---\r\n" +
            "SSTV=595\r\nFAX=595\r\n";
        static Dictionary<string, string> rules;
        static Dictionary<string, string> Rules
        {
            get
            {
                if (rules != null) return rules;
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RulesFileName);
                try { if (!File.Exists(f)) File.WriteAllText(f, DefaultRules, new UTF8Encoding(false)); } catch { }
                string txt = DefaultRules;
                try { if (File.Exists(f)) txt = File.ReadAllText(f, Encoding.UTF8); } catch { }
                rules = new Dictionary<string, string>();
                foreach (var line in txt.Split('\n'))
                {
                    string l = line.Trim(); if (l.Length == 0 || l[0] == '#') continue;
                    int i = l.IndexOf('='); if (i <= 0) continue;
                    rules[l.Substring(0, i).Trim().ToUpperInvariant()] = l.Substring(i + 1).Trim();
                }
                return rules;
            }
        }
        // Rapports par defaut (seulement si le contact n'en a pas). Retourne les modes sans regle -> nombre de contacts.
        public static Dictionary<string, int> ApplyReports(List<Dictionary<string, string>> qsos)
        {
            var unknown = new Dictionary<string, int>();
            foreach (var q in qsos)
            {
                if (q.ContainsKey("RST_SENT") && q.ContainsKey("RST_RCVD")) continue;
                string m; q.TryGetValue("MODE", out m); m = (m ?? "").ToUpperInvariant();
                string r; if (!Rules.TryGetValue(m, out r) || r == "") { string k = m == "" ? "(vide)" : m; int n; unknown.TryGetValue(k, out n); unknown[k] = n + 1; continue; }
                if (!q.ContainsKey("RST_SENT")) q["RST_SENT"] = r;
                if (!q.ContainsKey("RST_RCVD")) q["RST_RCVD"] = r;
            }
            return unknown;
        }
        // Suivi des contacts deja lus : par identifiant QRZ, ou (anciens fichiers sans identifiant) par contenu avec decompte
        public class Seen
        {
            readonly HashSet<string> ids = new HashSet<string>();
            readonly Dictionary<string, int> legacy = new Dictionary<string, int>();
            public void AddExisting(Dictionary<string, string> q)
            {
                string id;
                if (q.TryGetValue("_ID", out id) && id != "") ids.Add(id);
                else { string k = ContentKey(q); int n; legacy.TryGetValue(k, out n); legacy[k] = n + 1; }
            }
            public bool IsNew(Dictionary<string, string> q)
            {
                string id; int n;
                if (q.TryGetValue("_ID", out id) && id != "")
                {
                    if (ids.Contains(id)) return false;
                    string k = ContentKey(q);
                    if (legacy.TryGetValue(k, out n) && n > 0) { legacy[k] = n - 1; ids.Add(id); return false; }
                    ids.Add(id); return true;
                }
                string c = ContentKey(q);
                if (legacy.TryGetValue(c, out n) && n > 0) { legacy[c] = n - 1; return false; }
                return ids.Add("c" + c);
            }
        }
        public static string ContentKey(Dictionary<string, string> q)
        {
            string[] ks = { "CALL", "QSO_DATE", "TIME_ON", "BAND", "MODE", "FREQ" };
            return string.Join("|", ks.Select(k => { string v; return q.TryGetValue(k, out v) ? v : ""; }).ToArray());
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
 if(fr){var cl=[];var ft=fr.querySelectorAll('td');for(var z=0;z<ft.length;z++)cl.push(ft[z].className||'');cl.push('rownum');cl.push('rowpos');cl.push('rowbook');o.cols=cl;}
 for(var a3=0;a3<trs.length;a3++){if(trs[a3]===hr)continue;var tds=trs[a3].querySelectorAll('td');
  if(tds.length<4)continue;var row=[];for(var q=0;q<tds.length;q++){var cc=tds[q].className||'',tt=tds[q].getAttribute('title');row.push((/td_(date|time|freq|call|grid)/.test(cc)&&tt)?tt:tx(tds[q]));}row.push(trs[a3].getAttribute('data-rownum')||'');row.push(trs[a3].getAttribute('data-pos')||'');row.push(trs[a3].getAttribute('data-bookid')||'');o.rows.push(row);}}
return JSON.stringify(o);})()";

        // Fiche detaillee de chaque contact (requete directe du formulaire de la liste, 4 a la fois)
        public const string Detail = @"(async function(items){var f=document.getElementById('lbmenu');var res=[];var i=0;
function lab(tb,l){var e=[].filter.call(tb.querySelectorAll('th,td'),function(x){return x.textContent.replace(/\s+/g,' ').trim()===l;})[0];return e&&e.nextElementSibling?e.nextElementSibling.textContent.replace(/\s+/g,' ').trim():'';}
async function w(){while(i<items.length){var it=items[i++];try{var fd=new FormData(f);fd.set('logid',it.id);fd.set('logpos',it.pos);fd.set('bookid',it.book);
var r=await fetch(f.action,{method:'POST',body:new URLSearchParams(fd),credentials:'include'});var h=await r.text();var d=new DOMParser().parseFromString(h,'text/html');
var th=[].filter.call(d.querySelectorAll('th'),function(x){return x.textContent.trim()==='Station';})[0];if(!th){res.push({id:it.id,err:1});continue;}var tb=th.closest('table');
res.push({id:it.id,rr:lab(tb,'RST Rcvd'),rs:lab(tb,'RST Sent'),qth:lab(tb,'QTH'),st:lab(tb,'State'),cy:lab(tb,'County'),cm:lab(tb,'Comments')});}catch(e){res.push({id:it.id,err:1});}}}
await Promise.all([w(),w(),w(),w()]);return JSON.stringify(res);})(";

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
        TextBox txtOut, txtLog, txtKey, txtCall; CheckBox chkRst, chkReal; DateTimePicker dtpFrom, dtpTo;
        Button btnOpen, btnGo, btnApi, btnStop, btnDump, btnBrowse, btnClean, btnOpen2;
        NumericUpDown numDays; bool cleanAuto; int cleanMax = int.MaxValue;
        Button btnOpen3, btnDupScan, btnDupDel; string lastDupReport = "";
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
            Width = 780; Height = 720; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(640, 520);
            Font = new Font("Segoe UI", 9.5f);

            tabs = new TabControl { Dock = DockStyle.Top, Height = 210 };
            var t1 = new TabPage("1. Connecté (navigateur Edge)");
            var t2 = new TabPage("2. Clé API QRZ (plus fiable)");
            var t3 = new TabPage("3. Nettoyer « Incoming »");
            var t4 = new TabPage("4. Doublons");
            tabs.TabPages.Add(t1); tabs.TabPages.Add(t2); tabs.TabPages.Add(t3); tabs.TabPages.Add(t4);
            var h4 = new Label { Left = 10, Top = 8, Width = 730, Height = 80, Text =
                "Cherche dans votre journal QRZ les contacts en double (même indicatif, même bande déduite de la fréquence, à 2 minutes ou moins) et garde celui\r\n" +
                "qui contient le plus d'informations. 1) « Analyser » écrit un RAPPORT (rien n'est supprimé) à côté du fichier ADIF ci-dessous ; la période\r\n" +
                "ci-dessous limite l'analyse. 2) Contrôlez le rapport (modifiable). 3) « Supprimer » efface les doublons du rapport (définitif sur QRZ ; les\r\n" +
                "contacts supprimés sont gardés dans un fichier ADIF de secours pour les réimporter si besoin)." };
            btnOpen3 = new Button { Left = 10, Top = 100, Width = 200, Height = 34, Text = "Ouvrir QRZ dans Edge" };
            btnDupScan = new Button { Left = 220, Top = 100, Width = 240, Height = 34, Text = "1. Analyser les doublons" };
            btnDupDel = new Button { Left = 470, Top = 100, Width = 260, Height = 34, Text = "2. Supprimer les doublons…" };
            t4.Controls.AddRange(new Control[] { h4, btnOpen3, btnDupScan, btnDupDel });

            var h3 = new Label { Left = 10, Top = 10, Width = 730, Height = 78, Text =
                "Les demandes de confirmation (menu « Incoming » de QRZ) d'anciens contacts s'accumulent. Cet onglet les rejette une par une\r\n" +
                "avec le motif « Not in my logbook », jusqu'à ce que toutes les demandes anciennes aient disparu.\r\n" +
                "1) « Ouvrir QRZ dans Edge » et connectez-vous.  2) Choisissez le nombre de jours.  3) « Nettoyer » (confirmation demandée avant de commencer)." };
            var l3 = new Label { Left = 10, Top = 100, Width = 330, Text = "Rejeter les demandes dont la date du QSO a plus de :" };
            numDays = new NumericUpDown { Left = 345, Top = 96, Width = 70, Minimum = 0, Maximum = 36500, Value = 30 };
            var l3b = new Label { Left = 422, Top = 100, Width = 300, Text = "jours" };
            btnOpen2 = new Button { Left = 10, Top = 134, Width = 230, Height = 34, Text = "Ouvrir QRZ dans Edge" };
            btnClean = new Button { Left = 250, Top = 134, Width = 300, Height = 34, Text = "Nettoyer la liste « Incoming »" };
            t3.Controls.AddRange(new Control[] { h3, l3, numDays, l3b, btnOpen2, btnClean });

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

            var pnl = new Panel { Dock = DockStyle.Top, Height = 168 };
            var lo = new Label { Left = 10, Top = 12, Width = 120, Text = "Fichier ADIF :" };
            txtOut = new TextBox { Left = 130, Top = 8, Width = 480, Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QRZ-journal.adi") };
            btnBrowse = new Button { Left = 620, Top = 6, Width = 110, Height = 28, Text = "Parcourir..." };
            btnStop = new Button { Left = 10, Top = 42, Width = 100, Height = 28, Text = "Arrêter", Enabled = false };
            bar = new ProgressBar { Left = 120, Top = 44, Width = 610, Height = 24 };
            chkRst = new CheckBox { Left = 10, Top = 76, Width = 720, Checked = true, Text = "Ajouter un rapport par défaut selon le mode aux contacts qui n'en ont pas (règles : " + Adif.RulesFileName + ")" };
            var lp = new Label { Left = 10, Top = 108, Width = 70, Text = "Période :" };
            dtpFrom = new DateTimePicker { Left = 85, Top = 104, Width = 150, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
            var lp2 = new Label { Left = 243, Top = 108, Width = 22, Text = "au" };
            dtpTo = new DateTimePicker { Left = 268, Top = 104, Width = 150, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
            var lp3 = new Label { Left = 425, Top = 108, Width = 310, Text = "(cases décochées = tout le journal)" };
            chkReal = new CheckBox { Left = 10, Top = 136, Width = 720, Text = "Lire les VRAIS rapports (fiche de chaque contact : lent, environ 1 contact/seconde ; à réserver à une période)" };
            pnl.Controls.AddRange(new Control[] { lo, txtOut, btnBrowse, btnStop, bar, chkRst, lp, dtpFrom, lp2, dtpTo, lp3, chkReal });
            lblState = new Label { Dock = DockStyle.Top, Height = 24, Text = "Prêt.", Padding = new Padding(10, 3, 0, 0) };
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };

            Controls.Add(txtLog); Controls.Add(lblState); Controls.Add(pnl); Controls.Add(tabs);

            btnBrowse.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "ADIF (*.adi;*.adif)|*.adi;*.adif", FileName = Path.GetFileName(txtOut.Text), DefaultExt = "adi" }) if (d.ShowDialog() == DialogResult.OK) txtOut.Text = d.FileName; };
            btnOpen.Click += (s, e) => Start(() => OpenEdge("https://logbook.qrz.com/"));
            btnGo.Click += (s, e) =>
            {
                if (!EdgeAlive())
                {
                    Log("Edge n'est pas ouvert : cliquez d'abord sur « Ouvrir QRZ dans Edge » et connectez-vous.");
                    MessageBox.Show(this, "Edge n'est pas ouvert.\r\n\r\n1) Cliquez sur « Ouvrir QRZ dans Edge »\r\n2) Connectez-vous à QRZ et affichez la liste de vos contacts\r\n3) Relancez « Récupérer tout le journal »", "Récupérer le journal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                resumeChoice = false;
                string f = txtOut.Text;
                if (File.Exists(f) && new FileInfo(f).Length > 200)
                {
                    string info = "";
                    try
                    {
                        var old = Adif.Parse(File.ReadAllText(f, Encoding.UTF8));
                        string mn = "99999999", mx = "";
                        foreach (var q in old) { string dd = DateOf(q); if (dd != "") { if (string.CompareOrdinal(dd, mn) < 0) mn = dd; if (string.CompareOrdinal(dd, mx) > 0) mx = dd; } }
                        info = old.Count + " contacts, du " + mn + " au " + mx + ".\r\n\r\n";
                    }
                    catch { }
                    var r = MessageBox.Show(this, "Le fichier existe déjà :\r\n" + f + "\r\n" + info +
                        "OUI = COMPLÉTER ce fichier (reprise à partir de la date où il s'arrête, sans doublons)\r\n" +
                        "NON = RECOMMENCER (l'ancien fichier est conservé en .bak)\r\nANNULER = ne rien faire",
                        "Fichier existant", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    resumeChoice = r == DialogResult.Yes;
                }
                Start(RunWeb);
            };
            btnApi.Click += (s, e) => Start(RunApi);
            btnDump.Click += (s, e) => Start(RunDump);
            btnOpen2.Click += (s, e) => Start(() => OpenEdge("https://logbook.qrz.com/"));
            btnOpen3.Click += (s, e) => Start(() => OpenEdge("https://logbook.qrz.com/"));
            btnDupScan.Click += (s, e) => Start(RunDup);
            btnDupDel.Click += (s, e) =>
            {
                string f = lastDupReport;
                if (f == "" || !File.Exists(f))
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(txtOut.Text));
                    using (var d = new OpenFileDialog { Title = "Rapport de doublons à appliquer", Filter = "Rapport de doublons (QRZ-doublons-*.tsv)|QRZ-doublons-*.tsv|Tous|*.*", InitialDirectory = Directory.Exists(dir) ? dir : null })
                    { if (d.ShowDialog() != DialogResult.OK) return; f = d.FileName; }
                }
                else
                {
                    var q = MessageBox.Show(this, "Appliquer ce rapport ?\r\n" + f + "\r\n\r\nOUI = ce rapport   NON = en choisir un autre", "Supprimer les doublons", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (q == DialogResult.Cancel) return;
                    if (q == DialogResult.No)
                    {
                        using (var d = new OpenFileDialog { Filter = "Rapport de doublons (*.tsv)|*.tsv", InitialDirectory = Path.GetDirectoryName(f) })
                        { if (d.ShowDialog() != DialogResult.OK) return; f = d.FileName; }
                    }
                }
                string ff = f; Start(() => RunDupDel(ff));
            };
            btnClean.Click += (s, e) => { int d = (int)numDays.Value; Start(() => RunClean(d)); };

            // Mode test sans clic : QRZLogExport.exe --clean port=N days=N [max=N] [log=fichier]
            if (args.Length >= 1 && args[0] == "--clean")
            {
                cleanAuto = true; int dd = 30;
                for (int ai = 1; ai < args.Length; ai++)
                {
                    string a = args[ai];
                    if (a.StartsWith("port=")) port = int.Parse(a.Substring(5));
                    else if (a.StartsWith("days=")) dd = int.Parse(a.Substring(5));
                    else if (a.StartsWith("max=")) cleanMax = int.Parse(a.Substring(4));
                    else if (a.StartsWith("log=")) testLog = a.Substring(4);
                }
                Shown += (s, e) => Start(() => { if (port == 0) OpenEdge("https://logbook.qrz.com/"); RunClean(dd); Invoke((Action)Close); });
            }
            btnStop.Click += (s, e) => { stop = true; Log("Arrêt demandé..."); };

            // Mode test sans clic : --dup port=N [from=AAAA-MM-JJ] [to=AAAA-MM-JJ] [out=fichier.adi] [log=fichier]
            //                       --dupdel port=N file=rapport.tsv [max=N] [log=fichier]   (⚠ supprime vraiment)
            if (args.Length >= 1 && (args[0] == "--dup" || args[0] == "--dupdel"))
            {
                cleanAuto = true; testLog = Path.Combine(Path.GetTempPath(), "qrzlogexport-dup.log"); string rf = "";
                for (int ai = 1; ai < args.Length; ai++)
                {
                    string a = args[ai];
                    if (a.StartsWith("port=")) port = int.Parse(a.Substring(5));
                    else if (a.StartsWith("from=")) testFrom = a.Substring(5).Replace("-", "");
                    else if (a.StartsWith("to=")) testTo = a.Substring(3).Replace("-", "");
                    else if (a.StartsWith("out=")) txtOut.Text = a.Substring(4);
                    else if (a.StartsWith("file=")) rf = a.Substring(5);
                    else if (a.StartsWith("max=")) cleanMax = int.Parse(a.Substring(4));
                    else if (a.StartsWith("log=")) testLog = a.Substring(4);
                }
                bool del = args[0] == "--dupdel";
                Shown += (s, e) => Start(() => { if (port == 0) OpenEdge("https://logbook.qrz.com/"); if (del) RunDupDel(rf); else RunDup(); Invoke((Action)Close); });
            }

            // Mode test sans clic : QRZLogExport.exe --test <url> <sortie.adi>
            if (args.Length >= 3 && args[0] == "--test")
            {
                txtOut.Text = args[2]; testLog = args[2] + ".log";
                for (int ai = 3; ai < args.Length; ai++)
                {
                    string a = args[ai];
                    if (a.StartsWith("from=")) testFrom = a.Substring(5).Replace("-", "");
                    else if (a.StartsWith("to=")) testTo = a.Substring(3).Replace("-", "");
                    else if (a == "real=1") testReal = true;
                    else if (a == "resume=1") testResume = true;
                }
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
            btnGo.Enabled = btnApi.Enabled = btnDump.Enabled = btnOpen.Enabled = btnOpen2.Enabled = btnClean.Enabled = btnOpen3.Enabled = btnDupScan.Enabled = btnDupDel.Enabled = !b; btnStop.Enabled = b;
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
            // un Edge lancé auparavant avec notre profil tourne peut-être encore : on le réutilise
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        string cl = Convert.ToString(o["CommandLine"]);
                        if (cl.IndexOf("edge-profile", StringComparison.OrdinalIgnoreCase) < 0 || cl.Contains("--type=")) continue;
                        var m = Regex.Match(cl, @"remote-debugging-port=(\d+)");
                        if (!m.Success) continue;
                        port = int.Parse(m.Groups[1].Value);
                        if (EdgeAlive()) { Log("Edge est déjà ouvert : connectez-vous à QRZ si besoin (fenêtre Edge)."); try { using (var c0 = Attach()) { } } catch { } return; }
                        port = 0;
                    }
            }
            catch { }
            var l =new TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
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
                else if (c == "rowpos") f = "_POS";
                else if (c == "rowbook") f = "_BOOK";
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

        // ---------- Nettoyage de la liste « Incoming » ----------
        const string JsReqList =
            "(function(){var o=[];[].slice.call(document.querySelectorAll('tr[id^=rr-]')).forEach(function(tr){" +
            "var b=tr.querySelector('button[onclick^=\"lb_reject\"]');if(!b)return;" +
            "var m=/lb_reject\\('(\\d+)'\\)/.exec(b.getAttribute('onclick'));if(!m)return;var id=m[1];" +
            "var rd=document.getElementById('rdate-'+id),rc=document.getElementById('rcall-'+id);" +
            "o.push([id,rc?rc.value:'',rd?rd.value:'']);});" +
            "return JSON.stringify({page:!!document.querySelector('#lbrequests'),rows:o});})()";
        // envoie le meme formulaire que la fenetre « Reject QSO » (motif « Not in my logbook »)
        const string JsReject =
            "(async function(id){var f=document.getElementById('lbmenu');var fd=new FormData(f);" +
            "fd.set('op','reject');fd.set('reason','Not in my logbook');fd.set('comments','');fd.set('qso',id);" +
            "var r=await fetch(f.action,{method:'POST',body:new URLSearchParams(fd),credentials:'include'});" +
            "var h=await r.text();var ok=/rejected successfully/i.test(h);" +
            "if(ok){var b=document.querySelector('button[onclick=\"lb_reject(\\''+id+'\\')\"]');if(b){var tr=b.closest('tr');if(tr)tr.remove();}}" +
            "return JSON.stringify({ok:ok,status:r.status,snip:ok?'':h.replace(/<script[\\s\\S]*?<\\/script>/gi,'').replace(/<[^>]+>/g,' ').replace(/\\s+/g,' ').slice(0,200)});})(";

        class Req { public string id, call, date; }
        List<Req> ListRequests(Cdp c, string cutoff, out int total)
        {
            total = 0;
            var d = ser.Deserialize<Dictionary<string, object>>(c.Eval(JsReqList));
            var res = new List<Req>();
            if (!(bool)d["page"]) return null;
            foreach (object r in (System.Collections.IEnumerable)d["rows"])
            {
                var a = ((System.Collections.IEnumerable)r).Cast<object>().Select(x => Convert.ToString(x)).ToArray();
                total++;
                if (a[2].Length == 10 && string.CompareOrdinal(a[2], cutoff) < 0) res.Add(new Req { id = a[0], call = a[1], date = a[2] });
            }
            return res;
        }
        void GoRequests(Cdp c)
        {
            try { c.Eval("lb_go('requests','')"); } catch { }
            Thread.Sleep(4000);
        }

        void RunClean(int days)
        {
            string cutoff = DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd");
            if (!EdgeAlive())
            {
                Log("Edge n'est pas ouvert : cliquez d'abord sur « Ouvrir QRZ dans Edge » et connectez-vous.");
                if (!cleanAuto) Invoke((Action)(() => MessageBox.Show(this, "Edge n'est pas ouvert.\r\n\r\n1) Cliquez sur « Ouvrir QRZ dans Edge »\r\n2) Connectez-vous à QRZ\r\n3) Relancez « Nettoyer la liste Incoming »", "Nettoyer Incoming", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                return;
            }
            using (var c = Attach())
            {
                int total; List<Req> old = null;
                for (int i = 0; i < 6 && !stop; i++)
                {
                    try { old = ListRequests(c, cutoff, out total); } catch (Exception ex) { old = null; Log("lecture : " + ex.Message); }
                    if (old != null) break;
                    if (i == 0) Log("Ouverture de la liste « Incoming »...");
                    GoRequests(c);
                }
                if (old == null) { Log("La liste « Incoming » n'a pas pu être ouverte. Connectez-vous à QRZ (onglet 1) puis recommencez."); return; }
                ListRequests(c, cutoff, out total);
                Log("Liste « Incoming » : " + total + " demandes, dont " + old.Count + " avec un QSO antérieur au " + cutoff + " (plus de " + days + " jours).");
                if (old.Count == 0) { Log("Rien à nettoyer."); return; }
                if (!cleanAuto)
                {
                    string ex1 = old[0].call + " (" + old[0].date + ")", ex2 = old[old.Count - 1].call + " (" + old[old.Count - 1].date + ")";
                    var ans = (DialogResult)Invoke((Func<DialogResult>)(() => MessageBox.Show(this,
                        old.Count + " demande(s) vont être REJETÉES avec le motif « Not in my logbook ».\r\n\r\nPremière : " + ex1 + "\r\nDernière : " + ex2 +
                        "\r\n\r\nLe rejet ne peut pas être annulé. Continuer ?", "Nettoyer Incoming", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)));
                    if (ans != DialogResult.Yes) { Log("Annulé, rien n'a été rejeté."); return; }
                }
                int done = 0, fails = 0, pass = 0, planned = Math.Min(old.Count, cleanMax);
                Prog(0, planned);
                while (old.Count > 0 && !stop && done < cleanMax && pass < 30)
                {
                    pass++;
                    int before = done;
                    foreach (var r in old)
                    {
                        if (stop || done >= cleanMax) break;
                        bool ok = false; string why = "";
                        try
                        {
                            var d = ser.Deserialize<Dictionary<string, object>>(c.Eval(JsReject + "'" + r.id + "')"));
                            ok = (bool)d["ok"]; why = Convert.ToString(d["snip"]);
                        }
                        catch (Exception ex) { why = ex.Message; }
                        if (ok) { done++; fails = 0; Log("Rejeté " + done + " : " + r.call + " (QSO du " + r.date + ")"); Prog(done, Math.Max(planned, done)); }
                        else
                        {
                            fails++; Log("Échec pour " + r.call + " (" + r.date + ") : " + why);
                            if (fails >= 3) { Log("ARRÊT : 3 échecs de suite. Rien d'autre n'a été modifié."); return; }
                        }
                        Thread.Sleep(400);
                    }
                    if (done >= cleanMax || stop) break;
                    // on recharge la liste : s'il reste des demandes anciennes, on recommence
                    GoRequests(c);
                    try { old = ListRequests(c, cutoff, out total) ?? new List<Req>(); } catch { old = new List<Req>(); }
                    if (old.Count > 0 && done == before) { Log("Les demandes restantes ne partent pas : arrêt."); break; }
                }
                GoRequests(c);
                Log("Terminé : " + done + " demande(s) rejetée(s)." + (stop ? " (arrêté à la demande)" : ""));
            }
        }

        // ---------- Doublons ----------
        // Voile qui bloque clics et touches dans la page QRZ pendant le travail (disparait au rechargement ; remis avant chaque operation)
        const string JsLock =
            "(function(ttl){window.__qlt=Date.now()+ttl*1000;if(document.getElementById('qlx'))return 1;var d=document.createElement('div');d.id='qlx';" +
            "d.style.cssText='position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483647;background:rgba(0,0,0,.6);color:#fff;font:bold 28px Segoe UI,Arial;display:flex;align-items:center;justify-content:center;text-align:center;cursor:not-allowed';" +
            "d.textContent='QRZLogExport travaille : ne touchez pas à cette fenêtre';" +
            "['click','mousedown','mouseup','keydown','keypress','wheel','contextmenu'].forEach(function(t){d.addEventListener(t,function(e){e.preventDefault();e.stopPropagation();});});" +
            "window.addEventListener('keydown',function(e){e.preventDefault();e.stopPropagation();},true);document.documentElement.appendChild(d);var iv=setInterval(function(){if(Date.now()>window.__qlt){d.remove();clearInterval(iv);}},2000);return 1;})(";
        // Supprime une fiche comme le fait QRZ : fiche du contact -> « Delete » (op=logdel) ; verifie ensuite que la fiche n'existe plus
        const string JsDel =
            "(async function(id,pos,book){var f=document.getElementById('lbmenu');if(!f)return JSON.stringify({ok:false,why:'formulaire absent'});" +
            "var u=f.action;function st(d){return [].filter.call(d.querySelectorAll('th'),function(x){return x.textContent.trim()==='Station';})[0];}" +
            "async function show(){var fd=new FormData(f);fd.set('op','show');fd.set('logid',id);fd.set('logpos',pos);fd.set('bookid',book);" +
            "var r=await fetch(u,{method:'POST',body:new URLSearchParams(fd),credentials:'include'});return new DOMParser().parseFromString(await r.text(),'text/html');}" +
            "var d=await show();if(!st(d))return JSON.stringify({ok:false,why:'fiche introuvable'});" +
            "var m=d.getElementById('lbmenu');if(!m)return JSON.stringify({ok:false,why:'fiche sans formulaire'});" +
            "var li=m.querySelector('[name=logid]');if(!li||li.value!==id)return JSON.stringify({ok:false,why:'identifiant different'});" +
            "var f2=new FormData(m);f2.set('op','logdel');" +
            "await (await fetch(u,{method:'POST',body:new URLSearchParams(f2),credentials:'include'})).text();" +
            "var d3=await show();var gone=!st(d3);return JSON.stringify({ok:gone,why:gone?'':'la fiche existe toujours'});})(";

        // Indicatif de base : sans prefixe/suffixe de portable (F5PBG/P, EA8/F5PBG -> F5PBG)
        static string BaseCall(string c)
        {
            string best = ""; foreach (string part in (c ?? "").ToUpperInvariant().Split('/'))
                if (part.Any(char.IsDigit) && part.Any(char.IsLetter) && part.Length > best.Length) best = part;
            return best != "" ? best : (c ?? "").ToUpperInvariant();
        }
        // Bande deduite de la frequence affichee (a defaut, de la colonne bande)
        static string BandOf(Dictionary<string, string> q)
        {
            string fr, band; double f;
            if (q.TryGetValue("FREQ", out fr) && double.TryParse(fr, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { string b = Adif.BandFromFreq(f); if (b != "") return b; }
            if (q.TryGetValue("BAND", out band) && band != "") return band;
            return fr ?? "";
        }
        static double Secs(Dictionary<string, string> q)
        {
            string dt, tm; q.TryGetValue("QSO_DATE", out dt); q.TryGetValue("TIME_ON", out tm);
            DateTime d; if (dt == null || !DateTime.TryParseExact(dt, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return -1;
            tm = (tm ?? "").PadRight(6, '0');
            int h = int.Parse(tm.Substring(0, 2)), m = int.Parse(tm.Substring(2, 2)), s = int.Parse(tm.Substring(4, 2));
            return (d - new DateTime(2000, 1, 1)).TotalSeconds + h * 3600 + m * 60 + s;
        }
        static string MinuteKey(Dictionary<string, string> q) { return (q.ContainsKey("CALL") ? q["CALL"].ToUpperInvariant() : "") + "|" + BandOf(q); }
        // dans chaque groupe (meme indicatif de base, meme bande), regroupe les contacts a 2 minutes ou moins du premier du lot
        static List<List<Dictionary<string, string>>> Clusters(IEnumerable<List<Dictionary<string, string>>> groups)
        {
            var res = new List<List<Dictionary<string, string>>>();
            foreach (var g in groups)
            {
                if (g.Count < 2) continue;
                var s = g.Select(q => new { q, t = Secs(q) }).Where(x => x.t >= 0).OrderBy(x => x.t).ToList();
                int i = 0;
                while (i < s.Count)
                {
                    int j = i + 1; var cl = new List<Dictionary<string, string>> { s[i].q };
                    while (j < s.Count && s[j].t - s[i].t <= 120) { cl.Add(s[j].q); j++; }
                    if (cl.Count > 1) res.Add(cl);
                    i = j;
                }
            }
            return res;
        }
        static int InfoCount(Dictionary<string, string> q)
        {
            int n = 0; foreach (var kv in q) if (!kv.Key.StartsWith("_") && !string.IsNullOrWhiteSpace(kv.Value)) n++; return n;
        }
        static string Tsv(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        void RunDup()
        {
            Opts o = null; Invoke((Action)(() => o = ReadOpts()));
            if (!EdgeAlive())
            {
                Log("Edge n'est pas ouvert : cliquez d'abord sur « Ouvrir QRZ dans Edge » et connectez-vous.");
                if (!cleanAuto) Invoke((Action)(() => MessageBox.Show(this, "Edge n'est pas ouvert.\r\n\r\n1) Cliquez sur « Ouvrir QRZ dans Edge »\r\n2) Connectez-vous à QRZ et affichez la liste de vos contacts\r\n3) Relancez « Analyser les doublons »", "Doublons", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                return;
            }
            string outDir = Path.GetDirectoryName(Path.GetFullPath(o.file));
            using (var c = Attach())
            {
                var p = Extract(c);
                if (p.login && !p.table) { Log("La page demande un mot de passe : connectez-vous dans Edge, affichez votre journal, puis recommencez."); return; }
                if (!p.table || p.rows.Count == 0) { Log("Aucun tableau de contacts sur cette page (" + p.url + "). Affichez la liste de vos contacts (logbook.qrz.com) puis recommencez."); return; }
                int total = p.pages;
                var map = MapCols(p.cols, p.headers);
                if (!map.Contains("CALL")) { Log("Colonne d'indicatif non reconnue."); return; }
                int curPage; if (!int.TryParse(p.ipage, out curPage) || curPage <= 0) curPage = 1;
                string d1, d2; PageDates(p, map, out d1, out d2);
                bool desc = string.CompareOrdinal(d1, d2) >= 0;
                Log("Analyse des doublons : " + (total > 0 ? total + " pages" : "nombre de pages inconnu") + (o.from != "" || o.to != "" ? ". Période : " + (o.from == "" ? "début" : o.from) + " -> " + (o.to == "" ? "fin" : o.to) : ", tout le journal") + ".");
                string startBound = desc ? o.to : o.from;
                int startPage = 1;
                if (startBound != "" && total > 1) { startPage = FindStartPage(c, map, total, desc, startBound, ref p, ref curPage); if (startPage < 0) { Log("Recherche de la page de départ impossible."); return; } }
                if (curPage != startPage || p == null)
                {
                    var sp = GotoPage(c, startPage, p); if (sp == null) { Log(stop ? "Arrêté." : "Impossible d'atteindre la page " + startPage + "."); return; }
                    p = sp;
                }
                int page = startPage; int count = 0;
                var groups = new Dictionary<string, List<Dictionary<string, string>>>();
                var seenIds = new HashSet<string>();
                bool complete = false;
                while (!stop)
                {
                    try { c.Eval(JsLock + "60)"); } catch { }
                    string pf, pl; PageDates(p, map, out pf, out pl);
                    foreach (var r in p.rows)
                    {
                        var q = RowToQso(map, r); if (q == null) continue;
                        string dd = DateOf(q);
                        if (o.from != "" && dd != "" && string.CompareOrdinal(dd, o.from) < 0) continue;
                        if (o.to != "" && dd != "" && string.CompareOrdinal(dd, o.to) > 0) continue;
                        string id; if (q.TryGetValue("_ID", out id) && id != "" && !seenIds.Add(id)) continue;
                        List<Dictionary<string, string>> g; string k = MinuteKey(q);
                        if (!groups.TryGetValue(k, out g)) { g = new List<Dictionary<string, string>>(); groups[k] = g; }
                        g.Add(q); count++;
                    }
                    Prog(page, total > 0 ? total : page + 1);
                    if (page % 10 == 0 || page == startPage) Log("Page " + page + (total > 0 ? "/" + total : "") + " (" + pf + " ... " + pl + ") : " + count + " contacts lus.");
                    bool done = total > 0 && page >= total;
                    if (!done && pl != "")
                    {
                        if (desc && o.from != "" && string.CompareOrdinal(pl, o.from) < 0) done = true;
                        if (!desc && o.to != "" && string.CompareOrdinal(pl, o.to) > 0) done = true;
                    }
                    if (done) { complete = true; break; }
                    string sig = Signature(p);
                    if (c.Eval(Js.Next + (page + 1) + ")") == "") { Log("Pas de page suivante : fin."); complete = true; break; }
                    PageData np = null; bool changed = false;
                    for (int attempt = 0; attempt < 4 && !changed && !stop; attempt++)
                    {
                        if (attempt > 0) { Log("QRZ n'a pas répondu à la page " + (page + 1) + " : nouvel essai " + attempt + "/3..."); if (c.Eval(Js.Next + (page + 1) + ")") == "") break; }
                        DateTime t0 = DateTime.Now;
                        while ((DateTime.Now - t0).TotalSeconds < 45 && !stop)
                        {
                            Thread.Sleep(300);
                            try { np = Extract(c); } catch { continue; }
                            if (np.rows.Count > 0 && Signature(np) != sig && (np.ipage == "" || np.ipage == (page + 1).ToString())) { changed = true; break; }
                        }
                    }
                    if (!changed) { Log(stop ? "Arrêté." : "La page suivante ne s'est pas chargée après 4 essais : analyse interrompue."); break; }
                    p = Settle(c, np); page++;
                    if (total > 0 && page > total + 2) { complete = true; break; }
                }
                if (stop || !complete) { Log("Analyse incomplète : aucun rapport écrit."); return; }

                var dups = Clusters(groups.Values);
                Log(count + " contacts lus, " + dups.Count + " groupe(s) en double.");
                if (dups.Count == 0) { Log("Aucun doublon."); return; }
                // fiches detaillees des seuls contacts concernes (RST, QTH, commentaire...) pour comparer l'information
                var members = dups.SelectMany(g => g).ToList();
                Log("Lecture de la fiche de " + members.Count + " contacts concernés...");
                for (int i = 0; i < members.Count && !stop; i += 100)
                {
                    var part = members.Skip(i).Take(100).ToList();
                    try { FetchDetails(c, part); } catch (Exception ex) { Log("  fiches : " + ex.Message); }
                    Prog(Math.Min(i + 100, members.Count), members.Count);
                }
                if (stop) { Log("Arrêté : aucun rapport écrit."); return; }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
                string tsv = Path.Combine(outDir, "QRZ-doublons-" + stamp + ".tsv"), bak = Path.Combine(outDir, "QRZ-doublons-" + stamp + "-supprimes.adi");
                var sb = new StringBuilder();
                sb.Append("ACTION\tID\tPOS\tBOOK\tINDICATIF\tDATE\tHEURE\tBANDE\tMODE\tFREQ\tINFOS\tGROUPE\r\n");
                var toDel = new List<Dictionary<string, string>>(); int gi = 0;
                foreach (var g in dups.OrderBy(x => x[0].ContainsKey("QSO_DATE") ? x[0]["QSO_DATE"] : ""))
                {
                    gi++;
                    var ordered = g.OrderByDescending(InfoCount).ThenBy(q => { long n; string id; return q.TryGetValue("_ID", out id) && long.TryParse(id, out n) ? n : long.MaxValue; }).ToList();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var q = ordered[i]; bool keep = i == 0;
                        Func<string, string> v = k => { string s; return q.TryGetValue(k, out s) ? Tsv(s) : ""; };
                        sb.Append((keep ? "GARDER" : "SUPPRIMER") + "\t" + v("_ID") + "\t" + v("_POS") + "\t" + v("_BOOK") + "\t" + v("CALL") + "\t" + v("QSO_DATE") + "\t" + v("TIME_ON") + "\t" + v("BAND") + "\t" + v("MODE") + "\t" + v("FREQ") + "\t" + InfoCount(q) + "\t" + gi + "\r\n");
                        if (!keep) toDel.Add(q);
                    }
                }
                Directory.CreateDirectory(outDir);
                File.WriteAllText(tsv, sb.ToString(), new UTF8Encoding(true));
                File.WriteAllText(bak, Adif.Write(toDel), new UTF8Encoding(false));
                lastDupReport = tsv;
                Log("RAPPORT écrit : " + tsv);
                Log("  " + dups.Count + " groupe(s), " + toDel.Count + " contact(s) à supprimer. Sauvegarde ADIF des contacts à supprimer : " + bak);
                Log("  Rien n'est supprimé. Contrôlez le rapport (colonne ACTION : mettez GARDER pour épargner une ligne), puis « 2. Supprimer les doublons… ».");
            }
        }

        void RunDupDel(string tsv)
        {
            if (!File.Exists(tsv)) { Log("Rapport introuvable : " + tsv); return; }
            var items = new List<string[]>();
            foreach (var line in File.ReadAllLines(tsv, Encoding.UTF8))
            {
                var a = line.Split('\t'); if (a.Length < 6 || a[0].Trim().ToUpperInvariant() != "SUPPRIMER") continue;
                if (!Regex.IsMatch(a[1], @"^\d+$")) continue;
                items.Add(a);
            }
            string doneFile = tsv + ".fait";
            var done = new HashSet<string>(File.Exists(doneFile) ? File.ReadAllLines(doneFile) : new string[0]);
            items = items.Where(a => !done.Contains(a[1])).ToList();
            if (items.Count == 0) { Log("Rien à supprimer dans ce rapport (déjà fait ou aucune ligne SUPPRIMER)."); return; }
            if (!EdgeAlive())
            {
                Log("Edge n'est pas ouvert : cliquez d'abord sur « Ouvrir QRZ dans Edge » et connectez-vous.");
                if (!cleanAuto) Invoke((Action)(() => MessageBox.Show(this, "Edge n'est pas ouvert.\r\n\r\n1) Cliquez sur « Ouvrir QRZ dans Edge »\r\n2) Connectez-vous à QRZ\r\n3) Relancez « Supprimer les doublons »", "Doublons", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                return;
            }
            if (!cleanAuto)
            {
                var ans = (DialogResult)Invoke((Func<DialogResult>)(() => MessageBox.Show(this,
                    items.Count + " contact(s) vont être SUPPRIMÉS de votre journal QRZ (lignes SUPPRIMER du rapport) :\r\n" + tsv +
                    "\r\n\r\nLa suppression est définitive sur QRZ (le fichier ADIF « -supprimes.adi » permet de les réimporter). Continuer ?", "Supprimer les doublons", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)));
                if (ans != DialogResult.Yes) { Log("Annulé, rien n'a été supprimé."); return; }
            }
            int[] waits = { 30, 60, 120, 300 };
            Cdp c = null; int ok = 0, consec = 0, pauseN = 0, n = Math.Min(items.Count, cleanMax);
            Prog(0, n);
            var queue = items;
            try
            {
                for (int round = 1; round <= 4 && queue.Count > 0 && !stop && ok < cleanMax; round++)
                {
                    if (round > 1)
                    {
                        Log("Reprise des " + queue.Count + " échec(s) après une pause de 60 s (passage " + round + "/4)...");
                        if (c != null) { try { c.Dispose(); } catch { } c = null; }
                        for (int s = 0; s < 60 && !stop; s++) Thread.Sleep(1000);
                    }
                    var failed = new List<string[]>();
                    foreach (var a in queue)
                    {
                        if (stop || ok >= cleanMax) break;
                        bool good = false; string why = "";
                        try
                        {
                            if (c == null)
                            {
                                c = Attach();
                                if (round > 1 || consec > 0 || c.Eval("(document.getElementById('lbmenu')&&document.querySelector('tr.lrow'))?'1':''") == "")
                                {
                                    try { c.Eval("location.href='https://logbook.qrz.com/'"); } catch { }
                                    Thread.Sleep(6000);
                                    if (c.Eval("document.getElementById('lbmenu')?'1':''") == "") throw new Exception("la liste du journal n'est pas affichée dans Edge (connexion QRZ ?)");
                                }
                            }
                            try { c.Eval(JsLock + "400)"); } catch { }
                            var d = ser.Deserialize<Dictionary<string, object>>(c.Eval(JsDel + "'" + a[1] + "','" + (Regex.IsMatch(a[2], @"^\d+$") ? a[2] : "0") + "','" + Regex.Replace(a[3], @"[^\w-]", "") + "')"));
                            good = (bool)d["ok"]; why = Convert.ToString(d["why"]);
                        }
                        catch (Exception ex)
                        {
                            why = ex is AggregateException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                            if (c != null) { try { c.Dispose(); } catch { } c = null; }
                        }
                        string desc = a[4] + " " + a[5] + " " + a[6] + " " + a[7];
                        if (good) { ok++; consec = 0; pauseN = 0; File.AppendAllText(doneFile, a[1] + "\r\n"); Log("Supprimé " + ok + "/" + n + " : " + desc); Prog(ok, n); Thread.Sleep(500); continue; }
                        if (why == "fiche introuvable" && round >= 2) { Log("Fiche introuvable (déjà supprimée ?) : " + desc + " — ignorée."); File.AppendAllText(doneFile, a[1] + "\r\n"); continue; }
                        failed.Add(a); consec++;
                        Log("Échec pour " + desc + " : " + why + " — on passe au suivant.");
                        if (consec >= 3)
                        {
                            int w = waits[Math.Min(pauseN++, waits.Length - 1)];
                            Log("Plusieurs échecs de suite : pause de " + w + " s (QRZ se calme), puis reprise avec le contact suivant.");
                            if (c != null) { try { c.Dispose(); } catch { } c = null; }
                            for (int s = 0; s < w && !stop; s++) Thread.Sleep(1000);
                            consec = 0;
                        }
                    }
                    // contacts restants non traites (arret) : ne pas les perdre de vue
                    queue = failed;
                }
                if (queue.Count > 0 && !stop) Log(queue.Count + " contact(s) n'ont pas pu être supprimés (voir « Échec » ci-dessus). Relancez plus tard avec le même rapport : les contacts déjà supprimés sont ignorés.");
            }
            finally
            {
                if (c != null) { try { c.Eval("location.href='https://logbook.qrz.com/'"); } catch { } try { c.Dispose(); } catch { } }
            }
            Log("Terminé : " + ok + " contact(s) supprimé(s)." + (stop ? " (arrêté à la demande)" : ""));
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

        // ---------- Options d'une recuperation ----------
        class Opts { public string from = "", to = "", file = ""; public bool real, resume, defRst = true; }
        bool resumeChoice, bakDone; string testFrom = "", testTo = ""; bool testReal, testResume;
        Opts ReadOpts()
        {
            var o = new Opts();
            if (testLog != null) { o.from = testFrom; o.to = testTo; o.real = testReal; o.resume = testResume; o.file = txtOut.Text; o.defRst = chkRst.Checked; return o; }
            o.from = dtpFrom.Checked ? dtpFrom.Value.ToString("yyyyMMdd") : "";
            o.to = dtpTo.Checked ? dtpTo.Value.ToString("yyyyMMdd") : "";
            o.real = chkReal.Checked; o.resume = resumeChoice; o.file = txtOut.Text; o.defRst = chkRst.Checked;
            return o;
        }
        static string DateOf(Dictionary<string, string> q) { string d; return q != null && q.TryGetValue("QSO_DATE", out d) ? d : ""; }
        // dates (yyyyMMdd) de la 1re et de la derniere ligne datee d'une page
        void PageDates(PageData p, string[] map, out string first, out string last)
        {
            first = ""; last = "";
            foreach (var r in p.rows) { var q = RowToQso(map, r); string d = DateOf(q); if (d == "") continue; if (first == "") first = d; last = d; }
        }
        static string RowSig(PageData p)
        {
            var sb = new StringBuilder("" + p.rows.Count);
            for (int i = 0; i < Math.Min(3, p.rows.Count); i++) sb.Append("|" + string.Join(";", p.rows[i].ToArray()));
            if (p.rows.Count > 0) sb.Append("|" + string.Join(";", p.rows[p.rows.Count - 1].ToArray()));
            return sb.ToString();
        }
        // Va directement a la page n (la liste QRZ se recharge en AJAX). Recharge le logbook si QRZ ne repond pas.
        PageData GotoPage(Cdp c, int n, PageData cur)
        {
            string before = cur == null ? "" : RowSig(cur);
            for (int round = 0; round < 2 && !stop; round++)
            {
                for (int attempt = 0; attempt < 3 && !stop; attempt++)
                {
                    if (attempt > 0) Log("QRZ n'a pas répondu (page " + n + ") : nouvel essai " + attempt + "/2...");
                    try { c.Eval("jQuery('#ipage').val(" + n + ");goto('page',true);"); } catch { }
                    DateTime t0 = DateTime.Now; PageData q = null;
                    while ((DateTime.Now - t0).TotalSeconds < 45 && !stop)
                    {
                        Thread.Sleep(400);
                        try { q = Extract(c); } catch { continue; }
                        if (q.ipage == n.ToString() && q.rows.Count > 0 && (before == "" || RowSig(q) != before)) return Settle(c, q);
                    }
                }
                Log("Page bloquée : rechargement du logbook...");
                try { c.Eval("location.href='https://logbook.qrz.com/'"); } catch { }
                DateTime t1 = DateTime.Now; PageData f = null;
                while ((DateTime.Now - t1).TotalSeconds < 40 && !stop)
                {
                    Thread.Sleep(800);
                    try { f = Extract(c); } catch { continue; }
                    if (f.rows.Count > 0 && (f.ipage == "1" || f.ipage == "")) { f.ipage = "1"; f = Settle(c, f); if (n == 1) return f; before = RowSig(f); break; }
                }
            }
            return null;
        }
        // Recherche dichotomique : plus petite page dont la derniere ligne atteint la borne (desc : date <= borne ; sinon date >= borne)
        int FindStartPage(Cdp c, string[] map, int total, bool desc, string bound, ref PageData cur, ref int curPage)
        {
            int lo = 1, hi = total;
            while (lo < hi && !stop)
            {
                int mid = (lo + hi) / 2;
                var p = GotoPage(c, mid, cur); if (p == null) return -1;
                cur = p; curPage = mid;
                string f, l; PageDates(p, map, out f, out l);
                bool ok = l != "" && (desc ? string.CompareOrdinal(l, bound) <= 0 : string.CompareOrdinal(l, bound) >= 0);
                Log("  recherche du " + bound + " : page " + mid + " (" + f + " ... " + l + ")");
                if (ok) hi = mid; else lo = mid + 1;
            }
            return lo;
        }
        // Lit la fiche de chaque contact (rapports reels, QTH, commentaire...) par requete directe, 4 a la fois
        int FetchDetails(Cdp c, List<Dictionary<string, string>> fresh)
        {
            var byId = new Dictionary<string, Dictionary<string, string>>();
            var items = new List<Dictionary<string, object>>();
            foreach (var q in fresh)
            {
                string id, pos, book; if (!q.TryGetValue("_ID", out id) || id == "") continue;
                q.TryGetValue("_POS", out pos); q.TryGetValue("_BOOK", out book);
                byId[id] = q; items.Add(new Dictionary<string, object> { { "id", id }, { "pos", pos ?? "0" }, { "book", book ?? "" } });
            }
            if (items.Count == 0) return 0;
            string res = c.Eval(Js.Detail + ser.Serialize(items) + ")");
            int bad = 0, noRst = 0;
            foreach (Dictionary<string, object> d in (System.Collections.IEnumerable)ser.DeserializeObject(res))
            {
                Dictionary<string, string> q; if (!byId.TryGetValue(Convert.ToString(d["id"]), out q)) continue;
                if (d.ContainsKey("err")) { bad++; continue; }
                Func<string, string> g = k => d.ContainsKey(k) ? Convert.ToString(d[k]).Trim() : "";
                Func<string, bool> rstOk = v => Regex.IsMatch(v, @"^[+-]?\d{1,3}$");
                if (rstOk(g("rs"))) q["RST_SENT"] = g("rs"); else noRst++;
                if (rstOk(g("rr"))) q["RST_RCVD"] = g("rr");
                if (g("qth") != "" && !q.ContainsKey("QTH")) q["QTH"] = g("qth");
                if (g("st") != "" && !q.ContainsKey("STATE")) q["STATE"] = g("st");
                if (g("cy") != "" && !q.ContainsKey("CNTY")) q["CNTY"] = g("cy");
                if (g("cm") != "") q["COMMENT"] = g("cm");
            }
            if (bad > 0) Log("  " + bad + " fiche(s) illisible(s) sur " + items.Count + ".");
            return bad;
        }

        void RunWeb()
        {
            Opts o = null; Invoke((Action)(() => o = ReadOpts())); bakDone = false;
            using (var c = Attach())
            {
                var qsos = new List<Dictionary<string, string>>();
                var seen = new Adif.Seen();
                var p = Extract(c);
                if (p.login && !p.table) { Log("La page demande un mot de passe : connectez-vous dans Edge, affichez votre journal, puis recommencez."); return; }
                if (!p.table || p.rows.Count == 0) { Log("Aucun tableau de contacts sur cette page (" + p.url + "). Affichez la liste de vos contacts dans Edge. Au besoin, utilisez 'Enregistrer la page (diagnostic)'."); return; }
                int total = p.pages;
                var map = MapCols(p.cols, p.headers);
                if (!map.Contains("CALL")) { Log("Colonne d'indicatif non reconnue dans : " + string.Join(" | ", p.headers.ToArray()) + ". Utilisez 'Enregistrer la page (diagnostic)'."); return; }
                int curPage; if (!int.TryParse(p.ipage, out curPage) || curPage <= 0) curPage = 1;
                string d1, d2; PageDates(p, map, out d1, out d2);
                bool desc = string.CompareOrdinal(d1, d2) >= 0;
                Log("Liste QRZ : " + p.rows.Count + " lignes par page, " + (total > 0 ? total + " pages" : "nombre de pages inconnu") + ", ordre " + (desc ? "du plus récent au plus ancien" : "du plus ancien au plus récent") + (o.from != "" || o.to != "" ? ". Période : " + (o.from == "" ? "début" : o.from) + " -> " + (o.to == "" ? "fin" : o.to) : "") + (o.real ? ". Rapports RÉELS demandés (lent)." : "."));

                // reprise d'un fichier existant
                string resumeBound = "";
                if (o.resume && File.Exists(o.file))
                {
                    var old = Adif.Parse(File.ReadAllText(o.file, Encoding.UTF8));
                    string mn = "99999999", mx = "";
                    foreach (var q in old)
                    {
                        string lid; if (q.TryGetValue("APP_QRZLOG_LOGID", out lid)) { q["_ID"] = lid; q.Remove("APP_QRZLOG_LOGID"); }
                        seen.AddExisting(q); qsos.Add(q);
                        string dd = DateOf(q); if (dd != "") { if (string.CompareOrdinal(dd, mn) < 0) mn = dd; if (string.CompareOrdinal(dd, mx) > 0) mx = dd; }
                    }
                    resumeBound = desc ? mn : mx; if (mx == "") resumeBound = "";
                    Log("REPRISE : " + old.Count + " contacts déjà dans le fichier (du " + (mx == "" ? "?" : mn) + " au " + mx + "). On continue à partir du " + resumeBound + ".");
                }

                // page de depart
                string startBound = "";
                if (desc) { startBound = o.to; if (resumeBound != "" && (startBound == "" || string.CompareOrdinal(resumeBound, startBound) < 0)) startBound = resumeBound; }
                else { startBound = o.from; if (resumeBound != "" && (startBound == "" || string.CompareOrdinal(resumeBound, startBound) > 0)) startBound = resumeBound; }
                int startPage = 1;
                if (startBound != "" && total > 1) { startPage = FindStartPage(c, map, total, desc, startBound, ref p, ref curPage); if (startPage < 0) { Log("Recherche de la page de départ impossible."); return; } }
                if (curPage != startPage || p == null)
                {
                    var sp = GotoPage(c, startPage, p); if (sp == null) { Log(stop ? "Arrêté." : "Impossible d'atteindre la page " + startPage + "."); return; }
                    p = sp;
                }
                int page = startPage; int firstPage = startPage;
                if (startPage > 1) Log("Départ à la page " + startPage + ".");

                while (!stop)
                {
                    string pf, pl; PageDates(p, map, out pf, out pl);
                    var fresh = new List<Dictionary<string, string>>();
                    foreach (var r in p.rows)
                    {
                        var q = RowToQso(map, r); if (q == null) continue;
                        string dd = DateOf(q);
                        if (o.from != "" && dd != "" && string.CompareOrdinal(dd, o.from) < 0) continue;
                        if (o.to != "" && dd != "" && string.CompareOrdinal(dd, o.to) > 0) continue;
                        if (seen.IsNew(q)) fresh.Add(q);
                    }
                    if (o.real && fresh.Count > 0 && !stop)
                    {
                        int bad = FetchDetails(c, fresh);
                        if (bad >= 10 || (bad > 0 && bad * 2 >= fresh.Count))
                        {
                            Log("QRZ refuse de donner les fiches (limite de consultations atteinte ?). ARRÊT : cette page n'est pas enregistrée. Relancez plus tard avec le même fichier et choisissez « Compléter » : la reprise repart de cette page.");
                            break;
                        }
                    }
                    qsos.AddRange(fresh);
                    Log("Page " + page + (total > 0 ? "/" + total : "") + " (" + pf + " ... " + pl + ") : " + fresh.Count + " nouveaux contacts (total " + qsos.Count + ")");
                    Prog(page, total > 0 ? total : page + 1);
                    if ((page - firstPage + 1) % (o.real ? 5 : 100) == 0) Save(qsos, true, o.defRst);   // sauvegarde intermediaire
                    bool done = total > 0 && page >= total;
                    if (!done && pl != "")
                    {
                        if (desc && o.from != "" && string.CompareOrdinal(pl, o.from) < 0) done = true;
                        if (!desc && o.to != "" && string.CompareOrdinal(pl, o.to) > 0) done = true;
                    }
                    if (done) break;
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
                    if (!changed) { Log(stop ? "Arrêté." : "La page suivante ne s'est pas chargée après 4 essais : fin (le fichier contient les pages déjà lues ; relancez pour reprendre)."); break; }
                    np = Settle(c, np);
                    p = np; page++;
                    if (total > 0 && page > total + 2) break;
                }
                Save(qsos, false, o.defRst);
            }
        }

        void Save(List<Dictionary<string, string>> qsos, bool quiet, bool defRst)
        {
            string f = "";
            Invoke((Action)(() => f = txtOut.Text));
            if (qsos.Count == 0) { if (!quiet) Log("Aucun contact récupéré, fichier non modifié."); return; }
            if (defRst)
            {
                var unk = Adif.ApplyReports(qsos);
                if (!quiet)
                {
                    if (unk.Count == 0) Log("Rapports par défaut : tous les modes avaient une règle.");
                    else Log("MODES SANS RAPPORT (pas de règle dans " + Adif.RulesFileName + ") : " + string.Join(", ", unk.OrderByDescending(k => k.Value).Select(k => k.Key + " (" + k.Value + ")").ToArray()));
                }
            }
            Adif.Sort(qsos);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(f)));
            if (!bakDone && File.Exists(f)) { try { File.Copy(f, f + ".bak", true); } catch { } bakDone = true; }
            string tmp = f + ".tmp";
            File.WriteAllText(tmp, Adif.Write(qsos), new UTF8Encoding(false));
            if (File.Exists(f)) File.Delete(f);
            File.Move(tmp, f);
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
            bool dr = true; Invoke((Action)(() => dr = chkRst.Checked)); Save(qsos, false, dr);
        }
    }
}
