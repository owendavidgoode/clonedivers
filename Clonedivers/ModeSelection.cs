namespace Clonedivers;

public enum LauncherMode { Helldivers, Clonedivers, CommandoDivers, EmpireDivers }

public static class LauncherModes
{
    public const string CommandoOption = "commandos";
    public const string EmpireOption = "empire";
    public static bool SupportsEmpire(PackManifest? pack) => pack?.Options.Any(o => o.Id.Equals(EmpireOption, StringComparison.OrdinalIgnoreCase)) == true;
    public static string Name(LauncherMode mode) => mode switch
    {
        LauncherMode.Helldivers => "Helldivers",
        LauncherMode.Clonedivers => "Clonedivers",
        LauncherMode.EmpireDivers => "EmpireDivers",
        _ => "Commandodivers",
    };

    public static LauncherMode? Current(ModState state, Settings settings, PackManifest? pack) => state switch
    {
        ModState.Off or ModState.NoModFiles => LauncherMode.Helldivers,
        ModState.On when SupportsEmpire(pack) && settings.Options.GetValueOrDefault(EmpireOption) => LauncherMode.EmpireDivers,
        ModState.On when pack?.CombinedRoster == true => LauncherMode.Clonedivers,
        ModState.On => (settings.Options.TryGetValue(CommandoOption, out var on) ? on :
            pack?.Options.FirstOrDefault(o => o.Id.Equals(CommandoOption, StringComparison.OrdinalIgnoreCase))?.Default ?? false)
            ? LauncherMode.CommandoDivers : LauncherMode.Clonedivers,
        _ => null,
    };

    public static Dictionary<string, bool> OptionsFor(Settings settings, PackManifest pack, LauncherMode mode)
    {
        if (mode == LauncherMode.Helldivers) throw new ArgumentException("Vanilla mode parks the pack; it does not change pack options.", nameof(mode));
        if (mode == LauncherMode.EmpireDivers && !SupportsEmpire(pack))
            throw new InvalidOperationException("This pack does not include EmpireDivers yet.");
        if (mode == LauncherMode.CommandoDivers && !pack.Options.Any(o => o.Id.Equals(CommandoOption, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This pack does not include Commandodivers yet.");
        var options = pack.CombinedRoster ? settings.OptionsFor(pack) :
            settings.OptionsFor(pack, CommandoOption, mode == LauncherMode.CommandoDivers);
        if (SupportsEmpire(pack)) options[EmpireOption] = mode == LauncherMode.EmpireDivers;
        return options;
    }
}

