using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Service;
using Middt.Sample.Api.Model.Database;
using System.Collections.Generic;
using System.Dynamic;

namespace Middt.Sample.Common.Service
{
    public class Table1SecureService : BaseCrudRefit<ITable1SecureService, Table1Secure>
    {
        public override string controllerName => "Table1Secure";
        public Table1SecureService(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }

        public BaseResponseDataModel<List<ExpandoObject>> GetDataTable(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.GetDataTable("1.0", controllerName, model).Result;
            }
);
        }




        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuExcelTemplate(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.SozlesmeBedeliDokumuExcelTemplate("1.0", controllerName, model).Result;
            }
);
        }

        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuExcelToPdfTemplate(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.SozlesmeBedeliDokumuExcelToPdfTemplate("1.0", controllerName, model).Result;
            }
);
        }


        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuWordTemplate(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.SozlesmeBedeliDokumuWordTemplate("1.0", controllerName, model).Result;
            }
);
        }

        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuWordToPdfTemplate(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.SozlesmeBedeliDokumuWordToPdfTemplate("1.0", controllerName, model).Result;
            }
);
        }



        public BaseResponseDataModel<List<Table1Secure>> GetRandevuByFirmaID(BaseSearchRequestModel<Table1Secure> model)
        {
            return ExecutePolly(() =>
            {
                return api.GetRandevuByFirmaID("1.0", controllerName, model).Result;
            }
);
        }

        public BaseResponseDataModel<Table1Secure> GetFirmaByFirmaID()
        {
            return ExecutePolly(() =>
            {
                return api.GetFirmaByFirmaID("1.0", controllerName).Result;
            }
);
        }

    }
}