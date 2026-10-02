using Content.Shared._Forge.Newspapers;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Newspapers;

public sealed class NewspaperDeskBoundUserInterface : BoundUserInterface
{
    private NewspaperDeskWindow? _window;

    public NewspaperDeskBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NewspaperDeskWindow>();
        _window.OnSaveTemplate += template => SendMessage(new NewspaperSaveTemplateMessage(template));
        _window.OnSwitchPublication += (draft, id, name) => SendMessage(new NewspaperSwitchPublicationMessage(draft, id, name));
        _window.OnSave += draft => SendMessage(new NewspaperSaveDraftMessage(draft));
        _window.OnPublish += draft => SendMessage(new NewspaperPublishMessage(draft));
        _window.OnPrint += (count, edition) => SendMessage(new NewspaperPrintMessage(count, edition));
        _window.OnImageRequested += edition => SendMessage(new NewspaperImageRequestMessage(edition));
        _window.OnRemovePhoto += () => SendMessage(new NewspaperRemoveImageMessage());
        _window.OnSelectPhoto += id => SendMessage(new NewspaperSelectImageMessage(id));
        _window.OnForgetPhoto += id => SendMessage(new NewspaperForgetImageMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NewspaperDeskUiState deskState)
            _window?.UpdateState(deskState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is NewspaperImageMessage image)
            _window?.ReceivePhoto(image.PhotoId, image.Data, image.PhotoKey);
    }
}
