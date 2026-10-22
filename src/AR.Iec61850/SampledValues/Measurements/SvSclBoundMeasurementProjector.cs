using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Measurements;

/// <summary>
/// One numeric channel sample projected from an SV ASDU through an explicitly bound SCL payload layout.
/// RawValue is always the decoded numeric wire value. EngineeringValue is populated only when an
/// existing evidence-backed engineering-scale rule resolves to a real engineering unit.
/// </summary>
public sealed record SvProjectedMeasurementSample
{
    public int AsduIndex { get; init; }
    public ushort SampleCount { get; init; }
    public int ElementIndex { get; init; }
    public string SignalReference { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public double RawValue { get; init; }
    public SvEngineeringScale Scale { get; init; } = SvEngineeringScale.RawOnly("No engineering scale was resolved.");
    public double? EngineeringValue { get; init; }
    public SvMeasurementDomainValue? DomainValue { get; init; }
    public double? DisplayValue { get; init; }
}

public sealed record SvSclBoundMeasurementProjection
{
    public SvObservedStreamKey StreamKey { get; init; } = new();
    public string ControlBlockReference { get; init; } = string.Empty;
    public bool IsBoundToScl { get; init; }
    public bool HasEngineeringValues { get; init; }
    public IReadOnlyList<SvProjectedMeasurementSample> Samples { get; init; } = Array.Empty<SvProjectedMeasurementSample>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Conservative bridge from the existing SCL-backed SV payload decoder to numeric channel samples.
/// The projector never infers dataset order, channel identity, scale, CT/VT ratio, or engineering unit
/// from raw payload position. Any identity/configuration mismatch fails closed before payload decoding.
/// </summary>
public static class SvSclBoundMeasurementProjector
{
    public static SvSclBoundMeasurementProjection Project(
        SampledValuesFrame frame,
        SampledValuesPublisherProfile profile,
        SvStreamMeasurementContext? measurementContext = null,
        double? observedSamplesPerSecond = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(profile);

        var diagnostics = new List<string>();
        var streamKey = frame.Pdu.Asdus.Count > 0
            ? SvObservedStreamKey.FromFrame(frame)
            : new SvObservedStreamKey();

        if (!TryValidateBinding(frame, profile, diagnostics))
        {
            return new SvSclBoundMeasurementProjection
            {
                StreamKey = streamKey,
                ControlBlockReference = profile.Stream.ControlBlockReference,
                Diagnostics = diagnostics
            };
        }

        if (!profile.PayloadLayout.IsFullySupported)
        {
            diagnostics.Add(
                "The bound SCL payload layout contains unsupported elements. Engineering projection was not attempted because later byte offsets cannot be proven safely.");
            diagnostics.AddRange(profile.PayloadLayout.UnsupportedElements.Select(item => item.Diagnostic));
            return BoundEmpty(streamKey, profile, diagnostics);
        }

        if (frame.Pdu.Asdus.Any(asdu => asdu.SamplePayload.Length != profile.PayloadLayout.PayloadByteLength))
        {
            diagnostics.Add(
                $"The observed SV payload length does not exactly match the bound SCL layout ({profile.PayloadLayout.PayloadByteLength} byte(s)); projection failed closed.");
            return BoundEmpty(streamKey, profile, diagnostics);
        }

        var context = ValidateMeasurementContext(streamKey, frame, measurementContext, diagnostics);
        var fixedLegacyLayout = IsFixedFourCurrentFourVoltageValueQualityLayout(profile.PayloadLayout);
        var analogChannelCount = profile.PayloadLayout.Elements.Count(IsNumericAnalogElement);
        var declaredSampleRate = StableNullable(frame.Pdu.Asdus.Select(item => item.SampleRate));
        var declaredSampleMode = StableNullable(frame.Pdu.Asdus.Select(item => item.SampleMode));
        var samples = new List<SvProjectedMeasurementSample>();

        for (var asduIndex = 0; asduIndex < frame.Pdu.Asdus.Count; asduIndex++)
        {
            var asdu = frame.Pdu.Asdus[asduIndex];
            var decoded = SampledValuesPayloadDecoder.Decode(profile.PayloadLayout, asdu.SamplePayload);
            if (!decoded.IsComplete)
            {
                diagnostics.AddRange(decoded.Diagnostics.Select(item => $"ASDU {asduIndex}: {item}"));
                continue;
            }

            foreach (var decodedValue in decoded.Values)
            {
                if (!IsNumericAnalogElement(decodedValue.Element) ||
                    !TryGetFiniteNumber(decodedValue.Value, out var rawValue))
                {
                    continue;
                }

                var scale = SvEngineeringScaleResolver.Resolve(new SvEngineeringScaleEvidence
                {
                    Channel = decodedValue.Element.SignalReference,
                    Kind = decodedValue.Element.Cdc,
                    IsSclBound = true,
                    IsFixedFourCurrentFourVoltageLayout = fixedLegacyLayout,
                    AnalogChannelCount = analogChannelCount,
                    PayloadBytesPerAsdu = profile.PayloadLayout.PayloadByteLength,
                    DeclaredSampleRate = declaredSampleRate,
                    DeclaredSampleMode = declaredSampleMode,
                    ObservedSamplesPerSecond = observedSamplesPerSecond
                });

                double? engineeringValue = null;
                SvMeasurementDomainValue? domainValue = null;
                double? displayValue = null;

                if (scale.HasEngineeringUnit)
                {
                    engineeringValue = scale.Apply(rawValue);
                    if (context is not null)
                    {
                        var ratio = context.ResolveRatio(decodedValue.Element.SignalReference);
                        domainValue = SvMeasurementDomainResolver.Resolve(
                            engineeringValue.Value,
                            scale.Unit,
                            context.WireDomain,
                            ratio);
                        displayValue = context.DisplayDomain switch
                        {
                            SvMeasurementValueDomain.PrimaryEngineering => domainValue.PrimaryValue,
                            SvMeasurementValueDomain.SecondaryEquivalent => domainValue.SecondaryEquivalentValue,
                            _ => null
                        };
                    }
                }

                samples.Add(new SvProjectedMeasurementSample
                {
                    AsduIndex = asduIndex,
                    SampleCount = asdu.SampleCount,
                    ElementIndex = decodedValue.Element.Index,
                    SignalReference = decodedValue.Element.SignalReference,
                    Cdc = decodedValue.Element.Cdc,
                    RawValue = rawValue,
                    Scale = scale,
                    EngineeringValue = engineeringValue,
                    DomainValue = domainValue,
                    DisplayValue = displayValue
                });
            }
        }

        if (samples.Count == 0)
            diagnostics.Add("No numeric SCL-mapped measurement elements were projected from the accepted SV frame.");
        if (samples.Count > 0 && samples.All(item => !item.EngineeringValue.HasValue))
            diagnostics.Add("Numeric samples were decoded, but no evidence-backed engineering scale was established; values remain raw only.");

        return new SvSclBoundMeasurementProjection
        {
            StreamKey = streamKey,
            ControlBlockReference = profile.Stream.ControlBlockReference,
            IsBoundToScl = true,
            HasEngineeringValues = samples.Any(item => item.EngineeringValue.HasValue),
            Samples = samples,
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static bool TryValidateBinding(
        SampledValuesFrame frame,
        SampledValuesPublisherProfile profile,
        ICollection<string> diagnostics)
    {
        if (frame.Pdu.Asdus.Count == 0)
        {
            diagnostics.Add("The observed SV frame contains no ASDUs.");
            return false;
        }

        if (frame.AppId != profile.AppId ||
            !string.Equals(frame.Destination.ToString(), profile.Destination.ToString(), StringComparison.OrdinalIgnoreCase) ||
            frame.Vlan?.VlanId != profile.Vlan?.VlanId)
        {
            diagnostics.Add(
                "The SCL profile does not bind this Ethernet SV identity: APPID, destination MAC, and VLAN must match exactly before payload projection.");
            return false;
        }

        if (frame.Pdu.Asdus.Any(asdu =>
                !string.Equals(asdu.SvId, profile.Stream.SvId, StringComparison.Ordinal) ||
                !string.Equals(asdu.DataSetReference, profile.Stream.DataSetReference, StringComparison.Ordinal)))
        {
            diagnostics.Add(
                "The observed svID/DataSet reference does not match the bound SCL SampledValueControl stream; payload projection failed closed.");
            return false;
        }

        if (frame.Pdu.Asdus.Any(asdu => asdu.ConfigurationRevision != profile.Stream.ConfigurationRevision))
        {
            diagnostics.Add(
                "The observed confRev does not match the bound SCL SampledValueControl configuration; dataset layout cannot be assumed current.");
            return false;
        }

        return true;
    }

    private static SvStreamMeasurementContext? ValidateMeasurementContext(
        SvObservedStreamKey streamKey,
        SampledValuesFrame frame,
        SvStreamMeasurementContext? context,
        ICollection<string> diagnostics)
    {
        if (context is null)
            return null;

        var errors = context.Validate();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                diagnostics.Add($"Measurement context rejected: {error}");
            return null;
        }

        if (!string.Equals(context.StreamKey, streamKey.Id, StringComparison.Ordinal))
        {
            diagnostics.Add("Measurement context stream key does not match the observed stream identity; CT/VT ratio and display-domain conversion were not applied.");
            return null;
        }

        var observedSvId = frame.Pdu.Asdus[0].SvId;
        if (!string.IsNullOrWhiteSpace(context.SvId) &&
            !string.Equals(context.SvId, observedSvId, StringComparison.Ordinal))
        {
            diagnostics.Add("Measurement context svID does not match the observed stream; CT/VT ratio and display-domain conversion were not applied.");
            return null;
        }

        return context;
    }

    private static bool IsFixedFourCurrentFourVoltageValueQualityLayout(SampledValuesPayloadLayout layout)
    {
        if (layout.PayloadByteLength != 64 || layout.Elements.Count != 16)
            return false;

        var current = 0;
        var voltage = 0;
        for (var pair = 0; pair < 8; pair++)
        {
            var value = layout.Elements[pair * 2];
            var quality = layout.Elements[(pair * 2) + 1];
            if (value.Kind != SampledValuePayloadElementKind.Int32 ||
                quality.Kind != SampledValuePayloadElementKind.Quality)
            {
                return false;
            }

            switch (SvEngineeringScaleResolver.ResolveDomain(value.SignalReference, value.Cdc))
            {
                case SvMeasurementDomain.Current: current++; break;
                case SvMeasurementDomain.Voltage: voltage++; break;
                default: return false;
            }
        }

        return current == 4 && voltage == 4;
    }

    private static bool IsNumericAnalogElement(SampledValuePayloadElement element)
        => element.Kind is SampledValuePayloadElementKind.Int8
            or SampledValuePayloadElementKind.Int16
            or SampledValuePayloadElementKind.Int32
            or SampledValuePayloadElementKind.Int64
            or SampledValuePayloadElementKind.UInt8
            or SampledValuePayloadElementKind.UInt16
            or SampledValuePayloadElementKind.UInt24
            or SampledValuePayloadElementKind.UInt32
            or SampledValuePayloadElementKind.UInt64
            or SampledValuePayloadElementKind.Float32
            or SampledValuePayloadElementKind.Float64;

    private static bool TryGetFiniteNumber(MmsDataValue value, out double number)
    {
        number = value.Kind switch
        {
            MmsDataKind.Integer when value.Value is long signed => signed,
            MmsDataKind.Unsigned when value.Value is ulong unsigned => unsigned,
            MmsDataKind.FloatingPoint when value.Value is float single => single,
            MmsDataKind.FloatingPoint when value.Value is double floating => floating,
            _ => double.NaN
        };
        return double.IsFinite(number);
    }

    private static ushort? StableNullable(IEnumerable<ushort?> values)
    {
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static SvSclBoundMeasurementProjection BoundEmpty(
        SvObservedStreamKey streamKey,
        SampledValuesPublisherProfile profile,
        IEnumerable<string> diagnostics)
        => new()
        {
            StreamKey = streamKey,
            ControlBlockReference = profile.Stream.ControlBlockReference,
            IsBoundToScl = true,
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
}
