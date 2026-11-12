# Repository policy

The repository's GitHub settings are described as files in [`build/policy`](../build/policy), so the rules are visible,
reviewed like code, and can be put back if someone changes them by accident.

| File | What it describes |
|---|---|
| `repository.json` | Merge methods (squash only), branch clean-up, wiki and projects off, secret scanning, push protection, Dependabot security updates |
| `actions.json` | Which actions may run, read-only default token, no pull-request approval by workflows, approval for every outside contributor's workflow run |
| `environments.json` | The `nuget` environment the release workflow publishes from: you must approve each deployment, admins cannot skip that, and only `main` can deploy to it. Deployment rules that are not listed are removed |
| `code-scanning.json` | GitHub's "default setup" for code scanning stays off, because CodeQL runs from `.github/workflows/codeql.yml` (versioned with the repository, and it survives a move) |
| `rulesets/main.json` | `main`: changes by pull request only, squash and linear history, the CI checks must pass, no force-push, no deletion |
| `rulesets/release-tags-immutable.json` | A release tag (`*-v*`) can never be moved or deleted. Creating one is deliberately not restricted: GitHub does not let the Actions app on a ruleset's bypass list, so a creation rule would stop the release workflow tagging its own release, and a tag does not start anything. Publishing is gated by the `nuget` environment instead |

Private vulnerability reporting and Dependabot alerts are checked too. They have their own settings page in GitHub.

## Using it

You need the [GitHub CLI](https://cli.github.com) signed in as a repository admin (`gh auth login`).

```bash
# Read-only: lists where GitHub differs from the files. Exits 1 when it does.
dotnet run build/policy/Policy.cs

# Make GitHub match the files.
dotnet run build/policy/Policy.cs -- --apply
```

The tool never deletes: a ruleset that is not described here is reported and left alone.

## Changing a rule

Change the file in a pull request, merge it, then run the tool with `--apply`. A change made in the GitHub settings page
instead shows up as a difference the next time the check runs. That is how a change nobody reviewed gets noticed.

## Things to know

- **Required checks are matched by name.** `rulesets/main.json` lists `Build and test (ubuntu-latest)` and
  `Build and test (windows-latest)`, which are the job names in `ci.yml`. Rename a job and the rule must change with it, or
  pull requests wait for a check that never reports. For the same reason, do not add path filters to the CI workflow: a
  skipped required check also leaves a pull request waiting.
- **Nobody bypasses `main`.** Not even an admin can push to it directly. To change the rule in an emergency, edit the
  ruleset in GitHub, then put the file back in line afterwards.
- **Not in these files:** secrets and tokens, the nuget.org trusted-publishing policy (it lives in the nuget.org account), and
  organisation settings such as required two-factor authentication.
