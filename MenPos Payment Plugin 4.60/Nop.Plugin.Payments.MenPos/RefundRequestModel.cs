using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.MenPos
{
    public class RefundRequestModel
    {
        public string MerchantId { get; set; }
        public string MerchantOrderId { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
        public object Hash { get; set; }
    }
}
