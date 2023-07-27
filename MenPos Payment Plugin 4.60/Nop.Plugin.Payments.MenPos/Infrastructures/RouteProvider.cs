using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.MenPos.Infrastructures
{
    /// <summary>
    /// Represents plugin route provider
    /// </summary>
    public class RouteProvider : IRouteProvider
    { /// <summary>
      /// Register routes
      /// </summary>
      /// <param name="endpointRouteBuilder">Route builder</param>
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            endpointRouteBuilder.MapControllerRoute(KuveytTurkDefaults.Payment, "Plugins/PaymentMenPos/Payment",
                 new { controller = "PaymentMenPos", action = "Payment" });

            endpointRouteBuilder.MapControllerRoute(KuveytTurkDefaults.Fail, "Plugins/PaymentMenPos/Fail",
                 new { controller = "PaymentMenPos", action = "Fail" });

            endpointRouteBuilder.MapControllerRoute(KuveytTurkDefaults.Approval, "Plugins/PaymentMenPos/Approval",
                 new { controller = "PaymentMenPos", action = "Approval" });

            endpointRouteBuilder.MapControllerRoute(KuveytTurkDefaults.SendApprove, "Plugins/PaymentMenPos/SendApprove",
                 new { controller = "PaymentMenPos", action = "SendApprove" });

        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => 0;
    }
}
