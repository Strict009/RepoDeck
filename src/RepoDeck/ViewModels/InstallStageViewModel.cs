using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>One line of the installation activity display.</summary>
/// <remarks>
/// The four stages are shown all at once, from the moment installing begins, so the user
/// can see the whole job rather than one changing word. A stage that has not started is
/// dark, the one in progress is lit and filling, and a finished one is full.
///
/// Only Download has a real percentage. The other three report that they are happening and
/// then that they are done, so their meters fill to completion on arrival rather than
/// animating through a number RepoDeck does not have. A meter pretending to know how far
/// through an extraction it is would be inventing the one thing progress is for.
/// </remarks>
public sealed partial class InstallStageViewModel : ViewModelBase
{
    public InstallStageViewModel(InstallationStage stage, string label, string description)
    {
        Stage = stage;
        Label = label;
        Description = description;
    }

    public InstallationStage Stage { get; }

    /// <summary>DOWNLOAD, VERIFY, EXTRACT, REGISTER.</summary>
    public string Label { get; }

    /// <summary>What this stage actually does, for a tooltip.</summary>
    public string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private InstallStageState _state = InstallStageState.Waiting;

    public bool IsWaiting => State == InstallStageState.Waiting;
    public bool IsActive => State == InstallStageState.Running;
    public bool IsDone => State == InstallStageState.Done;

    /// <summary>
    /// A word beside the light, so the state never depends on seeing which meter is lit.
    /// </summary>
    public string StateText => State switch
    {
        InstallStageState.Running => "WORKING",
        InstallStageState.Done => "DONE",
        _ => "WAITING"
    };

    /// <summary>0 to 100. Only Download ever reports anything between the two ends.</summary>
    [ObservableProperty] private double _percent;

    /// <summary>"18.3 MB / 22.4 MB", when there is something real to say.</summary>
    [ObservableProperty] private string _detail = "";

    public void Reset()
    {
        State = InstallStageState.Waiting;
        Percent = 0;
        Detail = "";
    }

    public void Begin()
    {
        if (State == InstallStageState.Done) return;

        State = InstallStageState.Running;
    }

    public void Complete()
    {
        State = InstallStageState.Done;
        Percent = 100;
    }
}

public enum InstallStageState
{
    Waiting,
    Running,
    Done
}
