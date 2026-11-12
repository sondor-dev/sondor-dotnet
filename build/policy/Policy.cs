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
    // "permissions" is the base resource; the others are sub-resources of it (…/actions/permissions/workflow).
    var path = name == "permissions" ? $"repos/{repo}/actions/permissions" : $"repos/{repo}/actions/permissions/{name}";
    await Sync(report, gh, $"actions: {name}", path, HttpMethod.Put, body!);
}

// Code scanning runs from .github/workflows/codeql.yml, so GitHub's "default setup" must be off (it blocks that workflow).
await Sync(report, gh, "code scanning default setup", $"repos/{repo}/code-scanning/default-setup", HttpMethod.Patch, Load("code-scanning.json"));

// Environments: the release workflow publishes from "nuget", and GitHub creates a missing environment with no protection at all.
foreach (var (environmentName, environmentPolicy) in Load("environments.json").AsObject())
{
    await SyncEnvironment(report, gh, repo, environmentName, environmentPolicy!);
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

// An environment is described by who must approve a deployment and which branches or tags may deploy to it. GitHub reports it
// in a different shape from the one it accepts, so it is compared by meaning rather than as a JSON subset. The deployment
// refs are managed completely: one that is not listed is removed, so a rule that no longer applies cannot linger.
static async Task SyncEnvironment(Report report, GitHub gh, string repo, string name, JsonNode policy)
{
    var label = $"environment '{name}'";
    var path = $"repos/{repo}/environments/{Uri.EscapeDataString(name)}";

    var reviewers = policy["reviewers"]!.AsArray().Select(node => node!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
    var preventSelfReview = policy["prevent_self_review"]!.GetValue<bool>();
    var canAdminsBypass = policy["can_admins_bypass"]!.GetValue<bool>();
    var wantedRefs = policy["deployment_refs"]!.AsArray()
        .Select(node => (Name: node!["name"]!.GetValue<string>(), Type: node["type"]!.GetValue<string>()))
        .ToArray();

    var have = await gh.Get(path);
    var differences = new List<string>();
    var missingRefs = new List<(string Name, string Type)>();
    var staleRefIds = new List<long>();

    if (have is null)
    {
        differences.Add("missing");
        missingRefs.AddRange(wantedRefs);
    }
    else
    {
        var rule = have["protection_rules"]?.AsArray()
            .FirstOrDefault(candidate => candidate?["type"]?.GetValue<string>() == "required_reviewers");
        var haveReviewers = rule?["reviewers"]?.AsArray()
            .Select(item => item?["reviewer"]?["login"]?.GetValue<string>())
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];

        if (!haveReviewers.SequenceEqual(reviewers))
        {
            differences.Add($"reviewers should be [{string.Join(", ", reviewers)}] but are [{string.Join(", ", haveReviewers)}]");
        }

        if ((rule?["prevent_self_review"]?.GetValue<bool>() ?? false) != preventSelfReview)
        {
            differences.Add($"prevent_self_review should be {preventSelfReview}");
        }

        if ((have["can_admins_bypass"]?.GetValue<bool>() ?? true) != canAdminsBypass)
        {
            differences.Add($"can_admins_bypass should be {canAdminsBypass}");
        }

        if (have["deployment_branch_policy"]?["custom_branch_policies"]?.GetValue<bool>() != true)
        {
            differences.Add("deployments should be limited to the listed branches and tags");
        }

        var policies = (await gh.Get($"{path}/deployment-branch-policies"))?["branch_policies"]?.AsArray() ?? [];
        foreach (var wanted in wantedRefs)
        {
            var found = policies.Any(item => item?["name"]?.GetValue<string>() == wanted.Name && item["type"]?.GetValue<string>() == wanted.Type);
            if (!found)
            {
                missingRefs.Add(wanted);
                differences.Add($"{wanted.Type} '{wanted.Name}' is not allowed to deploy");
            }
        }

        foreach (var item in policies)
        {
            var itemName = item?["name"]?.GetValue<string>();
            var itemType = item?["type"]?.GetValue<string>();
            if (itemName is not null && !wantedRefs.Any(wanted => wanted.Name == itemName && wanted.Type == itemType))
            {
                staleRefIds.Add(item!["id"]!.GetValue<long>());
                differences.Add($"{itemType} '{itemName}' may deploy but is not in the policy");
            }
        }
    }

    if (differences.Count == 0)
    {
        Report.Matches(label);
        return;
    }

    report.Differs(label, string.Join("; ", differences));
    if (!report.ApplyMode)
    {
        return;
    }

    var reviewerEntries = new JsonArray();
    foreach (var login in reviewers)
    {
        var user = await gh.Get($"users/{Uri.EscapeDataString(login)}") ?? throw new InvalidOperationException($"GitHub user '{login}' not found.");
        reviewerEntries.Add(new JsonObject { ["type"] = "User", ["id"] = user["id"]!.GetValue<long>() });
    }

    await gh.Send(HttpMethod.Put, path, new JsonObject
    {
        ["wait_timer"] = 0,
        ["prevent_self_review"] = preventSelfReview,
        ["can_admins_bypass"] = canAdminsBypass,
        ["reviewers"] = reviewerEntries,
        ["deployment_branch_policy"] = new JsonObject { ["protected_branches"] = false, ["custom_branch_policies"] = true }
    });

    // Add the wanted refs before removing the others, so there is never a moment when nothing may deploy.
    foreach (var (refName, refType) in missingRefs)
    {
        await gh.Send(HttpMethod.Post, $"{path}/deployment-branch-policies", new JsonObject { ["name"] = refName, ["type"] = refType });
    }

    foreach (var id in staleRefIds)
    {
        await gh.Send(HttpMethod.Delete, $"{path}/deployment-branch-policies/{id.ToString(CultureInfo.InvariantCulture)}", null);
    }

    Report.Applied(label);
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
            // gh puts the error body on stdout and a one-line summary on stderr; either can carry the status.
            var text = result.Output + result.Error;
            string[] markers = ["HTTP 404", "HTTP 409", "\"status\":\"404\"", "\"status\":\"409\""];
            if (markers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
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
