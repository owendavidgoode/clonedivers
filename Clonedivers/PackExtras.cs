namespace Clonedivers;

public static class PackExtras
{
    public static bool AlwaysOn(PackManifest pack, string id) => pack.CombinedRoster &&
        (id.Equals("commandos", StringComparison.OrdinalIgnoreCase) || id.Equals("aimpoints", StringComparison.OrdinalIgnoreCase));
    public static IEnumerable<PackOption> Visible(PackManifest? pack) =>
        pack?.Options.Where(o => !AlwaysOn(pack, o.Id) && !o.Id.Equals("commandos", StringComparison.OrdinalIgnoreCase) &&
            !o.Id.Equals("skinny", StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<PackOption>();
}
