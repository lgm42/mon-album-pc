namespace MonAlbumPc.Server;

// JSON bodies of protocol v1 (docs/protocol.md), serialized in camelCase.

public sealed record PairRequest(string? Token, string? Code, PairDevice Device);

public sealed record PairDevice(string Name, string Model, string? AndroidVersion, string? AppVersion, int? AppVersionCode,
    string? HardwareId = null);

public sealed record PairResponse(Guid DeviceId, string Secret, Guid PcId, string PcName, string AlbumName, string CertSha256);

public sealed record ApkInfo(int VersionCode, string VersionName, string Sha256, long Size);

public sealed record StatusResponse(
    int Protocol,
    string PcName,
    string AlbumName,
    bool DestinationAvailable,
    string? Busy,
    ApkInfo? Apk,
    DateTimeOffset ServerTime);

public sealed record CheckItem(string Sha256, long Size);

public sealed record CheckRequest(IReadOnlyList<CheckItem> Items);

public sealed record CheckResponse(IReadOnlyList<string> Have);

public sealed record NameCheckItem(string Name, long Size);

public sealed record NameCheckRequest(IReadOnlyList<NameCheckItem> Items);

/// <summary>Positions, in the request, of the files the PC already has.</summary>
public sealed record NameCheckResponse(IReadOnlyList<int> Have);

/// <param name="RunPosition">Rank of this file in the phone's run (1 = first), for the status; absent from older phones.</param>
/// <param name="RunTotal">Number of files of the phone's run; absent from older phones.</param>
public sealed record UploadRequest(
    string Sha256,
    long Size,
    string Name,
    string? SourceFolder,
    string? Mime,
    DateTimeOffset? TakenAt,
    DateTimeOffset? ModifiedAt,
    int? RunPosition = null,
    int? RunTotal = null);

public sealed record UploadResponse(string Status, string? UploadId = null, long? Offset = null);

public sealed record OffsetResponse(long Offset, string? Status = null);

/// <summary>What is left on the phone at the end of its run.</summary>
public sealed record BackupFinishedRequest(int Pending, int Errors);

public sealed record ErrorBody(ErrorDetail Error);

public sealed record ErrorDetail(string Code, string Message);
