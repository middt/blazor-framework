using System.Net.Mail;
using Middt.Framework.Common.Model.Email;

namespace Middt.Framework.Common.Email;

public interface IEmailSender
{
    Task<EmailResponseModel> SendAsync(MailMessage mail, CancellationToken cancellationToken = default);
}
