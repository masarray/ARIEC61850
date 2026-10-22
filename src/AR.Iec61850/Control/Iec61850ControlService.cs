using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

public sealed class Iec61850ControlService : IIec61850ControlService
{
    private readonly Iec61850ControlServiceOptions _options;

    public Iec61850ControlService(Iec61850ControlServiceOptions? options = null)
    {
        _options = options ?? new Iec61850ControlServiceOptions();
    }

    public Task<Iec61850ControlObjectSession> OpenAsync(
        MmsClientSession session,
        string objectReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return OpenCoreAsync(new MmsClientControlTransport(session), objectReference, null, cancellationToken);
    }

    /// <summary>
    /// Opens a control object while reusing an authoritative name inventory already
    /// discovered on the same MMS association. The supplied inventory is structural
    /// evidence only: ctlModel, exact control type specifications, timeout values and
    /// status values remain live reads/probes so command safety stays fail-closed.
    /// </summary>
    public Task<Iec61850ControlObjectSession> OpenAsync(
        MmsClientSession session,
        string objectReference,
        IReadOnlyDictionary<string, IReadOnlyList<string>> authoritativeDomainVariables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authoritativeDomainVariables);
        return OpenCoreAsync(
            new MmsClientControlTransport(session),
            objectReference,
            authoritativeDomainVariables,
            cancellationToken);
    }

    /// <summary>
    /// Read-only exact CF ctlModel probe. An unsuccessful read, unknown encoding,
    /// and an explicitly reported StatusOnly model are distinct outcomes.
    /// Never discovers variable directories or writes any control service.
    /// </summary>
    public Task<Iec61850ControlModelProbe> ProbeControlModelAsync(
        MmsClientSession session,
        string objectReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ProbeControlModelCoreAsync(new MmsClientControlTransport(session), objectReference, cancellationToken);
    }

    internal async Task<Iec61850ControlModelProbe> ProbeControlModelCoreAsync(
        IIec61850ControlTransport transport,
        string objectReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!transport.IsAssociated)
            throw new InvalidOperationException("IEC 61850 ctlModel probe requires an active MMS association.");

        var references = Iec61850ControlObjectReferences.Parse(objectReference);
        var read = await transport.ReadAsync(references.CtlModel, cancellationToken).ConfigureAwait(false);
        var kind = read.Value?.Kind.ToString() ?? "Unavailable";
        var numeric = read.Value?.Kind is MmsDataKind.Integer or MmsDataKind.Unsigned
            ? Convert.ToString(read.Value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
            : "";
        var model = read.IsSuccess && read.Value != null
            ? DecodeControlModel(read.Value)
            : Iec61850ControlModel.Unknown;
        var decoded = read.IsSuccess && read.Value != null && model != Iec61850ControlModel.Unknown;
        var detail = !read.IsSuccess || read.Value == null
            ? $"MMS Read failed/unavailable: {read.Message}"
            : !decoded
                ? $"MMS Read returned unsupported ctlModel encoding/value ({kind}, {numeric})."
                : $"Exact MMS Read returned ctlModel={numeric} ({model}).";
        return new Iec61850ControlModelProbe(
            references.ObjectReference,
            references.CtlModel.ToString(),
            read.IsSuccess && read.Value != null,
            decoded,
            model,
            kind,
            numeric,
            detail,
            BoundedHex(transport.LastRequestHex),
            BoundedHex(transport.LastResponseHex));
    }

    /// <summary>
    /// Explicit commissioning-only ctlModel update. The caller must confirm
    /// device authorization separately. One exact CF write at most; no fallback
    /// sequence or implicit command Operate is attempted.
    /// </summary>
    public Task<Iec61850ControlModelConfigurationResult> ConfigureControlModelAsync(
        MmsClientSession session, string objectReference,
        Iec61850ControlModel expectedLiveModel, Iec61850ControlModel requestedModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ConfigureControlModelCoreAsync(
            new MmsClientControlTransport(session), objectReference,
            expectedLiveModel, requestedModel, cancellationToken);
    }

    internal async Task<Iec61850ControlModelConfigurationResult> ConfigureControlModelCoreAsync(
        IIec61850ControlTransport transport, string objectReference,
        Iec61850ControlModel expectedLiveModel, Iec61850ControlModel requestedModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!transport.IsAssociated)
            throw new InvalidOperationException("Control model configuration requires an active MMS association.");
        if (expectedLiveModel == Iec61850ControlModel.Unknown ||
            requestedModel is not (Iec61850ControlModel.StatusOnly or Iec61850ControlModel.DirectNormal or
                Iec61850ControlModel.SelectBeforeOperateNormal or
                Iec61850ControlModel.DirectEnhanced or
                Iec61850ControlModel.SelectBeforeOperateEnhanced))
            throw new ArgumentOutOfRangeException(nameof(requestedModel),
                "Only exact, known control models 0-4 can be commissioned.");

        var reference = Iec61850ControlObjectReferences.Parse(objectReference).CtlModel;
        var before = await ProbeControlModelCoreAsync(
            transport, objectReference, cancellationToken).ConfigureAwait(false);
        if (!before.Decoded || before.ControlModel != expectedLiveModel)
            return new Iec61850ControlModelConfigurationResult(
                false, false, before.ControlModel, Iec61850ControlModel.Unknown,
                reference.ToString(),
                $"No Write: live ctlModel changed or could not be verified. Expected={expectedLiveModel}; {before.Detail}");

        if (expectedLiveModel == requestedModel)
            return new Iec61850ControlModelConfigurationResult(
                false, true, before.ControlModel, before.ControlModel,
                reference.ToString(), "No Write: requested ctlModel is already active.");

        // Deliberate one-shot configuration: no retries, no auto-rollback.
        var write = await transport.WriteControlAsync(
            reference, MmsDataValue.Integer((long)requestedModel), cancellationToken)
            .ConfigureAwait(false);
        var writeAccepted = write.IsSuccess && write.AccessResults.All(x => x.IsSuccess);
        if (!writeAccepted)
            return new Iec61850ControlModelConfigurationResult(
                false, false, before.ControlModel, Iec61850ControlModel.Unknown,
                reference.ToString(),
                $"MMS ctlModel Write rejected; readback not assumed. {write.Message}");

        var after = await ProbeControlModelCoreAsync(
            transport, objectReference, cancellationToken).ConfigureAwait(false);
        var verified = after.Decoded && after.ControlModel == requestedModel;
        return new Iec61850ControlModelConfigurationResult(
            true, verified, before.ControlModel, after.ControlModel,
            reference.ToString(),
            verified
                ? $"ctlModel Write accepted and exact MMS readback confirmed {requestedModel}."
                : $"Write accepted but readback is not verified: {after.Detail}. Do not Operate until reinspection.");
    }

    private static string BoundedHex(string? hex)
        => string.IsNullOrWhiteSpace(hex) ? "" : hex.Length <= 512 ? hex : hex[..512] + "… (truncated)";

    internal Task<Iec61850ControlObjectSession> OpenCoreAsync(
        IIec61850ControlTransport transport,
        string objectReference,
        CancellationToken cancellationToken = default)
        => OpenCoreAsync(transport, objectReference, null, cancellationToken);

    internal async Task<Iec61850ControlObjectSession> OpenCoreAsync(
        IIec61850ControlTransport transport,
        string objectReference,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? authoritativeDomainVariables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!transport.IsAssociated)
            throw new InvalidOperationException("IEC 61850 control service requires an active MMS association.");

        var references = Iec61850ControlObjectReferences.Parse(objectReference);
        var evidence = new List<string>();

        var ctlModelRead = await transport.ReadAsync(references.CtlModel, cancellationToken).ConfigureAwait(false);
        if (!ctlModelRead.IsSuccess || ctlModelRead.Value == null)
            throw new InvalidOperationException($"Cannot discover ctlModel for {references.ObjectReference}: {ctlModelRead.Message}");

        var controlModel = DecodeControlModel(ctlModelRead.Value);
        evidence.Add($"ctlModel={controlModel}");
        if (controlModel is Iec61850ControlModel.StatusOnly or Iec61850ControlModel.Unknown)
            throw new InvalidOperationException($"{references.ObjectReference} is not command-ready: ctlModel={controlModel}.");

        var operAttributes = await RequireSpecificationAsync(transport, references.Oper, "Oper", cancellationToken).ConfigureAwait(false);
        var operSpecification = operAttributes.TypeSpecification!;
        var ctlValSpecification = FindRequiredChild(operSpecification, "ctlVal", "Oper");
        evidence.Add($"Oper={operSpecification.Signature}");

        MmsTypeSpecificationNode? sbowSpecification = null;
        if (controlModel == Iec61850ControlModel.SelectBeforeOperateEnhanced)
        {
            var sbowAttributes = await RequireSpecificationAsync(transport, references.SboWithValue, "SBOw", cancellationToken).ConfigureAwait(false);
            sbowSpecification = sbowAttributes.TypeSpecification!;
            var sbowCtlVal = FindRequiredChild(sbowSpecification, "ctlVal", "SBOw");
            if (!SpecificationsCompatible(ctlValSpecification, sbowCtlVal))
                throw new InvalidOperationException($"Live ctlVal type differs between Oper and SBOw for {references.ObjectReference}.");
            evidence.Add($"SBOw={sbowSpecification.Signature}");
        }

        MmsTypeSpecificationNode? cancelSpecification = null;
        var cancelAttributes = await transport.GetVariableSpecificationAsync(references.Cancel, cancellationToken).ConfigureAwait(false);
        if (cancelAttributes.IsSuccess && cancelAttributes.TypeSpecification != null)
        {
            cancelSpecification = cancelAttributes.TypeSpecification;
            evidence.Add($"Cancel={cancelSpecification.Signature}");
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> namesByDomain;
        if (authoritativeDomainVariables != null)
        {
            namesByDomain = authoritativeDomainVariables;
            evidence.Add("domainInventory=authoritative-reuse");
        }
        else
        {
            namesByDomain = await transport.DiscoverDomainVariablesAsync(cancellationToken).ConfigureAwait(false);
            evidence.Add("domainInventory=live-fallback");
        }

        namesByDomain.TryGetValue(references.Domain, out var domainNames);
        domainNames ??= Array.Empty<string>();

        var sboTimeout = await TryReadTimeoutAsync(
            transport,
            references.SboTimeout,
            domainNames,
            _options.DefaultSboTimeout,
            cancellationToken).ConfigureAwait(false);
        var operTimeout = await TryReadTimeoutAsync(
            transport,
            references.OperTimeout,
            domainNames,
            _options.DefaultOperateTimeout,
            cancellationToken).ConfigureAwait(false);

        var status = await FindStatusReferenceAsync(transport, references, domainNames,
            ctlValSpecification.MmsType.Equals("boolean", StringComparison.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
        var liveDoublePoint = status.TypedDoublePoint;
        if (!liveDoublePoint &&
            ctlValSpecification.MmsType.Equals("boolean", StringComparison.OrdinalIgnoreCase) &&
            status.Reference.Equals(references.ObjectReference + ".stVal",
                StringComparison.OrdinalIgnoreCase))
        {
            // A standard DPC can use a Boolean Oper.ctlVal while returning
            // two-bit coded stVal. Verify the actual wire shape, not a prefix,
            // a DO called Pos, or a remembered SCL CDC.
            var statusRead = await transport.ReadAsync(
                MmsObjectReference.Parse(status.Reference, "ST"),
                cancellationToken).ConfigureAwait(false);
            var actual = statusRead.Value;
            liveDoublePoint = statusRead.IsSuccess && actual != null &&
                actual.Kind == MmsDataKind.BitString &&
                actual.RawValue.Count == 2 && actual.RawValue[0] == 6;
            evidence.Add(liveDoublePoint
                ? "statusShape=live-two-bit-double-point"
                : "statusShape=not-proven-two-bit-double-point");
        }
        evidence.Add(string.IsNullOrWhiteSpace(status.Reference)
            ? "status=not-verified"
            : "status=live-confirmed:" + status.Reference);
        var supportsTimeActivated = operSpecification.Children.Any(x => NormalizeName(x.Name) == "opertm");
        var descriptor = new Iec61850ControlObjectDescriptor
        {
            ObjectReference = references.ObjectReference,
            // The exact live status shape distinguishes BSC TapChg's Tcmd enum
            // from DPC even when both ctlVal types have a two-bit MMS encoding.
            // Never classify a step-position value as an Open/Close switch.
            Cdc = status.Reference.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase)
                ? "BSC"
                : liveDoublePoint && ctlValSpecification.MmsType.Equals("boolean", StringComparison.OrdinalIgnoreCase)
                    ? "DPC"
                    : InferCdc(ctlValSpecification),
            ControlModel = controlModel,
            CtlValSpecification = ctlValSpecification,
            OperSpecification = operSpecification,
            SelectWithValueSpecification = sbowSpecification,
            CancelSpecification = cancelSpecification,
            StatusReference = status.Reference,
            StatusFunctionalConstraint = status.FunctionalConstraint,
            LiveStatusIsDoublePoint = liveDoublePoint,
            SboTimeout = sboTimeout,
            OperTimeout = operTimeout,
            SupportsTimeActivatedOperate = supportsTimeActivated,
            SupportsCommandTermination = controlModel is Iec61850ControlModel.DirectEnhanced or Iec61850ControlModel.SelectBeforeOperateEnhanced,
            DiscoveryEvidence = string.Join(" | ", evidence),
            References = references
        };

        if (!descriptor.IsOperationallyReady)
            throw new InvalidOperationException($"Control descriptor for {references.ObjectReference} is incomplete and cannot safely execute commands.");

        return new Iec61850ControlObjectSession(transport, descriptor, _options);
    }

    private static async Task<MmsVariableAccessAttributesResult> RequireSpecificationAsync(
        IIec61850ControlTransport transport,
        MmsObjectReference reference,
        string service,
        CancellationToken cancellationToken)
    {
        var attributes = await transport.GetVariableSpecificationAsync(reference, cancellationToken).ConfigureAwait(false);
        if (!attributes.IsSuccess || attributes.TypeSpecification == null)
            throw new InvalidOperationException($"Cannot retrieve exact live {service} type specification for {reference}: {attributes.Message}");
        return attributes;
    }

    private static Iec61850ControlModel DecodeControlModel(MmsDataValue value)
    {
        var number = value.Kind switch
        {
            MmsDataKind.Integer => Convert.ToInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture),
            MmsDataKind.Unsigned => checked((long)Convert.ToUInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => -1
        };
        return number switch
        {
            0 => Iec61850ControlModel.StatusOnly,
            1 => Iec61850ControlModel.DirectNormal,
            2 => Iec61850ControlModel.SelectBeforeOperateNormal,
            3 => Iec61850ControlModel.DirectEnhanced,
            4 => Iec61850ControlModel.SelectBeforeOperateEnhanced,
            _ => Iec61850ControlModel.Unknown
        };
    }

    private static MmsTypeSpecificationNode FindRequiredChild(MmsTypeSpecificationNode parent, string name, string service)
    {
        var match = parent.Children.FirstOrDefault(x => NormalizeName(x.Name) == NormalizeName(name));
        return match ?? throw new InvalidOperationException($"Live {service} specification has no named {name} field. Refusing positional guessing.");
    }

    private static bool SpecificationsCompatible(MmsTypeSpecificationNode left, MmsTypeSpecificationNode right)
        => string.Equals(left.Signature, right.Signature, StringComparison.OrdinalIgnoreCase) ||
           (string.Equals(left.MmsType, right.MmsType, StringComparison.OrdinalIgnoreCase) &&
            left.Children.Count == right.Children.Count &&
            left.Children.Zip(right.Children).All(x => SpecificationsCompatible(x.First, x.Second)));

    private static async Task<TimeSpan?> TryReadTimeoutAsync(
        IIec61850ControlTransport transport,
        MmsObjectReference reference,
        IReadOnlyList<string> domainNames,
        TimeSpan fallback,
        CancellationToken cancellationToken)
    {
        if (!domainNames.Contains(reference.Item, StringComparer.OrdinalIgnoreCase))
            return fallback;

        var read = await transport.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value == null)
            return fallback;

        var milliseconds = read.Value.Kind switch
        {
            MmsDataKind.Integer => Convert.ToInt64(read.Value.Value, System.Globalization.CultureInfo.InvariantCulture),
            MmsDataKind.Unsigned => checked((long)Convert.ToUInt64(read.Value.Value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => -1
        };
        return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : fallback;
    }

    private static (string Reference, string FunctionalConstraint) FindStatusReference(
        Iec61850ControlObjectReferences references,
        IReadOnlyList<string> names)
    {
        var prefixes = new[]
        {
            $"{references.LogicalNode}$ST${references.MmsDataObjectPath}$valWTr$posVal",
            $"{references.LogicalNode}$ST${references.MmsDataObjectPath}$stVal",
            $"{references.LogicalNode}$ST${references.MmsDataObjectPath}$posVal",
            $"{references.LogicalNode}$MX${references.MmsDataObjectPath}$mag$f",
            $"{references.LogicalNode}$MX${references.MmsDataObjectPath}$mag$i"
        };
        var match = prefixes.Select(prefix => names.FirstOrDefault(x => x.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (string.IsNullOrWhiteSpace(match))
            return (string.Empty, string.Empty);

        var parts = match.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return ($"{references.Domain}/{match.Replace('$', '.')}", string.Empty);

        // Hide the MMS functional-constraint segment in the user-facing IEC 61850
        // object reference: LD/LN.DO.DA instead of LD/LN.ST.DO.DA.
        return ($"{references.Domain}/{parts[0]}.{string.Join(".", parts.Skip(2))}", parts[1]);
    }

    // Some MMS NameLists omit ST scalar children or are truncated. A positive
    // typed ctlModel and Boolean Oper.ctlVal already prove an actual control
    // DataObject exists, regardless of arbitrary LN prefix/class/name. Allow
    // exactly one additional *read-only* ST.stVal probe of that same DO.
    // Only live two-bit evidence can authorize DPC; no name-only promotion.
    private static async Task<(string Reference, string FunctionalConstraint, bool TypedDoublePoint)> FindStatusReferenceAsync(
        IIec61850ControlTransport transport, Iec61850ControlObjectReferences references,
        IReadOnlyList<string> names, bool booleanControl, CancellationToken cancellationToken)
    {
        var exact = FindStatusReference(references, names);
        if (!string.IsNullOrWhiteSpace(exact.Reference))
            return (exact.Reference, exact.FunctionalConstraint, false);
        var root = references.LogicalNode + "$ST$" + references.MmsDataObjectPath;
        var hasListedStatusRoot = names.Any(name =>
            name.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(root + "$", StringComparison.OrdinalIgnoreCase));
        var standardPosition = Iec61850ControlSemanticResolver.IsStandardPosition(references.ObjectReference);

        // A sparse NameList cannot hide a real renamed control. Boolean
        // control structures may be SPC or DPC: only a typed ST read
        // distinguishes them. Other unknown objects stay fail-closed.
        if (!hasListedStatusRoot && !standardPosition && !booleanControl)
            return (exact.Reference, exact.FunctionalConstraint, false);

        var candidates = booleanControl || standardPosition
            ? new[] { (Suffix: "$stVal", Path: ".stVal", IsStep: false) }
            : new[]
            {
                (Suffix: "$valWTr$posVal", Path: ".valWTr.posVal", IsStep: true),
                (Suffix: "$stVal", Path: ".stVal", IsStep: false)
            };
        foreach (var candidate in candidates)
        {
            var item = new MmsObjectReference(references.Domain, root + candidate.Suffix, "ST");
            var read = await transport.ReadAsync(item, cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value == null)
                continue;
            var value = read.Value;
            var isExactTwoBitStatus = value.Kind == MmsDataKind.BitString &&
                value.RawValue.Count == 2 && value.RawValue[0] == 6;
            var valid = candidate.IsStep
                ? value.Kind is MmsDataKind.Integer or MmsDataKind.Unsigned
                : standardPosition && !booleanControl ? isExactTwoBitStatus :
                    isExactTwoBitStatus || value.Kind == MmsDataKind.Boolean;
            if (valid)
                return (references.Domain + "/" + references.LogicalNode + "." +
                        references.MmsDataObjectPath.Replace('$', '.') + candidate.Path,
                        "ST", isExactTwoBitStatus);
        }
        return (exact.Reference, exact.FunctionalConstraint, false);
    }

    private static string InferCdc(MmsTypeSpecificationNode ctlVal)
    {
        var type = ctlVal.MmsType.Trim().ToLowerInvariant();
        if (type == "boolean") return "SPC";
        if (type == "bit-string") return "DPC";
        if (type == "floating-point") return "APC";
        if (type is "integer" or "unsigned" or "bcd") return "INC/ISC";
        if (type == "structure")
        {
            var names = ctlVal.Children.Select(x => NormalizeName(x.Name)).ToArray();
            if (names.Contains("posval") || names.Contains("transind")) return "BSC";
            if (names.Contains("f") || names.Contains("i")) return "APC";
        }
        return "vendor-specific";
    }

    private static string NormalizeName(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}


/// <summary>Read-only ctlModel evidence; not permission to send an MMS control write.</summary>
public sealed record Iec61850ControlModelProbe(
    string ObjectReference,
    string MmsReference,
    bool ReadSucceeded,
    bool Decoded,
    Iec61850ControlModel ControlModel,
    string RawKind,
    string RawScalar,
    string Detail,
    string RequestHexPreview,
    string ResponseHexPreview);


/// <summary>Configuration result is not permission to operate primary equipment.</summary>
public sealed record Iec61850ControlModelConfigurationResult(
    bool WriteAccepted,
    bool ReadbackVerified,
    Iec61850ControlModel BeforeModel,
    Iec61850ControlModel AfterModel,
    string MmsReference,
    string Detail);
