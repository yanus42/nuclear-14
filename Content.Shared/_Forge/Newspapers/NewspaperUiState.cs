using Robust.Shared.Serialization;
using Content.Shared._Forge.Paper;

namespace Content.Shared._Forge.Newspapers;

[Serializable, NetSerializable]
public enum NewspaperDeskUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum NewspaperCopyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class NewspaperPublicationInfo
{
    public readonly int Id;
    public readonly string Name;
    public NewspaperPublicationInfo(int id, string name) { Id = id; Name = name; }
}

[Serializable, NetSerializable]
public sealed class NewspaperDeskUiState : BoundUserInterfaceState
{
    public readonly NewspaperEdition Draft;
    public readonly NewspaperEdition[] Editions;
    public readonly int PaperCount;
    public readonly NewspaperEdition[] Templates;
    public readonly NewspaperPublicationInfo[] Publications;
    public readonly Dictionary<int, string> PhotoKeys;
    public readonly int[] BufferedPhotos;

    public NewspaperDeskUiState(NewspaperEdition draft, NewspaperEdition[] editions, int paperCount, NewspaperEdition[] templates, NewspaperPublicationInfo[] publications, Dictionary<int, string>? photoKeys = null, int[]? bufferedPhotos = null)
    {
        Draft = draft;
        Editions = editions;
        PaperCount = paperCount;
        Templates = templates;
        Publications = publications;
        PhotoKeys = photoKeys ?? new();
        BufferedPhotos = bufferedPhotos ?? Array.Empty<int>();
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperCopyUiState : BoundUserInterfaceState
{
    public readonly NewspaperEdition Edition;

    public readonly PaperSurfaceAppearance? Surface;
    public readonly string PhotoKey;
    public NewspaperCopyUiState(NewspaperEdition edition, PaperSurfaceAppearance? surface = null, string photoKey = "")
    {
        Edition = edition;
        Surface = surface;
        PhotoKey = photoKey;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperSaveDraftMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;

    public NewspaperSaveDraftMessage(NewspaperEdition draft)
    {
        Draft = draft;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperPublishMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;

    public NewspaperPublishMessage(NewspaperEdition draft)
    {
        Draft = draft;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperPrintMessage : BoundUserInterfaceMessage
{
    public readonly int Count;
    public readonly int Edition;

    public NewspaperPrintMessage(int count, int edition)
    {
        Count = count;
        Edition = edition;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperImageRequestMessage : BoundUserInterfaceMessage
{
    public readonly int Edition;
    public readonly int PhotoId;
    public NewspaperImageRequestMessage(int edition, int photoId = -1) { Edition = edition; PhotoId = photoId; }
}

[Serializable, NetSerializable]
public sealed class NewspaperImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public readonly byte[] Data;
    public readonly string PhotoKey;
    public NewspaperImageMessage(int photoId, byte[] data, string photoKey = "")
    {
        PhotoId = photoId;
        Data = data;
        PhotoKey = photoKey;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperRemoveImageMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class NewspaperSelectImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public NewspaperSelectImageMessage(int photoId) => PhotoId = photoId;
}

[Serializable, NetSerializable]
public sealed class NewspaperForgetImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public NewspaperForgetImageMessage(int photoId) => PhotoId = photoId;
}

[Serializable, NetSerializable]
public sealed class NewspaperSaveTemplateMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Template;
    public NewspaperSaveTemplateMessage(NewspaperEdition template) => Template = template;
}

[Serializable, NetSerializable]
public sealed class NewspaperSwitchPublicationMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;
    public readonly int PublicationId;
    // Zero ID creates a new publication with this name.
    public readonly string Name;
    public NewspaperSwitchPublicationMessage(NewspaperEdition draft, int publicationId, string name = "")
    { Draft = draft; PublicationId = publicationId; Name = name; }
}
