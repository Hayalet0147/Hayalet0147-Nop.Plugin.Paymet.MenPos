using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using DocumentFormat.OpenXml.EMMA;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.MenPos.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System.Security.Cryptography;
using System.Xml.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Nop.Plugin.Payments.MenPos.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Payments;
using OfficeOpenXml.Style;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Vml;
using Nop.Services.Plugins;
using static Nop.Plugin.Payments.MenPos.Models.ConfigurationModel;
using System.Globalization;

namespace Nop.Plugin.Payments.MenPos.Controllers
{
    public class PaymentMenPosController : BasePaymentController
    {
        #region Fields
        private readonly ISettingService _settingService;
        private readonly IParamPaymentSettings _paramPaymentSettings;
        private readonly IPermissionService _permissionService;
        private readonly IWorkContext _workContext;
        private readonly IOrderService _orderService;
        private readonly IStoreContext _storeContext;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHelper _webHelper;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly KuveytTurkServices _kuveytTurkService;
        private readonly KuveytTurkPaymentSettings _kuveytTurkPaymentSettings;
        private readonly IOtherPaymentSettings _otherPaymentSettings;
        #endregion

        #region Ctor
        public PaymentMenPosController(ISettingService settingService, IParamPaymentSettings paramPaymentSettings, 
            IPermissionService permissionService, IWorkContext workContext, IOrderService orderService, 
            IStoreContext storeContext, IOrderProcessingService orderProcessingService, 
            IHttpContextAccessor httpContextAccessor, IWebHelper webHelper, KuveytTurkServices kuveytTurkServices, 
            IGenericAttributeService genericAttributeService, ILocalizationService localizationService, 
            INotificationService notificationService, KuveytTurkPaymentSettings kuveytTurkPaymentSettings,
            IOtherPaymentSettings otherPaymentSettings)
        {
            _settingService = settingService;
            _paramPaymentSettings = paramPaymentSettings;
            _permissionService = permissionService;
            _workContext = workContext;
            _orderService = orderService;
            _storeContext = storeContext;
            _orderProcessingService = orderProcessingService;
            _httpContextAccessor = httpContextAccessor;
            _webHelper = webHelper;
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _kuveytTurkPaymentSettings = kuveytTurkPaymentSettings;
            _kuveytTurkService = kuveytTurkServices;
            _otherPaymentSettings = otherPaymentSettings;
        }
        #endregion

        #region Methods

        /// <summary>
        /// Run this action if anything goes wronge
        /// </summary>
        /// <returns>Redirect to ShoppingCart</returns>
        public async Task<IActionResult> Fail()
        {
            //Get VPosTransactionResponseContract model from Request
            var model = _kuveytTurkService.GetVPosTransactionResponseContract(Request.Form["AuthenticationResponse"]);

            //Define err variable
            var err = _localizationService.GetResourceAsync(model.ResponseCode);

            //Send warning notification to user about error message
            _notificationService.WarningNotification($"{model.ResponseCode} - {err}({model.ResponseMessage})");

            //Repeate the order then delete it
            var order = await _orderService.GetOrderByGuidAsync(Guid.Parse(model.MerchantOrderId));
            if (order == null || order.Deleted || _workContext.GetCurrentCustomerAsync().Id != order.CustomerId)
                return Challenge();
            await _orderProcessingService.ReOrderAsync(order);
            await _orderService.DeleteOrderAsync(order);

            //Delete temp files of this customer in OrderPayments directory
            _kuveytTurkService.ClearOrderPaymentsFiles(order.CustomerId);

            //Redirect to ShoppingCart
            return RedirectToRoute("ShoppingCart");
        }

        /// <summary>
        /// Run this action if every thing goes write
        /// </summary>
        /// <returns></returns>
        public async Task<IActionResult> SendApprove()
        {
            //Get VPosTransactionResponseContract model from Request
            var model = _kuveytTurkService.GetVPosTransactionResponseContract(Request.Form["AuthenticationResponse"]);

            //Get order details
            var order = await _orderService.GetOrderByGuidAsync(new Guid(model.MerchantOrderId));

            //Save payment details in variables
            var merchantOrderId = model.MerchantOrderId;
            var amount = model.VPosMessage.Amount;
            var mD = model.MD;
            var customerId = _kuveytTurkPaymentSettings.CustomerId; //Müsteri Numarasi
            var merchantId = _kuveytTurkPaymentSettings.MerchantId; //Magaza Kodu
            var userName = _kuveytTurkPaymentSettings.UserName; //api rollü kullanici adı

            //Hash some data in one string result
            SHA1 sha = new SHA1CryptoServiceProvider();
            var hashedPassword = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(_kuveytTurkPaymentSettings.Password)));
            var hashstr = merchantId + merchantOrderId + amount + userName + hashedPassword;
            var hashbytes = System.Text.Encoding.GetEncoding("ISO-8859-9").GetBytes(hashstr);
            var inputbytes = sha.ComputeHash(hashbytes);
            var hashData = Convert.ToBase64String(inputbytes);

