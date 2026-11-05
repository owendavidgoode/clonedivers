namespace Clonedivers;

internal static class ForgeEntryPolicy
{
    internal static bool ForgeEdition {
        get {
#if FORGE_DEFAULT
            return true;
#else
            return false;
#endif
        }
    }
    internal static string[] Normalize(string[] args, bool forgeEdition)
    {
        var steam = args.Contains("--steam", StringComparer.Ordinal);
        var remaining = args.Where(a => a != "--steam" && (!steam || a != "--forge")).ToArray();
        if (!forgeEdition || steam || remaining.Contains("--forge") || remaining.Contains("--forge-preview")) return remaining;
        return ["--forge", .. remaining];
    }
}
