using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
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
                Subject = "Tu código para empezar en Padi",
                SubjectEncoding = Encoding.UTF8
            };
            message.To.Add(new MailAddress(email));

            var plainText = $"""
                ¡Hola! Qué lindo tenerte por acá

                Vimos que estás creando una cuenta en Padi. Te dejamos el código para que puedas seguir desde la app.

                {code}

                Usalo dentro de los próximos 10 minutos. Si no lo pediste, podés ignorar este correo.

                ¡Nos vemos en la app!
                El equipo de Padi
                """;

            var html = $"""
                <!doctype html>
                <html lang="es">
                <head><meta charset="utf-8"></head>
                <body style="margin:0;background:#f0edf7;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f0edf7;">
                    <tr><td align="center" style="padding:24px 12px;">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:552px;background:#F5F0FF;color:#2A0E7A;font-family:Arial,sans-serif;border:1px solid #ded5ee;border-radius:12px;overflow:hidden;">
                        <tr><td bgcolor="#5B2EE0" style="padding:20px 28px;color:#FFFFFF;font-size:24px;font-weight:bold;">padi.</td></tr>
                        <tr><td style="padding:24px 28px;font-size:16px;line-height:1.45;">
                          <h1 style="font-size:24px;margin:0 0 16px;">¡Hola! Qué lindo tenerte por acá</h1>
                          <p style="margin:0 0 16px;">Vimos que estás creando una cuenta en Padi. Te dejamos el código para que puedas seguir desde la app.</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                            <tr><td align="center" bgcolor="#FFD54A" style="padding:16px;color:#2A0E7A;font-family:monospace;font-size:32px;font-weight:bold;">{WebUtility.HtmlEncode(code)}</td></tr>
                          </table>
                          <p style="margin:16px 0;">Usalo dentro de los próximos 10 minutos. Si no lo pediste, podés ignorar este correo.</p>
                          <p style="margin:0;">¡Nos vemos en la app!<br>El equipo de Padi</p>
                        </td></tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;

            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainText, Encoding.UTF8, MediaTypeNames.Text.Plain));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));

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
