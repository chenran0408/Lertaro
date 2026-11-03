# Contributing to Lertaro

Thanks for your interest! Lertaro is a Windows launcher (file search + clipboard history + more)
built with C#/WPF and a small Rust core.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and (for the search
   index) the Rust toolchain with the `x86_64-pc-windows-msvc` and `aarch64-pc-windows-msvc`
   targets -- `Core/Core.csproj` compiles the native index library via cargo on build.
2. `build_and_run.bat` builds everything and starts the app locally.
3. Read `DEVELOPMENT_GUIDE.md` and `AGENTS.md` before your first change -- they define the coding
   standards (file size limits, per-project builds, MSTest coverage requirements) that CI enforces.

## Branches and releases

- `beta` is the development and release line; releases are cut by pushing a `v*` tag that matches
  the version in `App/App.csproj` (see `release-notes/` for the notes file pattern).
- `main` is currently behind; do not base work on it.

## Pull requests

- Keep changes focused; build and test only the projects you touched
  (`dotnet test Tests/<Area>/<Area>.csproj`).
- New non-trivial logic ships with MSTest coverage in the same change.
- Plugin strings must be translated in all seven locales under
  `Plugins/<Name>/Resources/Translations/`.
