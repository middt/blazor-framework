using Middt.Framework.Common.Model.Email;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Common.Email
{
    public interface IEmailSender
    {
        EmailResponseModel Send(MailMessage mail);
    }
}
