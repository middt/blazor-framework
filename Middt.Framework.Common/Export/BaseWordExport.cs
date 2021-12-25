using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace Middt.Framework.Common.Export
{
    public abstract class BaseWordExport //: IBaseExport
    {
        public abstract string Folder { get; }
        public abstract string TemplateFileName { get; }


        public virtual BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
        {
            return ConvertToWord(baseExportModel);
        }
        protected BaseResponseDataModel<byte[]> ConvertToWord(BaseExportModel baseExportModel)
        {
            BaseResponseDataModel<byte[]> response = new BaseResponseDataModel<byte[]>();

            try
            {
                response.Result = ResultEnum.Success;

                string path = Path.Combine(Environment.CurrentDirectory, Folder, TemplateFileName);
                using (FileStream readers = new FileStream(path, FileMode.Open))
                {

                    using (WordDocument wordDocument = new WordDocument(readers, FormatType.Docx))
                    {
                        if (baseExportModel != null && baseExportModel.ParamList != null && baseExportModel.ParamList.Count > 0)
                        {
                            MailMergeDataTable mailMergeDataTable = null;

                            List<ExportParameterModel> primitiveList = new List<ExportParameterModel>();

                            foreach (ExportParameterModel keyValue in baseExportModel.ParamList)
                            {
                                if (keyValue.Value.GetType().IsPrimitiveType())
                                {
                                    primitiveList.Add(keyValue);
                                }
                                else
                                {
                                    mailMergeDataTable = new MailMergeDataTable(keyValue.Key, (IEnumerable<object>)keyValue.Value);
                                    wordDocument.MailMerge.ExecuteGroup(mailMergeDataTable);
                                }
                            }

                            if (primitiveList.Count > 0)
                            {
                                List<string> keyList = primitiveList.Select(x => x.Key).ToList();
                                List<string> valueList = primitiveList.Select(x => x.Value.ToString()).ToList();

                                wordDocument.MailMerge.Execute(keyList.ToArray(), valueList.ToArray());
                            }
                        }

                        using (MemoryStream stream = new MemoryStream())
                        {
                            wordDocument.Save(stream, FormatType.Docx);
                            response.Data = stream.ToArray();

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.Result = Framework.Model.Model.Enumerations.ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }

            return response;
        }
    }
}
