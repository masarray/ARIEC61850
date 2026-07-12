using System.Globalization;
using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Simulation;

/// <summary>
/// Per-association server-side hooks used by <see cref="MmsConfirmedRequestBerDispatcher"/>.
/// A runtime can intercept RCB attribute reads (so the client observes live RptEna/SqNum/... values)
/// and RCB attribute writes (so the client can enable reporting, request GI, and tune IntgPd).
/// </summary>
public interface IMmsAssociationRuntime
{
    /// <summary>Returns true when <paramref name="iecTarget"/> resolves to an RCB or RCB attribute owned by this association.</summary>
    bool TryReadRcbAttribute(string iecTarget, out MmsDataValue value);

    /// <summary>
    /// Returns true when the target is an RCB attribute (the write was handled here).
    /// On failure <paramref name="dataAccessError"/> carries the ISO 9506 DataAccessError code.
    /// </summary>
    bool TryWriteRcbAttribute(string iecTarget, MmsDataValue value, out int dataAccessError);
}

/// <summary>
/// The single source of truth for the MMS attribute layout of report control blocks. The read-only
/// session uses it to build TypeDescriptions and the reporting runtime uses it to build live value
/// structures, so the type a client discovers always matches the values it reads.
/// Order and members follow IEC 61850-8-1 (URCB: RptID..GI, BRCB: RptID..TimeOfEntry).
/// </summary>
public static class MmsReportControlBlockLayout
{
    public static IReadOnlyList<(string Name, string BType)> AttributesFor(bool buffered)
        => buffered ? BrcbAttributes : UrcbAttributes;

    private static readonly (string Name, string BType)[] UrcbAttributes =
    [
        ("RptID", "VisString129"),
        ("RptEna", "BOOLEAN"),
        ("Resv", "BOOLEAN"),
        ("DatSet", "VisString129"),
        ("ConfRev", "INT32U"),
        ("OptFlds", "OPTFLDS"),
        ("BufTm", "INT32U"),
        ("SqNum", "INT8U"),
        ("TrgOps", "TRGOPS"),
        ("IntgPd", "INT32U"),
        ("GI", "BOOLEAN")
    ];

    private static readonly (string Name, string BType)[] BrcbAttributes =
    [
        ("RptID", "VisString129"),
        ("RptEna", "BOOLEAN"),
        ("DatSet", "VisString129"),
        ("ConfRev", "INT32U"),
        ("OptFlds", "OPTFLDS"),
        ("BufTm", "INT32U"),
        ("SqNum", "INT16U"),
        ("TrgOps", "TRGOPS"),
        ("IntgPd", "INT32U"),
        ("GI", "BOOLEAN"),
        ("PurgeBuf", "BOOLEAN"),
        ("EntryID", "ENTRYID"),
        ("TimeOfEntry", "ENTRYTIME"),
        ("ResvTms", "INT16")
    ];

    // OptFlds packed-list bits (bit 0 = MSB of the first byte, bit 0 itself is reserved):
    // 1 sequence-number, 2 report-time-stamp, 3 reason-for-inclusion, 4 data-set-name,
    // 5 data-reference, 6 buffer-overflow, 7 entryID, 8 conf-revision, 9 segmentation.
    public static byte[] ParseOptionalFields(string tokens)
    {
        byte first = 0;
        byte second = 0;
        foreach (var raw in SplitTokens(tokens))
        {
            switch (raw)
            {
                case "SEQNUM" or "SEQUENCE-NUMBER": first |= 0x40; break;
                case "TIMESTAMP" or "REPORT-TIME-STAMP" or "TIMEOFENTRY": first |= 0x20; break;
                case "REASONCODE" or "REASON-FOR-INCLUSION" or "REASONFORINCLUSION": first |= 0x10; break;
                case "DATASET" or "DATA-SET-NAME" or "DATSET": first |= 0x08; break;
                case "DATAREF" or "DATA-REFERENCE" or "DATAREFERENCE": first |= 0x04; break;
                case "BUFOVFL" or "BUFFER-OVERFLOW": first |= 0x02; break;
                case "ENTRYID": first |= 0x01; break;
                case "CONFREV" or "CONF-REVISION" or "CONFIGREF": second |= 0x80; break;
                case "SEGMENTATION": second |= 0x40; break;
            }
        }

        return [first, second];
    }

