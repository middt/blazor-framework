using RestEase;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using Suges.Template.Api.Model.Database;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;

namespace Suges.Template.Common.Service
{
    public interface ITable1SecureService : IBaseCrudRefit<Table1Secure>
    {
        [Post("/api/v{version}/{controllerName}/getdatatable")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<List<ExpandoObject>>> GetDataTable([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);




        [Post("/api/v{version}/{controllerName}/getrandevubyfirmaid")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<List<Table1Secure>>> GetRandevuByFirmaID([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);


        [Post("/api/v{version}/{controllerName}/getfirmabyfirmaid")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<Table1Secure>> GetFirmaByFirmaID([Path] string version, [Path] string controllerName);


        [Post("/api/v{version}/{controllerName}/sozlesmebedelidokumuexceltemplate")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<byte[]>> SozlesmeBedeliDokumuExcelTemplate([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);

        [Post("/api/v{version}/{controllerName}/sozlesmebedelidokumuexceltopdftemplate")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<byte[]>> SozlesmeBedeliDokumuExcelToPdfTemplate([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);



        [Post("/api/v{version}/{controllerName}/sozlesmebedelidokumuwordtemplate")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<byte[]>> SozlesmeBedeliDokumuWordTemplate([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);


        [Post("/api/v{version}/{controllerName}/sozlesmebedelidokumuwordtopdftemplate")]
        [Header("Authorization", "Bearer")]
        Task<BaseResponseDataModel<byte[]>> SozlesmeBedeliDokumuWordToPdfTemplate([Path] string version, [Path] string controllerName, [Body(BodySerializationMethod.Serialized)] BaseSearchRequestModel<Table1Secure> model);
    }
}
