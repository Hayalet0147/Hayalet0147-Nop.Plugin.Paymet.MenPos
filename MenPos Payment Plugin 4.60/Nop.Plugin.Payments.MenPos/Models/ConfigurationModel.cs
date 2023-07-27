using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper.Configuration;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using Octopus.Client.Model;

namespace Nop.Plugin.Payments.MenPos.Models;

public record ConfigurationModel : BaseNopModel
{
    //ParamPos

    [NopResourceDisplayName("Plugins.Payments.Param.UseSandbox.Hint")]
    public bool UseSandbox { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Param.ClientCode")]
    public string ClientCode { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Param.ClientUsername")]
    public string ClientUsername { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Param.ClientPassword")]
    public string ClientPassword { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Param.Guid")]
    public string Guid { get; set; }

    [NopResourceDisplayName("Test Url:")]
    public string TestUrl { get; set; }

    [NopResourceDisplayName("Product Url:")]
    public string ProductUrl { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Param.Installment.Hint")]
    public bool Installment { get; set; }


    //Kuveyt Türk
    [NopResourceDisplayName("Plugins.Payments.KuveytTurk.CustomerId")]
    public string CustomerId { get; set; }

    [NopResourceDisplayName("Plugins.Payments.KuveytTurk.MerchantId")]
    public string MerchantId { get; set; }

    [NopResourceDisplayName("Plugins.Payments.KuveytTurk.UserName")]
    public string UserName { get; set; }

    [NopResourceDisplayName("Plugins.Payments.KuveytTurk.Password")]
    [DataType(DataType.Password)]
    [Trim]
    public string Password { get; set; }
    
    
    //diğer ayarlar

    [NopResourceDisplayName("Plugins.Payments.MenPos.aratutar")]
    public decimal ara_tutar { get; set; }

    [NopResourceDisplayName("Plugins.Payments.MenPos.üsttutar")]
    public decimal ust_tutar { get; set; }
   
}
