using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Middt.Framework.Api.Swagger;
using Middt.Framework.Common.Email;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.SignalR;
using Middt.Template.Common.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace Middt.Template.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.1")]

    public class WeatherForecastController : Controller
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        IHubContext<TestHub, IBaseSignalRClient<string>> testHub;



        IHubContext<TestHub> testHub22;
        IEmailSender emailSender;
        public WeatherForecastController(
            IBaseLog baseLog, 
            IHubContext<TestHub, 
                IBaseSignalRClient<string>> _testHub,
            IHubContext<TestHub> _testHub22,
            IEmailSender _emailSender            )

        {
            baseLog.Info("Helloooo");

            testHub = _testHub;

            testHub22 = _testHub22;
            emailSender = _emailSender;
        }

        [HttpGet("[action]")]
        [SwaggerTagAttribute(true)]
        //[IpControl(CorsName = "MyPolicy")]
        public BaseResponseDataModel<List<WeatherForecastModel>> GetAll()
        {

            MailMessage mail = new MailMessage();

                mail.To.Add("mtosun@subilgi.com.tr");
            mail.Body = "Message body from izmirgaz";
            mail.Subject = "From İzmirgaz";

            mail.IsBodyHtml = false;
            mail.BodyEncoding = System.Text.Encoding.UTF8;
            mail.SubjectEncoding = System.Text.Encoding.UTF8;


  
            emailSender.Send(mail);



            testHub22.Clients.All.SendAsync("Receive", "Hello from controller 111111");

            testHub.Clients.All.Receive("Hello from controller");


            BaseResponseDataModel<List<WeatherForecastModel>> baseResponseDataModel = new BaseResponseDataModel<List<WeatherForecastModel>>();
            var rng = new Random();
            baseResponseDataModel.Data = Enumerable.Range(1, 5).Select(index => new WeatherForecastModel
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToList();

            return baseResponseDataModel;
        }
        [HttpPost("[action]")]
        public BaseResponseDataModel<List<WeatherForecastModel>> GetItems(BaseSearchRequestModel<WeatherForecastModel> baseSearchRequestModel)
        {
            BaseResponseDataModel<List<WeatherForecastModel>> baseResponseDataModel = new BaseResponseDataModel<List<WeatherForecastModel>>();
            var rng = new Random();
            baseResponseDataModel.Data = Enumerable.Range(1, 5).Select(index => new WeatherForecastModel
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToList();

            return baseResponseDataModel;
        }
    }
}
