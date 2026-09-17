using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Turns what RepoDeck knows into one of five plain answers about what it can do.
/// </summary>
/// <remarks>
/// Pure, so the verdict can be tested against real shapes without a network. Two entry
/// points, because the Discover grid and Quick Look know different amounts:
/// <see cref="FromMetadata"/> answers from a search result alone and is capped at
/// <see cref="Confidence.Possible"/>; <see cref="FromPlan"/> answers once a plan exists.
///
/// Nothing here is a safety judgement. The evaluator has no opinion on whether software
/// is trustworthy, it cannot form one, and no caller may present it as though it had.
/// </remarks>
public static class InstallabilityEvaluator
{
    /// <summary>
    /// The answer available from a search result: metadata only, nothing inspected.
    /// </summary>
    public static Installability FromMetadata(
        GitHubRepository repository, ApplicationLikelihood likelihood, SetupAssessment setup)
    {
        var reasons = new List<string>();

        // A library is a library whatever else is true, and saying so early spares the
        // user a details page that only tells them the same thing.
        if (setup.Level == SetupLevel.DeveloperFocused)
        {
            reasons.Add("This looks like a library or framework rather than a program.");
            reasons.AddRange(setup.Reasons);

            return new Installability
            {
                State = InstallabilityState.DeveloperFocused,
                Confidence = Confidence.Possible,
                Reasons = Trim(reasons)
            };
        }

        if (setup.Level == SetupLevel.Advanced)
        {
            reasons.Add("This looks like something that has to be built or configured by hand.");
            reasons.AddRange(setup.Reasons);

            return new Installability
            {
                State = InstallabilityState.DeveloperFocused,
                Confidence = Confidence.Possible,
                Reasons = Trim(reasons)
            };
        }

        // Everything else needs the release list, which a search result does not carry.
        // Claiming "Ready to install" here would be guessing at the one thing the user
        // most wants to rely on.
        reasons.Add("RepoDeck has not looked at this project's downloads yet.");

        if (setup.Level == SetupLevel.SomeSetup)
        {
            reasons.Add("Its description suggests something has to be set up first.");
        }

        if (!likelihood.LooksLikeApplication && likelihood.Reasons.Count > 0)
        {
            reasons.Add("It may not be a program you run: " + likelihood.Reasons[0]);
        }

        if (repository.IsArchived)
        {
            reasons.Add("The authors have stopped maintaining it.");
        }

        return new Installability
        {
            State = InstallabilityState.Unknown,
            Confidence = Confidence.Unknown,
            Reasons = Trim(reasons)
        };
    }

    /// <summary>
    /// The answer once a plan exists. This is the authoritative one, and the only one
    /// that may permit a direct INSTALL action.
    /// </summary>
    public static Installability FromPlan(
        InstallPlan plan, RepositoryAnalysis analysis, ReleaseAnalysis releases, MachineProfile machine)
    {
        var reasons = new List<string>();

        // Deliberately unsupported comes first: a source-build project is developer
        // focused whatever its release list happens to contain.
        if (plan.IsDeliberatelyUnsupported)
        {
            reasons.Add(plan.Strategy == InstallStrategy.SourceBuild
                ? "This would have to be compiled from source, which RepoDeck does not do."
                : "RepoDeck does not know how to install what this project publishes.");

            reasons.AddRange(plan.BlockingIssues);

            return new Installability
            {
                State = InstallabilityState.DeveloperFocused,
                Confidence = Confidence.Confirmed,
                Reasons = Trim(reasons)
            };
        }

        if (plan.CanProceed)
        {
            reasons.Add($"RepoDeck found {plan.AssetName} in release {plan.ReleaseTag ?? "the latest release"}.");

            if (plan.Platform != OsPlatform.Unknown)
            {
                reasons.Add($"It is built for {plan.Platform.DisplayName()}"
                            + (plan.Architecture == CpuArchitecture.Unknown
                                ? "."
                                : $" {plan.Architecture.DisplayName()}."));
            }

            reasons.AddRange(plan.Warnings);

            // A system installer is fetched and handed over, never run. That is not
            // "ready to install" by RepoDeck's own definition of installing.
            var handedOver = plan.Strategy is InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage;

            if (handedOver || plan.RequiresElevation)
            {
                return new Installability
                {
                    State = InstallabilityState.NeedsSetup,
                    Confidence = Confidence.Confirmed,
                    Reasons = Trim(reasons)
                };
            }

            return new Installability
            {
                State = InstallabilityState.ReadyToInstall,
                Confidence = plan.Confidence == Confidence.Unknown ? Confidence.Likely : plan.Confidence,
                Reasons = Trim(reasons)
            };
        }

        // No usable plan. The distinction that matters to the user is between "there is
        // nothing for your computer" and "there is nothing here at all".
        var ruledOut = releases.HasAnyBinary
                       && releases.Recommended is null
                       && releases.SoftwareAssets.All(a => !a.IsUsable);

        if (ruledOut)
        {
            reasons.Add($"This release has {releases.SoftwareAssets.Count} download"
                        + (releases.SoftwareAssets.Count == 1 ? "" : "s")
                        + $", none of them for {machine.Description}.");

            if (releases.NoRecommendationReason is { Length: > 0 } why) reasons.Add(why);

            return new Installability
            {
                State = InstallabilityState.NotCompatible,
                Confidence = Confidence.Confirmed,
                Reasons = Trim(reasons)
            };
        }

        reasons.AddRange(plan.BlockingIssues);
        if (releases.NoRecommendationReason is { Length: > 0 } reason) reasons.Add(reason);

        if (!analysis.IsComplete && analysis.IncompleteReason is { Length: > 0 } incomplete)
        {
            reasons.Add(incomplete);
        }

        return new Installability
        {
            State = InstallabilityState.Unknown,
            Confidence = Confidence.Unknown,
            Reasons = Trim(reasons)
        };
    }

    /// <summary>Keeps the evidence list to something a tooltip can actually hold.</summary>
    private static IReadOnlyList<string> Trim(IEnumerable<string> reasons) =>
        reasons.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
}
