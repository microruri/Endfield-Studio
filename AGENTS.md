# AGENTS.md

Guidance for agentic coding tools working in this repository.

## Project Snapshot

- Language: C# (.NET 10)
- Repo type: multi-project .NET workspace (no `.sln` file currently)
- Main modules:
  - `src/Endfield.BlcTool.Core` (core crypto/decode/parser library)
  - `src/Endfield.BlcTool.Cli` (single-file `.blc` decode CLI)
  - `src/Endfield.Cli` (batch conversion CLI for game directories)
  - `tests/Endfield.BlcTool.Core.Tests` (xUnit tests for core)
- Nullable reference types: enabled in all projects
- Implicit usings: enabled in all projects

## Rule Sources (Cursor / Copilot)

- Checked `.cursor/rules/**`: not present.
- Checked `.cursorrules`: not present.
- Checked `.github/copilot-instructions.md`: not present.
- Therefore, use this file + existing code conventions as the primary instruction source.

## Environment Expectations

- SDK target is `net10.0`; ensure .NET 10 SDK is installed.
- On Windows, prefer PowerShell or `cmd` with quoted paths.
- Build artifacts are expected under `bin/` and `obj/` and should not be committed.

## High-Value Commands

Run commands from repository root unless stated otherwise.

### Restore

- `dotnet restore src/Endfield.BlcTool.Core/Endfield.BlcTool.Core.csproj`
- `dotnet restore src/Endfield.BlcTool.Cli/Endfield.BlcTool.Cli.csproj`
- `dotnet restore src/Endfield.Cli/Endfield.Cli.csproj`
- `dotnet restore tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj`

### Build

- Core library:
  - `dotnet build src/Endfield.BlcTool.Core/Endfield.BlcTool.Core.csproj -c Debug`
- Decode CLI:
  - `dotnet build src/Endfield.BlcTool.Cli/Endfield.BlcTool.Cli.csproj -c Debug`
- Batch CLI:
  - `dotnet build src/Endfield.Cli/Endfield.Cli.csproj -c Debug`
- Tests project:
  - `dotnet build tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj -c Debug`

### Test

- Run all tests:
  - `dotnet test tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj -c Debug`
- Run a single test class:
  - `dotnet test tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj --filter "FullyQualifiedName~KeyDeriverTests"`
- Run a single test method (preferred exact pattern):
  - `dotnet test tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj --filter "FullyQualifiedName=Endfield.BlcTool.Core.Tests.KeyDeriverTests.GetCommonChachaKey_ReturnsExpectedHex"`
- Run by display name fragment:
  - `dotnet test tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj --filter "DisplayName~Decrypt_Throws_WhenInputTooShort"`
- Optional coverage (collector is configured):
  - `dotnet test tests/Endfield.BlcTool.Core.Tests/Endfield.BlcTool.Core.Tests.csproj --collect:"XPlat Code Coverage"`

### Formatting / Linting

- No dedicated lint project or style analyzers are configured.
- Use .NET formatter when available:
  - `dotnet format src/Endfield.BlcTool.Core/Endfield.BlcTool.Core.csproj`
  - `dotnet format src/Endfield.BlcTool.Cli/Endfield.BlcTool.Cli.csproj`
  - `dotnet format src/Endfield.Cli/Endfield.Cli.csproj`
- If `dotnet format` is unavailable, follow existing style manually and keep diffs minimal.

### Run CLIs (useful smoke checks)

- Single `.blc` decode CLI:
  - `dotnet run --project src/Endfield.BlcTool.Cli/Endfield.BlcTool.Cli.csproj -- decode -i <input.blc> -o <output.json> -v`
- Batch conversion CLI:
  - `dotnet run --project src/Endfield.Cli/Endfield.Cli.csproj -- -g "C:\Program Files\Hypergryph Launcher\games\EndField Game" -t blc-all -o "C:\temp\endfield-json"`

## Repository Structure Guidance

- Put reusable parsing/decryption logic in `Endfield.BlcTool.Core`.
- Keep CLI projects as thin orchestration layers (argument parsing, I/O, exit codes).
- Add/extend tests under `tests/Endfield.BlcTool.Core.Tests` with xUnit.
- Do not mix game-install absolute paths into library code.

## Code Style Conventions (Inferred)

### Language and syntax

- Use modern C# features compatible with .NET 10.
- File-scoped namespaces are preferred.
- Top-level statements are used in CLI `Program.cs` files.
- Prefer `var` when type is obvious from the right-hand side.
- Keep methods small and focused; split helpers for parse/option logic.

### Imports and usings

- Keep `using` directives minimal and explicit.
- Order: BCL namespaces first (`System.*`), then project namespaces.
- Remove unused usings.
- Rely on implicit usings, but add explicit usings when clarity improves readability.

### Formatting

- Use 4 spaces for indentation.
- Keep braces and spacing consistent with existing files.
- Keep line length readable; avoid dense one-liners except trivial expressions.
- Preserve existing XML doc comments style in core library code.

### Naming

- Public types/methods/properties: PascalCase.
- Local variables/parameters: camelCase.
- Private constants/static readonly fields: PascalCase (current codebase pattern).
- Test method names: `MethodName_Condition_ExpectedBehavior`.
- Use descriptive names tied to domain terms (`nonce`, `chunk`, `ivSeed`, `groupCfg`).

### Types and models

- Prefer concrete model types for parser outputs (already used in `Models/BlcModels.cs`).
- Initialize reference properties to non-null defaults when possible.
- Use `List<T>` for mutable collections in model objects.
- Use numeric types matching binary format width (`byte`, `int`, `long`, `uint`).

### Error handling and validation

- Validate public method arguments early and throw standard exceptions:
  - `ArgumentNullException` for null
  - `ArgumentException` for invalid lengths/values
- In CLI layers, catch exceptions and return non-zero exit codes.
- Emit concise error messages to `Console.Error` in CLI projects.
- Avoid swallowing exceptions silently.

### Parsing and crypto logic

- Keep binary parsing deterministic and side-effect free.
- Add comments only for non-obvious binary layout/endianness steps.
- Preserve endianness behavior explicitly (`BinaryPrimitives`, `Array.Reverse`).
- Keep crypto primitives isolated in dedicated classes under `Crypto/`.

### Testing expectations

- Add tests for every bug fix or behavior change in core logic.
- Prefer targeted unit tests over large integration fixtures.
- Include negative/guard-path tests for malformed input.
- Keep test data lightweight and deterministic.

## Change Management for Agents

- Prefer minimal, surgical edits over broad refactors.
- Do not introduce unrelated formatting churn.
- Update docs/help text when CLI arguments or behavior change.
- If adding new commands, document usage in CLI help output and this file.

## What to Check Before Finishing

- Build the touched project(s) successfully.
- Run relevant tests; at minimum, run changed test class or method.
- For CLI behavior changes, run one representative `dotnet run` invocation.
- Ensure new files are included in the right project and namespace.

## Known Gaps / Current State

- README is minimal and marked work-in-progress.
- No solution file (`.sln`) yet; commands are project-path based.
- No dedicated CI or lint configuration found in-repo.
- No Cursor/Copilot instruction files currently present.

## Agent Defaults for This Repo

- Default to implementing core behavior in `Endfield.BlcTool.Core` first.
- Keep CLIs as wrappers around core APIs.
- Preserve existing exit-code semantics in CLI tools.
- When uncertain, follow patterns from `BlcDecryptor`, `BlcParser`, and existing tests.
