# Releasing a package

Each package is versioned and released on its own, by pushing a tag.

## Versions

[MinVer](https://github.com/adamralph/minver) takes a package's version from the nearest tag with its prefix. A package sets
its prefix in its project file (`<MinVerTagPrefix>results-v</MinVerTagPrefix>`), so these tags are independent:

```
results-v1.0.0
cqrs-v0.3.1
```

A commit after the last tag builds as a pre-release (`1.0.1-preview.0.4`), which is what a local build and a pull request
produce.

## Releasing

1. Make sure `main` is green and holds what you want to release.
2. Move the package's `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`, in a pull request, and merge it.
3. Tag the merged commit and push the tag:

   ```bash
   git tag results-v1.0.0
   git push origin results-v1.0.0
   ```

4. The [Release workflow](../.github/workflows/release.yml) runs. It:
   - checks the tag is a release tag, names exactly one package, and points at a commit on `main`;
   - builds and tests everything, then packs the one package, and checks the file is exactly `<package>.<version>.nupkg`;
   - **waits for your approval** in the `nuget` environment, then publishes to nuget.org;
   - creates the GitHub release, with notes covering the changes since the previous release of that package.

A tag with a suffix (`results-v1.0.0-preview.1`) is published as a pre-release. A release tag cannot be moved or deleted once
pushed, so a bad release is fixed by releasing the next version, and the bad one is unlisted on nuget.org.

Only repository admins can create a release tag, and nothing can publish without going through the `nuget` environment.

## Version rules

- A breaking change to a public API is a new **major** version. `EnablePackageValidation` fails the pack otherwise.
- A new public API is a **minor** version.
- A fix is a **patch** version.
- Below 1.0 the API may still change; each package stays at 0.x until it has been used for real.

## One-time setup

Done once per repository, not per package.

1. **Create the `nuget` environment** (approval and tag rule). Do this before the first release: GitHub creates a missing
   environment with no protection at all.

   ```bash
   dotnet run build/policy/Policy.cs -- --apply
   ```

2. **Reserve the package ID prefix on nuget.org.** Sign in, open *Account → Reserved prefixes* (or the form linked from the
   [prefix reservation documentation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)) and request `Sondor.`.
   Reserved packages get a verified badge and nobody else can publish under the prefix. Approval is manual and can take a few
   days; publishing works in the meantime.
3. **Add a trusted-publishing policy on nuget.org.** In *Account → Trusted Publishing* add a policy with:
   - repository owner `sondor-dev` and repository `sondor-dotnet`;
   - workflow file `release.yml`;
   - environment `nuget`.

   No API key is created or stored. The workflow exchanges its own identity for a key that lasts a short time.
4. **Tell the workflow whose policy to use.** Add a repository variable named `NUGET_USER` holding your nuget.org *profile name*
   (not your email). It is a variable, not a secret: the name is public.

   ```bash
   gh variable set NUGET_USER --repo sondor-dev/sondor-dotnet
   ```

The policy belongs to the nuget.org account (or organisation) that owns the packages, and the workflow's login step is only
accepted from a job in the workflow file the policy names (`release.yml`), not from a reusable workflow in another repository.
See the [trusted publishing documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

**The first release of a package ID that has never been published:** check, on the policy page, that nuget.org accepts the
temporary key for creating a new ID. If it does not, publish that first version once with an API key from your account
scoped to push new packages (glob `Sondor.*`), delete the key straight after, and use trusted publishing for every release
from then on. This has not been tried yet; update this paragraph with what happens.
