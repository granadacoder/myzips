using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

using MyCompany.Components.Logging.LoggingAbstractBase;
using MyCompany.Direct.Provisioning.BusinessLayer.Constants.ValidationConstants;
using MyCompany.Direct.Provisioning.BusinessLayer.Managers.Interfaces;
using MyCompany.Direct.Provisioning.BusinessLayer.Workflows.DecommissionDomain.Constants;
using MyCompany.Direct.Provisioning.BusinessLayer.Workflows.Domain;
using MyCompany.Direct.Provisioning.BusinessLayer.Workflows.OnboardDomain.Constants;
using MyCompany.Direct.Provisioning.BusinessLayer.Workflows.RenewDomain.Constants;
using MyCompany.Direct.Provisioning.Domain.Args.CliArgs.ReportArgs;
using MyCompany.Direct.Provisioning.Domain.Dtos.Reports;
using MyCompany.Direct.Provisioning.Domain.Entities;
using MyCompany.Direct.Provisioning.Domain.Enums.Reports;

namespace MyCompany.Direct.Provisioning.BusinessLayer.Managers
{
    public class ReportsManager : IReportsManager
    {
        public const string ErrorMessageILoggerFactoryWrapperIsNull = "ILoggerFactoryWrapper is null";
        public const string ErrorMessageIFileSystemIsNull = "IFileSystem is null";
        public const string ErrorMessageIDirectCertificateRenewalManagerIsNull = "IDirectCertificateRenewalManager is null";
        public const string ErrorMessageIDirectProvisioningManagerIsNull = "IDirectProvisioningManager is null";
        public const string ErrorMessageIDirectRoutingServiceDirectRemovalSyncServiceManagerIsNull = "IDirectRoutingServiceDirectRemovalSyncServiceManager is null";

        public const string ReportNameOnboardWorkflowHistorySummary = "Onboard History Summary Report";
        public const string ReportNameRenewWorkflowHistorySummary = "Renew History Summary Report";
        public const string ReportNameDecommissionWorkflowHistorySummary = "Decommission History Summary Report";

        private const string FileExtensionJson = ".json";
        private const string FileExtensionXml = ".xml";
        private const string FileExtensionHtml = ".html";

        private const string XmlRootElementName = "Root";

        private readonly ILoggerWrapper<ReportsManager> logger;
        private readonly IFileSystem fileSystem;
        private readonly IDirectCertificateRenewalManager certificateRenewalManager;
        private readonly IDirectProvisioningManager provisioningManager;
        private readonly IDirectRoutingServiceDirectRemovalSyncServiceManager routingServiceDirectRemovalSyncServiceManager;

        public ReportsManager(ILoggerFactoryWrapper loggerFactory, IFileSystem fileSystem, IDirectCertificateRenewalManager certificateRenewalManager, IDirectProvisioningManager provisioningManager, IDirectRoutingServiceDirectRemovalSyncServiceManager routingServiceDirectRemovalSyncServiceManager)
        {
            if (null == loggerFactory)
            {
                throw new ArgumentNullException(ErrorMessageILoggerFactoryWrapperIsNull, (Exception)null);
            }

            this.logger = loggerFactory.CreateLoggerWrapper<ReportsManager>();

            this.fileSystem = fileSystem ?? throw new ArgumentNullException(ErrorMessageIFileSystemIsNull, (Exception)null);
            this.certificateRenewalManager = certificateRenewalManager ?? throw new ArgumentNullException(ErrorMessageIDirectCertificateRenewalManagerIsNull, (Exception)null);
            this.provisioningManager = provisioningManager ?? throw new ArgumentNullException(ErrorMessageIDirectProvisioningManagerIsNull, (Exception)null);
            this.routingServiceDirectRemovalSyncServiceManager = routingServiceDirectRemovalSyncServiceManager ?? throw new ArgumentNullException(ErrorMessageIDirectRoutingServiceDirectRemovalSyncServiceManagerIsNull, (Exception)null);
        }

