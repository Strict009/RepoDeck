using RepoDeck.Models;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The confirmation step, which is where RepoDeck's whole argument either holds or does
/// not: a numbered list of what will happen and an explicit list of what will not.
/// </summary>
public class InstallConfirmationTests
{
    private static InstallPlan Plan(
        InstallStrategy strategy = InstallStrategy.PortableArchive,
        bool extraction = true,
        long size = 59_484_318) => new()
    {
        Owner = "sharpemu",
        Name = "sharpemu",
        RepositoryUrl = "https://github.com/sharpemu/sharpemu",
        AssetName = "sharpemu-0.0.3-win-x64.zip",
        AssetUrl = "https://example.invalid/sharpemu.zip",
        AssetSize = size,
        ReleaseTag = "v0.0.3",
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        Strategy = strategy,
        RequiresExtraction = extraction,
        ProposedInstallDirectory = @"C:\Users\someone\AppData\Local\RepoDeck\Apps\sharpemu__sharpemu",
        Confidence = Confidence.Likely
    };

    [Fact]
    public void It_states_the_facts_a_person_needs_before_agreeing()
    {
        var confirmation = InstallConfirmation.From(Plan(), "Sharpemu");

        Assert.Equal("Sharpemu", confirmation.Title);
        Assert.Equal("v0.0.3", confirmation.VersionText);
        Assert.Equal("57 MB", confirmation.DownloadSizeText);
        Assert.Equal("sharpemu-0.0.3-win-x64.zip", confirmation.AssetName);
        Assert.Equal("sharpemu/sharpemu", confirmation.SourceText);
    }

    [Fact]
    public void The_destination_is_always_inside_RepoDecks_own_folder()
    {
        var confirmation = InstallConfirmation.From(Plan(), "Sharpemu");

        Assert.Contains("RepoDeck", confirmation.DestinationText, StringComparison.Ordinal);
        Assert.Contains("Apps", confirmation.DestinationText, StringComparison.Ordinal);
    }

