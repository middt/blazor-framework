using Suges.Framework.Common.Model.Email;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Common.Email
{
    public interface IEmailSender
    {
        EmailResponseModel Send(MailMessage mail);
    }
}