            //Generate XML code to send it to ProvisionGate
            var postData =
                "<KuveytTurkVPosMessage xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema'>" +
                "<APIVersion>1.0.0</APIVersion>" +
                "<HashData>" + hashData + "</HashData>" +
                "<MerchantId>" + merchantId + "</MerchantId>" +
                "<CustomerId>" + customerId + "</CustomerId>" +
                "<UserName>" + userName + "</UserName>" +
                "<CurrencyCode>0949</CurrencyCode>" +
                "<TransactionType>Sale</TransactionType>" +
                "<InstallmentCount>0</InstallmentCount>" +
                "<Amount>" + amount + "</Amount>" +
                "<MerchantOrderId>" + merchantOrderId + "</MerchantOrderId>" +
                "<TransactionSecurity>3</TransactionSecurity>" +
                "<KuveytTurkVPosAdditionalData>" +
                "<AdditionalData>" +
                "<Key>MD</Key>" +
                "<Data>" + mD + "</Data>" +
                "</AdditionalData>" +
                "</KuveytTurkVPosAdditionalData>" +
                "</KuveytTurkVPosMessage>";

            var responseString = _kuveytTurkService.PostPaymentDataToUrl("https://boa.kuveytturk.com.tr/sanalposservice/Home/ThreeDModelProvisionGate", postData);

            var modelRes = _kuveytTurkService.GetVPosTransactionResponseContract(responseString);

            if (modelRes.ResponseCode == "00")
            {
                order.PaymentStatus = PaymentStatus.Paid;
                await _orderService.UpdateOrderAsync(order);
                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync($"{KuveytTurkDefaults.LocalizationStringStart}PaymentDone"));

