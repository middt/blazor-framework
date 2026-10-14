using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Model.Email;
using Middt.Framework.Model.Model.Enumerations;

namespace Middt.Framework.Common.Email;

/// <summary>
/// SMTP based <see cref="IEmailSender"/>. A new <see cref="SmtpClient"/> is created per message
/// (the type is not thread-safe) and server certificates are validated by the OS.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<EmailResponseModel> SendAsync(MailMessage mail, CancellationToken cancellationToken = default)
    {
        if (mail is null)
        {
            return new EmailResponseModel { Result = ResultEnum.Error, MessageList = ["Lütfen göndermek için e-posta bilgilerini giriniz."] };
        }

        var settings = options.Value;
        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = settings.EnableSsl,
            UseDefaultCredentials = settings.DefaultCredentials,
        };

        if (!settings.DefaultCredentials && !string.IsNullOrEmpty(settings.UserName))
        {
            smtp.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        try
        {
            mail.From ??= new MailAddress(settings.From);
            await smtp.SendMailAsync(mail, cancellationToken);
            return new EmailResponseModel { Result = ResultEnum.Success, Data = true };
        }
        catch (SmtpException ex)
        {
            // Recipient addresses are personal data: log only their count.
            logger.LogError(ex, "E-mail could not be sent to {RecipientCount} recipient(s)", mail.To.Count + mail.CC.Count + mail.Bcc.Count);
            return new EmailResponseModel { Result = ResultEnum.Error, Data = false, MessageList = ["E-posta gönderilemedi."] };
        }
    }
}
