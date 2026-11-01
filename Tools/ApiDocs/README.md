# Generating the PluginSdk API reference

Prerequisites: .NET 10 SDK, Node.js 24 and npm. The pinned DocFX local tool is a documentation dependency only; it does not ship with Lertaro.

From the repository root:

```powershell
dotnet tool restore
cd Site
npm ci
npm run docs:build
npm run site:preview
```

Open `/dev-guide/api-reference` in the preview for the manual entry, or `/api/index.html` for the generated reference. The shared API pages keep the English comments written in source. All seven manual locales have their own entry page and navigation label.

`npm run api:build` extracts the public API from `PluginSdk/PluginSdk.csproj` with DocFX, then builds the API pages with warnings treated as errors. It includes signatures for undocumented public members, and renders the XML comments that exist; it does not create missing descriptions. `EnableWindowsTargeting` allows project analysis on the Ubuntu documentation runner.

Generated metadata goes into the ignored `Tools/ApiDocs/reference/` directory. The generated HTML goes into ignored `Site/public/api/`, which VitePress copies into `Site/.vitepress/dist/api/`. Commit the source comments and configuration, not these generated files. When doing a clean rebuild locally, clear these generated directories first to avoid leaving obsolete pages from a prior source revision.

`npm run site:build` remains the fast manual-only build; use `docs:build` when validating API links or preparing a deployment. The Code quality workflow builds the complete documentation for pull requests and Beta validation. The deployment workflow also regenerates it when SDK sources, the tool manifest or shared build configuration change.

Deployment retains the existing external Pages repository and token configuration in `.github/workflows/deploy-docs.yml`. A local build does not publish the site or verify access to that destination.
