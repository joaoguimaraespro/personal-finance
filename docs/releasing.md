# Releasing

1. Make sure `main` is green.
2. Add the release to [CHANGELOG.md](../CHANGELOG.md): what changed for people who install the app, grouped under
   *Added*, *Changed*, *Fixed*. Dependency bumps are left out unless they change behaviour.
3. Tag and push: `git tag v1.2.3 && git push origin v1.2.3`.
4. [`desktop.yml`](../.github/workflows/desktop.yml) builds and smoke-tests the Windows installer and both macOS disk
   images, then publishes a GitHub release with them and `SHA256SUMS.txt`. The generated notes leave out Dependabot
   ([`.github/release.yml`](../.github/release.yml)).
5. Replace the generated notes with the CHANGELOG entry plus the install table (see the
   [1.1.0 release](https://github.com/joaoguimaraespro/personal-finance/releases/tag/v1.1.0) for the layout):
   `gh release edit v1.2.3 --notes-file notes.md`.

Versions follow [Semantic Versioning](https://semver.org): a database change that needs a manual step, or a
removed feature, is a major version.