    [Fact]
    public void It_lists_what_will_happen_in_order()
    {
        var will = InstallConfirmation.From(Plan(), "Sharpemu").WillDo;

        Assert.True(will.Count >= 4);
        Assert.Contains("Download", will[0], StringComparison.Ordinal);
        Assert.Contains("Check", will[1], StringComparison.Ordinal);
        Assert.Contains("Installed", will[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_steps_say_which_platform_build_is_coming()
    {
        var will = string.Join(" ", InstallConfirmation.From(Plan(), "Sharpemu").WillDo);

        Assert.Contains("Windows", will, StringComparison.Ordinal);
        Assert.Contains("x64", will, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archive_says_it_will_be_unpacked_and_a_single_file_does_not()
    {
        var archive = string.Join(" ", InstallConfirmation.From(Plan(extraction: true), "X").WillDo);
        var single = string.Join(" ", InstallConfirmation.From(Plan(extraction: false), "X").WillDo);

        Assert.Contains("Unpack", archive, StringComparison.Ordinal);
        Assert.DoesNotContain("Unpack", single, StringComparison.Ordinal);
    }

    // ---- The half that matters most ---------------------------------------

    [Fact]
    public void It_rules_out_by_name_the_things_people_are_right_to_worry_about()
    {
        var never = string.Join(" ", InstallConfirmation.From(Plan(), "X").WillNotDo)
            .ToLowerInvariant();

        Assert.Contains("run an installer", never);
        Assert.Contains("administrator", never);
        Assert.Contains("windows setting", never);
        Assert.Contains("until you ask", never);
    }

    [Fact]
    public void The_two_lists_never_contradict_each_other()
    {
        var confirmation = InstallConfirmation.From(Plan(), "X");

        var will = string.Join(" ", confirmation.WillDo).ToLowerInvariant();

        // Nothing in the "will" list may describe running anything.
        Assert.DoesNotContain("run an", will, StringComparison.Ordinal);
        Assert.DoesNotContain("execute", will, StringComparison.Ordinal);
        Assert.DoesNotContain("launch", will, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_in_it_claims_the_software_is_safe()
    {
        var confirmation = InstallConfirmation.From(Plan(), "X");

        var everything = string.Join(" ",
            [.. confirmation.WillDo, .. confirmation.WillNotDo]).ToLowerInvariant();

        foreach (var word in new[] { "safe", "trusted", "verified", "scanned for" })
        {
            Assert.DoesNotContain(word, everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Warnings_from_the_plan_are_shown_rather_than_buried()
    {
        var plan = Plan() with
        {
            Warnings = ["Release v0.0.3 is marked as a pre-release and may be unfinished."]
        };

        var confirmation = InstallConfirmation.From(plan, "X");

        Assert.True(confirmation.HasWarnings);
        Assert.Contains(confirmation.Warnings, w => w.Contains("pre-release", StringComparison.Ordinal));
    }

    [Fact]
    public void A_release_that_did_not_report_a_size_says_nothing_rather_than_zero()
    {
        var confirmation = InstallConfirmation.From(Plan(size: 0), "X");

        Assert.False(confirmation.HasDownloadSize);
        Assert.Equal("", confirmation.DownloadSizeText);
    }
}

/// <summary>
/// The four stages shown while an installation runs.
/// </summary>
public class InstallActivityTests
{
    private static InstallActivityViewModel Activity() => new();

    [Fact]
    public void All_four_stages_are_visible_from_the_start()
    {
        // The whole job at once, rather than one changing word.
        var activity = Activity();

        Assert.Equal(4, activity.Stages.Count);
        Assert.Equal(["DOWNLOAD", "VERIFY", "EXTRACT", "REGISTER"],
            activity.Stages.Select(s => s.Label));
        Assert.All(activity.Stages, s => Assert.True(s.IsWaiting));
    }

    [Fact]
    public void Every_stage_has_a_word_beside_its_light()
    {
        // No state depends on seeing which meter is lit.
        var activity = Activity();

        foreach (var stage in activity.Stages)
        {
            Assert.Equal("WAITING", stage.StateText);
            stage.Begin();
            Assert.Equal("WORKING", stage.StateText);
            stage.Complete();
            Assert.Equal("DONE", stage.StateText);
        }
    }

    [Fact]
    public void Downloading_lights_the_first_stage_and_shows_real_progress()
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress
        {
            Stage = InstallationStage.Downloading,
            Download = new DownloadProgress { BytesReceived = 19_188_183, TotalBytes = 23_488_102 }
        });

        Assert.True(activity.Stages[0].IsActive);
        Assert.Equal(82, activity.Stages[0].Percent);
        Assert.Contains("22.4 MB", activity.BytesText);
        Assert.All(activity.Stages.Skip(1), s => Assert.True(s.IsWaiting));
    }

    [Fact]
    public void Reaching_a_later_stage_marks_the_earlier_ones_done()
    {
        // A report from a later stage is proof the earlier ones finished, and a stage that
        // quietly stayed dark would look like something had gone wrong.
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.Extracting });

        Assert.True(activity.Stages[0].IsDone);
        Assert.True(activity.Stages[1].IsDone);
        Assert.True(activity.Stages[2].IsActive);
        Assert.True(activity.Stages[3].IsWaiting);
    }

    [Fact]
    public void Finishing_completes_everything()
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.Finished });

        Assert.All(activity.Stages, s => Assert.True(s.IsDone));
        Assert.False(activity.HasBytes);
    }

    [Theory]
    [InlineData(InstallationStage.Failed)]
    [InlineData(InstallationStage.Cancelled)]
    public void Failing_marks_nothing_done_that_was_not_done(InstallationStage ending)
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.Extracting });
        activity.Apply(new InstallationProgress { Stage = ending });

        // Download and verify really did finish, so they stay finished.
        Assert.True(activity.Stages[0].IsDone);
        Assert.True(activity.Stages[1].IsDone);

        // Extract did not.
        Assert.False(activity.Stages[2].IsDone);
        Assert.True(activity.Stages[3].IsWaiting);
    }

    [Fact]
    public void A_stage_with_no_real_percentage_does_not_invent_one()
    {
        // Only Download knows how far through it is. The rest report happening and then
        // done, rather than animating through a number RepoDeck does not have.
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.Verifying });

        Assert.True(activity.Stages[1].IsActive);
        Assert.False(activity.HasBytes);
    }

    [Fact]
    public void Locating_the_executable_is_part_of_registering_as_far_as_anybody_watching_is_concerned()
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.LocatingExecutable });

        Assert.True(activity.Stages[3].IsActive);
    }

    [Fact]
    public void Resetting_puts_every_stage_back()
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress { Stage = InstallationStage.Registering });
        activity.Reset();

        Assert.All(activity.Stages, s => Assert.True(s.IsWaiting));
        Assert.All(activity.Stages, s => Assert.Equal(0, s.Percent));
        Assert.False(activity.HasBytes);
    }

    [Fact]
    public void A_download_with_no_reported_total_still_says_how_much_arrived()
    {
        var activity = Activity();

        activity.Apply(new InstallationProgress
        {
            Stage = InstallationStage.Downloading,
            Download = new DownloadProgress { BytesReceived = 1_048_576, TotalBytes = null }
        });

        Assert.True(activity.HasBytes);
        Assert.DoesNotContain("/", activity.BytesText, StringComparison.Ordinal);
    }
}
