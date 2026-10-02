using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Management;
using System.Drawing.Drawing2D;
using System.Globalization;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
namespace HermesRecovery {
public class ArchiveInfo {
 public string File, Hash; public int Count, Skipped; public long Bytes;
 public Dictionary<string,string> Critical = new Dictionary<string,string>();
 public Dictionary<string,long> Inventory = new Dictionary<string,long>();
}
public static class Core {
 public static DateTime? BackupTime(string path) {
  var name=Path.GetFileNameWithoutExtension(path);var m=Regex.Match(name,@"(\d{4}-\d{2}-\d{2}-\d{6}|\d{8}_\d{6})$");DateTime t;
  return m.Success&&DateTime.TryParseExact(m.Value,new[]{"yyyy-MM-dd-HHmmss","yyyyMMdd_HHmmss"},CultureInfo.InvariantCulture,DateTimeStyles.None,out t)?(DateTime?)t:null;
 }
 public static string ResolveExe(string directory) {
  if(string.IsNullOrWhiteSpace(directory))return "";
  foreach(var p in new[]{Path.Combine(directory,"bin","hermes.exe"),Path.Combine(directory,"hermes.exe"),Path.Combine(directory,"..","bin","hermes.exe")})if(System.IO.File.Exists(p))return Path.GetFullPath(p);
  return "";
 }
 public static string DetectInstall() {
  var paths=new List<string>{Environment.GetEnvironmentVariable("HERMES_HOME"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"hermes")};
  foreach(var path in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))if(!string.IsNullOrWhiteSpace(path)){try{if(System.IO.File.Exists(Path.Combine(path.Trim('"'),"hermes.exe")))paths.Add(Path.GetFileName(path.TrimEnd('\\')).Equals("bin",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(path.TrimEnd('\\')):path);}catch{}}
  foreach(var p in paths)if(!string.IsNullOrWhiteSpace(p)&&ResolveExe(p)!="")return Path.GetFullPath(p);return "";
 }
 public static bool IsHermesProcess(string name,string path,string cmd,string home,string exe) {
  string install=Path.GetDirectoryName(exe);if(Path.GetFileName(install).Equals("bin",StringComparison.OrdinalIgnoreCase))install=Path.GetDirectoryName(install);
  bool owned=path.StartsWith(install+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||cmd.IndexOf(install+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)>=0||cmd.IndexOf(home+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)>=0;
  if(!owned)return false;
  if(Regex.IsMatch(name,@"^hermes(?:[ _-].*)?\.exe$",RegexOptions.IgnoreCase))return true;
  if(name.Equals("electron.exe",StringComparison.OrdinalIgnoreCase))return cmd.IndexOf("desktop",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("desktop",StringComparison.OrdinalIgnoreCase)>=0;
  return Regex.IsMatch(name,@"^(pythonw?|node)\.exe$",RegexOptions.IgnoreCase)&&Regex.IsMatch(cmd,@"hermes_cli|run_gateway\.py|run_agent\.py|gateway[\\/]run|hermes-agent[\\/].*\b(serve|dashboard|gateway)\b|[\\/]desktop[\\/]",RegexOptions.IgnoreCase);
 }
 public static void StopBackground(string home,string exe,Action<string> log) {
  for(int attempt=0;attempt<3;attempt++) {
   var candidates=new List<Tuple<int,string,string>>();
   using(var q=new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process"))foreach(ManagementObject p in q.Get()) {
    int id=Convert.ToInt32(p["ProcessId"]);string name=Convert.ToString(p["Name"]),path=Convert.ToString(p["ExecutablePath"]),cmd=Convert.ToString(p["CommandLine"]);
    if(id!=Process.GetCurrentProcess().Id&&IsHermesProcess(name,path,cmd,home,exe))candidates.Add(Tuple.Create(id,name,path));
   }
   if(candidates.Count==0){log("Hermes 后台进程已停止。");return;}
   foreach(var item in candidates) {
    try {using(var current=new ManagementObject("Win32_Process.Handle='"+item.Item1+"'")) {
     current.Get();if(!IsHermesProcess(Convert.ToString(current["Name"]),Convert.ToString(current["ExecutablePath"]),Convert.ToString(current["CommandLine"]),home,exe))continue;
     using(var proc=Process.GetProcessById(item.Item1)) {log("正在关闭 Hermes 进程："+item.Item2+" (PID "+item.Item1+")");if(!proc.CloseMainWindow()||!proc.WaitForExit(2500)){proc.Kill();if(!proc.WaitForExit(5000))throw new Exception("进程未能退出。");}}
    }}catch(ArgumentException){}catch(ManagementException e){if(e.ErrorCode!=ManagementStatus.NotFound)throw;}catch(Exception e){throw new Exception("无法关闭 Hermes 后台进程："+item.Item2+"；"+e.Message);}
   }
  }
  if(ActiveProcesses(home,exe).Length>0)throw new Exception("Hermes 后台进程重新启动或无法停止，请关闭其他 Hermes 管理工具再试。");
 }
 static readonly uint[] CrcTable=MakeCrcTable();
 static uint[] MakeCrcTable(){var t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;t[i]=c;}return t;}
 static uint Crc(Stream s) { uint c=0xffffffff;var buf=new byte[65536];int n;while((n=s.Read(buf,0,buf.Length))>0) for(int i=0;i<n;i++) c=CrcTable[(c^buf[i])&255]^(c>>8);return ~c; }
 public static void CheckCrc(string file) {
  var crcs=new List<uint>();
  using(var stream=System.IO.File.OpenRead(file)) using(var r=new BinaryReader(stream)) {
   long start=Math.Max(0,stream.Length-65557), end=-1;stream.Position=start;var tail=r.ReadBytes((int)(stream.Length-start));
   for(int i=tail.Length-22;i>=0;i--) if(BitConverter.ToUInt32(tail,i)==0x06054b50 && i+22+BitConverter.ToUInt16(tail,i+20)==tail.Length){end=start+i;break;}
   if(end<0)throw new Exception("ZIP 目录损坏。");stream.Position=end+4;
   ushort disk=r.ReadUInt16(),dirDisk=r.ReadUInt16(),diskCount=r.ReadUInt16(),count=r.ReadUInt16();uint size=r.ReadUInt32(),offset=r.ReadUInt32();
   if(disk!=0||dirDisk!=0||diskCount!=count||count==65535||offset==uint.MaxValue||size==uint.MaxValue)throw new Exception("此版本不支持分卷或 ZIP64 备份。");
   stream.Position=offset;
   for(int i=0;i<count;i++){if(r.ReadUInt32()!=0x02014b50)throw new Exception("ZIP 条目目录损坏。");stream.Position+=12;uint crc=r.ReadUInt32();stream.Position+=8;ushort name=r.ReadUInt16(),extra=r.ReadUInt16(),comment=r.ReadUInt16();stream.Position+=12;stream.Position+=name+extra+comment;crcs.Add(crc);}
  }
  using(var z=ZipFile.OpenRead(file)) {if(z.Entries.Count!=crcs.Count)throw new Exception("ZIP 条目数不匹配。");for(int i=0;i<crcs.Count;i++)using(var s=z.Entries[i].Open())if(Crc(s)!=crcs[i])throw new Exception("备份 CRC 校验失败，文件已损坏。未进行恢复。");}
 }
 public static string Hash(Stream s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-",""); }
 public static string HashFile(string p) { using(var s=System.IO.File.OpenRead(p)) return Hash(s); }
 public static string Normalize(string p) {
  p=p.Replace('\\','/'); if(p.StartsWith("hermes/")) p=p.Substring(7); else if(p.StartsWith(".hermes/")) p=p.Substring(8);
  if(p.StartsWith("/") || p.IndexOfAny(new[]{':','*','?','\"','<','>','|','\0'})>=0 || p.Split('/').Any(x=>x==".." || x=="." || x.TrimEnd(' ','.')!=x && x!="")) throw new Exception("备份含不安全路径，已拒绝处理。");
  foreach(var part in p.Split('/')) if(Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)) throw new Exception("备份含 Windows 保留名称。");
  return p;
 }
 public static bool Skip(string p) {
  string n=Path.GetFileName(p); string root=p.Split('/')[0];
  return new[]{"hermes-agent","bin","node","runtime","desktop","desktop-plugins","gateway-service","bootstrap-cache","backups","state-snapshots"}.Contains(root,StringComparer.OrdinalIgnoreCase)
    || new[]{"gateway_state.json","gateway.pid","cron.pid","gateway.lock","processes.json","hermes-setup.exe","desktop-build-stamp.json","web-ui-build-stamp.json","install_id"}.Contains(n,StringComparer.OrdinalIgnoreCase)
    || n.EndsWith(".db-wal",StringComparison.OrdinalIgnoreCase) || n.EndsWith(".db-shm",StringComparison.OrdinalIgnoreCase) || n.EndsWith(".db-journal",StringComparison.OrdinalIgnoreCase);
 }
 public static ArchiveInfo Inspect(string file,Action<string> log) {
  using(var pre=ZipFile.OpenRead(file)) {long total=0;if(pre.Entries.Count>150000)throw new Exception("备份条目过多。");foreach(var e in pre.Entries){Normalize(e.FullName);total+=e.Length;if(total>30L*1024*1024*1024)throw new Exception("解压大小超过 30 GB，此版本不支持。");}}
  CheckCrc(file);
  var a=new ArchiveInfo{File=Path.GetFullPath(file)}; var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  using(var z=ZipFile.OpenRead(file)) {
   if(z.Entries.Count>150000) throw new Exception("备份条目过多，超出此工具支持范围。");
   foreach(var e in z.Entries) {
    var p=Normalize(e.FullName); if(e.FullName.EndsWith("/")) continue;
    if(!seen.Add(p)) throw new Exception("备份含重复路径，无法安全恢复。");
    if(p.StartsWith("_external/",StringComparison.OrdinalIgnoreCase)) throw new Exception("备份包含用户目录外的记忆服务数据。此版本不支持该格式，请交给 agent 审核。");
    if(((e.ExternalAttributes>>16)&0xf000)==0xa000) throw new Exception("备份包含符号链接，此版本拒绝自动恢复。");
    a.Count++; a.Bytes+=e.Length;
    if(a.Bytes>30L*1024*1024*1024) throw new Exception("解压大小超过 30 GB，此版本不支持。");
    using(var s=e.Open()) {
     // Fully decompress every member before any restore. Official import additionally checks CRC.
     string h=Hash(s);
     if(new[]{"config.yaml",".env","auth.json","SOUL.md"}.Contains(p) || p.StartsWith("memories/") && !p.EndsWith(".lock")) a.Critical[p]=h;
    }
    if(Skip(p)) a.Skipped++; else a.Inventory[p]=e.Length;
   }
  }
  if(!a.Critical.ContainsKey("config.yaml") || !a.Inventory.ContainsKey("state.db")) throw new Exception("未找到 config.yaml 和 state.db，不能认定为全量备份。");
  a.Hash=HashFile(file); log("已读取全部条目："+a.Count+"；用户数据："+a.Inventory.Count+"；本机程序/运行状态保留："+a.Skipped+"。");
  log("备份 SHA256："+a.Hash); return a;
 }
 public static void Filter(ArchiveInfo a,string output) {
  if(HashFile(a.File)!=a.Hash) throw new Exception("备份在检查后发生变化，请重新检查。");
  using(var src=ZipFile.OpenRead(a.File)) using(var dst=ZipFile.Open(output,ZipArchiveMode.Create)) {
   foreach(var e in src.Entries) { var p=Normalize(e.FullName); if(e.FullName.EndsWith("/")||Skip(p)) continue;
    var t=dst.CreateEntry(p,CompressionLevel.Fastest); using(var s=e.Open()) using(var d=t.Open()) s.CopyTo(d);
   }
  }
 }
 public static string Quote(string s) {
  var b=new StringBuilder("\"");int slash=0;foreach(char c in s){if(c=='\\'){slash++;continue;}if(c=='\"'){b.Append('\\',slash*2+1);b.Append(c);}else{b.Append('\\',slash);b.Append(c);}slash=0;}b.Append('\\',slash*2);b.Append('"');return b.ToString();
 }
 public static string Run(string exe,string args,string home,Action<string> log,int seconds=300) {
  var info=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  info.EnvironmentVariables["HERMES_HOME"]=home; info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8"; info.EnvironmentVariables["PYTHONUTF8"]="1"; info.EnvironmentVariables.Remove("HERMES_PROFILE");
  var output=new StringBuilder(); object sync=new object();
  using(var proc=new Process{StartInfo=info}) {
   DataReceivedEventHandler handler=(s,e)=>{if(e.Data!=null) { lock(sync) output.AppendLine(e.Data); log(e.Data); }};
   proc.OutputDataReceived+=handler; proc.ErrorDataReceived+=handler;
   if(!proc.Start()) throw new Exception("无法启动 Hermes。"); proc.StandardInput.Close(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
   if(!proc.WaitForExit(seconds*1000)) { try { proc.Kill(); } catch {} throw new Exception("操作超时，结果未知。关闭 Hermes 后检查日志；全新安装恢复失败可重新安装后再导入原始备份。"); }
   proc.WaitForExit(); var result=output.ToString();
   if(proc.ExitCode!=0 || Regex.IsMatch(result,@"(?im)^\s*(Error:|Import incomplete:|Backup incomplete:|Warnings \(\d+ files skipped)")) throw new Exception("Hermes 未完整完成操作（退出码 "+proc.ExitCode+"）。请复制日志给 agent。");
   return result;
  }
 }
 public static string[] ActiveProcesses(string home,string exe) {
  var list=new List<string>();
  using(var q=new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process"))
   foreach(ManagementObject p in q.Get()) {
    int id=Convert.ToInt32(p["ProcessId"]); if(id==Process.GetCurrentProcess().Id) continue;
    string cmd=Convert.ToString(p["CommandLine"]), path=Convert.ToString(p["ExecutablePath"]), name=Convert.ToString(p["Name"]);
    if(IsHermesProcess(name,path,cmd,home,exe)) list.Add(name+" (PID "+id+")");
   }
  return list.ToArray();
 }
 public static void EnsureNoLinks(string home,ArchiveInfo a) {
  foreach(var rel in a.Inventory.Keys) {
   string path=Path.Combine(home,rel.Replace('/',Path.DirectorySeparatorChar));
   while(path!=null) {
    if((System.IO.File.Exists(path)||Directory.Exists(path)) && (System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new Exception("目标存在链接或挂载点。此版本不自动覆盖，请让 agent 检查。");
    path=Path.GetDirectoryName(path);
   }
  }
 }
 public static void Verify(ArchiveInfo a,string home,Action<string> log) {
  int missing=0,bad=0; foreach(var item in a.Inventory) {
   var p=Path.Combine(home,item.Key.Replace('/',Path.DirectorySeparatorChar));
   if(!System.IO.File.Exists(p)) missing++;
  }
  foreach(var item in a.Critical) {
   var p=Path.Combine(home,item.Key.Replace('/',Path.DirectorySeparatorChar));
   if(!System.IO.File.Exists(p)||HashFile(p)!=item.Value) bad++;
  }
  log("恢复核对：缺失文件 "+missing+"；配置/凭据/原生记忆哈希差异 "+bad+"。");
  if(missing>0 || bad>0) throw new Exception("文件核对未通过，请复制日志给 agent。");
  log("文件落盘核对通过。数据库逻辑健康、API、网关与第三方记忆仍需下面的体检。");
 }
}


public static class CuteTheme {
 public static Color Navy=Color.FromArgb(37,67,114),Blue=Color.FromArgb(48,125,231),Pink=Color.FromArgb(233,103,143);
 public static GraphicsPath Round(RectangleF r,float rad){var p=new GraphicsPath();float d=rad*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static void Paw(Graphics g,float x,float y,float size,Color c){using(var b=new SolidBrush(c)){g.FillEllipse(b,x+size*.22f,y+size*.43f,size*.54f,size*.43f);g.FillEllipse(b,x,y+size*.20f,size*.22f,size*.28f);g.FillEllipse(b,x+size*.23f,y,size*.22f,size*.28f);g.FillEllipse(b,x+size*.51f,y,size*.22f,size*.28f);g.FillEllipse(b,x+size*.77f,y+size*.20f,size*.22f,size*.28f);}}
 public static void Glyph(Graphics g,string name,RectangleF r,Color c){if(string.IsNullOrEmpty(name))return;var state=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/32,r.Height/32);using(var pen=new Pen(c,2.5f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){
  if(name=="search"){g.DrawEllipse(pen,5,4,18,18);g.DrawLine(pen,21,21,28,28);}
  else if(name=="play"){g.DrawPolygon(pen,new[]{new Point(9,5),new Point(27,16),new Point(9,27)});}
  else if(name=="folder"){g.DrawLines(pen,new[]{new Point(3,25),new Point(3,8),new Point(12,8),new Point(15,11),new Point(29,11),new Point(29,25),new Point(3,25)});}
  else if(name=="backup"){g.DrawEllipse(pen,5,3,22,8);g.DrawArc(pen,5,10,22,8,0,180);g.DrawArc(pen,5,19,22,8,0,180);g.DrawLine(pen,5,7,5,23);g.DrawLine(pen,27,7,27,23);}
  else if(name=="doctor"){g.DrawArc(pen,4,3,16,17,0,180);g.DrawLine(pen,4,5,4,12);g.DrawLine(pen,20,5,20,12);g.DrawLine(pen,12,20,12,24);g.DrawArc(pen,12,19,14,10,0,180);g.DrawLine(pen,26,24,26,17);g.DrawEllipse(pen,22,9,8,8);}
  else if(name=="globe"){g.DrawEllipse(pen,3,3,26,26);g.DrawEllipse(pen,10,3,12,26);g.DrawLine(pen,3,16,29,16);}
  else if(name=="gear"){g.DrawEllipse(pen,6,6,20,20);g.DrawEllipse(pen,12,12,8,8);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(pen,16+(float)Math.Cos(a)*10,16+(float)Math.Sin(a)*10,16+(float)Math.Cos(a)*14,16+(float)Math.Sin(a)*14);}}
  else if(name=="save"){g.DrawRectangle(pen,4,4,24,24);g.DrawRectangle(pen,9,4,13,9);g.DrawRectangle(pen,9,20,13,8);}
  else if(name=="copy"){g.DrawRectangle(pen,10,8,17,21);g.DrawLines(pen,new[]{new Point(6,23),new Point(3,23),new Point(3,3),new Point(21,3)});}
  else if(name=="clear"){g.DrawLine(pen,7,7,25,25);g.DrawLine(pen,25,7,7,25);}
  else {g.DrawRectangle(pen,6,5,21,24);g.DrawLine(pen,11,13,22,13);g.DrawLine(pen,11,19,22,19);}
 }g.Restore(state);}
}
public class RaisedButton : Button {
 public bool Primary,Pink;public string Glyph="",Subtitle="";bool pressed,hover;
 public RaisedButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;BackColor=Color.FromArgb(253,254,255);}
 Color Surface(){for(Control c=Parent;c!=null;c=c.Parent)if(c is CardPanel)return Color.FromArgb(253,254,255);return Color.FromArgb(239,247,255);}
 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Surface());}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width>6&&Height>6){using(var p=CuteTheme.Round(new RectangleF(0,0,Width,Height),6*DeviceDpi/96f)){var old=Region;Region=new Region(p);if(old!=null)old.Dispose();}}Invalidate();}
 protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){pressed=false;hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Surface());g.SmoothingMode=SmoothingMode.AntiAlias;float d=DeviceDpi/96f;var r=new RectangleF(d/2,d/2,Width-d,Height-d);Color fill=Primary?CuteTheme.Blue:Pink?Color.FromArgb(255,236,243):Color.FromArgb(246,250,255);Color ink=Primary?Color.White:Pink?Color.FromArgb(188,68,108):CuteTheme.Navy;Color border=Primary?CuteTheme.Blue:Color.FromArgb(193,213,237);if(hover)fill=Primary?Color.FromArgb(35,107,209):Color.FromArgb(229,240,254);if(pressed)fill=Primary?Color.FromArgb(28,91,184):Color.FromArgb(211,229,251);if(!Enabled){fill=Color.FromArgb(243,246,250);ink=Color.FromArgb(139,155,175);border=Color.FromArgb(219,228,239);}using(var path=CuteTheme.Round(r,6*d)){using(var brush=new SolidBrush(fill))g.FillPath(brush,path);using(var pen=new Pen(border,d))g.DrawPath(pen,path);}TextRenderer.DrawText(g,Text,Font,Rectangle.Inflate(ClientRectangle,-(int)(5*d),0),ink,TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);if(Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-(int)(4*d),-(int)(4*d)));}

}
public class CardPanel : Panel {
 public bool Inset;public CardPanel(){DoubleBuffered=true;BackColor=Color.Transparent;Padding=new Padding(12);}
 protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(1,1,Width-6,Height-6);using(var sh=CuteTheme.Round(new RectangleF(r.X+2,r.Y+3,r.Width,r.Height),16))using(var b=new SolidBrush(Color.FromArgb(23,141,181,232)))e.Graphics.FillPath(b,sh);using(var path=CuteTheme.Round(r,16)){using(var b=new SolidBrush(Color.FromArgb(253,254,255)))e.Graphics.FillPath(b,path);using(var p=new Pen(Color.FromArgb(214,229,249)))e.Graphics.DrawPath(p,path);}if(Inset){using(var p=new Pen(Color.FromArgb(168,192,224)))e.Graphics.DrawLine(p,r.Left+15,r.Top+1,r.Right-15,r.Top+1);}}
}
public class SectionTitle : Control {
 string step,caption;public SectionTitle(string s,string text){step=s;caption=text;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;Dock=DockStyle.Fill;}
 protected override void OnPaint(PaintEventArgs e){TextRenderer.DrawText(e.Graphics,caption, new Font(Font.FontFamily,10,FontStyle.Bold),ClientRectangle,CuteTheme.Navy,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}

}
public class PathCaption : Control {
 string title,sub,glyph;public PathCaption(string t,string s,string icon){title=t;sub=s;glyph=icon;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint,true);BackColor=Color.Transparent;Dock=DockStyle.Fill;}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Color ink=glyph=="backup"?CuteTheme.Pink:CuteTheme.Blue;using(var p=CuteTheme.Round(new RectangleF(0,3,40,42),12))using(var b=new SolidBrush(glyph=="backup"?Color.FromArgb(255,239,245):Color.FromArgb(235,247,255)))g.FillPath(b,p);CuteTheme.Glyph(g,glyph,new RectangleF(8,10,25,25),ink);using(var f=new Font(Font.FontFamily,10,FontStyle.Bold))TextRenderer.DrawText(g,title,f,new Point(52,5),CuteTheme.Navy);using(var f=new Font(Font.FontFamily,8.5f))TextRenderer.DrawText(g,sub,f,new Point(52,29),Color.FromArgb(122,146,179));}
}
public class CatSidebar : Panel {
 Image art;public bool Loaded{get{return art!=null;}}public CatSidebar(){DoubleBuffered=true;using(var s=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CatSidebar"))if(s!=null)using(var im=Image.FromStream(s))art=new Bitmap(im);}
 protected override void Dispose(bool disposing){if(disposing&&art!=null)art.Dispose();base.Dispose(disposing);}
 protected override void OnPaintBackground(PaintEventArgs e){var g=e.Graphics;g.Clear(Color.FromArgb(227,241,255));g.InterpolationMode=InterpolationMode.HighQualityBicubic;float d=DeviceDpi/96f;if(art!=null){int h=(int)(Width*(double)art.Height/art.Width);g.DrawImage(art,new Rectangle(0,Height-h,Width,h));}using(var f=new Font("Microsoft YaHei UI",17,FontStyle.Bold))TextRenderer.DrawText(g,"Hermes",f,new Rectangle((int)(12*d),(int)(20*d),Width-(int)(24*d),(int)(40*d)),CuteTheme.Navy);using(var f=new Font("Microsoft YaHei UI",10,FontStyle.Bold))TextRenderer.DrawText(g,"数据恢复助手",f,new Rectangle((int)(12*d),(int)(60*d),Width-(int)(24*d),(int)(28*d)),CuteTheme.Navy);using(var f=new Font("Microsoft YaHei UI",9))TextRenderer.DrawText(g,"miu edition",f,new Rectangle((int)(12*d),(int)(90*d),Width-(int)(24*d),(int)(24*d)),Color.FromArgb(92,122,164));}

}

public class MainWindow : Form {
 TextBox backup=new TextBox(),home=new TextBox(),exe=new TextBox(),install=new TextBox(); RichTextBox logs=new RichTextBox(); Label status=new Label(),backupDate=new Label(),installNote=new Label(); ProgressBar progress=new ProgressBar();
 Button inspect,restore,doctor,network,gateway; List<Control> actions=new List<Control>(); ArchiveInfo archive; bool busy=false;
 HashSet<string> secrets=new HashSet<string>(); object secretLock=new object();Font logBold;
 string tempDir=Path.Combine(Path.GetTempPath(),"HermesRestore");

 public MainWindow() {

  SuspendLayout();Text="Hermes 数据恢复助手 · miu edition";ClientSize=new Size(940,620);MinimumSize=new Size(640,480);StartPosition=FormStartPosition.CenterScreen;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
  Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(239,247,255);
  var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0),Margin=new Padding(0)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,164));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);shell.Controls.Add(new CatSidebar{Dock=DockStyle.Fill,Margin=new Padding(0)},0,0);
  var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,AutoScrollMinSize=new Size(0,480),Margin=new Padding(0)};shell.Controls.Add(viewport,1,0);
  var outer=new TableLayoutPanel{Dock=DockStyle.Top,Height=620,Padding=new Padding(12),ColumnCount=1,RowCount=6,Margin=new Padding(0)};viewport.Controls.Add(outer);viewport.SizeChanged+=(sender,e)=>outer.Height=Math.Max((int)(480*DeviceDpi/96f),viewport.ClientSize.Height);Shown+=(sender,e)=>outer.Height=Math.Max((int)(480*DeviceDpi/96f),viewport.ClientSize.Height);
  foreach(var h in new[]{222,128,38,34})outer.RowStyles.Add(new RowStyle(SizeType.Absolute,h));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,10));
  var prep=new CardPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,6)};outer.Controls.Add(prep);
  var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=6};prep.Controls.Add(fields);fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,102));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));foreach(var h in new[]{30,32,24,32,24,40})fields.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
  var heading=new SectionTitle("01","恢复前 · 选择备份与安装位置");fields.Controls.Add(heading,0,0);fields.SetColumnSpan(heading,3);
  PathRow(fields,1,"全量备份",backup,()=>{using(var d=new OpenFileDialog{Filter="Hermes 备份 (*.zip)|*.zip",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)})if(d.ShowDialog()==DialogResult.OK)backup.Text=d.FileName;});
  backupDate.Dock=DockStyle.Fill;backupDate.ForeColor=Color.FromArgb(112,139,180);backupDate.Font=new Font(Font.FontFamily,9);fields.Controls.Add(backupDate,1,2);fields.SetColumnSpan(backupDate,2);
  PathRow(fields,3,"Hermes安装目录",install,()=>{using(var d=new FolderBrowserDialog{Description="选择本机 Hermes 安装目录",SelectedPath=install.Text})if(d.ShowDialog()==DialogResult.OK)install.Text=d.SelectedPath;});
  installNote.Dock=DockStyle.Fill;installNote.ForeColor=Color.FromArgb(112,139,180);installNote.Font=new Font(Font.FontFamily,9);fields.Controls.Add(installNote,1,4);fields.SetColumnSpan(installNote,2);
  var primary=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0),Padding=new Padding(0,2,0,0)};fields.Controls.Add(primary,0,5);fields.SetColumnSpan(primary,3);
  restore=Btn(primary,"开始恢复",StartRestore);inspect=Btn(primary,"检查备份与环境",()=>Work(Check));((RaisedButton)inspect).Primary=true;((RaisedButton)inspect).Glyph="search";((RaisedButton)restore).Pink=true;((RaisedButton)restore).Glyph="play";
  foreach(var b in new[]{inspect,restore}){b.Size=new Size(148,34);b.Font=new Font(Font.FontFamily,9,FontStyle.Bold);}restore.Enabled=false;
  var post=new CardPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,6)};outer.Controls.Add(post);
  var after=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};post.Controls.Add(after);after.RowStyles.Add(new RowStyle(SizeType.Absolute,28));after.RowStyles.Add(new RowStyle(SizeType.Absolute,22));after.RowStyles.Add(new RowStyle(SizeType.Percent,100));after.Controls.Add(new SectionTitle("02","恢复后 · 检查配置与启动服务"));after.Controls.Add(new Label{Text="数据恢复完成后，再检查模型、API 和消息平台。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(112,139,180),Font=new Font(Font.FontFamily,9),Padding=new Padding(54,0,0,0)});
  var row2=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,2,0,0)};after.Controls.Add(row2);
  doctor=Btn(row2,"本地配置体检",()=>Work(Diagnostics));network=Btn(row2,"API 联网检查",()=>{if(MessageBox.Show("将通过 Hermes 查询当前模型提供商的账户信息。会访问外网，不发送聊天消息；仅支持 Hermes usage 支持的提供商。继续？","联网检查",MessageBoxButtons.OKCancel)==DialogResult.OK)Work(()=>{Command("usage");SetStatus("账户接口检查完成；请在 Hermes 中再测试所选模型对话。");});});gateway=Btn(row2,"启动 / 修复网关",()=>{if(MessageBox.Show("将安装并启动 Hermes 网关。恢复的机器人和定时任务可能开始执行。继续？","启动网关",MessageBoxButtons.OKCancel)==DialogResult.OK)Work(()=>{Command("gateway install");Command("gateway status");SetStatus("网关操作完成，请实际测试平台收发和定时任务。");});});
  Action resizePost=()=>{int gap=(int)(6*DeviceDpi/96f);int w=Math.Max(1,(row2.ClientSize.Width-3*gap)/3);foreach(var button in new[]{doctor,network,gateway}){button.Width=w;button.Height=(int)(34*DeviceDpi/96f);button.Margin=new Padding(0,0,gap,0);}};row2.SizeChanged+=(sender,e)=>resizePost();Shown+=(sender,e)=>resizePost();
  status.Text="等待检查。恢复开始时会自动停止网关并关闭 Hermes 后台进程。";status.Dock=DockStyle.Fill;status.ForeColor=Color.FromArgb(87,115,155);status.Padding=new Padding(5,9,0,0);outer.Controls.Add(status);
  var logHead=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0)};logHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));logHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,84));logHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,84));logHead.Controls.Add(new Label{Text="操作日志 · 选中文字可复制",Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),ForeColor=CuteTheme.Navy,Padding=new Padding(5,6,0,0)},0,0);var save=new RaisedButton{Text="保存日志",Dock=DockStyle.Fill,Margin=new Padding(0,0,6,0)};save.Click+=(sender,e)=>SaveLog();logHead.Controls.Add(save,1,0);var clear=new RaisedButton{Text="清空日志",Glyph="clear",Dock=DockStyle.Fill,Margin=new Padding(0)};clear.Click+=(sender,e)=>logs.Clear();logHead.Controls.Add(clear,2,0);actions.Add(clear);outer.Controls.Add(logHead);
  var well=new CardPanel{Dock=DockStyle.Fill,Inset=true,Padding=new Padding(10),Margin=new Padding(0)};outer.Controls.Add(well);logs.ReadOnly=true;logs.ShortcutsEnabled=true;logs.HideSelection=false;logs.ScrollBars=RichTextBoxScrollBars.Both;logs.WordWrap=false;logs.Dock=DockStyle.Fill;logs.Font=new Font("Microsoft YaHei UI",9);logBold=new Font(logs.Font,FontStyle.Bold);logs.BackColor=Color.FromArgb(253,254,255);logs.ForeColor=Color.FromArgb(44,66,101);logs.BorderStyle=BorderStyle.None;logs.DetectUrls=false;well.Controls.Add(logs);
  var menu=new ContextMenuStrip();var copy=menu.Items.Add("复制选中文字 (Ctrl+C)");copy.Click+=(sender,e)=>logs.Copy();menu.Items.Add("全选 (Ctrl+A)").Click+=(sender,e)=>logs.SelectAll();menu.Opening+=(sender,e)=>copy.Enabled=logs.SelectionLength>0;logs.ContextMenuStrip=menu;logs.KeyDown+=(sender,e)=>{if(e.Control&&e.KeyCode==Keys.A){logs.SelectAll();e.SuppressKeyPress=true;}};progress.Dock=DockStyle.Fill;outer.Controls.Add(progress);
  Action adapt=()=>{float d=DeviceDpi/96f;bool show=ClientSize.Width>=880*d;shell.GetControlFromPosition(0,0).Visible=show;shell.ColumnStyles[0].Width=show?164*d:0;outer.Height=Math.Max((int)(480*d),viewport.ClientSize.Height);};SizeChanged+=(sender,e)=>adapt();DpiChanged+=(sender,e)=>BeginInvoke(adapt);Shown+=(sender,e)=>{var area=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width-24),Math.Min(MinimumSize.Height,area.Height-24));Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);adapt();};
  backup.TextChanged+=(sender,e)=>{InvalidateCheck();UpdateBackupDate();};install.TextChanged+=(sender,e)=>{InvalidateCheck();UpdateInstallation();};home.TextChanged+=(sender,e)=>InvalidateCheck();exe.TextChanged+=(sender,e)=>InvalidateCheck();
  install.Text=Core.DetectInstall();if(string.IsNullOrEmpty(install.Text))UpdateInstallation();
  UpdateBackupDate();
  status.Text="等待检查。恢复开始时会自动停止网关并关闭 Hermes 后台进程。";
  FormClosing+=(sender,e)=>{if(busy){e.Cancel=true;MessageBox.Show("操作正在进行，完成后再关闭。中断恢复可能留下部分数据。");}};CleanupStaleTemps();AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
 }
 void UpdateInstallation(){
  try{exe.Text=Core.ResolveExe(install.Text);string root=exe.Text!=""?Path.GetDirectoryName(exe.Text):install.Text;if(exe.Text!=""&&Path.GetFileName(root).Equals("bin",StringComparison.OrdinalIgnoreCase))root=Path.GetDirectoryName(root);
   home.Text=Environment.GetEnvironmentVariable("HERMES_HOME")??(!string.IsNullOrWhiteSpace(root)&&(System.IO.File.Exists(Path.Combine(root,"config.yaml"))||System.IO.File.Exists(Path.Combine(root,"state.db")))?root:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"hermes"));
   installNote.Text=exe.Text!=""?"已找到 Hermes 程序 · 用户数据恢复到："+home.Text:"未检测到 Hermes 程序，请先安装或选择正确安装目录。";
  }catch{exe.Text="";installNote.Text="安装目录无效，请重新选择。";}
 }
 void UpdateBackupDate(){
  try{var t=Core.BackupTime(backup.Text);backupDate.Text=t.HasValue?"备份生成时间："+t.Value.ToString("yyyy-MM-dd HH:mm:ss")+"（备份文件名记录）":System.IO.File.Exists(backup.Text)?"生成时间未记录 · 文件修改时间："+System.IO.File.GetLastWriteTime(backup.Text).ToString("yyyy-MM-dd HH:mm:ss"):"备份生成时间：请选择全量备份文件";}catch{backupDate.Text="备份生成时间：无法读取";}
 }
 void CleanupStaleTemps() {
  if(!Directory.Exists(tempDir))return;
  try {foreach(var file in Directory.GetFiles(tempDir,"import-*.zip")) {
   if(!Regex.IsMatch(Path.GetFileName(file),@"^import-[a-f0-9]{32}\.zip$"))continue;
   if(System.IO.File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-1))try{System.IO.File.Delete(file);Log("已清理上次异常退出遗留的临时导入文件。");}catch(Exception e){Log("临时文件清理失败："+e.Message);}
  }}catch(Exception e){Log("临时目录检查失败："+e.Message);}
 }


 void PathRow(TableLayoutPanel p,int i,string label,TextBox box,Action browse){p.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=CuteTheme.Navy,Margin=new Padding(0)},0,i);box.Dock=DockStyle.Fill;box.Margin=new Padding(0,3,8,0);box.BackColor=Color.FromArgb(253,254,255);box.BorderStyle=BorderStyle.FixedSingle;p.Controls.Add(box,1,i);var b=new RaisedButton{Text="选择…",Dock=DockStyle.Fill,Margin=new Padding(0,0,0,2)};b.Click+=(sender,e)=>browse();p.Controls.Add(b,2,i);actions.Add(b);actions.Add(box);}
 Button Btn(FlowLayoutPanel p,string text,Action click,bool lockBusy=true){var b=new RaisedButton{Text=text,Size=new Size(174,44),Margin=new Padding(0,0,8,0)};b.Click+=(sender,e)=>{try{click();}catch(Exception err){Log("操作失败："+err.Message);SetStatus("操作失败，请查看日志。");}};p.Controls.Add(b);if(lockBusy)actions.Add(b);return b;}
 void InvalidateCheck(){archive=null;restore.Enabled=false;status.Text="路径已改变，请重新检查。";}
 string Clean(string text) {
  lock(secretLock) foreach(var value in secrets.OrderByDescending(x=>x.Length)) text=text.Replace(value,"[已隐藏]");
  text=Regex.Replace(text,@"\x1B\[[0-?]*[ -/]*[@-~]", "");
  text=Regex.Replace(text,@"(?i)(Bearer\s+)\S+","$1[已隐藏]");
  text=Regex.Replace(text,@"(?i)([\w.-]*(?:key|secret|token|password|credential)[\w.-]*[\""']?\s*[:=]\s*)[^\r\n]+","$1[已隐藏]");
  text=Regex.Replace(text,@"(?i)\b(?:sk-|ghp_|github_pat_|AIza)[A-Za-z0-9_\-]{8,}","[已隐藏]");
  text=Regex.Replace(text,@"(https?://[^\s?#]+)[?#][^\s]+","$1?[已隐藏]");
  text=Regex.Replace(text,@"(https?://)[^/\s@]+@","$1[已隐藏]@");
  return text;
 }
 void LoadSecrets(string env,string auth) {
  lock(secretLock) {
   if(env!=null) foreach(var line in env.Split('\n')) { var m=Regex.Match(line,@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$");if(!m.Success)continue;var v=m.Groups[2].Value.Trim().Trim('\"','\'');if(v.Length>=4) secrets.Add(v); }
   if(auth!=null) foreach(Match m in Regex.Matches(auth,@"[\""'](?:[^\""']*(?:token|secret|key|password)[^\""']*)[\""']\s*:\s*[\""']([^\""']+)[\""']",RegexOptions.IgnoreCase)) if(m.Groups[1].Value.Length>=4) secrets.Add(m.Groups[1].Value);
  }
 }

 void Log(string text){var safe=Clean(text);if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>Log(safe)));return;}logs.SelectionStart=logs.TextLength;logs.SelectionLength=0;bool version=Regex.IsMatch(safe,@"(?i)Hermes(?: Agent)?\s+v?\d+|\bversion\s*[:=]?\s*v?\d+");logs.SelectionFont=version?logBold:logs.Font;logs.SelectionColor=version?Color.FromArgb(25,74,135):logs.ForeColor;logs.AppendText("["+DateTime.Now.ToString("HH:mm:ss")+"] "+safe+Environment.NewLine);logs.ScrollToCaret();}
 void SetStatus(string t){if(InvokeRequired){BeginInvoke((Action)(()=>SetStatus(t)));return;}status.Text=t;}
 async void Work(Action task) {
  if(busy)return;busy=true;foreach(var c in actions)c.Enabled=false;progress.Style=ProgressBarStyle.Marquee;
  try{await Task.Run(task);}catch(Exception e){Log("未完成："+e.Message);SetStatus("需要处理：请复制日志给 agent；可重新安装后再恢复原始备份。");}
  finally{busy=false;foreach(var c in actions)c.Enabled=true;restore.Enabled=archive!=null;progress.Style=ProgressBarStyle.Blocks;}
 }
 string TextOf(string p) {return (string)Invoke((Func<string>)(()=>p=="home"?home.Text:p=="exe"?exe.Text:backup.Text));}
 string Command(string args,int timeout=300) {Log("执行 Hermes："+Regex.Replace(args,@"\""[^\""]+\""","[路径]"));return Core.Run(TextOf("exe"),args,TextOf("home"),Log,timeout);}
 void Check() {
  archive=null;SetStatus("正在检查安装、备份及全部文件，请稍候…");
  var h=Path.GetFullPath(TextOf("home"));var ex=TextOf("exe");var b=TextOf("backup");
  if(!System.IO.File.Exists(ex) || !Path.GetFileName(ex).Equals("hermes.exe",StringComparison.OrdinalIgnoreCase)) throw new Exception("未找到 hermes.exe，请先安装 Hermes，再选择正确安装目录。");
  if(h==Path.GetPathRoot(h) || h.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),StringComparison.OrdinalIgnoreCase)) throw new Exception("用户数据位置不能是磁盘根目录或整个用户目录。");
  using(var z=ZipFile.OpenRead(b)) {Func<string,string> read=n=>{var e=z.GetEntry(n)??z.GetEntry("hermes/"+n)??z.GetEntry(".hermes/"+n);if(e==null||e.Length>4*1024*1024)return null;using(var r=new StreamReader(e.Open()))return r.ReadToEnd();};LoadSecrets(read(".env"),read("auth.json"));}
  if(Directory.Exists(h)) LoadSecrets(System.IO.File.Exists(Path.Combine(h,".env"))?System.IO.File.ReadAllText(Path.Combine(h,".env")):null,System.IO.File.Exists(Path.Combine(h,"auth.json"))?System.IO.File.ReadAllText(Path.Combine(h,"auth.json")):null);
  Command("--version",45);var result=Core.Inspect(b,Log);Core.EnsureNoLinks(h,result);
  var drive=new DriveInfo(Path.GetPathRoot(h));var temporaryDrive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(tempDir)));
  long targetNeeded=result.Bytes+512L*1024*1024,temporaryNeeded=result.Bytes+128L*1024*1024;
  if(drive.Name.Equals(temporaryDrive.Name,StringComparison.OrdinalIgnoreCase)){if(drive.AvailableFreeSpace<targetNeeded+temporaryNeeded)throw new Exception("可用空间不足，需容纳恢复数据和临时导入副本。");}
  else {if(drive.AvailableFreeSpace<targetNeeded)throw new Exception("资料盘可用空间不足。");if(temporaryDrive.AvailableFreeSpace<temporaryNeeded)throw new Exception("临时目录所在磁盘空间不足。");}
  Log("此工具用于全新安装后的恢复。临时导入副本在操作结束后自动删除。");Log("恢复不会更新程序、自动执行补丁或启动网关按钮；官方 import 自身可能自动启动网关，请恢复后核查任务。");
  Log("备份日期："+System.IO.File.GetLastWriteTime(b).ToString("yyyy-MM-dd HH:mm:ss")+"；压缩大小："+(new FileInfo(b).Length/1024.0/1024).ToString("F1")+" MB；展开："+(result.Bytes/1024.0/1024).ToString("F1")+" MB。");
  archive=result;SetStatus("检查通过："+result.Count+" 个条目。可以开始恢复；工具将自动关闭网关及 Hermes 后台进程。" );
 }
 void StartRestore() {
  if(archive==null)return;
  var a=archive;
  if(MessageBox.Show("将用选中的备份覆盖目标目录中的用户数据。备份后的新对话不会合并。\n\n目标："+home.Text+"\n备份："+Path.GetFileName(a.File)+"\n\n工具将停止网关并关闭本机 Hermes 桌面与后台进程，然后调用官方导入。请先保存未完成的操作。官方导入可能重新启动机器人和定时任务。\n\n请确认这是全新安装的资料目录。确认开始？","确认数据恢复",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
  Work(()=>Restore(a));
 }
 void Restore(ArchiveInfo a) {
  var h=TextOf("home"); if(Core.HashFile(a.File)!=a.Hash)throw new Exception("备份已改变，请重新检查。");
  Core.EnsureNoLinks(h,a);SetStatus("正在停止网关并关闭 Hermes 后台进程…");
  try{Command("gateway stop",90);}catch(Exception e){Log("网关停止命令未完成，将检查并关闭 Hermes 后台进程："+e.Message);}
  Core.StopBackground(h,TextOf("exe"),Log);
  Directory.CreateDirectory(tempDir);
  var filtered=Path.Combine(tempDir,"import-"+Guid.NewGuid().ToString("N")+".zip");
  try {
  Core.Filter(a,filtered);
  // Use official importer for SQLite semantics and runtime-state handling, never raw unzip.
  SetStatus("正在通过官方导入恢复用户数据…");Command("import "+Core.Quote(filtered)+" --force",900);
  // Import may auto-start gateway. Stop it before comparing files / reading DBs.
  Command("gateway stop",90);Core.Verify(a,h,Log);
  Log("恢复文件核对完成。先点本地配置体检，再按需联网检查及启动网关。");
  Log("local-overlays 已恢复为资料；没有自动执行补丁。OAuth 可能过期，旧盘符/用户名和外部工作区需确认。");
  SetStatus("数据恢复并完成文件核对。还需体检、API 连接与网关检查。");
  } finally {
   try {if(System.IO.File.Exists(filtered))System.IO.File.Delete(filtered);Log("临时导入副本已清理。");}
   catch(Exception e){Log("临时导入副本未能删除："+filtered+"；"+e.Message+"。下次启动会清理超过 24 小时的遗留文件。");}
  }
 }
 void Diagnostics() {
  SetStatus("正在检查配置及安装健康…");bool fail=false;
  foreach(var cmd in new[]{"doctor","status --all","gateway status"})try{Command(cmd,180);}catch(Exception e){fail=true;Log(e.Message);}
  string h=TextOf("home"),py=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(TextOf("exe"))),"hermes-agent","venv","Scripts","python.exe");
  if(System.IO.File.Exists(py)) {
   string script="import os,sqlite3,pathlib,yaml; h=pathlib.Path(os.environ['HERMES_HOME']); c=yaml.safe_load((h/'config.yaml').read_text(encoding='utf-8')) or {}; m=c.get('model',{}); print('MODEL_PROVIDER='+str(m.get('provider','unset'))); print('MODEL_DEFAULT='+str(m.get('default','unset'))); print('API_BASE_URL='+str(m.get('base_url','default'))); env=(h/'.env').read_text(encoding='utf-8') if (h/'.env').exists() else ''; print('ENV_VARIABLE_NAMES='+','.join(l.split('=',1)[0].strip() for l in env.splitlines() if '=' in l and not l.lstrip().startswith('#'))); assert (h/'state.db').is_file(), 'state.db missing'; dbs=list(h.glob('*.db'))+list(h.glob('profiles/*/*.db'))+list(h.glob('chromadb/*.sqlite3')); results=[];\nfor p in dbs:\n con=sqlite3.connect(p.resolve().as_uri()+'?mode=ro',uri=True); r=con.execute('PRAGMA quick_check').fetchall(); con.close(); ok=r==[('ok',)]; results.append(ok); print('DB_CHECK '+p.name+': '+('ok' if ok else 'FAILED'))\nprint('LOCAL_OVERLAYS='+str((h/'local-overlays').exists())); print('CHROMADB_DATA='+str((h/'chromadb').exists())); assert all(results), 'Database integrity check failed'";
   try{Core.Run(py,"-c "+Core.Quote(script),h,Log,180);}catch(Exception e){fail=true;Log("配置/数据库检查未通过："+e.Message);}
   try{Core.Run(py,"-c "+Core.Quote("import chromadb; print('CHROMADB_RUNTIME_IMPORT=ok; retrieval still requires agent verification')"),h,Log,60);}catch(Exception e){Log("ChromaDB 运行库未确认可用，请将日志交给 agent。"+e.Message);fail=true;}
  } else {fail=true;Log("未检测到标准 Python 环境，已跳过 YAML、数据库和 ChromaDB 检查；此项不能认定为通过。");}
  Log("本地体检不能证明 API 密钥有效。联网检查查询账户状态；模型可调用性还需在 Hermes 中发送一条测试消息。OAuth/飞书/微信需实际登录与收发验证。");
  SetStatus(fail?"体检有未通过或未确认项目，请查看日志。":"本地体检完成。API、模型对话与平台收发还需验证。");
 }
 void SaveLog(){using(var d=new SaveFileDialog{Filter="文本日志|*.txt",FileName="Hermes恢复日志-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"}) if(d.ShowDialog()==DialogResult.OK)System.IO.File.WriteAllText(d.FileName,Clean(logs.Text),Encoding.UTF8);}
 void PasteLog(){var dialog=new Form{Text="粘贴错误日志",Size=new Size(700,450),StartPosition=FormStartPosition.CenterParent};var input=new TextBox{Multiline=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill};var b=new Button{Text="脱敏后加入日志",Dock=DockStyle.Bottom,Height=45};b.Click+=(s,e)=>{Log("用户补充日志：\n"+input.Text);dialog.Close();};dialog.Controls.Add(input);dialog.Controls.Add(b);dialog.ShowDialog(this);}
}
static class Program {
 [STAThread] static void Main(string[] args) {if(args.Length>1 && args[0]=="--inspect") {var a=Core.Inspect(args[1],Console.WriteLine);Console.WriteLine(a.Count+" / "+a.Inventory.Count);return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainWindow());}
}
}