                return Redirect($"{_webHelper.GetStoreLocation()}");
            }

            var err = _kuveytTurkService.GetErrorMessage(modelRes.ResponseCode);
            _notificationService.WarningNotification($"{modelRes.ResponseCode} - {err}({model.ResponseMessage})");

            //Repeate the order then delete it

            if (order == null || order.Deleted || _workContext.GetCurrentCustomerAsync().Id != order.CustomerId)
                return Challenge();
            await _orderProcessingService.ReOrderAsync(order);
            await _orderService.DeleteOrderAsync(order);

            return Redirect("/");
        }


        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            
            var model = new ConfigurationModel()
            {
                //param Pos
                UseSandbox = _paramPaymentSettings.UseSandbox,
                ClientCode = _paramPaymentSettings.ClientCode,
                ClientUsername = _paramPaymentSettings.ClientUsername,
                ClientPassword = _paramPaymentSettings.ClientPassword,
                Guid = _paramPaymentSettings.Guid,
                TestUrl = _paramPaymentSettings.TestUrl,
                ProductUrl = _paramPaymentSettings.ProductUrl,
                Installment = _paramPaymentSettings.Installment,

                //Kuveyt Türk
                CustomerId = _kuveytTurkPaymentSettings.CustomerId,
                MerchantId = _kuveytTurkPaymentSettings.MerchantId,
                UserName = _kuveytTurkPaymentSettings.UserName,
                Password = _kuveytTurkPaymentSettings.Password,

                //Diğer ayarlar
                ara_tutar = _otherPaymentSettings.ara_tutar,
                ust_tutar = _otherPaymentSettings.ust_tutar,
            };


            return View("~/Plugins/Payment.MenPos/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //save settings
            //Param Pos
            _paramPaymentSettings.UseSandbox = model.UseSandbox;
            _paramPaymentSettings.ClientCode = model.ClientCode;
            _paramPaymentSettings.ClientUsername = model.ClientUsername;
            _paramPaymentSettings.ClientPassword = model.ClientPassword;
            _paramPaymentSettings.Guid = model.Guid;
            _paramPaymentSettings.TestUrl = model.TestUrl;
            _paramPaymentSettings.ProductUrl = model.ProductUrl;
            _paramPaymentSettings.Installment = model.Installment;
            // Kuveyt Türk
            _kuveytTurkPaymentSettings.CustomerId = model.CustomerId;
            _kuveytTurkPaymentSettings.MerchantId = model.MerchantId;
            _kuveytTurkPaymentSettings.UserName = model.UserName;
            _kuveytTurkPaymentSettings.Password = model.Password;
            //Diğer ayarlar
            _otherPaymentSettings.ara_tutar = model.ara_tutar; // result = "₺77,02";;
            _otherPaymentSettings.ust_tutar = model.ust_tutar; // result = "₺77,02";;
            await _settingService.SaveSettingAsync(_paramPaymentSettings);
            await _settingService.SaveSettingAsync(_kuveytTurkPaymentSettings);
            await _settingService.SaveSettingAsync(_otherPaymentSettings);

            return await Configure();
        }


        [HttpPost]
        public async Task<JsonResult> BIN_SanalPosAsync(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
            {
                return Json("");
            }

            if (string.IsNullOrEmpty(_paramPaymentSettings.ClientCode))
            {
                if ((await _workContext.GetWorkingLanguageAsync()).LanguageCulture == "tr-TR")
                {
                    return Json(new { error = "Hata: Ayarlardan 'Müşteri Kodu' girişi yapınız!" });
                }
                else
                {
                    return Json(new { error = "Error: Enter 'Client Code' from settings!" });
                }
            }
            if (string.IsNullOrEmpty(_paramPaymentSettings.ClientUsername))
            {
                if ((await _workContext.GetWorkingLanguageAsync()).LanguageCulture == "tr-TR")
                {
                    return Json(new { error = "Hata: Ayarlardan 'Müşteri Adı' girişi yapınız!" });
                }
                else
                {
                    return Json(new { error = "Error: Enter 'Client Username' from settings!" });
                }
            }
            if (string.IsNullOrEmpty(_paramPaymentSettings.ClientPassword))
            {
                if ((await _workContext.GetWorkingLanguageAsync()).LanguageCulture == "tr-TR")
                {
                    return Json(new { error = "Hata: Ayarlardan 'Müşteri Parola' girişi yapınız!" });
                }
                else
                {
                    return Json(new { error = "Error: Enter 'Client Password' from settings!" });
                }
            }
            if (string.IsNullOrEmpty(_paramPaymentSettings.ProductUrl))
            {
                if ((await _workContext.GetWorkingLanguageAsync()).LanguageCulture == "tr-TR")
                {
                    return Json(new { error = "Hata: Ayarlardan 'Product Url' girişi yapınız!" });
                }
                else
                {
                    return Json(new { error = "Error: Enter 'Product Url' from settings!" });
                }
            }
            if (string.IsNullOrEmpty(_paramPaymentSettings.ProductUrl))
            {
                if ((await _workContext.GetWorkingLanguageAsync()).LanguageCulture == "tr-TR")
                {
                    return Json(new { error = "Hata: Ayarlardan 'Product Url' girişi yapınız!" });
                }
                else
                {
                    return Json(new { error = "Error: Enter 'Product Url' from settings!" });
                }
            }
            string url = _paramPaymentSettings.UseSandbox ? _paramPaymentSettings.TestUrl : _paramPaymentSettings.ProductUrl;
            string clientCode = _paramPaymentSettings.ClientCode;
            string clientUsername = _paramPaymentSettings.ClientUsername;
            string clientPassword = _paramPaymentSettings.ClientPassword;

            string data = "" +
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<soap:Body>" +
                        "<BIN_SanalPos xmlns=\"https://turkpos.com.tr/\">" +
                            "<G>" +
                                "<CLIENT_CODE>" + clientCode + "</CLIENT_CODE>" +
                                "<CLIENT_USERNAME>" + clientUsername + "</CLIENT_USERNAME>" +
                                "<CLIENT_PASSWORD>" + clientPassword + "</CLIENT_PASSWORD>" +
                            "</G>" +
                            "<BIN>" + cardCode + "</BIN>" +
                        "</BIN_SanalPos>" +
                    "</soap:Body>" +
                "</soap:Envelope>";

            byte[] buffer = Encoding.ASCII.GetBytes(data);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "POST";
            request.ContentType = "text/xml; charset=\"utf-8\"";
            request.ContentLength = buffer.Length;
            request.Headers.Add("SOAPAction", "https://turkpos.com.tr/BIN_SanalPos");
            Stream post = request.GetRequestStream();

            post.Write(buffer, 0, buffer.Length);
            post.Close();

            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            Stream responseData = response.GetResponseStream();
            StreamReader responseReader = new StreamReader(responseData);
            string responseResult = responseReader.ReadToEnd();
            responseResult = responseResult.Replace(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"", "");
            responseResult = responseResult.Replace(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", "");
            responseResult = responseResult.Replace(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"", "");
            responseResult = responseResult.Replace("soap:", "").Replace(":soap", "");
            responseResult = responseResult.Replace(" xmlns=\"https://turkpos.com.tr/\"", "");
            responseResult = responseResult.Replace("<Body>", "").Replace("</Body>", "");
            responseResult = responseResult.Replace("<Envelope>", "<root>").Replace("</Envelope>", "</root>");

            XDocument xdoc = XDocument.Parse(responseResult);

            string sanalPOS_ID = xdoc.Descendants("SanalPOS_ID").FirstOrDefault().Value;
            string kart_Banka = xdoc.Descendants("Kart_Banka").FirstOrDefault().Value;


            var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync("https://lookup.binlist.net/" + cardCode);

            string name = new Regex(@"\{""name"":""([^""].*?)"",", RegexOptions.IgnoreCase).Match(json).Groups[1].Value;
            string brand = new Regex(@"""scheme"":""([^""].*?)"",", RegexOptions.IgnoreCase).Match(json).Groups[1].Value;
            string type = new Regex(@"""type"":""([^""].*?)"",", RegexOptions.IgnoreCase).Match(json).Groups[1].Value;
            brand = char.ToUpper(brand[0]) + brand.Substring(1);
            type = char.ToUpper(type[0]) + type.Substring(1);

            return Json(new { SanalPOS_ID = sanalPOS_ID, Kart_Banka = kart_Banka, Kart_Brand = brand, Kart_Tip = type });
        }



        [Route("PaymentMenPos/OrderRefresh/{orderId?}/")]
        public async Task<IActionResult> OrderRefreshAsync(int orderId = 0)
        {
            if (orderId > 0)
            {
                Order lastOrder = (await _orderService.GetOrderByIdAsync(orderId));

                if (lastOrder != null)
                {
                    if (lastOrder.PaymentMethodSystemName.ToLower().Equals("payments.param"))
                    {
                        if (lastOrder.PaymentStatus == PaymentStatus.Pending)
                        {
                            if (_orderProcessingService.CanCancelOrder(lastOrder))
                            {
                                await _orderProcessingService.DeleteOrderAsync(lastOrder);
                                await _orderProcessingService.ReOrderAsync(lastOrder);
                                return RedirectToRoute("Checkout");
                            }
                        }
                    }
                }

                return RedirectToRoute("OrderDetails", new { orderId = orderId });
            }

            return RedirectToRoute("Cart");
        }


        [Route("PaymentMenPos/OrderComplete/{hash?}/{orderId?}/")]
        public async Task<IActionResult> OrderCompleteAsync(string hash = "", int orderId = 0)
        {
            if (hash != "" && orderId > 0)
            {
                Order lastOrder = (await _orderService.GetOrderByIdAsync(orderId));

                if (lastOrder != null)
                {
                    if (lastOrder.PaymentMethodSystemName.ToLower().Equals("payments.param"))
                    {
                        if (lastOrder.OrderGuid.ToString() == hash)
                        {
                            if (lastOrder.PaymentStatus == PaymentStatus.Pending)
                            {
                                if (_orderProcessingService.CanCancelOrder(lastOrder))
                                {
                                    if (_orderProcessingService.CanMarkOrderAsPaid(lastOrder))
                                    {
                                        await _orderProcessingService.MarkOrderAsPaidAsync(lastOrder);
                                        return RedirectToRoute("CheckoutCompleted", new { orderId = orderId });
                                    }
                                }
                            }
                        }
                    }
                }

                return RedirectToRoute("OrderDetails", new { orderId = orderId });
            }

            return RedirectToRoute("Cart");
        }
        #endregion

    }
}
