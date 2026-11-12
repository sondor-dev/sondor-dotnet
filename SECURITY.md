# Security policy

## Reporting a vulnerability

Please report it privately, not as a public issue:
[report a vulnerability](https://github.com/sondor-dev/sondor-dotnet/security/advisories/new).

Include the package and version, what the problem allows, and the smallest code that shows it. You will get an
acknowledgement within a few working days. Fixes are released as patch versions of the affected package and credited in the
advisory unless you prefer otherwise.

## Supported versions

The latest release of each package is supported. A fix for an older major version is made only when the vulnerability is
serious and the upgrade is not straightforward.

## What the repository does for you

- Builds are deterministic and carry source link, so a published package can be matched to its source.
- Dependencies come from nuget.org only, and a vulnerable dependency fails the build (NuGet audit).
- Releases will be published from CI without a stored API key (nuget.org trusted publishing). Until that is in place, nothing
  is published from this repository.
