# Appearance tile pickers

Client-only extension of the existing humanoid profile editor. No game prototypes,
server integrations or database migrations are added.

- `HairTilePicker`: shared hair/beard picker, search, selected state and local favorites.
- `MarkingPicker.*`: partial adapter retaining species/sex restrictions, slot limits,
  ordering and per-layer colors. Click toggles a marking; a full category does not
  automatically replace another choice. Used markings remain visible in the catalogue.
- `MarkingPreviewComposition`: a temporary alternative replaces only its own category
  in the preview. Hover never assigns the saved profile.
- `HairPortraitView` / `AppearancePreviewDoll` / `AppearancePreviewQueue`: lazy owned
  entities, shared budget of two creations/updates per frame, latest-state coalescing,
  and release after 0.5 seconds offscreen or immediately on hide/removal/disposal.
  UI card controls themselves are still built for the catalogue.
- `AppearanceFavorites`: local text stores, independent of character saves and server.
- `LoadoutPreferenceDraft` / `LoadoutSelection`: preserve item customization and avoid
  charging an already selected loadout a second time when saving its custom fields.
- `MarkingPortraitRegion.Generated.cs`: offline-generated sprite bounds. Regeneration
  instructions are in `Tools/_Forge/AppearancePortraits/README.md`. Pixel reads must
  remain outside the client sandbox.

The shared clothing switch hides job clothing and loadout armor. The inventory toggle
keeps its preference when the main clothing switch is off. Preview-only items which
cannot be equipped are deleted by `LobbyUIController.GiveDummyLoadout`.

Sources: Goob-Station PRs [7455](https://github.com/Goob-Station/Goob-Station/pull/7455)
and [7463](https://github.com/Goob-Station/Goob-Station/pull/7463), pinned main reference
`039d51753294bcd9c030db57707b3ec3362ea8c8`. See `NOTICE.txt` and `GOOB-LICENSE.txt`.
Goob fonts, artwork, marking catalogues and lobby layout are not imported.
Existing Ghoul accessory-layer limitations are intentionally unchanged.

Regression checks:

    dotnet test Content.Tests/Content.Tests.csproj --filter FullyQualifiedName~AppearanceEditorTests

These tests cover model/queue/lifetime contracts, not a rendered editor session.
Manual review should include both interface themes, fast character/species changes,
scrolling, clothing/armor visibility, hover cancellation, layer colors, favorites,
profile save/reopen and loadout customization on a server. No numerical FPS improvement
is claimed solely from the model tests.
