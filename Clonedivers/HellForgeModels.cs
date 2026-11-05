using System.Text.Json;

namespace Clonedivers;

public sealed record HellForgeStatus(string Root, string Version, string Build, bool GameCopyReady, bool RunnerReady,
    bool Running, bool Multiplayer, bool LocalBackend, string? LastStatus, string? LastFailure, string? LastNotice = null, string? RecoveryProblem = null, string? CompatibilityProblem = null);
public sealed record HellForgeOption(string Name, bool On);
public sealed record HellForgeMod(string Id, string Name, bool Enabled, string Folder, string[] Tags, string Version = "", HellForgeOption[]? Options = null);
public sealed record HellForgeMods(List<HellForgeMod> Mods, string[] Problems, string? Code, string? HostingProblem = null);
public sealed class HellForgeCommandOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner);
