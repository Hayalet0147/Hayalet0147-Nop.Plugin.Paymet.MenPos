using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;
using Nop.Services.Payments;

namespace Nop.Plugin.Payments.MenPos
{

    public class IParamPaymentSettings : ISettings
    {
        public bool UseSandbox { get; set; }
        public string ClientCode { get; set; }
        public string ClientUsername { get; set; }
        public string ClientPassword { get; set; }
        public string Guid { get; set; }
        public string TestUrl { get; set; }
        public string ProductUrl { get; set; }
        public bool Installment { get; set; }
        public int? InstallmentInt { get; set; }
        public ProcessPaymentRequest ProcessPaymentRequest { get; set; }
    }

    public class KuveytTurkPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets an account Id
        /// </summary>
        public string CustomerId { get; set; }

        /// <summary>
        /// Gets or sets a MerchantId
        /// </summary>
        public string MerchantId { get; set; }

        /// <summary>
        /// Gets or sets a Username
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets a Password
        /// </summary>
        public string Password { get; set; }
    }

    public class IOtherPaymentSettings : ISettings
    {
        public decimal ara_tutar { get; set; }

        public decimal ust_tutar { get; set; }
    }
}