        public async Task<ReportCreateSummary> CreateOnboardWorkHistoryReport(OnboardWorkHistorySummaryReportArgs args, CancellationToken token)
        {
            if (null == args)
            {
                throw new ArgumentNullException(string.Format(ValidationMsgConstant.IsNullItem, typeof(OnboardWorkHistorySummaryReportArgs).Name));
            }

            ReportCreateSummary returnItem = await this.CreateWorkHistoryReport<DirectProvisioningEntity, OnboardWorkHistorySummaryReportArgs>(ReportNameOnboardWorkflowHistorySummary, args, this.provisioningManager.GetManyByOnboardWorkHistoryReportArgs, this.ScrubDirectProvisioningDto, this.UpdateProcessingStepStringDirectProvisioningEntity, token);

            return returnItem;
        }

        public async Task<ReportCreateSummary> CreateRenewWorkHistoryReport(RenewWorkHistorySummaryReportArgs args, CancellationToken token)
        {
            if (null == args)
            {
                throw new ArgumentNullException(string.Format(ValidationMsgConstant.IsNullItem, typeof(RenewWorkHistorySummaryReportArgs).Name));
            }

            ReportCreateSummary returnItem = await this.CreateWorkHistoryReport<DirectCertificateRenewalEntity, RenewWorkHistorySummaryReportArgs>(ReportNameRenewWorkflowHistorySummary, args, this.certificateRenewalManager.GetManyByRenewWorkHistoryReportArgs, this.ScrubDirectCertificateRenewalEntityData, this.UpdateProcessingStepStringDirectCertificateRenewalEntity, token);

            return returnItem;
        }

        public async Task<ReportCreateSummary> CreateDecommissionWorkHistoryReport(DecommissionWorkHistorySummaryReportArgs args, CancellationToken token)
        {
            if (null == args)
            {
                throw new ArgumentNullException(string.Format(ValidationMsgConstant.IsNullItem, typeof(DecommissionWorkHistorySummaryReportArgs).Name));
            }

            ReportCreateSummary returnItem = await this.CreateWorkHistoryReport<DirectRoutingServiceDirectRemovalSyncServiceEntity, DecommissionWorkHistorySummaryReportArgs>(ReportNameDecommissionWorkflowHistorySummary, args, this.routingServiceDirectRemovalSyncServiceManager.GetManyByDecommissionWorkHistoryReportArgs, this.ScrubDirectRoutingServiceDirectRemovalSyncServiceEntityData, this.UpdateProcessingStepStringDirectRoutingServiceDirectRemovalSyncServiceEntity, token);

            return returnItem;
        }