    // TrgOps packed-list bits (bit 0 reserved): 1 data-change, 2 quality-change,
    // 3 data-update, 4 integrity, 5 general-interrogation.
    public static byte ParseTriggerOptions(string tokens)
    {
        byte bits = 0;
        foreach (var raw in SplitTokens(tokens))
        {
            switch (raw)
            {
                case "DCHG" or "DATA-CHANGE" or "DATACHANGE": bits |= 0x40; break;
                case "QCHG" or "QUALITY-CHANGE" or "QUALITYCHANGE": bits |= 0x20; break;
                case "DUPD" or "DATA-UPDATE" or "DATAUPDATE": bits |= 0x10; break;
                case "PERIOD" or "INTEGRITY": bits |= 0x08; break;
                case "GI" or "GENERAL-INTERROGATION": bits |= 0x04; break;
            }
        }

        return bits;
    }

    public static bool OptionalFieldSequenceNumber(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x40) != 0;
    public static bool OptionalFieldTimeStamp(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x20) != 0;
    public static bool OptionalFieldReasonCode(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x10) != 0;
    public static bool OptionalFieldDataSet(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x08) != 0;
    public static bool OptionalFieldDataReference(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x04) != 0;
    public static bool OptionalFieldBufferOverflow(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x02) != 0;
    public static bool OptionalFieldEntryId(IReadOnlyList<byte> optFlds) => (First(optFlds) & 0x01) != 0;
    public static bool OptionalFieldConfRev(IReadOnlyList<byte> optFlds) => (Second(optFlds) & 0x80) != 0;

    public static bool TriggerIntegrity(byte trgOps) => (trgOps & 0x08) != 0;
    public static bool TriggerGeneralInterrogation(byte trgOps) => (trgOps & 0x04) != 0;

