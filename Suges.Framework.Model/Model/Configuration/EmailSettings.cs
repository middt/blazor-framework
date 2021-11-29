namespace Suges.Framework.Api.Configuration.Model
{
    public class EmailSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string From { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool DefaultCredentials { get; set; }

        public bool EnableSsl { get; set; }
    }
}
