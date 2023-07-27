using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Logging;
using System.Net.Http;
using System.Xml;
using Nop.Core.Infrastructure;
using Nop.Services.Security;
using Nop.Web.Framework.Mvc.Filters;
using Ubiety.Dns.Core;
using Nop.Plugin.Payments.MenPos.Models;
using Nop.Plugin.Payments.MenPos.Components;
using Microsoft.AspNetCore.Hosting;
using Nop.Plugin.Payments.MenPos.Services;
using Nop.Plugin.Payments.MenPos.Validators;
using Nop.Plugin.Payments.MenPos;

namespace Nop.Plugin.Payments.MenPos
{
    public class MenPosPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Entities
        private readonly ICustomerService _customerService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly ILocalizationService _localizationService;
        private readonly IWebHelper _webHelper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly IAddressService _addressService;
        private readonly IPaymentService _paymentService;
        private readonly INotificationService _notificationService;
        private readonly OrderSettings _orderSettings;
        private readonly ILogger _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly INopFileProvider _nopFileProvider;
        private readonly IParamPaymentSettings _paramPaymentSettings;
        private readonly KuveytTurkServices _kuveytTurkServices;

        #endregion

        // Kurucu metodda bu alanlara değer ata
        #region Ctor

        public MenPosPaymentProcessor(ICustomerService customerService, IEncryptionService encryptionService,
         IWebHostEnvironment webHostEnvironment, INopFileProvider nopFileProvider,
        ISettingService settingService,
        IStoreContext storeContext,
        IUrlHelperFactory urlHelperFactory,
        ILocalizationService localizationService,
        IHttpContextAccessor httpContextAccessor,
        IActionContextAccessor actionContextAccessor,
        ICurrencyService currencyService,
        IAddressService addressService,
        IPaymentService paymentService,
        INotificationService notificationService,
        CurrencySettings currencySettings,
        OrderSettings orderSettings,
        ILogger logger, IParamPaymentSettings paramPaymentSettings,
        IWebHelper webHelper, KuveytTurkServices kuveytTurkServices)
        {
            _customerService = customerService;
            _settingService = settingService;
            _storeContext = storeContext;
            _localizationService = localizationService;
            _webHelper = webHelper;
            _httpContextAccessor = httpContextAccessor;
            _urlHelperFactory = urlHelperFactory;
            _actionContextAccessor = actionContextAccessor;
            _currencyService = currencyService;
            _currencySettings = currencySettings;
            _addressService = addressService;
            _paymentService = paymentService;
            _notificationService = notificationService;
            _orderSettings = orderSettings;
            _logger = logger;
            _encryptionService = encryptionService;
            _webHostEnvironment = webHostEnvironment;
            _nopFileProvider = nopFileProvider;
            _paramPaymentSettings = paramPaymentSettings;
            _kuveytTurkServices = kuveytTurkServices;
        }
        #endregion

        #region Utilities

