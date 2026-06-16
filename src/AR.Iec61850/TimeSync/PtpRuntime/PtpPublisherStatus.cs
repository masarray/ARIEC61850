
namespace AR.Iec61850.TimeSync.PtpRuntime;

public sealed record PtpPublisherStatus(
    bool IsRunning,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastSentAt,
    long AnnounceSent,
    long SyncSent,
    long FollowUpSent,
    long PeerDelayResponsesSent,
    string LastError);
