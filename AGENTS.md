# Repository instructions

## Version confirmation before commits

- Before creating any commit, ask the user for explicit confirmation about whether the pending changes should update the application version. Do not create the commit until the user answers.
- State the current version and propose the next appropriate semantic version in the confirmation question. If the user already supplied a specific version while explicitly requesting the commit, that counts as confirmation for that version.
- If the user declines a version update, keep all version declarations unchanged and do not add a new release entry to `CHANGELOG.md`.
- If the user approves a version update, update every version declaration below to the same version before committing:
  - `src/InternetMeter/InternetMeter.csproj`: `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion`.
  - `src/InternetMeter/app.manifest`: `assemblyIdentity` version.
  - `scripts/build.ps1`: `$appVersion`.
  - `installer/InternetMeter.iss`: `MyAppVersion`.
- For an approved version update, add a dated entry to `CHANGELOG.md` that maps the new version to all user-visible changes included in the commit. Keep entries in descending version order and do not duplicate an existing version heading.
- Before committing an approved version update, search the repository for stale application-version declarations, run the relevant build/tests, and verify that the generated executable reports the intended file version.
