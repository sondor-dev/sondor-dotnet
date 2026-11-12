# Contributing

Thank you for taking an interest. This is a small project with a clear scope, so it helps to talk before you build: open an
issue describing the problem first, especially for a new package or a change to a public API.

## Building and testing

```bash
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
```

The build treats warnings as errors and runs the .NET security analyzers on library code. Fix a finding in the code. If a
suppression is genuinely right, put it next to the line with the reason.

## Pull requests

- Keep a pull request to one change. Say what it does and why.
- Every public type has tests: the happy path, the failure path, and any security boundary (malformed or hostile input).
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org) (`feat:`, `fix:`, `docs:`, `refactor:`).
- Squash-merge is used, so the pull request title becomes the commit message.

## Public API

Each package under `src/` tracks its public API in `PublicAPI.Shipped.txt` (what has been released) and
`PublicAPI.Unshipped.txt` (what the next release adds). The analyzer fails the build when they disagree with the code, so
an API change is always deliberate. `EnablePackageValidation` additionally fails the pack when a change would break
consumers of the last release.

## Adding a package

1. Create `src/Sondor.<Name>/` and `tests/Sondor.<Name>.Tests/`, and add both to `Sondor.slnx`.
2. In the project file, set `MinVerTagPrefix` (for example `results-v`) and a `Description`. Package versions are never
   written in a project file; they live in `Directory.Packages.props`.
3. Add `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt` and a `README.md` (it is shown on nuget.org).
4. Respect the layering: depend only on lower layers, and put optional dependencies in an adapter package.

## Releasing

Only maintainers release, from the Actions tab: the release workflow works out the next version of a package from its last
release tag, publishes it, and then creates the tag (`<prefix>-v<version>`, for example `results-v1.2.0`). You do not tag
anything in a pull request. See [docs/releasing.md](docs/releasing.md).

## Conduct

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
