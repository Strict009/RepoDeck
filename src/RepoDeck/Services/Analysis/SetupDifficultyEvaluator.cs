using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Works out how much effort stands between a person and using something.
/// </summary>
/// <remarks>
/// Pure, and deliberately blind to popularity. Two entry points because the two callers
/// can afford very different amounts of evidence: a search result gets metadata only,
/// while an opened project has been analysed properly. The cheap one is never allowed to
/// claim more confidence than it has earned.
/// </remarks>
public static class SetupDifficultyEvaluator
{
    /// <summary>
    /// The authoritative assessment, once the repository has been analysed and a plan built.
    /// </summary>
    public static SetupAssessment Evaluate(
        RepositoryAnalysis analysis, ReleaseAnalysis releases, InstallPlan plan)
    {
        var reasons = new List<string>();

        if (!analysis.IsComplete)
        {
            return new SetupAssessment
            {
                Level = SetupLevel.Unknown,
                Confidence = Confidence.Unknown,
                Reasons = ["RepoDeck could not finish examining this project."]
            };
        }

        // A library is not a program, however easy it would be to download.
        if (analysis.ApplicationType is ApplicationType.Library or ApplicationType.Framework)
        {
            reasons.Add($"RepoDeck thinks this is a {analysis.ApplicationType.ToDisplayString().ToLowerInvariant()} "
                        + "for building other software with, not a program you run.");

            return new SetupAssessment
            {
                Level = SetupLevel.DeveloperFocused,
                Confidence = analysis.ApplicationTypeConfidence,
                Reasons = reasons
            };
        }

        if (plan.CanProceed)
        {
            switch (plan.Strategy)
            {
                case InstallStrategy.PortableArchive:
                case InstallStrategy.StandaloneExecutable:
                case InstallStrategy.LinuxAppImage:
                    reasons.Add("A ready-made build for your computer is published.");
                    reasons.Add("RepoDeck can install it and start it for you.");

                    return new SetupAssessment
                    {
                        Level = SetupLevel.Easy,
                        Confidence = Confidence.Likely,
                        Reasons = reasons
                    };

                case InstallStrategy.WindowsInstaller:
                case InstallStrategy.LinuxPackage:
                    reasons.Add("A ready-made download is published for your computer.");
                    reasons.Add("It is an installer, so you run it yourself once RepoDeck has fetched it.");

                    return new SetupAssessment
                    {
                        Level = SetupLevel.SomeSetup,
                        Confidence = Confidence.Likely,
                        Reasons = reasons
                    };
            }
        }

        // No usable download. How hard it then is depends on what it is built with.
        if (!releases.HasRelease)
        {
            reasons.Add("Nothing ready-made is published at all.");
        }
        else if (!releases.HasAnyBinary)
        {
            reasons.Add("The published files are source code rather than a built program.");
        }
        else
        {
            reasons.Add("Nothing published suits your computer.");
        }

        return AssessFromProjectType(analysis, reasons);
    }

    private static SetupAssessment AssessFromProjectType(
        RepositoryAnalysis analysis, List<string> reasons)
    {
        // Interpreted languages need a runtime installed but not usually a compiler.
        if (analysis.PrimaryProjectType is ProjectType.Python or ProjectType.Node)
        {
            var runtime = analysis.PrimaryProjectType == ProjectType.Python ? "Python" : "Node.js";
            reasons.Add($"It needs {runtime} installed on your computer to run.");

            return new SetupAssessment
            {
                Level = SetupLevel.SomeSetup,
                Confidence = Confidence.Possible,
                Reasons = reasons
            };
        }

        if (analysis.PrimaryProjectType is ProjectType.Shell)
        {
            reasons.Add("It is a set of scripts, which you run yourself from a terminal.");

            return new SetupAssessment
            {
                Level = SetupLevel.SomeSetup,
                Confidence = Confidence.Possible,
                Reasons = reasons
            };
        }

        if (analysis.PrimaryProjectType is ProjectType.Unknown)
        {
            reasons.Add("RepoDeck could not tell what this is built with.");

            return new SetupAssessment
            {
                Level = SetupLevel.Unknown,
                Confidence = Confidence.Unknown,
                Reasons = reasons
            };
        }

        reasons.Add($"Using it would mean compiling it yourself with "
                    + $"{analysis.PrimaryProjectType.ToDisplayString()} tools.");

        return new SetupAssessment
        {
            Level = SetupLevel.Advanced,
            Confidence = Confidence.Likely,
            Reasons = reasons
        };
    }

    /// <summary>
    /// The cheap assessment for a search result, from repository metadata alone.
    /// </summary>
    /// <remarks>
    /// Search results carry no release or file information, so this can only ever be a
    /// provisional hint. Confidence is capped at Possible and the UI says the real answer
    /// comes from opening the project.
    /// </remarks>
    public static SetupAssessment EvaluateFromMetadata(
        GitHubRepository repository, ApplicationLikelihood likelihood)
    {
        var reasons = new List<string>();

        if (!likelihood.LooksLikeApplication && likelihood.Score < 30)
        {
            reasons.Add("Its description and tags suggest a developer resource rather than a program.");

            return new SetupAssessment
            {
                Level = SetupLevel.DeveloperFocused,
                Confidence = Confidence.Possible,
                Reasons = reasons
            };
        }

        if (repository.Language is { Length: > 0 } language
            && language is "Python" or "JavaScript" or "TypeScript" or "Ruby" or "PHP" or "Perl")
        {
            reasons.Add($"Written in {language}, which usually needs its runtime installed first.");

            return new SetupAssessment
            {
                Level = SetupLevel.SomeSetup,
                Confidence = Confidence.Possible,
                Reasons = reasons
            };
        }

        // Anything else is genuinely unknown until the releases have been examined.
        reasons.Add("RepoDeck checks properly when you open it.");

        return new SetupAssessment
        {
            Level = SetupLevel.Unknown,
            Confidence = Confidence.Unknown,
            Reasons = reasons
        };
    }
}
