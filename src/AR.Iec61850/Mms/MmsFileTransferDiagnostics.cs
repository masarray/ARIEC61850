using System.Text;

namespace AR.Iec61850.Mms;

/// <summary>
/// Captures a bounded, human-readable trace for the most recent MMS file transfer.
/// The trace is intended for field interoperability diagnostics and deliberately keeps
/// the first protocol failure even when the subsequent best-effort FileClose also fails.
/// </summary>
public sealed partial class MmsClientSession
{
    private readonly object _fileTransferDiagnosticSync = new();
    private FileTransferDiagnosticState? _activeFileTransferDiagnostic;
    private string _lastFileTransferDiagnosticText = string.Empty;

    /// <summary>
    /// Complete diagnostic text for the most recent MMS file transfer on this session.
    /// </summary>
    public string LastFileTransferDiagnosticText
    {
        get
        {
            lock (_fileTransferDiagnosticSync)
                return _lastFileTransferDiagnosticText;
        }
    }

    /// <summary>
    /// Last terminal receive-pump fault, when one occurred.
    /// </summary>
    public string ReceivePumpFaultMessage => _receivePump.LastFaultMessage;

    private void BeginFileTransferDiagnostic(string remotePath)
    {
        lock (_fileTransferDiagnosticSync)
        {
            _activeFileTransferDiagnostic = new FileTransferDiagnosticState(
                remotePath,
                DateTimeOffset.UtcNow);
            _lastFileTransferDiagnosticText = string.Empty;
        }
    }

