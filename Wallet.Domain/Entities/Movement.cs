using Wallet.Domain.Entities.Status;

namespace Wallet.Domain.Entities
{
    public sealed class Movement
    {
        public Guid Id { get; private set; }
        public Guid WalletId { get; private set; }
        public MovementType Type { get; private set; }
        public decimal Amount { get; private set; }
        public decimal BalanceAfter { get; private set; }
        public MovementSource Source { get; private set; }
        public Guid? TransferId { get; private set; }
        public Guid? DevelopmentFundingId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        private Movement() { }

        public static IReadOnlyList<Movement> ForCompletedTransfer(Transfer transfer, Wallet source, Wallet destination)
        {
            if (transfer.Status != TransferStatus.Completed || source.Id != transfer.SourceWalletId
                || destination.Id != transfer.DestinationWalletId || source.Currency != transfer.Currency
                || destination.Currency != transfer.Currency)
                throw new ArgumentException("Las billeteras no corresponden a la transferencia completada.");
            return Array.AsReadOnly(new[]
            {
                ForTransfer(transfer, true, source.Balance),
                ForTransfer(transfer, false, destination.Balance)
             });
        }

        public static Movement ForTransfer(Transfer transfer, bool debit, decimal balanceAfter)
        {
            if (balanceAfter < 0 || balanceAfter > MoneyRules.MaxBalance || decimal.Round(balanceAfter, 2) != balanceAfter
                || !debit && balanceAfter < transfer.Amount)
                throw new ArgumentException("Saldo posterior inválido.");
            return new() 
            {
                Id = Guid.NewGuid(),
                WalletId = debit ? transfer.SourceWalletId : transfer.DestinationWalletId,
                Type = debit ? MovementType.Debit : MovementType.Credit,
                Amount = transfer.Amount,
                BalanceAfter = balanceAfter,
                Source = MovementSource.Transfer,
                TransferId = transfer.Id,
                CreatedAt = transfer.CreatedAt
            };
        }
    }
}