        private async Task<ReportCreateSummary> CreateWorkHistoryReport<TEntity, TArgs>(string reportTitle, TArgs args, Func<TArgs, CancellationToken, Task<IEnumerable<TEntity>>> getItemsFunc, Func<IEnumerable<TEntity>, IEnumerable<TEntity>> scrubFunc, Func<IEnumerable<TEntity>, IEnumerable<TEntity>> updateProcessStepStringFunc, CancellationToken token) where TArgs : WorkflowHistoryReportArgsBase
        {
            if (null == args)
            {
                throw new ArgumentNullException(string.Format(ValidationMsgConstant.IsNullItem, typeof(TArgs).Name));
            }

            ReportCreateSummary returnItem = new ReportCreateSummary();

            IEnumerable<TEntity> items = await getItemsFunc(args, token);

            /* get rid of s3nsitiv3 data */
            items = scrubFunc(items);

            items = updateProcessStepStringFunc(items);

            if ((args.ReportOutput & ReportOutputEnum.Xml) != 0 || (args.ReportOutput & ReportOutputEnum.Json) != 0 || (args.ReportOutput & ReportOutputEnum.Html) != 0)
            {
                JsonNeedsSingleRootWorkaround<TEntity> jsonWorkaround = new JsonNeedsSingleRootWorkaround<TEntity>()
                {
                    Title = reportTitle,
                    Items = items.ToList(),
                    ParametersFlattened = args.ToString()
                };

                returnItem.ContainerFolderName = this.GetUserTempPath(jsonWorkaround.Uid);

                string jsonContents = this.ConvertToJson<JsonNeedsSingleRootWorkaround<TEntity>>(jsonWorkaround);

                if ((args.ReportOutput & ReportOutputEnum.Json) != 0)
                {
                    string jsonFileName = this.WriteToTempFile(jsonWorkaround.Uid, jsonContents, FileExtensionJson);
                    returnItem.JsonFileName = jsonFileName;
                }

                XNode node = Newtonsoft.Json.JsonConvert.DeserializeXNode(jsonContents, XmlRootElementName);
                string xmlContents = node.ToString();

                if ((args.ReportOutput & ReportOutputEnum.Xml) != 0)
                {
                    string xmlFileName = this.WriteToTempFile(jsonWorkaround.Uid, xmlContents, FileExtensionXml);
                    returnItem.XmlFileName = xmlFileName;
                }

                if ((args.ReportOutput & ReportOutputEnum.Html) != 0)
                {
                    string htmlFileName = this.GetTempFileNameWithExtension(jsonWorkaround.Uid, FileExtensionHtml);

                    XslCompiledTransform xslt = new XslCompiledTransform();
                    xslt.Load(args.XslFullFileName);

                    if ((args.ReportOutput & ReportOutputEnum.Xml) == 0)
                    {
                        using (StringReader sri = new StringReader(xmlContents))
                        {
                            using (XmlReader xri = XmlReader.Create(sri))
                            {
                                using (StringWriter sw = new StringWriter())
                                //// use OutputSettings of xsl, so it can be output as HTML
                                using (XmlWriter xwo = XmlWriter.Create(sw, xslt.OutputSettings))
                                {
                                    xslt.Transform(xri, xwo);
                                    string resultHtml = sw.ToString();
                                    htmlFileName = this.WriteContentsToConcreteFile(resultHtml, htmlFileName);
                                }
                            }
                        }
                    }
                    else
                    {
                        xslt.Transform(returnItem.XmlFileName, htmlFileName);
                    }

                    returnItem.HtmlFileName = htmlFileName;
                }
            }

            return returnItem;
        }

        private IEnumerable<DirectProvisioningEntity> UpdateProcessingStepStringDirectProvisioningEntity(IEnumerable<DirectProvisioningEntity> items)
        {
            IEnumerable<DirectProvisioningEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;

                foreach (DirectProvisioningEntity item in returnItems)
                {
                    if (null != item.DirectWorkflowHistoryEntities)
                    {
                        item.DirectWorkflowHistoryEntities.ToList().ForEach(it => this.UpdateProcessStepString(OnboardProcessSteps.AllEntries, it));
                    }
                }
            }

            return returnItems;
        }

        private IEnumerable<DirectCertificateRenewalEntity> UpdateProcessingStepStringDirectCertificateRenewalEntity(IEnumerable<DirectCertificateRenewalEntity> items)
        {
            IEnumerable<DirectCertificateRenewalEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;

                foreach (DirectCertificateRenewalEntity item in returnItems)
                {
                    if (null != item.DirectWorkflowHistoryEntities)
                    {
                        item.DirectWorkflowHistoryEntities.ToList().ForEach(it => this.UpdateProcessStepString(RenewalProcessSteps.AllEntries, it));
                    }
                }
            }

