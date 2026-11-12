# Releasing a package

Each package is versioned and released on its own, from the Actions tab. The workflow works out the version, publishes, and
only then creates the tag, so **a release tag always means the package is on nuget.org**.

## Versions

A package's version is its last stable release tag plus a bump. Tags are `<prefix>-v<version>`, where the prefix is the
package's `MinVerTagPrefix`:

```
results-v0.1.0
cqrs-v0.3.1
```

A build that is not a release (a pull request, a local build) is versioned by [MinVer](https://github.com/adamralph/minver)
from the nearest tag, as a pre-release such as `0.1.1-preview.0.4`.

## Releasing

1. Make sure `main` is green and holds what you want to release.
2. For a **stable** release, move the package's `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt` in a pull
   request, and merge it. The workflow refuses a stable release while anything is left in the unshipped file.
3. Open the repository's **Actions** tab → **Release** → **Run workflow**, from `main`, and fill in:

   | Input | What to enter |
   |---|---|
   | Package | The package ID, for example `Sondor.Results` |
   | Bump | `patch`, `minor` or `major`, applied to the last stable release |
   | Version | Only for a package's **first** stable release, or to jump: an exact `major.minor.patch`. It wins over the bump. |
   | Pre-release | Empty for a stable release, or a label such as `preview.1` or `rc.1` |

4. The workflow:
   - **plans** the release: works out the version and the tag, and stops with the reason if the request cannot be released
     (an unknown package, a malformed version, a tag that already exists, a first release without a version);
   - **builds and tests** everything, then packs that one package at that version and checks the file is what was planned;
   - **waits for your approval** in the `nuget` environment (a yellow *Review deployments* banner on the run), then publishes
     to nuget.org;
   - **creates the tag and the GitHub release** at the commit that was built, with notes covering the changes since the
     package's previous release.

If the publish fails, nothing is tagged and nothing is half-released: fix the cause and run the workflow again with the same
inputs. If it fails after the package was published (the tag step), run it again: the push skips a version that is already on
nuget.org and the workflow carries on to the tag.

### Examples

| Goal | Bump | Version | Pre-release | Result (from last stable 0.1.4) |
|---|---|---|---|---|
| First stable release (none yet) | `patch` | `0.1.0` | | 0.1.0 |
| A fix | `patch` | | | 0.1.5 |
| A new feature | `minor` | | | 0.2.0 |
| Promote to 1.0 | `major` | | | 1.0.0 |
| Try the next minor first | `minor` | | `preview.1` | 0.2.0-preview.1 |

Releases below 1.0 may still change their API; each package stays at 0.x until it has been used for real.

## Version rules

- A breaking change to a public API is a new **major** version. `EnablePackageValidation` fails the pack otherwise.
- A new public API is a **minor** version.
- A fix is a **patch** version.

A release tag cannot be moved or deleted once created, so a bad release is fixed by releasing the next version, and the bad
one is unlisted on nuget.org.

## One-time setup

Done once per repository, not per package.

1. **The `nuget` environment and the tag rules.** They are described in `build/policy` and applied with:

   ```bash
   dotnet run build/policy/Policy.cs -- --apply
   ```

   Do this before the first release: GitHub creates a missing environment with no protection at all.
2. **Reserve the package ID prefix on nuget.org.** Email account@nuget.org with your owner display name and the prefix
   `Sondor.`; see the
   [prefix reservation documentation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation). Reserved packages get
   a verified badge and nobody else can publish under the prefix. Publishing works while the request is pending.
3. **A trusted-publishing policy on nuget.org** (*Account → Trusted Publishing*):
   - repository owner `sondor-dev` and repository `sondor-dotnet`;
   - workflow file `release.yml`;
   - environment `nuget`;
   - the push permission only, and the package pattern `Sondor.*`. Unlisting is deliberately not allowed from CI.

   No API key is created or stored. The workflow exchanges its own identity for a key that lasts a short time. The policy
   belongs to the nuget.org account that owns the packages, and the login is only accepted from a job in `release.yml`.
4. **A repository variable** named `NUGET_USER` holding the nuget.org *profile name* (not the email). It is a variable, not
   a secret: the name is public. If the profile is renamed, update it.

   ```bash
   gh variable set NUGET_USER --repo sondor-dev/sondor-dotnet
   ```

The first release of a brand-new package ID works with the temporary key: `Sondor.Results` 0.1.0-preview.1 was published
that way.
