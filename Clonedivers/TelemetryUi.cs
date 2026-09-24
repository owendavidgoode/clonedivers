using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using static Clonedivers.Palette;

namespace Clonedivers;

public sealed partial class MainForm
{
    void ShowPerformanceSettings(Form owner)
    {
        if(preview)return;
        using var dialog=new Form {Text="Performance sharing",StartPosition=FormStartPosition.CenterParent,Size=new(630,510),MinimumSize=new(530,480),BackColor=Bg,ForeColor=TextMain,Font=new("Segoe UI",10)};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(18)};
        var explanation=new Label{AutoSize=true,MaximumSize=new(560,0),Text="Share detailed HD2 performance with Owen: hardware, graphics/mod settings, frame timings, resource use and game-related crash events. Reports upload automatically while this launcher stays open. No screenshots, chat, account details or memory dumps."};
        var enabled=new CheckBox{AutoSize=true,Text="Share performance with Owen",Checked=settings.Telemetry.Enabled};
        var nickname=new TextBox{Width=540,MaxLength=40,Text=settings.Telemetry.Nickname,PlaceholderText="Your squad nickname"};
        var endpoint=new TextBox{Width=540,Text=settings.Telemetry.Endpoint,PlaceholderText="Collector address supplied by Owen (https://…)"};
        var invitation=new TextBox{Width=540,UseSystemPasswordChar=true,PlaceholderText="Squad invitation code (first connection only)"};
        var state=new Label{AutoSize=true,MaximumSize=new(550,0),Text=telemetryRecorder is {} recorder ? recorder.Status+". "+recorder.UploadStatus : "Sharing is off. FPS recording needs Windows tracing permission."};
        var save=new Button{AutoSize=true,Text="Save"};var permissions=new Button{AutoSize=true,Text="Set up FPS permission…"};
        foreach(var control in new Control[]{explanation,enabled,nickname,endpoint,invitation,permissions,state,save}){control.Margin=new(0,0,0,12);layout.Controls.Add(control);}
        permissions.Click+=async(_,_)=>{
            permissions.Enabled=false;
            try {
                var sid=WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException();
                if(!System.Text.RegularExpressions.Regex.IsMatch(sid,@"^S-1-[0-9-]+$"))throw new InvalidOperationException();
                var script="$ErrorActionPreference='Stop'; $groupSid='S-1-5-32-559'; $memberSid='"+sid+"'; if (-not (Get-LocalGroupMember -SID $groupSid | Where-Object {$_.SID.Value -eq $memberSid})) { Add-LocalGroupMember -SID $groupSid -Member $memberSid }";
                var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))};
                using var p=Process.Start(info);if(p is null)throw new InvalidOperationException();await p.WaitForExitAsync();
                state.Text=p.ExitCode==0?"Permission configured. Sign out of Windows and back in once before recording FPS.":"Windows could not configure tracing. Sharing can still collect resource samples.";
            }catch{state.Text="FPS permission wasn't changed. You can try again later.";}
            finally{permissions.Enabled=true;}
        };
        save.Click+=async(_,_)=>{
            save.Enabled=false;
            try {
                if(!enabled.Checked) {
                    settings.Telemetry.Enabled=false;telemetryRecorder?.Dispose();telemetryRecorder=null;settings.Save();dialog.Close();return;
                }
                var url=endpoint.Text.Trim().TrimEnd('/')+"/";
                if(string.IsNullOrWhiteSpace(nickname.Text))throw new InvalidDataException("Enter your squad nickname.");
                var next=settings.Telemetry;
                if(next.DeviceId.Length==0 || next.Endpoint!=url || !string.IsNullOrWhiteSpace(invitation.Text))next=await TelemetryStore.Enroll(url,nickname.Text,invitation.Text);
                else {next.Nickname=TelemetryStore.SafeText(nickname.Text,40);next.Enabled=true;}
                if(!next.Ready)throw new InvalidDataException("Collector connection is incomplete.");
                telemetryRecorder?.Dispose();settings.Telemetry=next;settings.Save();telemetryRecorder=new(next,SessionContext());dialog.Close();
            }catch(Exception e){state.Text=e is InvalidDataException?e.Message:"Couldn't connect. Check the collector address and squad invitation code.";}
            finally{save.Enabled=true;}
        };
        dialog.Controls.Add(layout);dialog.ShowDialog(owner);
    }
}