    public static byte[] ToBinaryTime6(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var days = (int)(utc.UtcDateTime.Date - new DateTime(1984, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
        if (days < 0)
            days = 0;
        var milliseconds = (uint)utc.TimeOfDay.TotalMilliseconds;
        return
        [
            (byte)(milliseconds >> 24), (byte)(milliseconds >> 16), (byte)(milliseconds >> 8), (byte)milliseconds,
            (byte)(days >> 8), (byte)days
        ];
    }

    private static byte First(IReadOnlyList<byte> bytes) => bytes.Count > 0 ? bytes[0] : (byte)0;
    private static byte Second(IReadOnlyList<byte> bytes) => bytes.Count > 1 ? bytes[1] : (byte)0;

    private static IEnumerable<string> SplitTokens(string tokens)
        => (tokens ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant());
}

/// <summary>Live, per-association state of one report control block.</summary>
public sealed class MmsRcbRuntimeState
{
    public required MmsReadOnlyReportControlBlock Definition { get; init; }

    /// <summary>MMS reference of the control block, e.g. <c>SIE7SL87CTRL/LLN0$RP$A_URCB</c>.</summary>
    public required string MmsReference { get; init; }

    public string RptId = string.Empty;
    public bool RptEna;
    public bool Resv;
    public short ResvTms;
    public string DatSet = string.Empty;
    public uint ConfRev = 1;
    public byte[] OptFlds = new byte[2];
    public uint BufTm;
    public uint SqNum;
    public byte TrgOps;
    public uint IntgPd;
    public bool PurgeBuf;
    public byte[] EntryId = new byte[8];
    public DateTimeOffset TimeOfEntry = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-association reporting engine. It owns the runtime state of every RCB in the served model,
/// accepts client writes to RCB attributes (RptEna, GI, TrgOps, OptFlds, IntgPd, DatSet, RptID,
/// BufTm, Resv/ResvTms, PurgeBuf, EntryID), reflects that state on reads, and emits IEC 61850-8-1
/// unsolicited MMS InformationReport PDUs (general-interrogation and integrity) over the owning
/// association's socket via the injected send delegate.
/// </summary>
public sealed class MmsAssociationReportingRuntime : IMmsAssociationRuntime, IDisposable
{
    private const int DataAccessErrorTemporarilyUnavailable = 2;
    private const int DataAccessErrorObjectAccessDenied = 3;
    private const int DataAccessErrorTypeInconsistent = 7;
    private const int DataAccessErrorObjectNonExistent = 10;

    private readonly Func<MmsReadOnlyServerSession> _sessionFactory;
    private readonly Func<byte[], CancellationToken, Task> _sendPresentationPayload;
    private readonly int _presentationContextId;
    private readonly Action<string, bool, string>? _activity;
    private readonly Dictionary<string, MmsRcbRuntimeState> _states;
    private readonly Dictionary<string, Timer> _integrityTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public MmsAssociationReportingRuntime(
        Func<MmsReadOnlyServerSession> sessionFactory,
        Func<byte[], CancellationToken, Task> sendPresentationPayload,
        int presentationContextId = 3,
        Action<string, bool, string>? activity = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _sendPresentationPayload = sendPresentationPayload ?? throw new ArgumentNullException(nameof(sendPresentationPayload));
        _presentationContextId = presentationContextId;
        _activity = activity;

        var profile = _sessionFactory().Profile;
        _states = new Dictionary<string, MmsRcbRuntimeState>(StringComparer.OrdinalIgnoreCase);
        foreach (var rcb in profile.ReportControlBlocks)
        {
            var mmsReference = ToMmsReference(rcb.Reference);
            if (string.IsNullOrWhiteSpace(mmsReference) || _states.ContainsKey(mmsReference))
                continue;

            _states[mmsReference] = new MmsRcbRuntimeState
            {
                Definition = rcb,
                MmsReference = mmsReference,
                RptId = string.IsNullOrWhiteSpace(rcb.ReportId) ? mmsReference : rcb.ReportId,
                DatSet = ToMmsReference(rcb.DataSetReference),
                ConfRev = (uint)Math.Max(0, rcb.ConfRev),
                OptFlds = MmsReportControlBlockLayout.ParseOptionalFields(rcb.OptionalFields),
                BufTm = (uint)Math.Max(0, rcb.BufferTimeMs),
                TrgOps = MmsReportControlBlockLayout.ParseTriggerOptions(rcb.TriggerOptions),
                IntgPd = (uint)Math.Max(0, rcb.IntegrityPeriodMs)
            };
        }
    }

    public IReadOnlyCollection<MmsRcbRuntimeState> States => _states.Values;

    public bool TryReadRcbAttribute(string iecTarget, out MmsDataValue value)
    {
        value = MmsDataValue.Boolean(false);
        if (!TryResolve(iecTarget, out var state, out var attribute))
            return false;

        lock (_gate)
        {
            if (attribute.Length == 0)
            {
                value = MmsDataValue.Structure(
                    MmsReportControlBlockLayout.AttributesFor(state.Definition.Buffered)
                        .Select(a => AttributeValue(state, a.Name)));
                return true;
            }

            var known = MmsReportControlBlockLayout.AttributesFor(state.Definition.Buffered)
                .Any(a => string.Equals(a.Name, attribute, StringComparison.OrdinalIgnoreCase));
            if (!known)
                return false;

            value = AttributeValue(state, attribute);
            return true;
        }
    }

    public bool TryWriteRcbAttribute(string iecTarget, MmsDataValue value, out int dataAccessError)
    {
        dataAccessError = 0;
        if (!TryResolve(iecTarget, out var state, out var attribute) || attribute.Length == 0)
            return false;

        var sendGeneralInterrogation = false;
        lock (_gate)
        {
            switch (attribute.ToUpperInvariant())
            {
                case "RPTENA":
                    if (!TryBoolean(value, out var enable))
                    {
                        dataAccessError = DataAccessErrorTypeInconsistent;
                        break;
                    }

                    if (enable && !state.RptEna)
                    {
                        state.RptEna = true;
                        state.SqNum = 0;
                        RestartIntegrityTimerLocked(state);
                    }
                    else if (!enable && state.RptEna)
                    {
                        state.RptEna = false;
                        StopIntegrityTimerLocked(state);
                    }

                    break;

                case "GI":
                    if (!TryBoolean(value, out var gi))
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    else if (!state.RptEna)
                        dataAccessError = DataAccessErrorTemporarilyUnavailable;
                    else if (gi)
                        sendGeneralInterrogation = true;
                    break;

                case "RESV":
                    if (TryBoolean(value, out var resv))
                        state.Resv = resv;
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "RESVTMS":
                    if (TrySigned(value, out var resvTms))
                        state.ResvTms = (short)Math.Clamp(resvTms, short.MinValue, short.MaxValue);
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "PURGEBUF":
                    if (TryBoolean(value, out var purge))
                    {
                        state.PurgeBuf = false;
                        if (purge)
                        {
                            state.SqNum = 0;
                            System.Array.Clear(state.EntryId, 0, state.EntryId.Length);
                        }
                    }
                    else
                    {
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    }

                    break;

                case "RPTID":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryString(value, out var rptId))
                        state.RptId = rptId;
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "DATSET":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryString(value, out var datSet))
                        state.DatSet = datSet;
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "OPTFLDS":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryBitString(value, 2, out var optFlds))
                        state.OptFlds = optFlds;
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "TRGOPS":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryBitString(value, 1, out var trgOps))
                        state.TrgOps = trgOps.Length > 0 ? trgOps[0] : (byte)0;
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "BUFTM":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryUnsigned(value, out var bufTm))
                        state.BufTm = (uint)Math.Min(bufTm, uint.MaxValue);
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "INTGPD":
                    if (state.RptEna)
                        dataAccessError = DataAccessErrorObjectAccessDenied;
                    else if (TryUnsigned(value, out var intgPd))
                        state.IntgPd = (uint)Math.Min(intgPd, uint.MaxValue);
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "ENTRYID":
                    if (value.Kind == MmsDataKind.OctetString)
                        state.EntryId = value.RawValue.ToArray();
                    else
                        dataAccessError = DataAccessErrorTypeInconsistent;
                    break;

                case "SQNUM":
                case "CONFREV":
                case "TIMEOFENTRY":
                case "OWNER":
                    dataAccessError = DataAccessErrorObjectAccessDenied;
                    break;

                default:
                    dataAccessError = DataAccessErrorObjectNonExistent;
                    break;
            }
        }

        if (sendGeneralInterrogation)
            QueueReport(state, ReasonGeneralInterrogation);

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();
        lock (_gate)
        {
            foreach (var timer in _integrityTimers.Values)
                timer.Dispose();
            _integrityTimers.Clear();
        }

        _cts.Dispose();
    }

    private const byte ReasonGeneralInterrogation = 0x08; // reason bit 4 (GI)
    private const byte ReasonIntegrity = 0x10;            // reason bit 3 (integrity)

    private void RestartIntegrityTimerLocked(MmsRcbRuntimeState state)
    {
        StopIntegrityTimerLocked(state);
        if (!state.RptEna || state.IntgPd == 0 || !MmsReportControlBlockLayout.TriggerIntegrity(state.TrgOps))
            return;

        var period = TimeSpan.FromMilliseconds(Math.Max(100, state.IntgPd));
        _integrityTimers[state.MmsReference] = new Timer(_ => QueueReport(state, ReasonIntegrity), null, period, period);
    }

    private void StopIntegrityTimerLocked(MmsRcbRuntimeState state)
    {
        if (_integrityTimers.Remove(state.MmsReference, out var timer))
            timer.Dispose();
    }

    private void QueueReport(MmsRcbRuntimeState state, byte reason)
    {
        if (_disposed)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await SendReportAsync(state, reason, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Association closing.
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                _activity?.Invoke(state.MmsReference, false, $"InformationReport send failed: {ex.Message}");
            }
        }, CancellationToken.None);
    }

    private async Task SendReportAsync(MmsRcbRuntimeState state, byte reason, CancellationToken cancellationToken)
    {
        byte[] payload;
        int memberCount;
        string rptId;
        lock (_gate)
        {
            if (!state.RptEna)
                return;
            rptId = state.RptId;
        }

        var session = _sessionFactory();
        var dataSetResponse = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.ReadDataSet,
            Target = FromMmsReference(state.DatSet)
        });

        if (!dataSetResponse.IsSuccess || dataSetResponse.Values.Count == 0)
        {
            _activity?.Invoke(state.MmsReference, false,
                $"InformationReport skipped: DataSet '{state.DatSet}' unresolved ({dataSetResponse.Message}).");
            return;
        }

        lock (_gate)
        {
            state.SqNum = state.Definition.Buffered ? (state.SqNum + 1) & 0xFFFF : (state.SqNum + 1) & 0xFF;
            state.TimeOfEntry = DateTimeOffset.UtcNow;
            if (state.Definition.Buffered)
            {
                var stamp = state.TimeOfEntry.ToUnixTimeMilliseconds();
                for (var i = 0; i < 8; i++)
                    state.EntryId[7 - i] = (byte)(stamp >> (8 * i));
            }

            memberCount = dataSetResponse.Values.Count;
            payload = EncodeInformationReport(state, reason, dataSetResponse.Values, dataSetResponse.Items);
        }

        await _sendPresentationPayload(payload, cancellationToken).ConfigureAwait(false);
        _activity?.Invoke(state.MmsReference, true,
            $"InformationReport sent: rptId='{rptId}' reason={(reason == ReasonGeneralInterrogation ? "GI" : "integrity")} members={memberCount.ToString(CultureInfo.InvariantCulture)} sqNum={state.SqNum.ToString(CultureInfo.InvariantCulture)}.");
    }

    private byte[] EncodeInformationReport(
        MmsRcbRuntimeState state,
        byte reason,
        IReadOnlyList<MmsReadOnlyPoint> members,
        IReadOnlyList<string> memberReferences)
    {
        var entries = new List<byte[]>
        {
            MmsDataCodec.Encode(MmsDataValue.VisibleString(state.RptId)),
            MmsDataCodec.Encode(MmsDataValue.BitString(6, state.OptFlds))
        };

        if (MmsReportControlBlockLayout.OptionalFieldSequenceNumber(state.OptFlds))
            entries.Add(MmsDataCodec.Encode(MmsDataValue.Unsigned(state.SqNum)));

        if (MmsReportControlBlockLayout.OptionalFieldTimeStamp(state.OptFlds))
            entries.Add(MmsDataCodec.Encode(MmsDataValue.BinaryTime(MmsReportControlBlockLayout.ToBinaryTime6(state.TimeOfEntry))));

        if (MmsReportControlBlockLayout.OptionalFieldDataSet(state.OptFlds))
            entries.Add(MmsDataCodec.Encode(MmsDataValue.VisibleString(state.DatSet)));

        if (MmsReportControlBlockLayout.OptionalFieldBufferOverflow(state.OptFlds) && state.Definition.Buffered)
            entries.Add(MmsDataCodec.Encode(MmsDataValue.Boolean(false)));

        if (MmsReportControlBlockLayout.OptionalFieldEntryId(state.OptFlds) && state.Definition.Buffered)
            entries.Add(MmsDataCodec.Encode(MmsDataValue.OctetString(state.EntryId)));

        if (MmsReportControlBlockLayout.OptionalFieldConfRev(state.OptFlds))
            entries.Add(MmsDataCodec.Encode(MmsDataValue.Unsigned(state.ConfRev)));

        // Inclusion-bitstring: one bit per DataSet member, all set for GI/integrity snapshots.
        var memberCount = members.Count;
        var inclusionBytes = new byte[(memberCount + 7) / 8];
        for (var i = 0; i < memberCount; i++)
            inclusionBytes[i / 8] |= (byte)(0x80 >> (i % 8));
        var unusedInclusionBits = (byte)(inclusionBytes.Length * 8 - memberCount);
        entries.Add(MmsDataCodec.Encode(MmsDataValue.BitString(unusedInclusionBits, inclusionBytes)));

        if (MmsReportControlBlockLayout.OptionalFieldDataReference(state.OptFlds))
        {
            for (var i = 0; i < memberCount; i++)
            {
                var reference = i < memberReferences.Count ? ToMmsReference(memberReferences[i]) : ToMmsReference(members[i].Reference);
                entries.Add(MmsDataCodec.Encode(MmsDataValue.VisibleString(reference)));
            }
        }

        foreach (var member in members)
            entries.Add(MmsConfirmedRequestBerDispatcher.EncodePointAccessResult(member));

        if (MmsReportControlBlockLayout.OptionalFieldReasonCode(state.OptFlds))
        {
            for (var i = 0; i < memberCount; i++)
                entries.Add(MmsDataCodec.Encode(MmsDataValue.BitString(2, [reason])));
        }

        // InformationReport ::= SEQUENCE {
        //   variableAccessSpecification CHOICE { variableListName [1] ObjectName { vmd-specific [0] "RPT" } },
        //   listOfAccessResult [0] IMPLICIT SEQUENCE OF AccessResult }
        var vmdSpecificRpt = BerWriter.EncodeTlv(0x80, BerWriter.EncodeAscii("RPT"));
        var variableListName = BerWriter.EncodeTlv(0xA1, vmdSpecificRpt);
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, ConcatAll(entries));
        var informationReport = ConcatAll([variableListName, listOfAccessResult]);

        // UnconfirmedService ::= CHOICE { informationReport [0] IMPLICIT InformationReport }
        // Unconfirmed-PDU ::= [3] IMPLICIT SEQUENCE { unconfirmedService }
        var unconfirmedService = BerWriter.EncodeTlv(0xA0, informationReport);
        var unconfirmedPdu = BerWriter.EncodeTlv(0xA3, unconfirmedService);
        return MmsPresentation.WrapIsoPresentationPData(unconfirmedPdu, _presentationContextId);
    }

    private MmsDataValue AttributeValue(MmsRcbRuntimeState state, string attribute)
        => attribute.ToUpperInvariant() switch
        {
            "RPTID" => MmsDataValue.VisibleString(state.RptId),
            "RPTENA" => MmsDataValue.Boolean(state.RptEna),
            "RESV" => MmsDataValue.Boolean(state.Resv),
            "RESVTMS" => MmsDataValue.Integer(state.ResvTms),
            "DATSET" => MmsDataValue.VisibleString(state.DatSet),
            "CONFREV" => MmsDataValue.Unsigned(state.ConfRev),
            "OPTFLDS" => MmsDataValue.BitString(6, state.OptFlds),
            "BUFTM" => MmsDataValue.Unsigned(state.BufTm),
            "SQNUM" => MmsDataValue.Unsigned(state.SqNum),
            "TRGOPS" => MmsDataValue.BitString(2, [state.TrgOps]),
            "INTGPD" => MmsDataValue.Unsigned(state.IntgPd),
            "GI" => MmsDataValue.Boolean(false),
            "PURGEBUF" => MmsDataValue.Boolean(state.PurgeBuf),
            "ENTRYID" => MmsDataValue.OctetString(state.EntryId),
            "TIMEOFENTRY" => MmsDataValue.BinaryTime(MmsReportControlBlockLayout.ToBinaryTime6(state.TimeOfEntry)),
            _ => MmsDataValue.Boolean(false)
        };

    private bool TryResolve(string iecTarget, out MmsRcbRuntimeState state, out string attribute)
    {
        state = null!;
        attribute = string.Empty;
        var mmsTarget = ToMmsReference(iecTarget);
        if (string.IsNullOrWhiteSpace(mmsTarget))
            return false;

        if (_states.TryGetValue(mmsTarget, out var exact))
        {
            state = exact;
            return true;
        }

        foreach (var candidate in _states.Values)
        {
            if (!mmsTarget.StartsWith(candidate.MmsReference + "$", StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = mmsTarget[(candidate.MmsReference.Length + 1)..];
            if (remainder.Contains('$', StringComparison.Ordinal))
                return false; // RCB attributes are single-level.

            state = candidate;
            attribute = remainder;
            return true;
        }

        return false;
    }

    private static string ToMmsReference(string reference)
    {
        var normalized = (reference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        if (slash < 0)
            return normalized.Replace('.', '$');

        return normalized[..slash] + "/" + normalized[(slash + 1)..].Replace('.', '$');
    }

    private static string FromMmsReference(string reference)
    {
        var normalized = (reference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        if (slash < 0)
            return normalized.Replace('$', '.');

        return normalized[..slash] + "/" + normalized[(slash + 1)..].Replace('$', '.');
    }

    private static bool TryBoolean(MmsDataValue value, out bool result)
    {
        result = value.Kind == MmsDataKind.Boolean && value.Value is bool b && b;
        return value.Kind == MmsDataKind.Boolean;
    }

    private static bool TryString(MmsDataValue value, out string result)
    {
        result = value.Value as string ?? string.Empty;
        return value.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString;
    }

    private static bool TryUnsigned(MmsDataValue value, out ulong result)
    {
        switch (value.Kind)
        {
            case MmsDataKind.Unsigned when value.Value is ulong u:
                result = u;
                return true;
            case MmsDataKind.Integer when value.Value is long s && s >= 0:
                result = (ulong)s;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TrySigned(MmsDataValue value, out long result)
    {
        switch (value.Kind)
        {
            case MmsDataKind.Integer when value.Value is long s:
                result = s;
                return true;
            case MmsDataKind.Unsigned when value.Value is ulong u && u <= long.MaxValue:
                result = (long)u;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryBitString(MmsDataValue value, int minimumBytes, out byte[] result)
    {
        result = System.Array.Empty<byte>();
        if (value.Kind != MmsDataKind.BitString || value.RawValue.Count < 1)
            return false;

        // RawValue = [unusedBits][data...]
        var data = value.RawValue.Skip(1).ToArray();
        if (data.Length < minimumBytes)
            data = data.Concat(Enumerable.Repeat((byte)0, minimumBytes - data.Length)).ToArray();

        result = data;
        return true;
    }

    private static byte[] ConcatAll(IReadOnlyList<byte[]> parts)
    {
        var total = parts.Sum(x => x.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
            offset += part.Length;
        }

        return buffer;
    }
}
