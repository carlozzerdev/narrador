using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Narrador.WinForms;

public interface IVideoFileSharer
{
    Task ShareAsync(IWin32Window owner, string path);
}

public sealed class WindowsVideoFileSharer : IVideoFileSharer, IDisposable
{
    private DataTransferManager? _manager;
    private nint _window;
    private StorageFile? _file;

    public async Task ShareAsync(IWin32Window owner, string path)
    {
        _file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var window = owner is Control control ? control.FindForm()?.Handle ?? owner.Handle : owner.Handle;
        if (_manager is null || window != _window)
        {
            Dispose();
            _window = window;
            _manager = DataTransferManagerInterop.GetForWindow(window);
            _manager.DataRequested += OnDataRequested;
        }
        DataTransferManagerInterop.ShowShareUIForWindow(window);
    }

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        if (_file is null) { args.Request.FailWithDisplayText("El vídeo ya no está disponible."); return; }
        args.Request.Data.Properties.Title = "Compartir vídeo · " + _file.Name;
        args.Request.Data.SetStorageItems(new IStorageItem[] { _file });
        args.Request.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    public void Dispose()
    {
        if (_manager is not null) _manager.DataRequested -= OnDataRequested;
        _manager = null;
    }
}
