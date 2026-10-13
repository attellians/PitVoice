# PitVoice

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`).

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- **MAJOR / MINOR** are changed by hand: edit `VERSION`, stage it, commit. A manually staged `VERSION` is committed as is, without an extra bump.
- Skip the bump for one commit: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
