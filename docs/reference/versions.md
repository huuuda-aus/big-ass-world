# Pinned versions

Update only through an ADR (CLAUDE.md section 3).

| Component | Version | Notes |
|---|---|---|
| .NET SDK | 8.0.131 | Installed on the dev machine. Target framework `net8.0`. |
| C# language | 12.0 | Set in `Directory.Build.props`. |
| xUnit | 2.4.2 | From the `dotnet new xunit` template. See [known issues](../known-issues.md). |
| xunit.runner.visualstudio | 2.4.5 | Template default. |
| Microsoft.NET.Test.Sdk | 17.6.0 | Template default. |
| coverlet.collector | 6.0.0 | Template default. |
| YamlDotNet | 18.1.0 | Realm.Data. Latest stable when added; see [ADR 0003](../decisions/0003-yaml-content-loading.md). |
| Godot (.NET edition) | 4.5.1-stable (`4.5.1.stable.mono.official.f62fdbde1`) | Downloaded from the official godot-builds release; SHA-512 verified. Lives in `~/tools/godot-mono-4.5.1/` on the developer machine, not in the repository. Standard (non-.NET) builds cannot run the C# projects. |
| Godot.NET.Sdk / GodotSharp | 4.5.1 | NuGet, referenced by `src/Realm.Game/Realm.Game.csproj`. Requires the .NET SDK 8.0 or later. |
| Terrain3D | 1.0.2-stable | Vendored in `src/Realm.Game/addons/terrain_3d` (Linux and Windows binaries only; Android, iOS, macOS and web binaries removed). Documented for Godot 4.4 to 4.6; verified working on 4.5.1 by the streaming spike. Zip SHA-256 `a071850250ec5e596aa54da61c01d75768774eb379ee997584d426a45f4884a2`. Godot 4.7 is not verified. The 1.1.0 release candidate was not used. |
| Lua runtime, LZ4/Zstd | **Not pinned** | Chosen when first needed. |
