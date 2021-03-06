using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
//using Syncfusion.DocIO;
//using Syncfusion.DocIO.DLS;

namespace Middt.Sample.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class ViewOrderController : BaseCrudControllerWithoutAuth<ViewOrder, ViewOrderRepository>
    {

    }
}
