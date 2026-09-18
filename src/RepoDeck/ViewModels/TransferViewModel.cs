using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Services.Install;

namespace RepoDeck.ViewModels;

/// <summary>One download or install, as the Downloads page shows it.</summary>
/// <remarks>
/// There is deliberately no Run button here and never will be. The Downloads page exists
/// because RepoDeck fetched something it would not install, and offering to run it would
/// undo the reason the page exists.
/// </remarks>
public sealed partial class TransferViewModel : ViewModelBase
{
    private readonly Action<TransferViewModel> _forget;

    public TransferViewModel(TransferRecord record, Action<TransferViewModel> forget)
    {
        Record = record;
        _forget = forget;
    }

    public TransferRecord Record { get; }

    public string Id => Record.Id;
    public string Name => Record.ApplicationName;

    public string VersionText => Record.Version ?? "";
    public bool HasVersion => VersionText.Length > 0;

    public string AssetText => Record.AssetName ?? "";
    public bool HasAsset => AssetText.Length > 0;

    public bool IsActive => Record.IsActive;
    public bool IsFailed => Record.State == TransferState.Failed;
    public bool IsCompleted => Record.State == TransferState.Completed;
    public bool IsCancelled => Record.State == TransferState.Cancelled;

    /// <summary>A word, always, so no state depends on seeing a colour.</summary>
    public string StateText => Record.State switch
    {
        TransferState.Active => Record.IsUpdate ? "UPDATING" : "DOWNLOADING",
        TransferState.Completed => "DONE",
        TransferState.Failed => "FAILED",
        _ => "STOPPED"
    };

    public string KindText => Record.IsUpdate ? "Update" : "Install";

    /// <summary>0-100 while a size is known; the bar is indeterminate otherwise.</summary>
    public double Percent => Record.Fraction is { } fraction ? fraction * 100 : 0;

    public bool HasKnownSize => Record.Fraction is not null;

    public string BytesText => Record.TotalBytes is > 0
        ? $"{Humanize.FileSizePrecise(Record.BytesReceived)} / "
          + Humanize.FileSizePrecise(Record.TotalBytes.Value)
        : Record.BytesReceived > 0
            ? Humanize.FileSizePrecise(Record.BytesReceived)
            : "";

    public bool HasBytes => BytesText.Length > 0;

    public string Message => Record.Message ?? "";
    public bool HasMessage => Message.Length > 0;

    public string WhenText => Record.EndedAt is { } ended
        ? Humanize.RelativeTime(ended)
        : Humanize.RelativeTime(Record.StartedAt);

    /// <summary>Only a finished record can be forgotten; an active one is still using the network.</summary>
    public bool CanForget => Record.HasFinished;

    [RelayCommand]
    private void Forget() => _forget(this);
}
