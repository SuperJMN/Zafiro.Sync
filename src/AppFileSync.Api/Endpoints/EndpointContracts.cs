namespace AppFileSync.Api.Endpoints;

public static partial class AppFileSyncEndpoints
{
    private sealed record AppsResponse(IReadOnlyList<AppResponse> Apps);
    private sealed record AppResponse(string AppId, string DisplayName, int MaxPlaintextBytes);
    private sealed record ChangesResponse(string NextCursor, bool HasMore, IReadOnlyList<ChangeResponse> Changes);
    private sealed record ChangeResponse(string FileId, long Revision, bool IsDeleted, int PlaintextSizeBytes, int CiphertextSizeBytes, string CipherHash, string EncryptedMetadata);
    private sealed record FileResponse(string FileId, long Revision, string EncryptedMetadata, string Ciphertext, string CipherHash, int PlaintextSizeBytes, int CiphertextSizeBytes, DateTimeOffset UpdatedAt);
    private sealed record PutFileRequest(long? BaseRevision, Guid DeviceId, string EncryptedMetadata, string Ciphertext, string CipherHash, int PlaintextSizeBytes);
    private sealed record WriteResponse(long Revision, string Cursor, DateTimeOffset UpdatedAt);
    private sealed record DeleteFileRequest(long BaseRevision, Guid DeviceId);
    private sealed record DeleteResponse(long Revision, string Cursor, bool Deleted);
    private sealed record DevicesResponse(IReadOnlyList<DeviceResponse> Devices);
    private sealed record DeviceResponse(Guid DeviceId, string DisplayName, string PublicKey, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
    private sealed record RegisterDeviceRequest(Guid DeviceId, string DisplayName, string PublicKey);
    private sealed record KeyEnvelopesResponse(IReadOnlyList<KeyEnvelopeResponse> KeyEnvelopes);
    private sealed record KeyEnvelopeResponse(Guid DeviceId, int EnvelopeVersion, string EncryptedAppKey, DateTimeOffset CreatedAt);
    private sealed record AddKeyEnvelopeRequest(Guid DeviceId, int EnvelopeVersion, string EncryptedAppKey);
    private sealed record CreateAppIdentityChallengeRequest(string AppId, string PublicKey);
    private sealed record AppIdentityChallengeResponse(Guid ChallengeId, string AppId, string PublicKey, string Challenge, DateTimeOffset ExpiresAt);
    private sealed record CreateAppIdentitySessionRequest(Guid ChallengeId, string PublicKey, string Signature, Guid DeviceId);
    private sealed record AppIdentitySessionResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer");
}
