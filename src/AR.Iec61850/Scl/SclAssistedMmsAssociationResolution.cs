using System.Globalization;
using AR.Iec61850.Acse;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Scl;

public enum SclAssociationFieldState
{
    Unspecified,
    PresentValid,
    Invalid,
    Ambiguous
}

public enum SclAssociationCandidateSource
{
    ExactScl,
    EngineProfileTemplate,
    EngineRawCompatibility
}

public sealed class SclAssociationFieldEvidence
{
    public string Name { get; init; } = string.Empty;
    public SclAssociationFieldState State { get; init; }
    public string DeclaredText { get; init; } = string.Empty;
}

public sealed class SclAssistedMmsAssociationCandidate
{
    public string Name { get; init; } = string.Empty;
    public SclAssociationCandidateSource Source { get; init; }
    public CotpConnectParameters Cotp { get; init; } = new();
    public AcseAssociationProfile AssociationProfile { get; init; } =
        new("invalid", "invalid", Array.Empty<byte>());
    public IReadOnlyList<string> ResolutionNotes { get; init; } = Array.Empty<string>();
}

public sealed class SclAssistedMmsAssociationResolution
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public IReadOnlyList<SclAssociationFieldEvidence> Fields { get; init; } =
        Array.Empty<SclAssociationFieldEvidence>();
    public IReadOnlyList<SclAssistedMmsAssociationCandidate> Candidates { get; init; } =
        Array.Empty<SclAssistedMmsAssociationCandidate>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsSuccess =>
        Errors.Count == 0 &&
        !string.IsNullOrWhiteSpace(Host) &&
        Port is >= 1 and <= 65535 &&
        Candidates.Count > 0;
}

/// <summary>
/// Builds a small deterministic set of SCL-constrained association candidates.
/// Explicit valid SCL values are immutable constraints. Only values that are truly
/// unspecified may be supplied by engine-owned interoperability profiles. Malformed
/// or conflicting declarations fail closed before any socket is opened.
/// </summary>
public static class SclAssistedMmsAssociationCandidateResolver
{
    public const int MaximumCandidateCount = 8;

    public static SclAssistedMmsAssociationResolution Resolve(
        SclMmsAccessPoint remote,
        MmsLocalAssociationProfile exactLocalProfile,
        int port = 102)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(exactLocalProfile);

        var errors = new List<string>();
        var warnings = new List<string>();
        var host = (remote.Endpoint.IpAddress ?? string.Empty).Trim();
        var normalizedPort = port <= 0 ? 102 : port;

        if (string.IsNullOrWhiteSpace(host))
            errors.Add("SCL-assisted association requires a resolved TCP endpoint.");
        if (normalizedPort is < 1 or > 65535)
            errors.Add($"TCP port must be in 1..65535; received {normalizedPort}.");

        var tsel = ParseSelector(remote, "OSI-TSEL", remote.Association.TransportSelector, errors);
        var ssel = ParseSelector(remote, "OSI-SSEL", remote.Association.SessionSelector, errors);
        var psel = ParseSelector(remote, "OSI-PSEL", remote.Association.PresentationSelector, errors);
        var apTitle = ParseApTitle(remote, remote.Association.ApTitle, errors);
        var aeQualifier = ParseAeQualifier(remote, errors);

        var fields = new[]
        {
            tsel.Evidence,
            ssel.Evidence,
            psel.Evidence,
            apTitle.Evidence,
            aeQualifier.Evidence
        };

        if (errors.Count > 0)
            return Build(remote, normalizedPort, fields, Array.Empty<SclAssistedMmsAssociationCandidate>(), errors, warnings);

        var missing = fields
            .Where(field => field.State == SclAssociationFieldState.Unspecified)
            .Select(field => field.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            warnings.Add(
                $"SCL ConnectedAP leaves {string.Join(", ", missing)} unspecified; " +
                "only those fields may be resolved from the bounded engine interoperability profiles.");
        }

        var candidates = new List<SclAssistedMmsAssociationCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Complete SCL remains the highest-authority, byte-stable path.
        var exact = SclAssistedMmsAssociationPlanBuilder.BuildExact(remote, exactLocalProfile);
        if (exact.IsSuccess && exact.Plan is not null)
        {
            AddCandidate(
                candidates,
                seen,
                FromPlan(
                    exact.Plan,
                    "SCL exact",
                    SclAssociationCandidateSource.ExactScl,
                    Array.Empty<string>()));
        }

