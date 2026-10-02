using Content.Server.Administration.Logs;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Shared._Forge.Newspapers;
using Content.Shared._Forge.Paper;
using Content.Shared.Database;
using Content.Shared.Materials;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using System.Linq;
using Content.Server._Forge.Photo;
using Content.Shared.Interaction;
using System.Security.Cryptography;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;

namespace Content.Server._Forge.Newspapers;

public sealed class NewspaperSystem : EntitySystem
{
    private const string PaperMaterial = "Paper";
    private const int PaperPerCopy = 100;
    private const int MaxPrintCount = 10;
    private const int MaxPhotoBytes = 4 * 1024 * 1024;
    private readonly Dictionary<EntityUid, (TimeSpan Start, int Count)> _imageRequests = new();
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MaterialStorageSystem _materials = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _imageRequests.Clear());

        SubscribeLocalEvent<NewspaperDeskComponent, AfterActivatableUIOpenEvent>(OnDeskOpen);
        SubscribeLocalEvent<NewspaperDeskComponent, MaterialAmountChangedEvent>(OnMaterialChanged);
        SubscribeLocalEvent<NewspaperDeskComponent, InteractUsingEvent>(OnAttachPhoto,
            before: new[] { typeof(MaterialStorageSystem) });
        Subs.BuiEvents<NewspaperDeskComponent>(NewspaperDeskUiKey.Key, subs =>
        {
            subs.Event<NewspaperSaveDraftMessage>(OnSaveDraft);
            subs.Event<NewspaperSwitchPublicationMessage>(OnSwitchPublication);
            subs.Event<NewspaperSaveTemplateMessage>(OnSaveTemplate);
            subs.Event<NewspaperPublishMessage>(OnPublish);
            subs.Event<NewspaperPrintMessage>(OnPrint);
            subs.Event<NewspaperImageRequestMessage>(OnDeskImageRequested);
            subs.Event<NewspaperRemoveImageMessage>(OnRemoveImage);
            subs.Event<NewspaperSelectImageMessage>(OnSelectImage);
            subs.Event<NewspaperForgetImageMessage>(OnForgetImage);
        });

        SubscribeLocalEvent<NewspaperCopyComponent, AfterActivatableUIOpenEvent>(OnCopyOpen);
        Subs.BuiEvents<NewspaperCopyComponent>(NewspaperCopyUiKey.Key, subs =>
            subs.Event<NewspaperImageRequestMessage>(OnCopyImageRequested));
    }

    private void OnAttachPhoto(EntityUid uid, NewspaperDeskComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<PhotoCardComponent>(args.Used, out var photo))
            return;
        args.Handled = true;
        if (photo.ImageData == null || photo.ImageData.Length > MaxPhotoBytes || !AllowImageRequest(args.User))
            return;
        var key = Convert.ToHexString(SHA256.HashData(photo.ImageData));
        var existing = component.PhotoKeys.FirstOrDefault(p => p.Value == key);
        if (existing.Key != 0 && component.BufferedPhotos.Contains(existing.Key))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-photo-already-buffered"), uid, args.User);
            return;
        }
        var bytes = component.BufferedPhotos.Sum(id => (long)(component.Photos.GetValueOrDefault(id)?.Length ?? 0));
        if (!NewspaperPhotoBuffer.CanImport(component.BufferedPhotos.Count, bytes, photo.ImageData.Length))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-photo-buffer-full"), uid, args.User);
            return;
        }
        var photoId = existing.Key != 0 ? existing.Key : ++component.NextPhotoId;
        component.Photos[photoId] = photo.ImageData;
        component.PhotoKeys[photoId] = key;
        component.BufferedPhotos.Add(photoId);
        PrunePhotos(component);
        UpdateDeskUi(uid, component);
        _popup.PopupEntity(Loc.GetString("newspaper-photo-attached"), uid, args.User);
    }

    private static void PrunePhotos(NewspaperDeskComponent component)
    {
        var retained = component.Editions.Select(edition => edition.PhotoId).ToHashSet();
        retained.Add(component.Draft.PhotoId);
        retained.UnionWith(component.PublicationDrafts.Values.Select(d => d.PhotoId));
        retained.UnionWith(component.BufferedPhotos);
        foreach (var id in component.Photos.Keys.Where(id => !retained.Contains(id)).ToArray())
            component.Photos.Remove(id);
    }

    private void OnRemoveImage(Entity<NewspaperDeskComponent> ent, ref NewspaperRemoveImageMessage msg)
    {
        ent.Comp.Draft.PhotoId = -1;
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnSelectImage(Entity<NewspaperDeskComponent> ent, ref NewspaperSelectImageMessage msg)
    {
        if (msg.PhotoId != -1 && !ent.Comp.BufferedPhotos.Contains(msg.PhotoId)) return;
        ent.Comp.Draft.PhotoId = msg.PhotoId;
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnForgetImage(Entity<NewspaperDeskComponent> ent, ref NewspaperForgetImageMessage msg)
    {
        if (!ent.Comp.BufferedPhotos.Remove(msg.PhotoId)) return;
        if (ent.Comp.Draft.PhotoId == msg.PhotoId) ent.Comp.Draft.PhotoId = -1;
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnDeskImageRequested(Entity<NewspaperDeskComponent> ent, ref NewspaperImageRequestMessage msg)
    {
        if (!AllowImageRequest(msg.Actor) || msg.Edition < 0 || msg.Edition > ent.Comp.Editions.Count)
            return;
        var edition = msg.Edition == 0 ? ent.Comp.Draft : ent.Comp.Editions[msg.Edition - 1];
        var photoId = msg.PhotoId >= 0 ? msg.PhotoId : edition.PhotoId;
        if (photoId != edition.PhotoId && !ent.Comp.BufferedPhotos.Contains(photoId)) return;
        if (ent.Comp.Photos.TryGetValue(photoId, out var data) && data.Length <= MaxPhotoBytes)
            _ui.ServerSendUiMessage(ent.Owner, NewspaperDeskUiKey.Key,
                new NewspaperImageMessage(photoId, data, ent.Comp.PhotoKeys.GetValueOrDefault(photoId, "")), msg.Actor);
    }

    private void OnCopyImageRequested(Entity<NewspaperCopyComponent> ent, ref NewspaperImageRequestMessage msg)
    {
        if (AllowImageRequest(msg.Actor) && ent.Comp.ImageData is { Length: <= MaxPhotoBytes })
            _ui.ServerSendUiMessage(ent.Owner, NewspaperCopyUiKey.Key,
                new NewspaperImageMessage(ent.Comp.Edition.PhotoId, ent.Comp.ImageData, ent.Comp.PhotoKey), msg.Actor);
    }

    private void OnDeskOpen(EntityUid uid, NewspaperDeskComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateDeskUi(uid, component);
    }

    private void OnMaterialChanged(EntityUid uid, NewspaperDeskComponent component, ref MaterialAmountChangedEvent args)
    {
        UpdateDeskUi(uid, component);
    }

    private void OnCopyOpen(EntityUid uid, NewspaperCopyComponent component, AfterActivatableUIOpenEvent args)
    {
        if (component.CachedUiState == null)
        {
            if (component.ImageData != null && component.PhotoKey.Length == 0)
                component.PhotoKey = Convert.ToHexString(SHA256.HashData(component.ImageData));
            component.CachedUiState = new NewspaperCopyUiState(component.Edition,
                TryComp<PaperSurfaceComponent>(uid, out var paper) ? paper.Appearance.Clone() : null, component.PhotoKey);
        }
        _ui.SetUiState(uid, NewspaperCopyUiKey.Key, component.CachedUiState);
    }

    private bool AllowImageRequest(EntityUid actor)
    {
        var now = _timing.RealTime;
        var entry = _imageRequests.GetValueOrDefault(actor);
        if (now - entry.Start > TimeSpan.FromSeconds(2)) entry = (now, 0);
        entry.Count++; _imageRequests[actor] = entry;
        return entry.Count <= 5;
    }

    private static bool TryValidate(NewspaperEdition input, bool requireContent, out NewspaperEdition clean)
    {
        clean = new NewspaperEdition();
        if (!NewspaperLayout.IsValid(input, requireContent))
            return false;
        clean = input.Clone();
        NewspaperLayout.ResolvePhotoCaptions(clean);
        if (!NewspaperLayout.IsValid(clean, requireContent)) return false;
        clean.Name = clean.Name.Trim();
        clean.TemplateName = clean.TemplateName.Trim();
        clean.Number = 0;
        foreach (var block in clean.Blocks)
        {
            block.TextKey = null;
            block.DemoKey = null;
        }
        return true;
    }

    private void OnSaveTemplate(Entity<NewspaperDeskComponent> ent, ref NewspaperSaveTemplateMessage msg)
    {
        if (!TryValidate(msg.Template, false, out var template) || string.IsNullOrWhiteSpace(template.TemplateName))
            return;
        var index = ent.Comp.Templates.FindIndex(t => t.TemplateName == template.TemplateName);
        if (index < 0 && ent.Comp.Templates.Count >= NewspaperLayout.MaxTemplates)
        {
            _popup.PopupEntity(Loc.GetString("newspaper-template-limit"), ent, msg.Actor);
            return;
        }
        template.PhotoId = -1;
        if (index < 0)
            ent.Comp.Templates.Add(template);
        else
            ent.Comp.Templates[index] = template;
        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-template-saved"), ent, msg.Actor);
    }

    private static bool TryValidateActiveDraft(NewspaperDeskComponent component, NewspaperEdition input, bool requireContent, out NewspaperEdition clean)
    {
        if (!TryValidate(input, requireContent, out clean) || input.PublicationId != component.Draft.PublicationId)
            return false;
        var id = clean.PublicationId; var name = clean.Name;
        return string.IsNullOrEmpty(name) || !component.PublicationDrafts.Values.Any(d =>
            d.PublicationId != id && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void StoreDraft(NewspaperDeskComponent component, NewspaperEdition draft)
    {
        component.Draft = draft;
        component.PublicationDrafts[draft.PublicationId] = draft;
    }

    private void OnSwitchPublication(Entity<NewspaperDeskComponent> ent, ref NewspaperSwitchPublicationMessage msg)
    {
        if (!TryValidateActiveDraft(ent.Comp, msg.Draft, false, out var draft))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-invalid-content"), ent, msg.Actor);
            return;
        }
        NewspaperEdition target;
        if (msg.PublicationId == 0)
        {
            var name = msg.Name?.Trim() ?? "";
            // The initial unnamed draft becomes the first publication, preserving its layout and text.
            if (name.Length is >= 1 and <= 80 && ent.Comp.PublicationDrafts.Count == 1 &&
                string.IsNullOrWhiteSpace(ent.Comp.Draft.Name) && ent.Comp.Editions.Count == 0)
            {
                draft.Name = name; draft.PhotoId = ent.Comp.Draft.PhotoId;
                StoreDraft(ent.Comp, draft);
                UpdateDeskUi(ent, ent.Comp);
                return;
            }
            if (name.Length is < 1 or > 80 || ent.Comp.PublicationDrafts.Count >= NewspaperLayout.MaxPublications ||
                string.Equals(draft.Name, name, StringComparison.OrdinalIgnoreCase) ||
                ent.Comp.PublicationDrafts.Values.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _popup.PopupEntity(Loc.GetString("newspaper-publication-invalid"), ent, msg.Actor);
                return;
            }
            target = new NewspaperEdition { Name = name, PublicationId = ++ent.Comp.NextPublicationId };
        }
        else if (!ent.Comp.PublicationDrafts.TryGetValue(msg.PublicationId, out target!))
            return;
        draft.PhotoId = ent.Comp.Draft.PhotoId;
        StoreDraft(ent.Comp, draft);
        StoreDraft(ent.Comp, target.PublicationId == draft.PublicationId ? draft : target);
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnSaveDraft(Entity<NewspaperDeskComponent> ent, ref NewspaperSaveDraftMessage msg)
    {
        if (!TryValidateActiveDraft(ent.Comp, msg.Draft, false, out var draft))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-invalid-content"), ent, msg.Actor);
            return;
        }

        draft.PhotoId = ent.Comp.Draft.PhotoId;
        StoreDraft(ent.Comp, draft);
        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-draft-saved"), ent, msg.Actor);
    }

    private void OnPublish(Entity<NewspaperDeskComponent> ent, ref NewspaperPublishMessage msg)
    {
        if (!TryValidateActiveDraft(ent.Comp, msg.Draft, true, out var draft))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-invalid-content"), ent, msg.Actor);
            return;
        }

        draft.PhotoId = ent.Comp.Draft.PhotoId;
        StoreDraft(ent.Comp, draft);
        var published = draft.Clone();
        published.Number = NewspaperLayout.NextEditionNumber(ent.Comp.Editions, draft.PublicationId);
        ent.Comp.Editions.Add(published);
        _adminLogger.Add(LogType.Chat, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor):actor} approved newspaper edition {published.Number} at {ToPrettyString(ent):desk}: {published.Name} / {string.Join(" / ", published.Blocks.Where(b => b.Kind == NewspaperBlockKind.Text).Select(b => b.Text))}");
        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-published", ("edition", published.Number)), ent, msg.Actor);
    }

    private void OnPrint(Entity<NewspaperDeskComponent> ent, ref NewspaperPrintMessage msg)
    {
        if (msg.Count is < 1 or > MaxPrintCount || msg.Edition < 1 || msg.Edition > ent.Comp.Editions.Count)
            return;

        var edition = ent.Comp.Editions[msg.Edition - 1];

        var cost = msg.Count * PaperPerCopy;
        if (!_materials.TryChangeMaterialAmount(ent, PaperMaterial, -cost))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-no-paper"), ent, msg.Actor);
            return;
        }

        for (var i = 0; i < msg.Count; i++)
        {
            var copy = Spawn("N14PrintedBulletin", Transform(ent).Coordinates);
            var component = Comp<NewspaperCopyComponent>(copy);
            component.Edition = edition;
            component.ImageData = ent.Comp.Photos.GetValueOrDefault(edition.PhotoId);
            _meta.SetEntityName(copy, Loc.GetString("newspaper-printed-name",
                ("name", edition.Name), ("edition", edition.Number)));
        }

        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-printed", ("count", msg.Count)), ent, msg.Actor);
    }

    private void UpdateDeskUi(EntityUid uid, NewspaperDeskComponent component)
    {
        if (component.PublicationDrafts.Count == 0)
        {
            component.Draft.PublicationId = ++component.NextPublicationId;
            component.PublicationDrafts[component.Draft.PublicationId] = component.Draft;
            foreach (var edition in component.Editions) edition.PublicationId = component.Draft.PublicationId;
        }
        foreach (var (id, data) in component.Photos)
            if (!component.PhotoKeys.ContainsKey(id)) component.PhotoKeys[id] = Convert.ToHexString(SHA256.HashData(data));
        foreach (var id in component.PhotoKeys.Keys.Where(id => !component.Photos.ContainsKey(id)).ToArray()) component.PhotoKeys.Remove(id);
        var paperCount = _materials.GetMaterialAmount(uid, PaperMaterial) / PaperPerCopy;
        _ui.SetUiState(uid, NewspaperDeskUiKey.Key,
            new NewspaperDeskUiState(component.Draft, component.Editions.ToArray(), paperCount, component.Templates.ToArray(),
                component.PublicationDrafts.Select(p => new NewspaperPublicationInfo(p.Key, p.Value.Name)).ToArray(), new(component.PhotoKeys), component.BufferedPhotos.ToArray()));
    }
}
