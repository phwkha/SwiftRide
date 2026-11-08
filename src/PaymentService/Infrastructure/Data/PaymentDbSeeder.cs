using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Data;

/// <summary>
/// Tao schema PayDB va seed vi mau theo bo GUID co dinh cua du an
/// (khop InMemoryIdentityStore cua ApiGateway).
/// </summary>
public static class PaymentDbSeeder
{
    public static readonly Guid Rider1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Driver1Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Driver2Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Driver3Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid PlatformId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid Rider1MomoTokenId = Guid.Parse("e7b0a708-3bb8-4fc1-a589-cf7320b7a8a1");

    public static async Task SeedAsync(PaymentDbContext dbContext, CancellationToken ct = default)
    {
        await dbContext.Database.EnsureCreatedAsync(ct);

        if (!await dbContext.Accounts.AnyAsync(ct))
        {
            dbContext.Accounts.AddRange(
                Wallet(Rider1Id, "Rider", 500000m),
                Wallet(Driver1Id, "Driver", 100000m),
                Wallet(Driver2Id, "Driver", 100000m),
                Wallet(Driver3Id, "Driver", 200000m),
                new Account { OwnerId = PlatformId, OwnerType = "Platform", AccountType = AccountType.PlatformRevenue, Balance = 0m },
                new Account { OwnerId = PlatformId, OwnerType = "Platform", AccountType = AccountType.EscrowHolding, Balance = 10000000m });
        }

        if (!await dbContext.PaymentTokens.AnyAsync(ct))
        {
            dbContext.PaymentTokens.Add(new PaymentToken
            {
                Id = Rider1MomoTokenId,
                UserId = Rider1Id,
                Provider = "Momo",
                Token = "tok_momo_88a91b2c3d",
                MaskedToken = "**** 5678",
                IsDefault = true
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static Account Wallet(Guid ownerId, string ownerType, decimal balance)
        => new() { OwnerId = ownerId, OwnerType = ownerType, AccountType = AccountType.WalletLiability, Balance = balance };
}
