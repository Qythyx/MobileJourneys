# MobileJourneys — Repo Notes

## Versioning (MinVer)

This repo uses [MinVer](https://github.com/adamralph/minver) to derive the `MobileJourneys` library
version from git tags at build time. There is **no** `<Version>` in any csproj — it's wholly
tag-driven.

### How it works

- MinVer is referenced as `PrivateAssets="All"` in
  [MobileJourneys.csproj](MobileJourneys/MobileJourneys.csproj), so it runs at build time only and
  is not a runtime dependency.
- On build, MinVer walks back from `HEAD` to find the most recent tag matching SemVer 2.0 (e.g.,
  `1.4.2`), then stamps the assembly's `Version`, `FileVersion`, `AssemblyVersion`, and
  `InformationalVersion` attributes.
- If `HEAD` _is_ a tagged commit, the version is the tag exactly (`1.4.2`).
- If commits have been added since the last tag, the version becomes a pre-release:
  `1.4.3-alpha.0.<height>+<sha>`, where `<height>` is the number of commits past the tag (the
  `alpha.0` prefix comes from `MinVerDefaultPreReleaseIdentifiers` in the csproj).
- With no tags at all, builds produce `0.0.0-alpha.0.<height>+<sha>`.

### Releasing a new version (automatic)

Tagging is automated. The `release` job in
[ci.yml](.github/workflows/ci.yml) runs on every push to `main` (after tests pass) and uses
[`mathieudutour/github-tag-action`](https://github.com/mathieudutour/github-tag-action) to read the
[Conventional Commits](https://www.conventionalcommits.org/) since the last tag, compute the next
SemVer, push the tag, and publish a GitHub Release with the changelog. MinVer then stamps that tag
on the next build. **No manual tagging is needed — just merge with well-formed commit messages.**

Bump rules:

| Commit type(s) since last tag                                | Result         |
| ------------------------------------------------------------ | -------------- |
| `feat:`                                                      | minor          |
| `fix:` / `perf:`                                             | patch          |
| `!` or `BREAKING CHANGE:`                                    | major          |
| only `docs`/`chore`/`ci`/`refactor`/`test`/`style`/`build`   | no release     |

Tags are plain SemVer (`1.0.0`, not `v1.0.0`) via `tag_prefix: ""`.

### Releasing manually (override)

To cut a specific version by hand (e.g., to skip a number or force a release the commit messages
wouldn't produce), tag and push directly:

```sh
git tag 1.0.0
git push origin 1.0.0
```

The next build stamps the assembly as `1.0.0`. If a `v` prefix is ever desired, set
`<MinVerTagPrefix>v</MinVerTagPrefix>` in the csproj **and** the workflow's `tag_prefix`.

### Where the version surfaces at runtime

[`TestAssembly.FrameworkVersion`](MobileJourneys/Framework/TestAssembly.cs) reads
`AssemblyInformationalVersionAttribute` via reflection. That attribute is exactly what MinVer
stamps, so the value flows through automatically — the runner's console header shows the
MinVer-derived version with no additional plumbing.
