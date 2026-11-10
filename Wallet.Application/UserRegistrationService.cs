using System.ComponentModel.DataAnnotations;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

/// <summary>
/// Servicio de dominio de aplicación que orquesta el onboarding automático de usuarios.
/// Garantiza la verificación de identidad (RENAPER), la creación atómica del Usuario,
/// generación de CVU/Alias únicos y creación de su Billetera virtual dentro de la misma transacción.
/// </summary>
public sealed class UserRegistrationService(
    IUserRepository users,
    IWalletRepository wallets,
    IPasswordService passwords,
    ICvuGenerator cvuGenerator,
    IAliasGenerator aliasGenerator,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IIdentityVerifier verifier,
    IdentityVerificationOptions? verificationOptions = null,
    ICardRepository? cards = null,
    DevelopmentFundingService? fundingService = null)
{
    public const decimal WelcomeBonusAmount = 50_000m;

    private static bool IsValid(object request) => Validator.TryValidateObject(
        request, new ValidationContext(request), new List<ValidationResult>(), true);

    /// <summary>
    /// Registra un nuevo usuario en la plataforma validando previamente su identidad ante el registro civil,
    /// asegurando que el DNI sea único y generando automáticamente su CVU, Alias y Billetera asociada.
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
        var effectiveDni = request.Dni.Trim();

        // 1. Verificación previa de unicidad de DNI en repositorio local
        if (await users.ExistsByDniAsync(effectiveDni, ct))
            return Result<UserRegistrationResult>.Failure("DNI_ALREADY_REGISTERED", "Ya existe un usuario registrado con ese DNI.");

        // 2. Consulta al verificador de identidad (RENAPER) fuera de transacción
        var isVerificationEnabled = verificationOptions?.Enabled ?? false;
        var isVerified = false;

        if (isVerificationEnabled)
        {
            VerifiedIdentity? found;
            try
            {
                found = await verifier.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, effectiveDni), ct);
            }
            catch (IdentityProviderUnavailableException)
            {
                return Result<UserRegistrationResult>.Failure("IDENTITY_PROVIDER_UNAVAILABLE", "El servicio de validación de identidad no está disponible temporalmente.");
            }

            if (found is null)
                return Result<UserRegistrationResult>.Failure("IDENTITY_NOT_FOUND", "El DNI ingresado no fue encontrado en el registro nacional de las personas.");

            if (!IdentityNameMatcher.Matches(request.FirstName, request.LastName, found.FullName))
                return Result<UserRegistrationResult>.Failure("IDENTITY_MISMATCH", "Los datos de nombre y apellido no coinciden con los registrados ante RENAPER.");

            isVerified = true;
        }

        // 3. Creación de la entidad de dominio User
        User user;
        try
        {
            user = User.RegisterClient(effectiveDni, request.FirstName, request.LastName, request.Email, now);
        }
        catch (ArgumentException)
        {
            return Result<UserRegistrationResult>.Failure("INVALID_INPUT", "Datos de usuario inválidos.");
        }

        if (isVerified)
        {
            user.MarkIdentityVerified(now);
        }

        user.SetAccountType(request.AccountType);
        user.SetPasswordHash(passwords.Hash(user, request.Password));

        // 4. Generación de credenciales financieras (CVU y Alias)
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
            VirtualCardCredentials.Brand,
            CardType.Debit,
            last4,
            12,
            now.Year + 5,
            now);

        // 5. Persistencia atómica de entidades en la base de datos
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
        catch (DuplicateDniException)
        {
            await tx.RollbackAsync(ct);
            return Result<UserRegistrationResult>.Failure("DNI_ALREADY_REGISTERED", "Ya existe un usuario registrado con ese DNI.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // 6. Acreditación de fondos de bienvenida
        if (fundingService is not null)
        {
            try
            {
                await fundingService.FundAsync(new DevelopmentFundingRequest(wallet.Id, WelcomeBonusAmount, wallet.Id), ct);
            }
            catch (StorageUnavailableException)
            {
                wallet.Credit(WelcomeBonusAmount);
            }
        }
        else
        {
            wallet.Credit(WelcomeBonusAmount);
        }

        var result = new UserRegistrationResult(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Dni,
            wallet.Id,
            wallet.Cvu,
            wallet.Alias,
            user.AccountType,
            IdentityVerified: isVerified);

        return Result<UserRegistrationResult>.Success(result);
    }
}
