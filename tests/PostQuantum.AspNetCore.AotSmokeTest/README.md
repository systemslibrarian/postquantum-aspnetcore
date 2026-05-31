# PostQuantum.AspNetCore.AotSmokeTest

A tiny consuming app that exists for **one purpose**: prove
`PostQuantum.AspNetCore` can be consumed in a `PublishAot=true` app
without IL trim warnings.

The project's source touches every public-API entry point this library
promises is AOT-safe: `AddPostQuantumJwtBearer`, all four event hooks
(`OnMessageReceived` / `OnTokenValidated` / `OnAuthenticationFailed` /
`OnChallenge`), the key-ring DI helpers (`AddPostQuantumJwtKeyRing` and
`AddPostQuantumJwtKeyRing<T>`), and the warmup hosted service
(`AddPostQuantumJwtKeyRingWarmup`).

The csproj sets:

- `<PublishAot>true</PublishAot>` — forces native-AOT compilation.
- `<TrimmerSingleWarn>false</TrimmerSingleWarn>` — every individual
  IL warning surfaces (not just one aggregate per assembly).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — an
  AOT-unsafe regression in the library will fail this project's
  publish, which fails CI.

## Running it

### CI (the authoritative run)

The `aot-publish` job in `.github/workflows/ci.yml` runs
`dotnet publish -c Release -p:PublishAot=true` on Ubuntu (which has
clang for the native-link step) against this project on every push
and PR. A regression there blocks the PR.

### Locally — Linux / macOS

```bash
dotnet publish tests/PostQuantum.AspNetCore.AotSmokeTest -c Release -p:PublishAot=true
```

### Locally — Windows

Native AOT on Windows requires the **Visual Studio Build Tools** with
the C++ workload (specifically MSVC + the Windows SDK). Without them,
the IL compile succeeds but the native link step fails with a clear
"link.exe not found" error. Install the Visual Studio Build Tools or
rely on CI.

A plain `dotnet build` of this project (no publish) catches the
build-time IL analyzer warnings, which is most of what AOT cares about
— so the project is still useful as a local smoke test even without
the full native link.

## What this does NOT prove

- Runtime correctness of the AOT-published binary. The smoke-test app
  never actually runs (`app.Run()` is intentionally omitted) — the IL
  trim analyzer's clean exit is the assertion.
- AOT compatibility of the underlying `PostQuantum.Jwt` engine. That
  package declares `IsAotCompatible=true` and is exercised here
  transitively, but a deeper proof lives in the engine repo.
- Specific architectures other than `linux-x64`. If you ship to
  `osx-arm64` or `win-x64`, run the publish against your target
  triple as part of release verification.
