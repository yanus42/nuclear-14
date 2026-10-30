# Portrait region generation

Run from the repository root after changing marking sprites:

    dotnet run --project Tools/_Forge/AppearancePortraits/GeneratePortraitRegions.csproj -- .

Build the client first: the tool references its existing ImageSharp DLL.
The tool runs outside the client sandbox and writes
Content.Client/_Forge/Humanoid/MarkingPortraitRegion.Generated.cs.
It scans marking YAML files and unions source alpha bounds across all poses/frames.
Client code combines the generated bounds of each marking's layers and adds framing margins.
Unknown sprite states safely use the full-body frame until this table is regenerated.
No textures or game prototypes are added.
