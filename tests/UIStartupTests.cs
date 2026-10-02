using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using HermesRecovery;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class StartupWindow : MainWindow {
 public int VisibleSizeChanges;public Rectangle FirstVisibleBounds;public bool Track;
 static IEnumerable<Control> All(Control c){yield return c;foreach(Control child in c.Controls)foreach(var n in All(child))yield return n;}
 public StartupWindow(){foreach(var c in All(this))c.SizeChanged+=(s,e)=>{if(Track)VisibleSizeChanges++;};}
 protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible&&!Track){FirstVisibleBounds=Bounds;Track=true;}}
}
class UIStartupTests {
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
 [STAThread]static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  using(var f=new StartupWindow()){
   f.Show();Application.DoEvents();Application.DoEvents();
   Require(f.VisibleSizeChanges==0,"startup layout settled before first visibility");
   Require(f.FirstVisibleBounds==f.Bounds,"startup window bounds remain stable");
   f.Track=false;var shell=(TableLayoutPanel)f.Controls[0];
   Require(shell is BufferedTable,"root layout uses buffered drawing");
   foreach(bool wide in new[]{false,true,false,true}){
    var d=f.DeviceDpi/96f;f.ClientSize=new Size((int)((wide?940:760)*d),(int)(600*d));Application.DoEvents();Application.DoEvents();
    Require(shell.GetControlFromPosition(0,0).Visible==wide,"sidebar visibility follows width");
    Require(shell.GetColumnWidths()[0]==(wide?(int)(164*d):0),"hidden sidebar leaves no blank column");
    var viewport=shell.GetControlFromPosition(1,0);Require(viewport.Left==shell.GetColumnWidths()[0],"viewport follows settled columns");
   }
   f.Close();
  }
 }
}
