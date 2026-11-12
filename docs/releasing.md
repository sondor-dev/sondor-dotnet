# Releasing a package

Each package is versioned and released on its own.

## Versions

[MinVer](https://github.com/adamralph/minver) takes a package's version from the nearest tag with its prefix. A package sets
its prefix in its project file (`<MinVerTagPrefix>results-v</MinVerTagPrefix>`), so these tags are independent:

```
results-v1.0.0
cqrs-v0.3.1
```

A commit after the last tag builds as a pre-release (`1.0.1-preview.0.4`), which is what a local build and a pull request
produce.

## Steps

1. Make sure `main` is green.
2. Move the package's `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`.
3. Tag the commit: `git tag results-v1.0.0 && git push origin results-v1.0.0`.
4. The release workflow packs that package and publishes it to nuget.org.

> The release workflow is not in place yet; it comes with nuget.org trusted publishing. Until then nothing is published
> from this repository.

## Version rules

- A breaking change to a public API is a new **major** version. `EnablePackageValidation` fails the pack otherwise.
- A new public API is a **minor** version.
- A fix is a **patch** version.
