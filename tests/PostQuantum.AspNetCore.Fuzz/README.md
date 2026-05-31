# PostQuantum.AspNetCore.Fuzz

Coverage-guided fuzzing harness for `PostQuantum.AspNetCore` and the
`PqJwtValidator` it sits on. Uses [SharpFuzz](https://github.com/Metalnem/sharpfuzz)
to instrument the built assembly, then [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet)
to drive it.

This is **not** run by `dotnet test`. The in-process structured fuzz
tests in `PostQuantum.AspNetCore.Tests/FuzzTests.cs` already cover the
"any input should fail-closed" contract at PR time. SharpFuzz is for
**deeper** exploration — coverage-guided mutation finds inputs the
random in-process loops never hit.

## What the harness exercises

For each input, the target hits three code paths:

1. **`HeaderEncoding.TryGetBearerToken`** — the `Authorization: Bearer …`
   parser.
2. **`HeaderEncoding.EscapeForQuotedString`** — the realm escaper used
   in `WWW-Authenticate` challenge headers.
3. **`PqJwtValidator.Validate`** — the engine. This is the most
   interesting target because it owns the most surface (header parse,
   payload parse, signature verify, claim binding).

A crash = libfuzzer-dotnet caught an exception class that wasn't in the
expected fail-closed family:

| Expected (accepted)            | Caller's intent                                                  |
|--------------------------------|------------------------------------------------------------------|
| `PqJwtException` + subclasses  | The engine's intended fail-closed type. Tokens are rejected. |
| `FormatException`              | **Known engine leak** (bad Base64). Logged in `KNOWN-GAPS.md`. |
| `CryptographicException`       | **Known engine leak** (malformed key blob). Logged.             |
| `OperationCanceledException`   | Cancellation; valid even if no token was canceled.               |

**Any other exception is a bug.** That's the contract this harness is
checking.

## Running locally

Prerequisites:

```bash
dotnet tool install --global SharpFuzz.CommandLine
# libfuzzer-dotnet binary — build once, see
# https://github.com/Metalnem/libfuzzer-dotnet#building
```

Build, instrument, run:

```bash
# 1. Build the harness in Release
dotnet build tests/PostQuantum.AspNetCore.Fuzz -c Release

# 2. Instrument PostQuantum.AspNetCore.dll (the library, not the harness)
sharpfuzz tests/PostQuantum.AspNetCore.Fuzz/bin/Release/net10.0/PostQuantum.AspNetCore.dll

# 3. Drive it with libfuzzer-dotnet
./libfuzzer-dotnet \
    --target_path=tests/PostQuantum.AspNetCore.Fuzz/bin/Release/net10.0/PostQuantum.AspNetCore.Fuzz \
    tests/PostQuantum.AspNetCore.Fuzz/corpus/ \
    -max_total_time=300
```

`-max_total_time=300` gives you a 5-minute run. For overnight soaks,
omit the cap.

The corpus seeds are checked in under `corpus/` — keep small
representative inputs there so libfuzzer has a starting point.

## CI

A future workflow will run a bounded fuzz session on PRs labelled
`fuzz`. Until then, this is local-only — a maintainer should run a
soak before each `0.x` → `1.0` candidate.

## Why not run the harness via `dotnet test`

SharpFuzz needs:

1. The **library** assembly to be instrumented (`sharpfuzz` rewrites
   the IL).
2. The harness process to be driven by **libfuzzer-dotnet** (which
   bridges libFuzzer's protocol to .NET).
3. Long-running execution that doesn't fit xUnit's test-per-fact shape.

The harness's `Main` ends with `Fuzzer.Run(input => ...)`, which only
returns under libfuzzer's control. Running this binary without
libfuzzer would hang.