            return returnItems;
        }

        private IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> UpdateProcessingStepStringDirectRoutingServiceDirectRemovalSyncServiceEntity(IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> items)
        {
            IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;

                foreach (DirectRoutingServiceDirectRemovalSyncServiceEntity item in returnItems)
                {
                    if (null != item.DirectWorkflowHistoryEntities)
                    {
                        item.DirectWorkflowHistoryEntities.ToList().ForEach(it => this.UpdateProcessStepString(DecommissionProcessSteps.AllEntries, it));
                    }
                }
            }

            return returnItems;
        }

        private DirectWorkflowHistoryEntity UpdateProcessStepString(ICollection<ProcessStepEntry> processStepDictionaryItems, DirectWorkflowHistoryEntity item)
        {
            DirectWorkflowHistoryEntity returnItem = null;

            if (null != item)
            {
                returnItem = item;
                if (returnItem.ProcessStep.HasValue)
                {
                    ProcessStepEntry foundProcessStepEntry = processStepDictionaryItems.FirstOrDefault(ps => ps.Value == returnItem.ProcessStep.Value);
                    if (null != foundProcessStepEntry)
                    {
                        returnItem.ProcessStepString = foundProcessStepEntry.Name;
                    }
                }
            }

            return returnItem;
        }

        private IEnumerable<DirectProvisioningEntity> ScrubDirectProvisioningDto(IEnumerable<DirectProvisioningEntity> items)
        {
            IEnumerable<DirectProvisioningEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;
                returnItems.ToList().ForEach(it => this.ScrubDirectProvisioningDto(it));
            }

            return returnItems;
        }

        private void ScrubDirectProvisioningDto(DirectProvisioningEntity item)
        {
            if (null != item)
            {
                item.Base64CertificateData = null;
                item.CertPass = null;
                item.Pkcs12CertificateData = null;
            }
        }

        private IEnumerable<DirectCertificateRenewalEntity> ScrubDirectCertificateRenewalEntityData(IEnumerable<DirectCertificateRenewalEntity> items)
        {
            IEnumerable<DirectCertificateRenewalEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;
                returnItems.ToList().ForEach(it => this.ScrubDirectCertificateRenewalEntityData(it));
            }

            return returnItems;
        }

        private void ScrubDirectCertificateRenewalEntityData(DirectCertificateRenewalEntity item)
        {
            if (null != item)
            {
                item.Base64CertificateData = null;
                item.Pkcs12CertificateData = null;
                item.NewCertPass = null;
            }
        }

        private IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> ScrubDirectRoutingServiceDirectRemovalSyncServiceEntityData(IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> items)
        {
            IEnumerable<DirectRoutingServiceDirectRemovalSyncServiceEntity> returnItems = null;
            if (null != items)
            {
                returnItems = items;
                returnItems.ToList().ForEach(it => this.ScrubDirectRoutingServiceDirectRemovalSyncServiceEntityData(it));
            }

            return returnItems;
        }

        private void ScrubDirectRoutingServiceDirectRemovalSyncServiceEntityData(DirectRoutingServiceDirectRemovalSyncServiceEntity item)
        {
            if (null != item)
            {
            }
        }

        private string ConvertToJson<T>(T value)
        {
            /* see https://stackoverflow.com/questions/9110724/serializing-a-list-to-json/9110986#9110986 for DNC 2.1 vs 3.1 notes */
            /* coded for DNC 2.1 for time being */
            string returnValue = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            return returnValue;
        }

        private string WriteContentsToConcreteFile(string contents, string fileName)
        {
            this.fileSystem.File.WriteAllText(fileName, contents);
            return fileName;
        }

        private string WriteToTempFile(string uid, string contents, string extension)
        {
            // Writes text to a temporary file and returns path 
            string fileName = this.GetTempFileNameWithExtension(uid, extension);
            return this.WriteContentsToConcreteFile(contents, fileName);
        }

        private string GetTempFileNameWithExtension(string uid, string extension)
        {
            string fileName = System.IO.Path.GetTempFileName();
            fileName = fileName.Replace(".tmp", extension);
            fileName = System.IO.Path.Combine(this.GetUserTempPath(uid), System.IO.Path.GetFileName(fileName));
            return fileName;
        }

        private string GetUserTempPath(string uid)
        {
            string path = Path.GetTempPath();

            string linuxHasNoSpecificUserTempFolderWorkAround = string.Empty;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                linuxHasNoSpecificUserTempFolderWorkAround = new string(Environment.UserName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray());
            }

            path = Path.Combine(path, linuxHasNoSpecificUserTempFolderWorkAround, uid);
            Directory.CreateDirectory(path);

            return path;
        }

        private class JsonNeedsSingleRootWorkaround<T>
        {
            public string Uid { get; private set; } = Guid.NewGuid().ToString("N");

            public string Title { get; set; }

            public DateTimeOffset GenerationUtc { get; set; } = DateTimeOffset.Now;

            public string ParametersFlattened { get; set; }

            /* this is purposely a List<T> to deal with json serialization issues */
            public List<T> Items { get; set; }
        }
    }
}
