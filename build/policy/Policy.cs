// Compares this repository's GitHub settings with the desired state in this folder, and optionally applies it.
//
//   dotnet run build/policy/Policy.cs                  check: read-only, exits 1 when GitHub differs from the files
//   dotnet run build/policy/Policy.cs -- --apply       make GitHub match the files
//   dotnet run build/policy/Policy.cs -- --repo owner/name
//
// Needs the GitHub CLI (`gh`), signed in as a repository admin. It never deletes anything: a ruleset that is not described
// here is reported and left alone. Secrets never belong in these files.

// A file-based app defaults to Native AOT, which pulls in a compiler package that this repository's audit rejects. It is a
// script, not something we publish.
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var apply = args.Contains("--apply");
var repo = ArgumentAfter("--repo") ?? RepositoryFromOrigin();
var policyDirectory = Path.GetDirectoryName(GetSourceFile())!;

if (repo is null)
{
    Console.Error.WriteLine("Could not work out the repository. Pass --repo owner/name.");
    return 2;
}

var gh = new GitHub(Environment.GetEnvironmentVariable("GH_COMMAND") ?? "gh",
    Environment.GetEnvironmentVariable("GH_COMMAND_PREFIX"));
var report = new Report(apply);

Console.WriteLine($"{(apply ? "Applying" : "Checking")} policy for {repo}");

// Repository settings and the security features that are part of the same resource.
await Sync(report, gh, "repository settings", $"repos/{repo}", HttpMethod.Patch, Load("repository.json"));

// Features with an endpoint of their own.
await SyncFeature(report, gh, "private vulnerability reporting", $"repos/{repo}/private-vulnerability-reporting",
    presentWhenOk: false, expectEnabledProperty: true);
await SyncFeature(report, gh, "Dependabot alerts", $"repos/{repo}/vulnerability-alerts",
    presentWhenOk: true, expectEnabledProperty: false);

// GitHub Actions.
var actions = Load("actions.json");
foreach (var (name, body) in actions.AsObject())
{
    await Sync(report, gh, $"actions: {name}", $"repos/{repo}/actions/{name}", HttpMethod.Put, body!);
}

// Rulesets.
var existing = (await gh.Get($"repos/{repo}/rulesets"))?.AsArray() ?? [];
var managed = new HashSet<string>(StringComparer.Ordinal);
foreach (var file in Directory.GetFiles(Path.Combine(policyDirectory, "rulesets"), "*.json").Order(StringComparer.Ordinal))
{
    var want = JsonNode.Parse(File.ReadAllText(file))!;
    var name = want["name"]!.GetValue<string>();
    managed.Add(name);

    var match = existing.FirstOrDefault(item => item!["name"]!.GetValue<string>() == name);
    if (match is null)
    {
        report.Differs($"ruleset '{name}'", "missing");
        if (apply)
        {
            await gh.Send(HttpMethod.Post, $"repos/{repo}/rulesets", want);
            Report.Applied($"ruleset '{name}'");
        }

        continue;
    }

    var id = match["id"]!.GetValue<long>();
    var have = (await gh.Get($"repos/{repo}/rulesets/{id.ToString(CultureInfo.InvariantCulture)}"))!;
    var differences = Json.Differences(want, have, "$");

    if (differences.Count == 0)
    {
        Report.Matches($"ruleset '{name}'");
        continue;
    }

    report.Differs($"ruleset '{name}'", string.Join("; ", differences));
    if (apply)
    {
        await gh.Send(HttpMethod.Put, $"repos/{repo}/rulesets/{id.ToString(CultureInfo.InvariantCulture)}", want);
        Report.Applied($"ruleset '{name}'");
    }
}

foreach (var item in existing)
{
    var name = item!["name"]!.GetValue<string>();
    if (!managed.Contains(name))
    {
        Report.Unmanaged($"ruleset '{name}' is not described in build/policy/rulesets and was left alone");
    }
}

return report.Finish();

string? ArgumentAfter(string flag)
{
    var index = Array.IndexOf(args, flag);

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

JsonNode Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(policyDirectory, file)))!;

static string GetSourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

static string? RepositoryFromOrigin()
{
    var url = Shell.Run("git", "remote get-url origin").Output.Trim();
    var marker = "github.com";
    var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
        return null;
    }

    var path = url[(index + marker.Length)..].TrimStart('/', ':');

    return path.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
}

static async Task Sync(Report report, GitHub gh, string label, string path, HttpMethod method, JsonNode want)
{
    var have = await gh.Get(path);
    var differences = Json.Differences(want, have, "$");

    if (differences.Count == 0)
    {
        Report.Matches(label);
        return;
    }

    report.Differs(label, string.Join("; ", differences));
    if (report.ApplyMode)
    {
        await gh.Send(method, path, want);
        Report.Applied(label);
    }
}

