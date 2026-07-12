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
        return OpenCoreAsync(new MmsClientControlTransport(session), objectReference, cancellationToken);
    }

    internal async Task<Iec61850ControlObjectSession> OpenCoreAsync(
        IIec61850ControlTransport transport,
        string objectReference,
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

        var namesByDomain = await transport.DiscoverDomainVariablesAsync(cancellationToken).ConfigureAwait(false);
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

        var status = FindStatusReference(references, domainNames);
        var supportsTimeActivated = operSpecification.Children.Any(x => NormalizeName(x.Name) == "opertm");
        var descriptor = new Iec61850ControlObjectDescriptor
        {
            ObjectReference = references.ObjectReference,
            Cdc = InferCdc(ctlValSpecification),
            ControlModel = controlModel,
            CtlValSpecification = ctlValSpecification,
            OperSpecification = operSpecification,
            SelectWithValueSpecification = sbowSpecification,
            CancelSpecification = cancelSpecification,
            StatusReference = status.Reference,
            StatusFunctionalConstraint = status.FunctionalConstraint,
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
