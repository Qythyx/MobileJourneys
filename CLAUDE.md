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

### Releasing a new version

1. Make sure `main` is clean and pushed.
2. Decide the bump per SemVer (breaking → major, feature → minor, fix → patch).
3. Tag and push:

   ```sh
   git tag 1.0.0
   git push origin 1.0.0
   ```

4. The next build (locally or in CI) will stamp the assembly as `1.0.0`.

Tags must be plain SemVer (`1.0.0`, not `v1.0.0`). If a `v` prefix is ever desired, set
`<MinVerTagPrefix>v</MinVerTagPrefix>` in the csproj.

### Where the version surfaces at runtime

[`TestAssembly.FrameworkVersion`](MobileJourneys/Framework/TestAssembly.cs) reads
`AssemblyInformationalVersionAttribute` via reflection. That attribute is exactly what MinVer
stamps, so the value flows through automatically — MTP framework/provider Version slots see the
MinVer-derived version with no additional plumbing.
