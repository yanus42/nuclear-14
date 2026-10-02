using Content.Shared._Forge.Newspapers;

namespace Content.Server._Forge.Newspapers;

[RegisterComponent]
public sealed partial class NewspaperDeskComponent : Component
{
    [DataField] public NewspaperEdition Draft = new();
    [DataField] public Dictionary<int, NewspaperEdition> PublicationDrafts = new();
    [DataField] public int NextPublicationId;
    [DataField] public List<NewspaperEdition> Editions = new();
    [DataField] public Dictionary<int, byte[]> Photos = new();
    [DataField] public List<int> BufferedPhotos = new();
    [DataField] public int NextPhotoId;
    public Dictionary<int, string> PhotoKeys = new();
    [DataField] public List<NewspaperEdition> Templates = new();
}
