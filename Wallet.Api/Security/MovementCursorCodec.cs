using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Wallet.Application;

namespace Wallet.Api.Security;

public sealed class MovementCursorCodec(IDataProtectionProvider provider) : IMovementCursorCodec
{
    private IDataProtector Protector(Guid walletId) => provider.CreateProtector("Wallet.MovementHistory.v1", walletId.ToString("N"));
    public string Encode(Guid walletId, MovementPosition position) => Protector(walletId).Protect(JsonSerializer.Serialize(position));
    public MovementPosition? Decode(Guid walletId, string cursor)
    {
        if (cursor.Length is 0 or > 2048) return null;
        try
        {
            var position = JsonSerializer.Deserialize<MovementPosition>(Protector(walletId).Unprotect(cursor));
            return position is { Id: var id, CreatedAt.Offset: var offset } && id != Guid.Empty && offset == TimeSpan.Zero ? position : null;
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }
}
