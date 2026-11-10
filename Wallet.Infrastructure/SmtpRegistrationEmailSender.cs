using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class MailOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}

public sealed class SmtpRegistrationEmailSender(MailOptions options, ILogger<SmtpRegistrationEmailSender> logger) : IRegistrationEmailSender
{
    public async Task SendCodeAsync(string email, string code, CancellationToken ct)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(options.Username, "Padi"),
                Subject = "Tu código de registro de Padi",
                Body = $"Tu código de registro es {code}. Vence en 10 minutos. Si no lo pediste, podés ignorar este correo."
            };
            message.To.Add(new MailAddress(email));

            using var smtp = new SmtpClient(options.Host, options.Port)
            {
                EnableSsl = options.EnableSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(options.Username, options.Password)
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await smtp.SendMailAsync(message, timeout.Token);
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("No se pudo enviar el código de registro por SMTP ({Status}).",
                ex is SmtpException smtpError ? smtpError.StatusCode.ToString() : ex.GetType().Name);
            throw new RegistrationEmailUnavailableException();
        }
    }
}