        var defaultCotp = new CotpConnectParameters();
        var engineProfiles = AcseMmsInitiateRequest.BuildAssociationProfiles();
        var qualifiedTemplate = engineProfiles
            .Select(profile => new
            {
                Profile = profile,
                Evidence = AcseAssociationRequestIdentityReader.Read(
                    profile.Payload,
                    defaultCotp.DestinationTsap)
            })
            .FirstOrDefault(item =>
                item.Evidence.HasQualifiedCalledApplicationIdentity &&
                item.Evidence.CalledPresentationSelector.Length > 0 &&
                item.Evidence.CalledSessionSelector.Length > 0);

        foreach (var profile in engineProfiles)
        {
            if (candidates.Count >= MaximumCandidateCount)
                break;

            var evidence = AcseAssociationRequestIdentityReader.Read(
                profile.Payload,
                defaultCotp.DestinationTsap);

            // Raw profile attempts reproduce the native discovery association bytes. They
            // are admitted only when every explicit SCL field embedded in that payload
            // agrees exactly. TSEL is carried by COTP and can therefore remain SCL-exact.
            if (RawProfileSatisfiesConstraints(ssel, psel, apTitle, aeQualifier, evidence))
            {
                var destinationTsel = tsel.Value ?? defaultCotp.DestinationTsap;
                var notes = BuildRawResolutionNotes(
                    profile.Name,
                    tsel,
                    ssel,
                    psel,
                    apTitle,
                    aeQualifier,
                    evidence);

                AddCandidate(
                    candidates,
                    seen,
                    new SclAssistedMmsAssociationCandidate
                    {
                        Name = $"Engine raw profile: {profile.Name}",
                        Source = SclAssociationCandidateSource.EngineRawCompatibility,
                        Cotp = new CotpConnectParameters
                        {
                            SourceTsap = defaultCotp.SourceTsap.ToArray(),
                            DestinationTsap = destinationTsel.ToArray(),
                            TpduSizeExponent = defaultCotp.TpduSizeExponent
                        },
                        AssociationProfile = new AcseAssociationProfile(
                            profile.Name,
                            profile.Description,
                            profile.Payload.ToArray()),
                        ResolutionNotes = notes
                    });
            }
        }

        // A qualified engine profile can also act as one coherent missing-field template.
        // This is not a Cartesian product: all missing remote values come from the same
        // engine profile while every explicit SCL value is overlaid unchanged.
        if (qualifiedTemplate is not null && candidates.Count < MaximumCandidateCount)
        {
            var template = qualifiedTemplate.Evidence;
            var effectiveRemote = BuildEffectiveRemote(
                remote,
                tsel,
                ssel,
                psel,
                apTitle,
                aeQualifier,
                template,
                defaultCotp);

            foreach (var local in DistinctLocalProfiles(exactLocalProfile))
            {
                if (candidates.Count >= MaximumCandidateCount)
                    break;

                var planned = SclAssistedMmsAssociationPlanBuilder.BuildExact(effectiveRemote, local);
                if (!planned.IsSuccess || planned.Plan is null)
                    continue;

                AddCandidate(
                    candidates,
                    seen,
                    FromPlan(
                        planned.Plan,
                        $"SCL constrained: {qualifiedTemplate.Profile.Name}/{local.Name}",
                        SclAssociationCandidateSource.EngineProfileTemplate,
                        BuildTypedResolutionNotes(
                            qualifiedTemplate.Profile.Name,
                            tsel,
                            ssel,
                            psel,
                            apTitle,
                            aeQualifier)));
            }
        }

        if (candidates.Count == 0)
        {
            errors.Add(
                "No bounded engine association candidate satisfies the explicit SCL constraints. " +
                "The SCL was not altered and no socket should be opened.");
        }

