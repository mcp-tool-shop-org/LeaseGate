# Contributing to LeaseGate

Thanks for contributing.

## Development Setup

1. Install .NET 8 SDK
2. Restore/build/test:

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

3. Run sample for behavioral validation:

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

## Contribution Rules

- Keep changes scoped and minimal
- Add or update tests for behavior changes
- Preserve protocol backward compatibility where possible
- Keep deny reasons explicit and recommendation text actionable
- Never make audit failures crash the governor

## Pull Request Checklist

- [ ] Build passes (`dotnet build`)
- [ ] Tests pass (`dotnet test`)
- [ ] Sample scenario still runs
- [ ] Documentation updated for behavior/config changes
- [ ] Changelog entry added

## Commit Guidance

Prefer clear, phase/scoped commit messages, for example:

- `protocol: add <feature>`
- `service: enforce <rule>`
- `policy: update <constraint>`
- `client: add <fallback behavior>`

## Security Notes

If you find a security issue:

- do not publish exploit details in issues
- report privately to maintainers first