        /// <summary>
        /// Gets eWay URL
        /// </summary>
        /// <returns></returns>
        private string GeteWayUrl()
        {
            return _paramPaymentSettings.UseSandbox ? _paramPaymentSettings.TestUrl : _paramPaymentSettings.ProductUrl;

        }
        #endregion



        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => true;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => true;


        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => true;


        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => true;


        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.Manual;


        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;


        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;


        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.MenPos.PaymentMethodDescription");
        }


        #endregion

        #region Methods
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            _paramPaymentSettings.ProcessPaymentRequest = processPaymentRequest;

            if (_paramPaymentSettings.ProcessPaymentRequest.CustomValues.ContainsKey("Pos"))
            {
                _pos = (string)_paramPaymentSettings.ProcessPaymentRequest.CustomValues["Pos"];
                _pos = _pos.Replace(" ", "").Replace("{", "").Replace("}", "");
                _paramPaymentSettings.ProcessPaymentRequest.CustomValues.Remove("Pos");
            }

            return await Task.FromResult(new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending });
        }

        string _pos = "";

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //It also validates whether order is also paid (after redirection) so customers will not be able to pay twice
            //payment status should be Pending
            if (order.PaymentStatus != PaymentStatus.Pending)
                return Task.FromResult(false);

            //let's ensure that at least 1 minute passed after order is placed
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes < 1)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        public async Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(decimal.Zero);
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            var sanalPOSID = form["SanalPOSID"].ToString();
            var installment = form["Installment"].ToString() == "" ? "1" : form["Installment"].ToString();
            var rate = form["Rate"].ToString() == "" ? "1" : form["Rate"].ToString().Replace(".", ",");
            var installmentTotal = (double.Parse(form["st" + sanalPOSID.Substring(0, 1)].ToString()) * (100 + double.Parse(rate)) / 100);

            var customValues = new Dictionary<string, object>();
            customValues.Add("Pos", string.Concat(sanalPOSID, "|", installment, "|", rate, "|", installmentTotal));

            var paymentInfo = new ProcessPaymentRequest
            {
                CreditCardType = form["CreditCardType"].ToString(),
                CreditCardName = form["CardholderName"].ToString(),
                CreditCardNumber = form["CardNumber"].ToString(),
                CreditCardExpireMonth = int.Parse(form["ExpireMonth"].ToString()),
                CreditCardExpireYear = int.Parse(form["ExpireYear"].ToString()),
                CreditCardCvv2 = form["CardCode"].ToString(),
                CustomValues = customValues
            };


            ////Convert data from ProcessPaymentRequest to Xml object
            //var postData = _kuveytTurkServices.GetDataAsXml(paymentInfo);
            ////Send Xml object to url and get result
            //var result = _kuveytTurkServices.PostPaymentDataToUrl("https://boa.kuveytturk.com.tr/sanalposservice/Home/ThreeDModelPayGate", postData);

            ////Create directory and save Html Code in it
            //var file = _kuveytTurkServices.PutHtmlCodeInFile(result);

            ////Redirect to new file HTML page
            //_httpContextAccessor.HttpContext.Response.Redirect($"{_webHelper.GetStoreLocation()}OrderPayments/{file}");

            return Task.FromResult(paymentInfo);

        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentMenPos/Configure";
        }

        public Type GetPublicViewComponent()
        {
            return typeof(PaymentMenPosViewComponent);
        }

        public override async Task InstallAsync()
        {
            var settings = new IParamPaymentSettings()
            {
                UseSandbox = true,
                ClientCode = "10738",
                ClientUsername = "Test",
                ClientPassword = "Test",
                Guid = "0c13d406-873b-403b-9c09-a5766840d98c",
                TestUrl = "https://test-dmz.param.com.tr:4443/turkpos.ws/service_turkpos_test.asmx?wsdl",
                ProductUrl = "https://posws.param.com.tr/turkpos.ws/service_turkpos_prod.asmx?wsdl",
                Installment = true
            };
            await _settingService.SaveSettingAsync(settings);
            //https://test-dmz.param.com.tr:4443/turkpos.ws/service_turkpos_test.asmx
            //https://dmzws.param.com.tr/turkpos.ws/service_turkpos_prod.asmx

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Param.UseSandbox"] = "Test Mode:",
                ["Plugins.Payments.Param.UseSandbox.Hint"] = "Use test mode?:",
                ["Plugins.Payments.Param.ClientCode"] = "Client Code:",
                ["Plugins.Payments.Param.ClientUsername"] = "Client Username:",
                ["Plugins.Payments.Param.ClientUsername.Hint"] = "Enter Client Username.",
                ["Plugins.Payments.Param.ClientPassword"] = "Client Password:",
                ["Plugins.Payments.Param.Guid"] = "GUID:",
                ["Plugins.Payments.Param.Installment"] = "Installment:",
                ["Plugins.Payments.Param.Installment.Hint"] = "Use installment?:",
                ["Plugins.Payments.Param.PaymentMethodDescription"] = "Pay with credit card via the param.",
                ["Plugins.Payments.Param.BankName"] = "Bank Name:",
                ["Plugins.Payments.Param.ErrorAvailable"] = "This page has expired. Please, create a new order."
            }, 1); //EN

            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Param.UseSandbox"] = "Test Modu:",
                ["Plugins.Payments.Param.UseSandbox.Hint"] = "Test Modunu?:",
                ["Plugins.Payments.Param.ClientCode"] = "Müşteri Kodu:",
                ["Plugins.Payments.Param.ClientUsername"] = "Müşteri Kullanıcı:",
                ["Plugins.Payments.Param.ClientPassword"] = "Müşteri Parola:",
                ["Plugins.Payments.Param.Guid"] = "GUID:",
                ["Plugins.Payments.Param.Installment"] = "Taksitlendirme:",
                ["Plugins.Payments.Param.Installment.Hint"] = "Taksitlendirme?:",
                ["Plugins.Payments.Param.PaymentMethodDescription"] = "Param ile kredi kartı ödemesi yaparsınız.",
                ["Plugins.Payments.Param.BankName"] = "Banka Adı:",
                ["Plugins.Payments.Param.ErrorAvailable"] = "Bu ödeme sayfasının süresi doldu. Lütfen yeni bir sipariş oluşturun."
            }, 2); //TR


            //settings
            await _settingService.SaveSettingAsync(new KuveytTurkPaymentSettings()
            {
                //UseSandbox = true,
                //UseMd5Hashing = true
            });

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}CustomerId", "Account No");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}CustomerId.Hint", "Enter account no.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}MerchantId", "Merchant No");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}MerchantId.Hint", "Enter merchant no.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}UserName", "User Name");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}UserName.Hint", "Enter User Name.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}Password", "Password");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}Password.Hint", "Enter Password.");

            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}PaymentMethodDescription", "You will be redirected to pay after complete the order.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}RedirectionTip", "You will be redirected to pay after complete the order.");

            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}PaymentDone", "Payment have been done!");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartiVerenBankayiAraLim", "Call the bank.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizUyeIsyeri", "Invalid Member Merchant.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartaElKoyunuz", "Not working card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemOnaylanmadi", "The transaction has not been approved.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}VipIslemIcinOnayVerildi", "Approved for VIP Operation.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizIslem", "No Transaction.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizIslemTutari", "No Transaction Amount.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizKartNumarasi", "Invalid Card Number.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartVerenBankaTanimsiz", "Card Issuer Bank Undefined.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}VadeSonuGecmisKartaElKoy", "Seize Card Overdue.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SahtekarlikKartaelKoyunuz", "Falsify Your Card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KisitliKartKartaElKoyunuz", "Card limit exceeded.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GuvenligiUyarinizKartaElKoyunuz", "The card was rejected for security reasons.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KayipKartKartaElKoy", "Lost card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}CalintiKartKartaElKoy", "Stolen card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}BakiyesiKrediLimitiYetersiz", "Balance Credit Limit Insufficient.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}DovizHesabiBulunamasi", "No Exchange Account Found.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}VadeSonuGecmisKart", "Expiry Card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}HataliKartSifresi", "Wrong Card Password.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartTanimliDegil", "Card Not Defined.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemTipineIzinYok", "No Transaction Type.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemTipiTerminaleKapali", "Operation Type Closed to Terminal.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SahtekarlikSuphesi", "Suspicion of Fraud.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}ParaCekmeTutarLimitiAsild", "Withdrawal Amount Limit Exceeded.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KisitlanmisKart", "Restricted Card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GuvenlikIhlali", "Security Violation.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}ParaÇekmeAdetLimitiAsildi", "Withdrawal Number Limit Exceeded.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemiReddedinizGuvenligi", "Transaction Rejected Security.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}BuHesaptaHicbirIslemYapila", "No Transactions Made On This Account.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}TanimsizSube", "Undefined Branch.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreDenemeSayisiAsildi", "Number of Enter Password Excided.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifrelerUyusmuyorKey", "Encryption Key is not Match.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreScriptTalebiReddedildi", "Password Script Request Denied.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreGuvenilirBulanmadi", "Security Password Not Found.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}ARQCKontroluBasarisiz", "ARQC Control Failed.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreDegisikligi/YuklemeOnay", "Password Change/Download Confirmation.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemSupheliTamamlandiKontrol", "Operation Suspicious Completed Check.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}EkKartIleBuIslemYapilmaz", "This Operation Cannot Be Done By Additional Card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}GunSonuDevamEdiyor", "End of Day Calculating Continues.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartiVerenBankaHizmetdisi", "Bank Issuing Card Out of Service.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartVerenBankaTanimliDegil", "Unknown Bank Card.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}SistemArizali", "Problem in system.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}IPAdresiTanimliDegildir", "Your IP is not define.");
            await _localizationService.AddOrUpdateLocaleResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}OtherError", "Unknown Error.");


            //settings
            await _settingService.SaveSettingAsync(new IOtherPaymentSettings()
            {
            });
            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.MenPos.aratutar"] = "are amount:",
                ["Plugins.Payments.MenPos.üsttutar"] = "top amount:",
                ["Plugins.Payments.MenPos.aratutar.Hint"] = "are amount:",
                ["Plugins.Payments.MenPos.üsttutar.Hint"] = "top amount:",
            }, 1); //EN

            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.MenPos.aratutar"] = "ara tutar:",
                ["Plugins.Payments.MenPos.üsttutar"] = "üst tutar:",
                ["Plugins.Payments.MenPos.aratutar.Hint"] = "ara tutar:",
                ["Plugins.Payments.MenPos.üsttutar.Hint"] = "üst tutar:",

            }, 2); //TR

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
           

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.UseSandbox");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.UseSandbox.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientCode");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientCode.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientUsername");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientUsername.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientPassword");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.ClientPassword.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.Guid");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.Guid.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.Installment");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.Installment.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Param.PaymentMethodDescription");

            //settings
            await _settingService.DeleteSettingAsync<KuveytTurkPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}CustomerId");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}CustomerId.Hint");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}MerchantId");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}MerchantId.Hint");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}UserName");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}UserName.Hint");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}Password");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}Password.Hint");

            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}PaymentMethodDescription");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}RedirectionTip");

            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}PaymentDone");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartiVerenBankayiAraLim");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizUyeIsyeri");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartaElKoyunuz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemOnaylanmadi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}VipIslemIcinOnayVerildi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizIslem");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizIslemTutari");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GecersizKartNumarasi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartVerenBankaTanimsiz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}VadeSonuGecmisKartaElKoy");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SahtekarlikKartaelKoyunuz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KisitliKartKartaElKoyunuz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GuvenligiUyarinizKartaElKoyunuz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KayipKartKartaElKoy");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}CalintiKartKartaElKoy");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}BakiyesiKrediLimitiYetersiz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}DovizHesabiBulunamasi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}VadeSonuGecmisKart");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}HataliKartSifresi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartTanimliDegil");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemTipineIzinYok");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemTipiTerminaleKapali");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SahtekarlikSuphesi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}ParaCekmeTutarLimitiAsild");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KisitlanmisKart");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GuvenlikIhlali");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}ParaÇekmeAdetLimitiAsildi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemiReddedinizGuvenligi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}BuHesaptaHicbirIslemYapila");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}TanimsizSube");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreDenemeSayisiAsildi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifrelerUyusmuyorKey");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreScriptTalebiReddedildi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreGuvenilirBulanmadi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}ARQCKontroluBasarisiz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SifreDegisikligi/YuklemeOnay");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IslemSupheliTamamlandiKontrol");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}EkKartIleBuIslemYapilmaz");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}GunSonuDevamEdiyor");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartiVerenBankaHizmetdisi");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}KartVerenBankaTanimliDegil");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}SistemArizali");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}IPAdresiTanimliDegildir");
            await _localizationService.DeleteLocaleResourcesAsync($"{KuveytTurkDefaults.LocalizationStringStart}OtherError");

            //settings
            await _settingService.DeleteSettingAsync<IOtherPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.MenPos.alttutar");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.MenPos.alttutar.Hint");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.MenPos.üsttutar");
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.MenPos.üsttutar.Hint");

            await base.UninstallAsync();
        }

        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(false);
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            if (postProcessPaymentRequest == null)
                throw new ArgumentNullException(nameof(postProcessPaymentRequest));

            if (postProcessPaymentRequest.Order == null)
                throw new NopException("Order cannot be loaded");

            if (postProcessPaymentRequest.Order.PaymentStatus == PaymentStatus.Paid || postProcessPaymentRequest.Order.PaymentStatus == PaymentStatus.Authorized)
                return;


            var paramRequest = new GatewayRequest
            {
                ClientCode = _paramPaymentSettings.ClientCode,
                ClientUsername = _paramPaymentSettings.ClientUsername,
                ClientPassword = _paramPaymentSettings.ClientPassword,
                Guid = _paramPaymentSettings.Guid,
                KK_Sahibi = _paramPaymentSettings.ProcessPaymentRequest.CreditCardName,
                KK_No = _paramPaymentSettings.ProcessPaymentRequest.CreditCardNumber,
                KK_SK_Ay = _paramPaymentSettings.ProcessPaymentRequest.CreditCardExpireMonth.ToString(),
                KK_SK_Yil = _paramPaymentSettings.ProcessPaymentRequest.CreditCardExpireYear.ToString(),
                Islem_Tutar = postProcessPaymentRequest.Order.OrderSubtotalExclTax.ToString(),
                Toplam_Tutar = postProcessPaymentRequest.Order.OrderSubtotalExclTax.ToString()
            };

            string sanalPosId = "";
            int taksit = 1;
            double oran = 1;
            double taksitTutar = 1;

            if (_pos != "" && _pos.Contains("|"))
            {
                sanalPosId = _pos.Split('|')[0] ?? "";
                taksit = int.Parse(_pos.Split('|')[1] ?? "1");
                oran = double.Parse(_pos.Split('|')[2] ?? "1");
                taksitTutar = double.Parse(_pos.Split('|')[3] ?? "1");

                if (taksit == 1 && oran == 1)
                {
                    oran = 0;
                }
            }

            paramRequest.Toplam_Tutar = taksitTutar.ToString();
            var OrderTotal = (decimal)taksitTutar;

            paramRequest.Taksit = taksit;
            paramRequest.SanalPosId = sanalPosId;

            string url = _paramPaymentSettings.UseSandbox ? _paramPaymentSettings.TestUrl : _paramPaymentSettings.ProductUrl;
            paramRequest.Ref_URL = _webHelper.GetStoreLocation() + "onepagecheckout/";
            var successUrl = _webHelper.GetStoreLocation() + "PaymentParam/OrderComplete/" + _paramPaymentSettings.ProcessPaymentRequest.OrderGuid + "/" + postProcessPaymentRequest.Order.Id.ToString() + "/";
            var failUrl = _webHelper.GetStoreLocation() + "PaymentParam/OrderRefresh/" + postProcessPaymentRequest.Order.Id.ToString() + "/";
            paramRequest.Basarili_URL = successUrl;
            paramRequest.Hata_URL = failUrl;

            var processPaymentResult = await _paymentService.ProcessPaymentAsync(_paramPaymentSettings.ProcessPaymentRequest);

            var billingAddress = await _addressService.GetAddressByIdAsync(postProcessPaymentRequest.Order.BillingAddressId)
                ?? throw new NopException("Billing address cannot be loaded");
            if (billingAddress != null)
            {
                paramRequest.KK_Sahibi_GSM = CommonHelper.EnsureNumericOnly(billingAddress.PhoneNumber);
            }
            paramRequest.Siparis_ID = postProcessPaymentRequest.Order.Id.ToString();
            paramRequest.Siparis_Aciklama = (await _storeContext.GetCurrentStoreAsync()).Name
                + ". Order #" + postProcessPaymentRequest.Order.Id.ToString()
                + ", " + DateTime.Now
                + " tarihli ödeme.";
            paramRequest.Islem_Guvenlik_Tip = "3D";
            paramRequest.Islem_ID = processPaymentResult.SubscriptionTransactionId;
            paramRequest.KK_CVC = _paramPaymentSettings.ProcessPaymentRequest.CreditCardCvv2;


            var toplam = postProcessPaymentRequest.Order.OrderSubtotalExclTax + ((postProcessPaymentRequest.Order.OrderSubtotalExclTax * (decimal)oran) / 100);
            var ekucret = toplam - postProcessPaymentRequest.Order.OrderSubtotalExclTax;

            toplam += postProcessPaymentRequest.Order.OrderShippingExclTax;

            paramRequest.Islem_Tutar = Math.Round(toplam - ekucret, 2).ToString("0.00");
            paramRequest.Toplam_Tutar = Math.Round(toplam, 2).ToString("0.00");

            string islem_Guvenlik_Str = paramRequest.ClientCode + paramRequest.Guid + paramRequest.SanalPosId +
                paramRequest.Taksit + paramRequest.Islem_Tutar + paramRequest.Toplam_Tutar + paramRequest.Siparis_ID + paramRequest.Hata_URL + paramRequest.Basarili_URL;
            string data1 = "" +
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<soap:Body>" +
                        "<SHA2B64 xmlns=\"https://turkpos.com.tr/\">" +
                            "<Data>" + islem_Guvenlik_Str + "</Data>" +
                        "</SHA2B64>" +
                    "</soap:Body>" +
                "</soap:Envelope>";

            byte[] buffer1 = Encoding.ASCII.GetBytes(data1);

            HttpWebRequest request1 = WebRequest.Create(url) as HttpWebRequest;
            request1.Method = "POST";
            request1.ContentType = "text/xml; charset=\"utf-8\"";
            request1.ContentLength = buffer1.Length;
            request1.Headers.Add("SOAPAction", "https://turkpos.com.tr/SHA2B64");
            Stream post1 = request1.GetRequestStream();

            post1.Write(buffer1, 0, buffer1.Length);
            post1.Close();

            string responseResult1 = "";
            try
            {
                HttpWebResponse response1 = request1.GetResponse() as HttpWebResponse;
                Stream responseData1 = response1.GetResponseStream();
                StreamReader responseReader1 = new StreamReader(responseData1);
                responseResult1 = responseReader1.ReadToEnd();
                HttpStatusCode statusCode = response1.StatusCode;
                string statusCodeStr = response1.StatusCode.ToString();
                if (statusCode != HttpStatusCode.OK)
                {
                    await _logger.ErrorAsync("PARAM SHA2B64 REQUEST:\nError Code: " + statusCodeStr + "\n" + data1, null, null);
                }
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("PARAM SHA2B64 REQUEST:\n" + data1, ex, null);
            }

            responseResult1 = responseResult1.Replace(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"", "");
            responseResult1 = responseResult1.Replace(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", "");
            responseResult1 = responseResult1.Replace(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"", "");
            responseResult1 = responseResult1.Replace("soap:", "").Replace(":soap", "");
            responseResult1 = responseResult1.Replace(" xmlns=\"https://turkpos.com.tr/\"", "");
            responseResult1 = responseResult1.Replace("<Body>", "").Replace("</Body>", "");
            responseResult1 = responseResult1.Replace("<Envelope>", "<root>").Replace("</Envelope>", "</root>");

            XDocument xdoc1 = XDocument.Parse(responseResult1);

            string sHA2B64Result = xdoc1.Descendants("SHA2B64Result").FirstOrDefault().Value;

            paramRequest.Islem_Hash = sHA2B64Result;
            paramRequest.IPAdr = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress.ToString();
            paramRequest.IPAdr = paramRequest.IPAdr.Replace("::1", "127.0.0.1");

            var data1Array = new Dictionary<string, string>();
            data1Array.Add("Last4Digits", _paramPaymentSettings.ProcessPaymentRequest.CreditCardNumber.Substring(_paramPaymentSettings.ProcessPaymentRequest.CreditCardNumber.Length - 4));
            data1Array.Add("ExpiryDate", _paramPaymentSettings.ProcessPaymentRequest.CreditCardExpireMonth.ToString() + "/" + _paramPaymentSettings.ProcessPaymentRequest.CreditCardExpireYear.ToString());
            paramRequest.Data1 = Convert.ToBase64String(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(data1Array)));


            string aciklama = "Taksit: " + taksit.ToString() + "\n";
            aciklama += "Komisyon Oranı: %" + oran.ToString() + "\n";
            aciklama += "Komisyon Tutarı: " + postProcessPaymentRequest.Order.CustomerCurrencyCode + " " + Math.Round(ekucret, 2).ToString("0.00") + "\n";
            aciklama += "Tahsil Edilen Toplam Tutar: " + postProcessPaymentRequest.Order.CustomerCurrencyCode + " " + Math.Round(toplam, 2).ToString("0.00") + "\n";
            paramRequest.Data2 = aciklama;

            paramRequest.Data3 = string.Empty;
            paramRequest.Data4 = string.Empty;
            paramRequest.Data5 = string.Empty;


            string data2 = "" +
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<soap:Body>" +
                        "<TP_Islem_Odeme xmlns=\"https://turkpos.com.tr/\">" +
                            "<G>" +
                                "<CLIENT_CODE>" + paramRequest.ClientCode + "</CLIENT_CODE>" +
                                "<CLIENT_USERNAME>" + paramRequest.ClientUsername + "</CLIENT_USERNAME>" +
                                "<CLIENT_PASSWORD>" + paramRequest.ClientPassword + "</CLIENT_PASSWORD>" +
                            "</G>" +
                            "<SanalPOS_ID>" + sanalPosId + "</SanalPOS_ID>" +
                            "<GUID>" + paramRequest.Guid + "</GUID>" +
                            "<KK_Sahibi>" + paramRequest.KK_Sahibi + "</KK_Sahibi>" +
                            "<KK_No>" + paramRequest.KK_No + "</KK_No>" +
                            "<KK_SK_Ay>" + paramRequest.KK_SK_Ay + "</KK_SK_Ay>" +
                            "<KK_SK_Yil>" + paramRequest.KK_SK_Yil + "</KK_SK_Yil>" +
                            "<KK_CVC>" + paramRequest.KK_CVC + "</KK_CVC>" +
                            "<KK_Sahibi_GSM>" + paramRequest.KK_Sahibi_GSM + "</KK_Sahibi_GSM>" +
                            "<Hata_URL>" + paramRequest.Hata_URL + "</Hata_URL>" +
                            "<Basarili_URL>" + paramRequest.Basarili_URL + "</Basarili_URL>" +
                            "<Siparis_ID>" + paramRequest.Siparis_ID + "</Siparis_ID>" +
                            "<Siparis_Aciklama>" + paramRequest.Siparis_Aciklama + "</Siparis_Aciklama>" +
                            "<Taksit>" + paramRequest.Taksit.ToString() + "</Taksit>" +
                            "<Islem_Tutar>" + paramRequest.Islem_Tutar + "</Islem_Tutar>" +
                            "<Toplam_Tutar>" + paramRequest.Toplam_Tutar + "</Toplam_Tutar>" +
                            "<Islem_Hash>" + paramRequest.Islem_Hash + "</Islem_Hash>" +
                            "<Islem_ID>" + paramRequest.Islem_ID + "</Islem_ID>" +
                            "<IPAdr>" + paramRequest.IPAdr + "</IPAdr>" +
                            "<Ref_URL>" + paramRequest.Ref_URL + "</Ref_URL>" +
                            "<Data1>" + paramRequest.Data1 + "</Data1>" +
                            "<Data2>" + paramRequest.Data2 + "</Data2>" +
                            "<Data3>" + paramRequest.Data3 + "</Data3>" +
                            "<Data4>" + paramRequest.Data4 + "</Data4>" +
                            "<Data5>" + paramRequest.Data5 + "</Data5>" +
                        "</TP_Islem_Odeme>" +
                    "</soap:Body>" +
                "</soap:Envelope>";


            byte[] buffer2 = Encoding.ASCII.GetBytes(data2);
            HttpWebRequest request2 = WebRequest.Create(url) as HttpWebRequest;
            request2.Method = "POST";
            request2.ContentType = "text/xml; charset=\"utf-8\"";
            request2.ContentLength = buffer2.Length;
            request2.Headers.Add("SOAPAction", "https://turkpos.com.tr/TP_Islem_Odeme");
            Stream post2 = request2.GetRequestStream();

            post2.Write(buffer2, 0, buffer2.Length);
            post2.Close();

            string responseResult2 = "";
            try
            {
                HttpWebResponse response2 = request2.GetResponse() as HttpWebResponse;
                Stream responseData2 = response2.GetResponseStream();
                StreamReader responseReader2 = new StreamReader(responseData2);
                responseResult2 = responseReader2.ReadToEnd();
                HttpStatusCode statusCode = response2.StatusCode;
                string statusCodeStr = response2.StatusCode.ToString();
                if (statusCode != HttpStatusCode.OK)
                {
                    await _logger.ErrorAsync("PARAM TP_İŞLEM_ÖDEME REQUEST:\nError Code: " + statusCodeStr + "\n" + data2, null, null);
                }
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("PARAM TP_İŞLEM_ÖDEME REQUEST:\n" + data2, ex, null);
            }

            responseResult2 = responseResult2.Replace(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"", "");
            responseResult2 = responseResult2.Replace(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", "");
            responseResult2 = responseResult2.Replace(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"", "");
            responseResult2 = responseResult2.Replace("soap:", "").Replace(":soap", "");
            responseResult2 = responseResult2.Replace(" xmlns=\"https://turkpos.com.tr/\"", "");
            responseResult2 = responseResult2.Replace("<Body>", "").Replace("</Body>", "");
            responseResult2 = responseResult2.Replace("<Envelope>", "<root>").Replace("</Envelope>", "</root>");

            XDocument xdoc2 = XDocument.Parse(responseResult2);

            string islem_ID = xdoc2.Descendants("Islem_ID").FirstOrDefault().Value;
            string uCD_URL = xdoc2.Descendants("UCD_URL").FirstOrDefault().Value;
            string sonuc_Str = xdoc2.Descendants("Sonuc_Str").FirstOrDefault().Value;
            string banka_Sonuc_Kod = xdoc2.Descendants("Banka_Sonuc_Kod").FirstOrDefault().Value;

            bool onePageCheckout = _orderSettings.OnePageCheckoutEnabled;

            if (islem_ID != "" && islem_ID != "0" && uCD_URL.StartsWith("https") && uCD_URL.Contains("TURKPOS_3D_TRAN") && postProcessPaymentRequest.Order.PaymentStatus == PaymentStatus.Pending)
            {
                _httpContextAccessor.HttpContext.Response.Clear();
                if (onePageCheckout)
                {
                    _httpContextAccessor.HttpContext.Response.ContentType = "application/json;";
                    await _httpContextAccessor.HttpContext.Response.WriteAsync("{\"redirect\":\"" + uCD_URL + "\"}");
                }
                else
                {
                    _httpContextAccessor.HttpContext.Response.ContentType = "text/html;";
                    await _httpContextAccessor.HttpContext.Response.WriteAsync("<script>document.location.href='" + uCD_URL + "';</script>");
                }
            }
            else if (islem_ID == "0" && sonuc_Str != "")
            {
                _httpContextAccessor.HttpContext.Response.Clear();
                if (onePageCheckout)
                {
                    _httpContextAccessor.HttpContext.Response.ContentType = "application/json;";
                    await _httpContextAccessor.HttpContext.Response.WriteAsync("{\"Text\":\"" + sonuc_Str + "\" }");
                    //await _httpContextAccessor.HttpContext.Response.WriteAsync("{\"redirect\":\"" + failUrl + "\", \"Text\":\"" + sonuc_Str + "\" }");
                }
                else
                {
                    _httpContextAccessor.HttpContext.Response.ContentType = "text/html;";
                    await _httpContextAccessor.HttpContext.Response.WriteAsync("<script>alert('" + sonuc_Str + "');document.location.href='" + failUrl + "';</script>");
                }
            }
            else
            {
                _httpContextAccessor.HttpContext.Response.Clear();
                _httpContextAccessor.HttpContext.Response.ContentType = "application/json;";
            }

            //Get payment details
            var creditCardName = _encryptionService.DecryptText(postProcessPaymentRequest.Order.CardName);
            var creditCardNumber = _encryptionService.DecryptText(postProcessPaymentRequest.Order.CardNumber);
            var creditCardExpirationYear = _encryptionService.DecryptText(postProcessPaymentRequest.Order.CardExpirationYear);
            var creditCardExpirationMonth = _encryptionService.DecryptText(postProcessPaymentRequest.Order.CardExpirationMonth);
            var creditCardCvv2 = _encryptionService.DecryptText(postProcessPaymentRequest.Order.CardCvv2);

            //Save details in an object
            var processPaymentRequest = new ProcessPaymentRequest
            {
                CreditCardName = creditCardName,
                CreditCardNumber = creditCardNumber,
                CreditCardExpireYear = Convert.ToInt32(creditCardExpirationYear),
                CreditCardExpireMonth = Convert.ToInt32(creditCardExpirationMonth),
                CreditCardCvv2 = creditCardCvv2,
                OrderGuid = postProcessPaymentRequest.Order.OrderGuid,
                OrderTotal = postProcessPaymentRequest.Order.OrderTotal,
            };

            //Convert data from ProcessPaymentRequest to Xml object
            var postData = _kuveytTurkServices.GetDataAsXml(processPaymentRequest);
            //Send Xml object to url and get result
            var result = _kuveytTurkServices.PostPaymentDataToUrl("https://boa.kuveytturk.com.tr/sanalposservice/Home/ThreeDModelPayGate", postData);

            //Create directory and save Html Code in it
            var file = _kuveytTurkServices.PutHtmlCodeInFile(result);

            //Redirect to new file HTML page
            _httpContextAccessor.HttpContext.Response.Redirect($"{_webHelper.GetStoreLocation()}OrderPayments/{file}");

            return;
        }

        public async Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return await Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            //get the transaction id from the order
            var transactionId = refundPaymentRequest.Order.CaptureTransactionId;

            //get the amount to refund from the request
            var amount = refundPaymentRequest.AmountToRefund;

            //get the currency code from the order
            var currency = refundPaymentRequest.Order.CustomerCurrencyCode;

            //get the apm reference from the order
            var apmreference = refundPaymentRequest.Order.AuthorizationTransactionId;

            //create a HttpClient object
            var client = new HttpClient();

            //set the base address of the web service
            client.BaseAddress = new Uri("https://entegrasyon.asseco-see.com.tr/fim/est3Dgate");

            //create a dictionary to store the parameters
            var parameters = new Dictionary<string, string>();

            //add the parameters to the dictionary
            parameters.Add("clientid", _paramPaymentSettings.ClientCode); //your Parampos client id
            parameters.Add("oid", transactionId); //the transaction id of the payment
            parameters.Add("amount", amount.ToString()); //the amount to refund
            parameters.Add("currency", currency); //the currency code of the payment
            parameters.Add("apmreference", apmreference); //the apm reference of the payment

            //create a form url encoded content from the dictionary
            var content = new FormUrlEncodedContent(parameters);

            //send a post request to the web service and get the response
            var response = await client.PostAsync("/refund", content);

            //create a RefundPaymentResult object
            var result = new RefundPaymentResult();

            //check if the response is successful
            if (response.IsSuccessStatusCode)
            {
                //read the response content as a string
                var responseContent = await response.Content.ReadAsStringAsync();

                //parse the response content as a xml document
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(responseContent);

                //get the ProcReturnCode element from the xml document
                var procReturnCode = xmlDocument.SelectSingleNode("//ProcReturnCode");

                //check if the ProcReturnCode is 00, which means success
                if (procReturnCode.InnerText == "00")
                {
                    //set the new payment status as refunded or partially refunded
                    result.NewPaymentStatus = refundPaymentRequest.IsPartialRefund ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
                }
                else
                {
                    //get the ErrMsg element from the xml document
                    var errMsg = xmlDocument.SelectSingleNode("//ErrMsg");

                    //set the error message from the ErrMsg element
                    result.Errors.Add(errMsg.InnerText);
                }
            }
            else
            {
                //set a generic error message
                result.Errors.Add("An error occurred while calling the web service.");
            }

            //return the result object
            return result;
        }

        public async Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            var warnings = new List<string>();        

            //validate
            var validator = new PaymnetInfoValidators(_localizationService);
            var model = new PaymentInfoModel
            {
                CardholderName = form["CardholderName"],
                CardNumber = form["CardNumber"],
                CardCode = form["CardCode"],
                ExpireMonth = form["ExpireMonth"],
                ExpireYear = form["ExpireYear"]
            };
            var validationResult = validator.Validate(model);

            if (!validationResult.IsValid)
                warnings.AddRange(validationResult.Errors.Select(error => error.ErrorMessage));

            warnings.AddRange(validationResult.Errors.Select(error => error.ErrorMessage));
            if (warnings.Count > 0)
            {
                return await Task.FromResult<IList<string>>(warnings);
            }

            string url = _paramPaymentSettings.UseSandbox ? _paramPaymentSettings.TestUrl : _paramPaymentSettings.ProductUrl;

            string data = "" +
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<soap:Body>" +
                        "<TP_KK_Verify xmlns=\"https://turkpos.com.tr/\">" +
                            "<G>" +
                                "<CLIENT_CODE>" + _paramPaymentSettings.ClientCode + "</CLIENT_CODE>" +
                                "<CLIENT_USERNAME>" + _paramPaymentSettings.ClientUsername + "</CLIENT_USERNAME>" +
                                "<CLIENT_PASSWORD>" + _paramPaymentSettings.ClientPassword + "</CLIENT_PASSWORD>" +
                            "</G>" +
                            "<KK_No>" + form["CardNumber"] + "</KK_No>" +
                            "<KK_SK_Ay>" + form["ExpireMonth"] + "</KK_SK_Ay>" +
                            "<KK_SK_Yil>" + form["ExpireYear"] + "</KK_SK_Yil>" +
                            "<KK_CVC>" + form["CardCode"] + "</KK_CVC>" +
                            "<Return_URL></Return_URL>" +
                            "<Data1></Data1>" +
                            "<Data2></Data2>" +
                            "<Data3></Data3>" +
                            "<Data4></Data4>" +
                            "<Data5></Data5>" +
                        "</TP_KK_Verify>" +
                    "</soap:Body>" +
                "</soap:Envelope>";


            byte[] buffer = Encoding.ASCII.GetBytes(data);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "POST";
            request.ContentType = "text/xml; charset=\"utf-8\"";
            request.ContentLength = buffer.Length;
            request.Headers.Add("SOAPAction", "https://turkpos.com.tr/TP_KK_Verify");
            Stream post = request.GetRequestStream();

            post.Write(buffer, 0, buffer.Length);
            post.Close();



            string responseResult = "";
            try
            {
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                Stream responseData = response.GetResponseStream();
                StreamReader responseReader = new StreamReader(responseData);
                responseResult = responseReader.ReadToEnd();
                HttpStatusCode statusCode = response.StatusCode;
                string statusCodeStr = response.StatusCode.ToString();
                if (statusCode != HttpStatusCode.OK)
                {
                    await _logger.ErrorAsync("PARAM CARD VALIDATE REQUEST:\nError Code: " + statusCodeStr + "\n" + data, null, null);
                }
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("PARAM CARD VALIDATE REQUEST:\n" + data, ex, null);
            }

            responseResult = responseResult.Replace(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"", "");
            responseResult = responseResult.Replace(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", "");
            responseResult = responseResult.Replace(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"", "");
            responseResult = responseResult.Replace("soap:", "").Replace(":soap", "");
            responseResult = responseResult.Replace(" xmlns=\"https://turkpos.com.tr/\"", "");
            responseResult = responseResult.Replace("<Body>", "").Replace("</Body>", "");
            responseResult = responseResult.Replace("<Envelope>", "<root>").Replace("</Envelope>", "</root>");

            XDocument xdoc = XDocument.Parse(responseResult);

            string islem_ID = xdoc.Descendants("Islem_ID").FirstOrDefault().Value;
            string uCD_URL = xdoc.Descendants("UCD_URL").FirstOrDefault().Value;
            string sonuc_Str = xdoc.Descendants("Sonuc_Str").FirstOrDefault().Value;
            string banka_Sonuc_Kod = xdoc.Descendants("Banka_Sonuc_Kod").FirstOrDefault().Value;

            if (islem_ID == "0" && sonuc_Str != "")
            {
                warnings.Add(sonuc_Str);
            }

            return await Task.FromResult<IList<string>>(warnings);
        }

        public async Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            //get the transaction id from the order
            var transactionId = voidPaymentRequest.Order.CaptureTransactionId;

            //get the amount to void from the request
            var amount = _paramPaymentSettings.ClientCode;

            //get the currency code from the order
            var currency = voidPaymentRequest.Order.CustomerCurrencyCode;

            //get the apm reference from the order
            var apmreference = voidPaymentRequest.Order.AuthorizationTransactionId;

            //create a HttpClient object
            var client = new HttpClient();

            //set the base address of the web service
            client.BaseAddress = new Uri("https://entegrasyon.asseco-see.com.tr/fim/est3Dgate");

            //create a dictionary to store the parameters
            var parameters = new Dictionary<string, string>();

            //add the parameters to the dictionary
            parameters.Add("clientid", _paramPaymentSettings.ClientCode); //your Parampos client id
            parameters.Add("oid", transactionId); //the transaction id of the payment
            parameters.Add("amount", amount.ToString()); //the amount to void
            parameters.Add("currency", currency); //the currency code of the payment
            parameters.Add("apmreference", apmreference); //the apm reference of the payment

            //create a form url encoded content from the dictionary
            var content = new FormUrlEncodedContent(parameters);

            //send a post request to the web service and get the response
            var response = await client.PostAsync("/void", content);

            //create a VoidPaymentResult object
            var result = new VoidPaymentResult();

            //check if the response is successful
            if (response.IsSuccessStatusCode)
            {
                //read the response content as a string
                var responseContent = await response.Content.ReadAsStringAsync();

                //parse the response content as a xml document
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(responseContent);

                //get the ProcReturnCode element from the xml document
                var procReturnCode = xmlDocument.SelectSingleNode("//ProcReturnCode");

                //check if the ProcReturnCode is 00, which means success
                if (procReturnCode.InnerText == "00")
                {
                    //set the new payment status as voided
                    result.NewPaymentStatus = PaymentStatus.Voided;
                }
                else
                {
                    //get the ErrMsg element from the xml document
                    var errMsg = xmlDocument.SelectSingleNode("//ErrMsg");

                    //set the error message from the ErrMsg element
                    result.Errors.Add(errMsg.InnerText);
                }
            }
            else
            {
                //set a generic error message
                result.Errors.Add("An error occurred while calling the web service.");
            }

            //return the result object
            return result;
        }

        #endregion

    }
}
