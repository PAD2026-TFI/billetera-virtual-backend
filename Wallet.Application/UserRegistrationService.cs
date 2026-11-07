using System.ComponentModel.DataAnnotations;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

/// <summary>
/// Servicio de dominio de aplicación que orquesta el onboarding automático de usuarios.
/// Garantiza la creación atómica del Usuario, generación de CVU/Alias únicos y creación de su Billetera virtual
/// dentro de la misma transacción de base de datos (Unit of Work).
/// </summary>
public sealed class UserRegistrationService(
    IUserRepository users,
    IWalletRepository wallets,
    IPasswordService passwords,
    ICvuGenerator cvuGenerator,
    IAliasGenerator aliasGenerator,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ICardRepository? cards = null)
{
    private static bool IsValid(object request) => Validator.TryValidateObject(
        request, new ValidationContext(request), new List<ValidationResult>(), true);

    /// <summary>
    /// Registra un nuevo usuario en la plataforma generando automáticamente su CVU y Alias y creando su Billetera asociada de forma transaccional.
    /// </summary>
    public async Task<Result<UserRegistrationResult>> RegisterAsync(RegisterUserRequest request, CancellationToken ct)
    {
        if (!IsValid(request) || string.IsNullOrWhiteSpace(request.Password))
            return Result<UserRegistrationResult>.Failure("INVALID_INPUT", "Revisá los campos obligatorios y sus límites.");

        var normalizedEmail = User.NormalizeEmail(request.Email);
        var existingUser = await users.FindByEmailAsync(normalizedEmail, ct);
        if (existingUser is not null)
            return Result<UserRegistrationResult>.Failure("EMAIL_ALREADY_REGISTERED", "El email ya está registrado.");

        var now = clock.GetUtcNow();
        var effectiveDni = string.IsNullOrWhiteSpace(request.Dni) ? "00000000" : request.Dni.Trim();

        User user;
        try
        {
            user = User.RegisterClient(effectiveDni, request.FirstName, request.LastName, request.Email, now);
        }
        catch (ArgumentException)
        {
            return Result<UserRegistrationResult>.Failure("INVALID_INPUT", "Datos de usuario inválidos.");
        }

        user.SetPasswordHash(passwords.Hash(user, request.Password));

        string cvu;
        var cvuAttempts = 0;
        do
        {
            if (++cvuAttempts > 10)
                throw new InvalidOperationException("Could not generate a unique CVU after multiple attempts.");

            cvu = cvuGenerator.Generate();
        } while (await wallets.ExistsCvuAsync(cvu, ct));

        string alias;
        var aliasAttempts = 0;
        do
        {
            if (++aliasAttempts > 10)
                throw new InvalidOperationException("Could not generate a unique Alias after multiple attempts.");

            alias = aliasGenerator.Generate();
        } while (await wallets.ExistsAliasAsync(alias, ct));

        var wallet = DomainWallet.CreateForUser(user.Id, cvu, alias, now);
        user.LinkWallet(wallet.Id);

        var last4 = $"{Random.Shared.Next(1000, 9999)}";
        var cardHolder = $"{user.FirstName} {user.LastName}".Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cardHolder)) cardHolder = "CLIENTE TITULAR";

        var card = Card.Create(
            user.Id,
            cardHolder,
            "VISA DÉBITO",
            CardType.Debit,
            last4,
            12,
            now.Year + 5,
            now);

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await users.AddAsync(user, ct);
            await wallets.CreateAsync(wallet, ct);
            if (cards is not null)
            {
                await cards.CreateAsync(card, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DuplicateEmailException)
        {
            await tx.RollbackAsync(ct);
            return Result<UserRegistrationResult>.Failure("EMAIL_ALREADY_REGISTERED", "El email ya está registrado.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var result = new UserRegistrationResult(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Dni,
            wallet.Id,
            wallet.Cvu,
            wallet.Alias);

        return Result<UserRegistrationResult>.Success(result);
    }
}
