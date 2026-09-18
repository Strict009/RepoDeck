using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// The four stages of an installation, shown as a stack of meters.
/// </summary>
/// <remarks>
/// A single bar reading "Installing..." tells somebody nothing about what is happening to
/// their computer. Four named stages tell them RepoDeck downloads, checks what it
/// downloaded, unpacks it and writes it down - which is the whole argument for trusting
/// the thing, made visible while it happens rather than claimed in a paragraph.
///
/// The stages mirror <see cref="InstallationStage"/> but collapse the ones a person does
/// not need separated: locating the executable is part of registering as far as anybody
/// watching is concerned.
/// </remarks>
public sealed partial class InstallActivityViewModel : ViewModelBase
{
    public InstallActivityViewModel()
    {
        Stages =
        [
            new InstallStageViewModel(InstallationStage.Downloading, "DOWNLOAD",
                "Fetching the release file from GitHub."),
            new InstallStageViewModel(InstallationStage.Verifying, "VERIFY",
                "Checking the file arrived complete and is what it claimed to be."),
            new InstallStageViewModel(InstallationStage.Extracting, "EXTRACT",
                "Unpacking it into RepoDeck's own folder. Nothing is run."),
            new InstallStageViewModel(InstallationStage.Registering, "REGISTER",
                "Finding the program and adding it to your Installed list.")
        ];
    }

    public ObservableCollection<InstallStageViewModel> Stages { get; }

    /// <summary>The current stage in words, for anything that wants one line.</summary>
    [ObservableProperty] private string _summary = "";

    /// <summary>"18.3 MB / 22.4 MB" while downloading, otherwise empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBytes))]
    private string _bytesText = "";

    public bool HasBytes => BytesText.Length > 0;

    public void Reset()
    {
        foreach (var stage in Stages) stage.Reset();

        Summary = "";
        BytesText = "";
    }

    /// <summary>
    /// Applies a progress report. Every stage before the current one is marked done,
    /// because a report from a later stage is proof the earlier ones finished - and a
    /// stage that quietly stayed dark would look like something had gone wrong.
    /// </summary>
    public void Apply(InstallationProgress progress)
    {
        Summary = progress.Describe();

        var current = Map(progress.Stage);

        if (progress.Stage is InstallationStage.Finished)
        {
            foreach (var stage in Stages) stage.Complete();
            BytesText = "";
            return;
        }

        if (progress.Stage is InstallationStage.Failed or InstallationStage.Cancelled)
        {
            // Whatever was running stops where it stopped. Nothing is marked done that
            // was not done, and nothing is marked failed that did finish.
            foreach (var stage in Stages.Where(s => s.IsActive)) stage.Reset();
            BytesText = "";
            return;
        }

        if (current is null) return;

        var index = Stages.IndexOf(current);

        for (var i = 0; i < Stages.Count; i++)
        {
            if (i < index) Stages[i].Complete();
            else if (i == index) Stages[i].Begin();
        }

        if (progress.Stage == InstallationStage.Downloading && progress.Download is { } download)
        {
            current.Percent = download.Percent ?? 0;

            BytesText = download.TotalBytes is > 0
                ? $"{Humanize.FileSizePrecise(download.BytesReceived)} / {Humanize.FileSizePrecise(download.TotalBytes.Value)}"
                : Humanize.FileSizePrecise(download.BytesReceived);
        }
        else
        {
            // No real percentage to show, so the meter reads "happening" rather than a
            // number RepoDeck would be making up.
            current.Percent = 100;
            BytesText = "";
        }

        if (progress.Detail is { Length: > 0 } detail) current.Detail = detail;
    }

    private InstallStageViewModel? Map(InstallationStage stage) => stage switch
    {
        InstallationStage.Preparing or InstallationStage.Downloading => Stages[0],
        InstallationStage.Verifying => Stages[1],
        InstallationStage.Extracting => Stages[2],
        InstallationStage.LocatingExecutable or InstallationStage.Registering => Stages[3],
        _ => null
    };
}
