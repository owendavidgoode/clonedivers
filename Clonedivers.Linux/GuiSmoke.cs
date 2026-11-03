using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace Clonedivers;

internal static class GuiSmoke
{
    internal static async Task RunAsync(LauncherWindow window)
    {
        var checks = new List<string>(); var code = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("GUI: " + name); checks.Add(name); }
        async Task Click(Button button)
        {
            Check(button.IsEnabled, "button enabled: " + button.Content?.ToString());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.Operation;
        }
        try
        {
            var game = window.GameDirectory ?? throw new InvalidOperationException("GUI smoke requires a synthetic game.");
            var exe = Path.Combine(game, "bin", "helldivers2.exe");
            if (!File.Exists(Path.Combine(game, ".clonedivers-ui-fixture")) || new FileInfo(exe).Length != 7 || File.ReadAllText(exe) != "fixture")
                throw new InvalidOperationException("GUI smoke only accepts an explicitly marked synthetic fixture, never a real game.");
            Check(window.IsVisible && window.Modes.Count == 3, "native desktop window and three universe buttons are visible");
            await window.UpdateOperation;
            Check(window.ViewUpdateButton.IsVisible && window.UpdateNotice.Text!.Contains("99.0.0"), "desktop shows a cached newer Linux launcher release");
            Check(window.CheckUpdateButton.IsEnabled, "manual launcher update check stays available");
            await Click(window.ViewUpdateButton);
            await Click(window.Modes[LauncherMode.Clonedivers]); await Click(window.ApplyButton);
            Check(ModFiles.GetState(game) == ModState.On, "Clonedivers button installs and enables pack");
            await Click(window.Modes[LauncherMode.EmpireDivers]); await Click(window.ApplyButton);
            var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(game), game)!;
            Check(receipt.Options.GetValueOrDefault("empire"), "Empire button installs Imperial selection");
            await Click(window.Modes[LauncherMode.Helldivers]); await Click(window.ApplyButton);
            Check(ModFiles.GetState(game) == ModState.Off, "vanilla button parks the pack");
            await Click(window.RepairButton);
            Check(ModFiles.GetState(game) == ModState.Off, "GUI repair preserves vanilla mode");
            await Click(window.Modes[LauncherMode.Clonedivers]); await Click(window.LaunchButton);
            Check(ModFiles.GetState(game) == ModState.On, "GUI launch applies selected mode before Steam handoff");
            foreach (var file in ModFiles.ListPatchFiles(Path.Combine(game, "data"))) File.Delete(file);
            await Click(window.RepairButton);
            Check(ModFiles.GetState(game) == ModState.On, "GUI repair restores a completely missing active pack");
            Check(File.ReadAllText(Path.Combine(game, "data", "base-archive")) == "untouched", "GUI operations preserve base game files");
            var shortcut = DesktopShortcut.Install();
            Check(File.ReadAllText(shortcut).Contains("Terminal=false") && File.ReadAllText(shortcut).Contains("--gui"), "Applications shortcut opens the GUI without a terminal");
            await Task.Delay(250);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(window.SmokeOutput!);
            Console.WriteLine($"ALL {checks.Count} GUI CHECKS PASSED");
        }
        catch (Exception ex) { code = 1; checks.Add("FAILED: " + ex); Console.Error.WriteLine(ex); }
        File.WriteAllText(window.SmokeOutput! + ".json", JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(code);
    }
}
