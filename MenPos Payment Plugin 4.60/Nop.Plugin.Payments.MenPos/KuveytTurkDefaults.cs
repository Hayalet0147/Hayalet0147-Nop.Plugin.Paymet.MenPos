using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.MenPos
{

    
    /// <summary>
    /// Represents plugin constants
    /// </summary>
    public class KuveytTurkDefaults
    {
        /// <summary>
        /// Gets a name of the view component to display payment info in public store
        /// </summary>
        public const string PAYMENT_INFO_VIEW_COMPONENT_NAME = "PaymentMenPosViewComponent";

        /// <summary>
        /// Gets payment method system name
        /// </summary>
        public static string SystemName => "Payments.PaymentMenPos";

        /// <summary>
        /// Gets IPN handler route name
        /// </summary>
        public static string Payment => "Plugin.Payments.PaymentMenPos.Payment";
        public static string Fail => "Plugin.Payments.PaymentMenPos.Fail";
        public static string Approval => "Plugin.Payments.PaymentMenPos.Approval";
        public static string SendApprove => "Plugin.Payments.PaymentMenPos.SendApprove";

        public static string OrderPaymentsDirectory => "OrderPayments";

        public static string LocalizationStringStart => "Plugins.Payments.PaymentMenPos.";
    }
}
