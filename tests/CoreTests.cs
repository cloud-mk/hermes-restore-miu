using System;
using System.IO;
using System.IO.Compression;
using HermesRecovery;
class CoreTests {
 static int count;
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
 static void Main(){string root=Path.Combine(Path.GetTempPath(),"MiuCoreTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var backup=Path.Combine(root,"fixture.zip");using(var zip=ZipFile.Open(backup,ZipArchiveMode.Create))foreach(var name in new[]{"config.yaml","state.db",".env","memories/MEMORY.md","bin/hermes.exe","gateway.lock"})using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write("synthetic "+name);
  var archive=Core.Inspect(backup,Console.WriteLine);Check(archive.Count==6&&archive.Skipped==2,"program and runtime exclusions");var filtered=Path.Combine(root,"filtered.zip");Core.Filter(archive,filtered);Core.CheckCrc(filtered);Check(true,"filtered CRC validation");var target=Path.Combine(root,"target");ZipFile.ExtractToDirectory(filtered,target);Core.Verify(archive,target,Console.WriteLine);Check(true,"restored critical content verification");File.WriteAllText(Path.Combine(target,".env"),"different synthetic content");Reject(()=>Core.Verify(archive,target,Console.WriteLine),"credential mismatch detected");Reject(()=>Core.Normalize("../outside"),"traversal blocked");Reject(()=>Core.Normalize("C:/outside"),"absolute path blocked");Reject(()=>Core.Normalize("folder/NUL.txt"),"device path blocked");Check(Core.BackupTime("hermes-backup-20260101_120000.zip")==new DateTime(2026,1,1,12,0,0),"backup time parsing");Check(Core.BackupTime("unknown.zip")==null,"unknown timestamp not fabricated");
  var broken=Path.Combine(root,"broken.zip");File.Copy(backup,broken);var bytes=File.ReadAllBytes(broken);for(int i=0;i<bytes.Length-20;i++)if(BitConverter.ToUInt32(bytes,i)==0x02014b50){bytes[i+16]^=1;break;}File.WriteAllBytes(broken,bytes);Reject(()=>Core.CheckCrc(broken),"CRC tampering detected");
  // Delete only the known synthetic files, never recurse over an arbitrary path.
  foreach(var file in new[]{backup,filtered,broken})File.Delete(file);foreach(var file in new[]{"config.yaml","state.db",".env","memories/MEMORY.md"})File.Delete(Path.Combine(target,file));Directory.Delete(Path.Combine(target,"memories"));Directory.Delete(target);Directory.Delete(root);Console.WriteLine("TOTAL "+count);
 }
}