    private void RecordFileTransferDiagnostic(
        string stage,
        bool success,
        string message,
        int? invokeId = null,
        int? fileReadStateMachineId = null,
        int? readOperation = null,
        long bytesTransferred = 0,
        bool? moreFollows = null,
        string? requestHex = null,
        string? responseHex = null,
        Exception? exception = null)
    {
        lock (_fileTransferDiagnosticSync)
        {
            _activeFileTransferDiagnostic ??= new FileTransferDiagnosticState(
                remotePath: string.Empty,
                DateTimeOffset.UtcNow);

            _activeFileTransferDiagnostic.Add(new FileTransferDiagnosticEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Stage = stage,
                Success = success,
                Message = message,
                InvokeId = invokeId,
                FileReadStateMachineId = fileReadStateMachineId,
                ReadOperation = readOperation,
                BytesTransferred = bytesTransferred,
                MoreFollows = moreFollows,
                RequestHex = requestHex ?? string.Empty,
                ResponseHex = responseHex ?? string.Empty,
                ExceptionType = exception?.GetType().FullName ?? string.Empty,
                ExceptionMessage = exception?.Message ?? string.Empty,
                ReceiveRouting = LastReceiveRoutingSummary,
                ReceivePumpFault = _receivePump.LastFaultMessage,
                AssociationState = State.ToString(),
                TransportConnected = IsTransportConnected,
                ReceivePumpRunning = IsReceivePumpRunning,
                PendingConfirmedOperations = PendingConfirmedOperationCount
            });
        }
    }

    private void CompleteFileTransferDiagnostic(
        bool success,
        string message,
        long bytesTransferred,
        int readOperations,
        int? fileReadStateMachineId,
        bool remoteFileClosed)
    {
        lock (_fileTransferDiagnosticSync)
        {
            _activeFileTransferDiagnostic ??= new FileTransferDiagnosticState(
                remotePath: string.Empty,
                DateTimeOffset.UtcNow);

            _activeFileTransferDiagnostic.CompletedUtc = DateTimeOffset.UtcNow;
            _activeFileTransferDiagnostic.Success = success;
            _activeFileTransferDiagnostic.CompletionMessage = message;
            _activeFileTransferDiagnostic.BytesTransferred = bytesTransferred;
            _activeFileTransferDiagnostic.ReadOperations = readOperations;
            _activeFileTransferDiagnostic.FileReadStateMachineId = fileReadStateMachineId;
            _activeFileTransferDiagnostic.RemoteFileClosed = remoteFileClosed;
            _activeFileTransferDiagnostic.FinalAssociationState = State.ToString();
            _activeFileTransferDiagnostic.FinalTransportConnected = IsTransportConnected;
            _activeFileTransferDiagnostic.FinalReceivePumpRunning = IsReceivePumpRunning;
            _activeFileTransferDiagnostic.FinalPendingConfirmedOperations = PendingConfirmedOperationCount;
            _activeFileTransferDiagnostic.FinalReceiveRouting = LastReceiveRoutingSummary;
            _activeFileTransferDiagnostic.FinalReceivePumpFault = _receivePump.LastFaultMessage;

            _lastFileTransferDiagnosticText = _activeFileTransferDiagnostic.BuildText();
            _activeFileTransferDiagnostic = null;
        }
    }

    private sealed class FileTransferDiagnosticState
    {
        private const int MaximumEntries = 64;
        private readonly List<FileTransferDiagnosticEntry> _entries = new();
        private FileTransferDiagnosticEntry? _primaryFailure;
        private int _omittedEntryCount;

        public FileTransferDiagnosticState(string remotePath, DateTimeOffset startedUtc)
        {
            RemotePath = remotePath;
            StartedUtc = startedUtc;
        }

        public string RemotePath { get; }
        public DateTimeOffset StartedUtc { get; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public bool Success { get; set; }
        public string CompletionMessage { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public int ReadOperations { get; set; }
        public int? FileReadStateMachineId { get; set; }
        public bool RemoteFileClosed { get; set; }
        public string FinalAssociationState { get; set; } = string.Empty;
        public bool FinalTransportConnected { get; set; }
        public bool FinalReceivePumpRunning { get; set; }
        public int FinalPendingConfirmedOperations { get; set; }
        public string FinalReceiveRouting { get; set; } = string.Empty;
        public string FinalReceivePumpFault { get; set; } = string.Empty;

        public void Add(FileTransferDiagnosticEntry entry)
        {
            if (!entry.Success && _primaryFailure == null)
                _primaryFailure = entry;

            if (_entries.Count < MaximumEntries)
            {
                _entries.Add(entry);
                return;
            }

            _omittedEntryCount++;
            if (!entry.Success)
                _entries[^1] = entry;
        }

        public string BuildText()
        {
            var builder = new StringBuilder(4096);
            builder.AppendLine("ARSAS / ARIEC61850 MMS FILE TRANSFER DIAGNOSTIC");
            builder.AppendLine(new string('=', 72));
            builder.AppendLine($"Started UTC       : {StartedUtc:O}");
            builder.AppendLine($"Completed UTC     : {(CompletedUtc.HasValue ? CompletedUtc.Value.ToString("O") : "-")}");
            builder.AppendLine($"Remote path       : {ValueOrDash(RemotePath)}");
            builder.AppendLine($"Result            : {(Success ? "SUCCESS" : "FAILED")}");
            builder.AppendLine($"Completion        : {ValueOrDash(CompletionMessage)}");
            builder.AppendLine($"FRSM              : {(FileReadStateMachineId?.ToString() ?? "not-opened")}");
            builder.AppendLine($"Read operations   : {ReadOperations}");
            builder.AppendLine($"Bytes transferred : {BytesTransferred}");
            builder.AppendLine($"Remote closed     : {RemoteFileClosed}");

            if (_primaryFailure != null)
            {
                builder.AppendLine();
                builder.AppendLine("PRIMARY FAILURE");
                builder.AppendLine(new string('-', 72));
                AppendEntry(builder, _primaryFailure);
            }

            builder.AppendLine();
            builder.AppendLine("PROTOCOL TIMELINE");
            builder.AppendLine(new string('-', 72));
            if (_entries.Count == 0)
            {
                builder.AppendLine("No protocol operation was recorded.");
            }
            else
            {
                foreach (var entry in _entries)
                {
                    AppendEntry(builder, entry);
                    builder.AppendLine();
                }
            }

            if (_omittedEntryCount > 0)
                builder.AppendLine($"... {_omittedEntryCount} additional successful operation(s) omitted by the bounded trace.");

            builder.AppendLine("FINAL SESSION STATE");
            builder.AppendLine(new string('-', 72));
            builder.AppendLine($"Association       : {ValueOrDash(FinalAssociationState)}");
            builder.AppendLine($"Transport ready   : {FinalTransportConnected}");
            builder.AppendLine($"Receive pump      : {FinalReceivePumpRunning}");
            builder.AppendLine($"Pending confirmed : {FinalPendingConfirmedOperations}");
            builder.AppendLine($"Receive routing   : {ValueOrDash(FinalReceiveRouting)}");
            builder.AppendLine($"Receive pump fault: {ValueOrDash(FinalReceivePumpFault)}");
            return builder.ToString().TrimEnd();
        }

        private static void AppendEntry(StringBuilder builder, FileTransferDiagnosticEntry entry)
        {
            builder.AppendLine($"[{entry.TimestampUtc:O}] {entry.Stage} => {(entry.Success ? "OK" : "FAILED")}");
            builder.AppendLine($"Message           : {ValueOrDash(entry.Message)}");
            builder.AppendLine($"Invoke ID         : {(entry.InvokeId?.ToString() ?? "-")}");
            builder.AppendLine($"FRSM              : {(entry.FileReadStateMachineId?.ToString() ?? "-")}");
            builder.AppendLine($"Read operation    : {(entry.ReadOperation?.ToString() ?? "-")}");
            builder.AppendLine($"Bytes before/after: {entry.BytesTransferred}");
            builder.AppendLine($"moreFollows       : {(entry.MoreFollows?.ToString() ?? "-")}");
            builder.AppendLine($"Exception         : {ValueOrDash(CombineException(entry))}");
            builder.AppendLine($"Association       : {entry.AssociationState}; transport={entry.TransportConnected}; pump={entry.ReceivePumpRunning}; pending={entry.PendingConfirmedOperations}");
            builder.AppendLine($"Receive routing   : {ValueOrDash(entry.ReceiveRouting)}");
            builder.AppendLine($"Receive pump fault: {ValueOrDash(entry.ReceivePumpFault)}");
            builder.AppendLine($"Request hex       : {ValueOrDash(entry.RequestHex)}");
            builder.AppendLine($"Response hex      : {ValueOrDash(entry.ResponseHex)}");
        }

        private static string CombineException(FileTransferDiagnosticEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.ExceptionType))
                return string.Empty;
            return string.IsNullOrWhiteSpace(entry.ExceptionMessage)
                ? entry.ExceptionType
                : $"{entry.ExceptionType}: {entry.ExceptionMessage}";
        }

        private static string ValueOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private sealed class FileTransferDiagnosticEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }
        public string Stage { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int? InvokeId { get; init; }
        public int? FileReadStateMachineId { get; init; }
        public int? ReadOperation { get; init; }
        public long BytesTransferred { get; init; }
        public bool? MoreFollows { get; init; }
        public string RequestHex { get; init; } = string.Empty;
        public string ResponseHex { get; init; } = string.Empty;
        public string ExceptionType { get; init; } = string.Empty;
        public string ExceptionMessage { get; init; } = string.Empty;
        public string ReceiveRouting { get; init; } = string.Empty;
        public string ReceivePumpFault { get; init; } = string.Empty;
        public string AssociationState { get; init; } = string.Empty;
        public bool TransportConnected { get; init; }
        public bool ReceivePumpRunning { get; init; }
        public int PendingConfirmedOperations { get; init; }
    }
}