        return Build(remote, normalizedPort, fields, candidates, errors, warnings);
    }

    public static SclAssistedMmsAssociationResolution FromExactPlan(
        SclAssistedMmsAssociationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new SclAssistedMmsAssociationResolution
        {
            Host = plan.Host,
            Port = plan.Port,
            IedName = plan.IedName,
            AccessPointName = plan.AccessPointName,
            Candidates =
            [
                FromPlan(
                    plan,
                    "SCL exact",
                    SclAssociationCandidateSource.ExactScl,
                    Array.Empty<string>())
            ]
        };
    }

    private static IEnumerable<MmsLocalAssociationProfile> DistinctLocalProfiles(
        MmsLocalAssociationProfile primary)
    {
        yield return primary;

        var fallback = MmsLocalAssociationProfile.SclInteroperabilityDefault;
        if (!SameLocalProfile(primary, fallback))
            yield return fallback;
    }

    private static bool SameLocalProfile(
        MmsLocalAssociationProfile left,
        MmsLocalAssociationProfile right)
        => left.TransportSelector.SequenceEqual(right.TransportSelector) &&
           left.SessionSelector.SequenceEqual(right.SessionSelector) &&
           left.PresentationSelector.SequenceEqual(right.PresentationSelector) &&
           left.ApTitle.SequenceEqual(right.ApTitle) &&
           left.AeQualifier == right.AeQualifier &&
           left.TpduSizeExponent == right.TpduSizeExponent &&
           left.Initiate.LocalDetailCalling == right.Initiate.LocalDetailCalling &&
           left.Initiate.MaxOutstandingCalling == right.Initiate.MaxOutstandingCalling &&
           left.Initiate.MaxOutstandingCalled == right.Initiate.MaxOutstandingCalled &&
           left.Initiate.NestingLevel == right.Initiate.NestingLevel;

    private static SclMmsAccessPoint BuildEffectiveRemote(
        SclMmsAccessPoint source,
        SelectorConstraint tsel,
        SelectorConstraint ssel,
        SelectorConstraint psel,
        ApTitleConstraint apTitle,
        AeQualifierConstraint aeQualifier,
        AcseAssociationRequestIdentityEvidence template,
        CotpConnectParameters defaultCotp)
        => new()
        {
            IedName = source.IedName,
            AccessPointName = source.AccessPointName,
            SubNetworkName = source.SubNetworkName,
            SubNetworkType = source.SubNetworkType,
            Endpoint = source.Endpoint,
            Parameters = source.Parameters,
            AmbiguousParameters = source.AmbiguousParameters,
            Association = new SclIsoAssociationAddress
            {
                TransportSelector = ToHex(tsel.Value ?? defaultCotp.DestinationTsap),
                SessionSelector = ToHex(ssel.Value ?? template.CalledSessionSelector),
                PresentationSelector = ToHex(psel.Value ?? template.CalledPresentationSelector),
                ApTitle = apTitle.Value is not null
                    ? string.Join(",", apTitle.Value)
                    : template.CalledApTitleText,
                AeQualifier = aeQualifier.Value ?? template.CalledAeQualifier,
                AeQualifierText = (aeQualifier.Value ?? template.CalledAeQualifier)?
                    .ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }
        };

    private static bool RawProfileSatisfiesConstraints(
        SelectorConstraint ssel,
        SelectorConstraint psel,
        ApTitleConstraint apTitle,
        AeQualifierConstraint aeQualifier,
        AcseAssociationRequestIdentityEvidence candidate)
        => Matches(ssel, candidate.CalledSessionSelector) &&
           Matches(psel, candidate.CalledPresentationSelector) &&
           Matches(apTitle, candidate.CalledApTitle) &&
           Matches(aeQualifier, candidate.CalledAeQualifier);

    private static bool Matches(SelectorConstraint constraint, byte[] candidate)
        => constraint.State == SclAssociationFieldState.Unspecified ||
           (constraint.State == SclAssociationFieldState.PresentValid &&
            constraint.Value is not null &&
            candidate.Length > 0 &&
            constraint.Value.SequenceEqual(candidate));

    private static bool Matches(ApTitleConstraint constraint, uint[] candidate)
        => constraint.State == SclAssociationFieldState.Unspecified ||
           (constraint.State == SclAssociationFieldState.PresentValid &&
            constraint.Value is not null &&
            candidate.Length >= 2 &&
            constraint.Value.SequenceEqual(candidate));

    private static bool Matches(AeQualifierConstraint constraint, int? candidate)
        => constraint.State == SclAssociationFieldState.Unspecified ||
           (constraint.State == SclAssociationFieldState.PresentValid &&
            constraint.Value.HasValue &&
            candidate == constraint.Value);

    private static IReadOnlyList<string> BuildRawResolutionNotes(
        string profileName,
        SelectorConstraint tsel,
        SelectorConstraint ssel,
        SelectorConstraint psel,
        ApTitleConstraint apTitle,
        AeQualifierConstraint aeQualifier,
        AcseAssociationRequestIdentityEvidence evidence)
    {
        var notes = new List<string>();
        AddResolved(notes, "OSI-TSEL", tsel.State, ToHex(evidence.CalledTransportSelector), profileName);
        AddResolved(notes, "OSI-SSEL", ssel.State, ToHex(evidence.CalledSessionSelector), profileName);
        AddResolved(notes, "OSI-PSEL", psel.State, ToHex(evidence.CalledPresentationSelector), profileName);
        AddResolved(notes, "OSI-AP-Title", apTitle.State, evidence.CalledApTitleText, profileName);
        AddResolved(
            notes,
            "OSI-AE-Qualifier",
            aeQualifier.State,
            evidence.CalledAeQualifier?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            profileName);
        return notes;
    }

    private static IReadOnlyList<string> BuildTypedResolutionNotes(
        string profileName,
        SelectorConstraint tsel,
        SelectorConstraint ssel,
        SelectorConstraint psel,
        ApTitleConstraint apTitle,
        AeQualifierConstraint aeQualifier)
    {
        var notes = new List<string>();
        AddTemplateResolution(notes, "OSI-TSEL", tsel.State, profileName);
        AddTemplateResolution(notes, "OSI-SSEL", ssel.State, profileName);
        AddTemplateResolution(notes, "OSI-PSEL", psel.State, profileName);
        AddTemplateResolution(notes, "OSI-AP-Title", apTitle.State, profileName);
        AddTemplateResolution(notes, "OSI-AE-Qualifier", aeQualifier.State, profileName);
        return notes;
    }

    private static void AddResolved(
        ICollection<string> notes,
        string field,
        SclAssociationFieldState state,
        string candidateValue,
        string profileName)
    {
        if (state != SclAssociationFieldState.Unspecified)
            return;

        notes.Add(string.IsNullOrWhiteSpace(candidateValue)
            ? $"{field} remains omitted by engine profile {profileName}."
            : $"{field} resolved from engine profile {profileName}; source SCL remains unchanged.");
    }

    private static void AddTemplateResolution(
        ICollection<string> notes,
        string field,
        SclAssociationFieldState state,
        string profileName)
    {
        if (state == SclAssociationFieldState.Unspecified)
            notes.Add($"{field} resolved from the single engine profile template {profileName}; source SCL remains unchanged.");
    }

    private static SclAssistedMmsAssociationCandidate FromPlan(
        SclAssistedMmsAssociationPlan plan,
        string name,
        SclAssociationCandidateSource source,
        IReadOnlyList<string> notes)
        => new()
        {
            Name = name,
            Source = source,
            Cotp = new CotpConnectParameters
            {
                SourceTsap = plan.Cotp.SourceTsap.ToArray(),
                DestinationTsap = plan.Cotp.DestinationTsap.ToArray(),
                TpduSizeExponent = plan.Cotp.TpduSizeExponent
            },
            AssociationProfile = new AcseAssociationProfile(
                name,
                $"SCL-assisted candidate ({source}).",
                plan.SessionPresentationAcseMmsRequest.ToArray()),
            ResolutionNotes = notes
        };

    private static void AddCandidate(
        ICollection<SclAssistedMmsAssociationCandidate> candidates,
        ISet<string> seen,
        SclAssistedMmsAssociationCandidate candidate)
    {
        var cotp = CotpConnectRequest.Build(candidate.Cotp);
        var key = Convert.ToHexString(cotp) + ":" +
                  Convert.ToHexString(candidate.AssociationProfile.Payload);
        if (!seen.Add(key))
            return;

        candidates.Add(candidate);
    }

    private static SclAssistedMmsAssociationResolution Build(
        SclMmsAccessPoint remote,
        int port,
        IReadOnlyList<SclAssociationFieldEvidence> fields,
        IReadOnlyList<SclAssistedMmsAssociationCandidate> candidates,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
        => new()
        {
            Host = remote.Endpoint.IpAddress?.Trim() ?? string.Empty,
            Port = port,
            IedName = remote.IedName,
            AccessPointName = remote.AccessPointName,
            Fields = fields,
            Candidates = candidates,
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };

    private static SelectorConstraint ParseSelector(
        SclMmsAccessPoint remote,
        string name,
        string text,
        ICollection<string> errors)
    {
        if (remote.AmbiguousParameters.Contains(name))
        {
            errors.Add($"SCL ConnectedAP declares conflicting duplicate {name} values.");
            return new SelectorConstraint(
                SclAssociationFieldState.Ambiguous,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Ambiguous });
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new SelectorConstraint(
                SclAssociationFieldState.Unspecified,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Unspecified });
        }

        var compact = text.Trim();
        if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            compact = compact[2..];
        compact = compact
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);

        if (compact.Length == 0 || (compact.Length & 1) != 0 || compact.Length > 32)
        {
            errors.Add($"SCL {name} '{text}' is not a 1-to-16-byte hexadecimal selector.");
            return InvalidSelector(name, text);
        }

        var bytes = new byte[compact.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(
                    compact.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out bytes[i]))
            {
                errors.Add($"SCL {name} '{text}' contains non-hexadecimal data.");
                return InvalidSelector(name, text);
            }
        }

        return new SelectorConstraint(
            SclAssociationFieldState.PresentValid,
            bytes,
            new SclAssociationFieldEvidence
            {
                Name = name,
                State = SclAssociationFieldState.PresentValid,
                DeclaredText = text
            });
    }

    private static SelectorConstraint InvalidSelector(string name, string text)
        => new(
            SclAssociationFieldState.Invalid,
            null,
            new SclAssociationFieldEvidence
            {
                Name = name,
                State = SclAssociationFieldState.Invalid,
                DeclaredText = text
            });

    private static ApTitleConstraint ParseApTitle(
        SclMmsAccessPoint remote,
        string text,
        ICollection<string> errors)
    {
        const string name = "OSI-AP-Title";
        if (remote.AmbiguousParameters.Contains(name))
        {
            errors.Add("SCL ConnectedAP declares conflicting duplicate OSI-AP-Title values.");
            return new ApTitleConstraint(
                SclAssociationFieldState.Ambiguous,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Ambiguous });
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ApTitleConstraint(
                SclAssociationFieldState.Unspecified,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Unspecified });
        }

        var tokens = text.Split(
            [',', '.', ' ', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            errors.Add($"SCL OSI-AP-Title '{text}' does not contain a valid OID.");
            return InvalidApTitle(text);
        }

        var arcs = new uint[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!uint.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out arcs[i]))
            {
                errors.Add($"SCL OSI-AP-Title '{text}' contains an invalid OID arc '{tokens[i]}'.");
                return InvalidApTitle(text);
            }
        }

        if (arcs[0] > 2 || (arcs[0] < 2 && arcs[1] > 39))
        {
            errors.Add($"SCL OSI-AP-Title '{text}' violates ASN.1 object-identifier arc rules.");
            return InvalidApTitle(text);
        }

        return new ApTitleConstraint(
            SclAssociationFieldState.PresentValid,
            arcs,
            new SclAssociationFieldEvidence
            {
                Name = name,
                State = SclAssociationFieldState.PresentValid,
                DeclaredText = text
            });
    }

    private static ApTitleConstraint InvalidApTitle(string text)
        => new(
            SclAssociationFieldState.Invalid,
            null,
            new SclAssociationFieldEvidence
            {
                Name = "OSI-AP-Title",
                State = SclAssociationFieldState.Invalid,
                DeclaredText = text
            });

    private static AeQualifierConstraint ParseAeQualifier(
        SclMmsAccessPoint remote,
        ICollection<string> errors)
    {
        const string name = "OSI-AE-Qualifier";
        if (remote.AmbiguousParameters.Contains(name))
        {
            errors.Add("SCL ConnectedAP declares conflicting duplicate OSI-AE-Qualifier values.");
            return new AeQualifierConstraint(
                SclAssociationFieldState.Ambiguous,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Ambiguous });
        }

        var text = remote.Association.AeQualifierText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AeQualifierConstraint(
                SclAssociationFieldState.Unspecified,
                null,
                new SclAssociationFieldEvidence { Name = name, State = SclAssociationFieldState.Unspecified });
        }

        if (!remote.Association.AeQualifier.HasValue ||
            remote.Association.AeQualifier.Value is < 0 or > ushort.MaxValue)
        {
            errors.Add($"SCL OSI-AE-Qualifier '{text}' is invalid; expected 0..65535.");
            return new AeQualifierConstraint(
                SclAssociationFieldState.Invalid,
                null,
                new SclAssociationFieldEvidence
                {
                    Name = name,
                    State = SclAssociationFieldState.Invalid,
                    DeclaredText = text
                });
        }

        return new AeQualifierConstraint(
            SclAssociationFieldState.PresentValid,
            remote.Association.AeQualifier.Value,
            new SclAssociationFieldEvidence
            {
                Name = name,
                State = SclAssociationFieldState.PresentValid,
                DeclaredText = text
            });
    }

    private static string ToHex(byte[] bytes)
        => bytes.Length == 0 ? string.Empty : Convert.ToHexString(bytes);

    private sealed record SelectorConstraint(
        SclAssociationFieldState State,
        byte[]? Value,
        SclAssociationFieldEvidence Evidence);

    private sealed record ApTitleConstraint(
        SclAssociationFieldState State,
        uint[]? Value,
        SclAssociationFieldEvidence Evidence);

    private sealed record AeQualifierConstraint(
        SclAssociationFieldState State,
        int? Value,
        SclAssociationFieldEvidence Evidence);
}