// Features switched on and off by their own endpoint. Reading returns either {"enabled": bool} or 204/404.
static async Task SyncFeature(Report report, GitHub gh, string label, string path, bool presentWhenOk, bool expectEnabledProperty)
{
    var have = await gh.Get(path);
    var enabled = expectEnabledProperty
        ? have?["enabled"]?.GetValue<bool>() == true
        : presentWhenOk && have is not null;

    if (enabled)
    {
        Report.Matches(label);
        return;
    }

    report.Differs(label, "disabled");
    if (report.ApplyMode)
    {
        await gh.Send(HttpMethod.Put, path, null);
        Report.Applied(label);
    }
}

/// <summary>Reads and writes through the GitHub CLI, which holds the sign-in.</summary>
internal sealed class GitHub(string command, string? prefix)
{
    /// <summary>Returns the JSON at <paramref name="path"/>, or null for an empty answer or a 404 (a feature that is off).</summary>
    public async Task<JsonNode?> Get(string path)
    {
        var result = await Run(["api", path], null);
        if (result.ExitCode != 0)
        {
            // 404: the feature is off. 409: the setting does not apply in the current mode (selected-actions is
            // unavailable until allowed actions is "selected"). Either way the wanted state is not in place.
            if (result.Error.Contains("HTTP 404", StringComparison.Ordinal) || result.Error.Contains("HTTP 409", StringComparison.Ordinal))
            {
                return null;
            }

            throw new InvalidOperationException($"GET {path} failed: {result.Error.Trim()}");
        }

        return string.IsNullOrWhiteSpace(result.Output) ? JsonNode.Parse("{}") : JsonNode.Parse(result.Output);
    }

    public async Task Send(HttpMethod method, string path, JsonNode? body)
    {
        var result = await Run(["api", "--method", method.Method, path, "--input", "-"], body?.ToJsonString() ?? "{}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{method.Method} {path} failed: {result.Error.Trim()}");
        }
    }

    private Task<ProcessResult> Run(string[] arguments, string? input)
    {
        var all = (prefix is null ? [] : prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Concat(arguments);

        return Shell.RunAsync(command, all, input);
    }
}

internal readonly record struct ProcessResult(int ExitCode, string Output, string Error);

internal static class Shell
{
    public static ProcessResult Run(string command, string arguments) =>
        RunAsync(command, arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), null).GetAwaiter().GetResult();

    public static async Task<ProcessResult> RunAsync(string command, IEnumerable<string> arguments, string? input)
    {
        var info = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{command}'. Is it installed?");

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await output, await error);
    }
}

internal static class Json
{
    /// <summary>
    /// What is in <paramref name="want"/> but not in <paramref name="have"/>. Objects match when every wanted property
    /// matches (GitHub adds ids and timestamps of its own); arrays match when they have the same length and every wanted
    /// element matches some element, whatever the order.
    /// </summary>
    public static List<string> Differences(JsonNode? want, JsonNode? have, string path)
    {
        var differences = new List<string>();
        Compare(want, have, path, differences);

        return differences;
    }

    private static void Compare(JsonNode? want, JsonNode? have, string path, List<string> differences)
    {
        switch (want)
        {
            case JsonObject wantObject:
                if (have is not JsonObject haveObject)
                {
                    differences.Add($"{path} is missing");
                    return;
                }

                foreach (var (key, value) in wantObject)
                {
                    Compare(value, haveObject[key], $"{path}.{key}", differences);
                }

                break;

            case JsonArray wantArray:
                if (have is not JsonArray haveArray || haveArray.Count != wantArray.Count)
                {
                    differences.Add($"{path} should have {wantArray.Count} item(s)");
                    return;
                }

                for (var index = 0; index < wantArray.Count; index++)
                {
                    var element = wantArray[index];
                    if (!haveArray.Any(candidate => Differences(element, candidate, path).Count == 0))
                    {
                        differences.Add($"{path}[{index}] not found");
                    }
                }

                break;

            default:
                if (have is null || want is null || want.ToJsonString() != have.ToJsonString())
                {
                    differences.Add($"{path} should be {want?.ToJsonString() ?? "null"} but is {have?.ToJsonString() ?? "missing"}");
                }

                break;
        }
    }
}

internal sealed class Report(bool applyMode)
{
    private int _differences;

    public bool ApplyMode { get; } = applyMode;

    public static void Matches(string label) => Console.WriteLine($"  ok       {label}");

    public void Differs(string label, string detail)
    {
        _differences++;
        Console.WriteLine($"  DIFFERS  {label}: {detail}");
    }

    public static void Applied(string label) => Console.WriteLine($"  applied  {label}");

    public static void Unmanaged(string message) => Console.WriteLine($"  note     {message}");

    public int Finish()
    {
        if (ApplyMode)
        {
            Console.WriteLine(_differences == 0 ? "Nothing to change." : $"Applied {_differences} change(s). Run the check again to confirm.");

            return 0;
        }

        Console.WriteLine(_differences == 0 ? "GitHub matches the policy." : $"{_differences} difference(s). Run with --apply to fix them.");

        return _differences == 0 ? 0 : 1;
    }
}
