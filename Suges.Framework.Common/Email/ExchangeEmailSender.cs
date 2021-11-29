using Suges.Framework.Api.Configuration.Model;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Model.Email;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Suges.Framework.Common.Email
{
    public class ExchangeEmailSender : IEmailSender
    {       
        readonly SmtpClient smtp = new SmtpClient();

        IBaseConfiguration baseConfiguration;
        EmailSettings emailSettings;
        IBaseLog baseLog;

        public ExchangeEmailSender(IBaseConfiguration _baseConfiguration, IBaseLog _baseLog)
        {
            baseConfiguration = _baseConfiguration;
            baseLog = _baseLog;

            LoadConfig();
        }
        protected void LoadConfig()
        {
            emailSettings = baseConfiguration.Get<EmailSettings>();

            smtp.Host = emailSettings.Host;
            smtp.Port = emailSettings.Port;
            smtp.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
            smtp.UseDefaultCredentials = emailSettings.DefaultCredentials;
            //smtp.EnableSsl = emailSettings.EnableSsl;
            ServicePointManager.ServerCertificateValidationCallback = delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };

            smtp.Credentials = new NetworkCredential(emailSettings.UserName, emailSettings.Password);
        }

        public EmailResponseModel Send(MailMessage mail)
        {
            EmailResponseModel emailResponseModel = new EmailResponseModel();
            if (mail != null)
            {

                try
                {
                    mail.From = new MailAddress(emailSettings.From);
                    smtp.Send(mail);
                    emailResponseModel.Data = true;
                }
                catch (Exception ex)
                {
                    emailResponseModel.Data = false;
                    emailResponseModel.Result = ResultEnum.Error;
                    emailResponseModel.MessageList.Add(ex.ToString());

                    baseLog.Error(ex);
                }
            }
            else
            {
                emailResponseModel.Data = false;
                emailResponseModel.Result = ResultEnum.Error;
                emailResponseModel.MessageList.Add("Lütfen göndermek için e-posta bilgilerini giriniz.");
            }
            return emailResponseModel;
        }
    }
}
