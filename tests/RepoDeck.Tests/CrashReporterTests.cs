using RepoDeck.Infrastructure;

namespace RepoDeck.Tests;

/// <summary>
/// What RepoDeck says when it cannot start.
/// </summary>
/// <remarks>
/// This is the machine RepoDeck has never run on. Before this existed, a failure during
/// startup produced nothing at all: the log is written by a service built after Avalonia
/// starts, so anything that went wrong first vanished without a trace and the user was left
/// with a shortcut that appeared to do nothing.
///
/// A crash report is the only evidence an alpha tester can send, so these check it is
/// readable, that it says what the failure probably means where that is recognisable, and
/// that it carries nothing that should not leave somebody's machine.
/// </remarks>
public class CrashReporterTests
{
    [Fact]
    public void The_report_leads_with_what_happened()
    {
        var report = CrashReporter.Compose(new InvalidOperationException("it broke"), "A problem");

        Assert.Contains("RepoDeck crash report", report);
        Assert.Contains("A problem stopped RepoDeck.", report);
        Assert.Contains("it broke", report);
    }

    [Fact]
    public void The_report_names_the_machine_it_failed_on()
    {
        // Most first-run failures are explained by these four lines rather than by the
        // stack trace underneath them.
        var report = CrashReporter.Compose(new Exception("x"), "A problem");

        Assert.Contains("RepoDeck:", report);
        Assert.Contains("OS:", report);
        Assert.Contains("Process:", report);
        Assert.Contains(".NET:", report);
    }

    [Fact]
    public void The_report_carries_the_version_so_a_tester_can_name_their_build()
    {
        var report = CrashReporter.Compose(new Exception("x"), "A problem");

        Assert.Contains(AppVersion.Current, report);
    }

    [Fact]
    public void Inner_exceptions_are_included_because_the_real_cause_is_usually_inside()
    {
        var exception = new InvalidOperationException(
            "outer", new DllNotFoundException("libSkiaSharp"));

        var report = CrashReporter.Compose(exception, "A problem");

        Assert.Contains("outer", report);
        Assert.Contains("Caused by:", report);
        Assert.Contains("libSkiaSharp", report);
    }

    [Fact]
    public void A_cycle_of_inner_exceptions_cannot_run_away()
    {
        // Depth-bounded rather than trusting the chain to end.
        var exception = new Exception("a", new Exception("b", new Exception("c")));

        var report = CrashReporter.Compose(exception, "A problem");

        Assert.Contains("c", report);
    }

    [Fact]
    public void It_says_where_to_send_the_report()
    {
        var report = CrashReporter.Compose(new Exception("x"), "A problem");

        Assert.Contains("github.com/Strict009/RepoDeck/issues", report);
    }

    // ---- The failures a clean machine actually produces --------------------

    [Fact]
    public void A_missing_native_library_is_explained_in_words()
    {
        // The Dave-from-Ohio case: the installation is incomplete or an antivirus ate
        // something. Telling him to reinstall is more useful than a stack trace.
        var hint = CrashReporter.Diagnose(new DllNotFoundException("VCRUNTIME140.dll"));

        Assert.NotNull(hint);
        Assert.Contains("libraries it ships with", hint);
        Assert.Contains("Reinstalling", hint);
    }

    [Fact]
    public void A_missing_entry_point_is_treated_the_same_way()
    {
        Assert.NotNull(CrashReporter.Diagnose(new EntryPointNotFoundException("nope")));
    }

    [Fact]
    public void An_architecture_mismatch_says_which_build_this_is()
    {
        var hint = CrashReporter.Diagnose(new BadImageFormatException("wrong arch"));

        Assert.NotNull(hint);
        Assert.Contains("64-bit", hint);
    }

    [Fact]
    public void Being_refused_access_points_at_the_usual_culprits()
    {
        var hint = CrashReporter.Diagnose(new UnauthorizedAccessException("denied"));

        Assert.NotNull(hint);
        Assert.Contains("security software", hint);
    }

    [Fact]
    public void A_full_disk_is_named_as_a_full_disk()
    {
        var hint = CrashReporter.Diagnose(
            new IOException("There is not enough space on the disk."));

        Assert.NotNull(hint);
        Assert.Contains("full", hint);
    }

    [Fact]
    public void A_cause_buried_inside_is_still_recognised()
    {
        // Avalonia wraps startup failures, so the useful exception is rarely the outer one.
        var exception = new InvalidOperationException(
            "Unable to initialize the rendering subsystem",
            new DllNotFoundException("libSkiaSharp"));

        Assert.NotNull(CrashReporter.Diagnose(exception));
    }

    [Fact]
    public void An_unfamiliar_failure_gets_no_invented_explanation()
    {
        // Guessing here would be worse than silence: a confident wrong explanation sends
        // somebody off to fix the wrong thing.
        Assert.Null(CrashReporter.Diagnose(new InvalidOperationException("something odd")));
    }

    [Fact]
    public void An_unfamiliar_failure_still_produces_a_readable_report()
    {
        var report = CrashReporter.Compose(new InvalidOperationException("something odd"), "A problem");

        Assert.Contains("something odd", report);
        Assert.DoesNotContain("What this probably means", report);
    }

    [Fact]
    public void The_report_promises_only_what_it_delivers()
    {
        // It says it contains no credentials. That has to stay true as fields are added.
        var report = CrashReporter.Compose(new Exception("x"), "A problem");

        Assert.Contains("no tokens, credentials or personal data", report);
        Assert.DoesNotContain("REPODECK_GITHUB_TOKEN", report);
        Assert.DoesNotContain("gho_", report);
    }
}
