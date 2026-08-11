using LadowebservisMVC.Controllers.Models;
using LadowebservisMVC.Models;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Script.Serialization;
using static System.Net.WebRequestMethods;

namespace LadowebservisMVC.Util
{

    public class Mailer
    {
        private static bool TrySendSmtp(MailMessage mail, string smtpHost, int smtpPort, bool smtpUseSsl, string smtpUser, string smtpPass, out Exception error)
        {
            error = null;
            try
            {
                // Ensure modern TLS where supported (important for some SMTP providers).
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

                using (var client = new SmtpClient(smtpHost))
                {
                    client.EnableSsl = smtpUseSsl;
                    client.Port = smtpPort;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                    client.Send(mail);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static bool TrySendWithConfiguredSmtp(MailMessage mail)
        {
            var smtpHostPrimary = (System.Configuration.ConfigurationManager.AppSettings["Smtp:Host"] ?? string.Empty).Trim();
            var smtpUserPrimary = (System.Configuration.ConfigurationManager.AppSettings["Smtp:User"] ?? string.Empty).Trim();
            var smtpPassPrimary = (System.Configuration.ConfigurationManager.AppSettings["Smtp:Pass"] ?? string.Empty).Trim();
            var smtpPortPrimaryStr = (System.Configuration.ConfigurationManager.AppSettings["Smtp:Port"] ?? string.Empty).Trim();
            var smtpUseSslPrimaryStr = (System.Configuration.ConfigurationManager.AppSettings["Smtp:UseSsl"] ?? string.Empty).Trim();

            var smtpHostLegacy = (System.Configuration.ConfigurationManager.AppSettings["smtpHost"] ?? "email.active24.com").Trim();
            var smtpUserLegacy = (System.Configuration.ConfigurationManager.AppSettings["smtpUser"] ?? "info@ladowebservis.sk").Trim();
            var smtpPassLegacy = (System.Configuration.ConfigurationManager.AppSettings["smtpPassword"] ?? string.Empty).Trim();
            var smtpPortLegacyStr = (System.Configuration.ConfigurationManager.AppSettings["smtpPort"] ?? "587").Trim();
            var smtpUseSslLegacyStr = (System.Configuration.ConfigurationManager.AppSettings["smtpUseSsl"] ?? "true").Trim();

            // Prefer explicit Smtp:* values when they are present, otherwise fall back to legacy keys.
            var smtpHost = !string.IsNullOrWhiteSpace(smtpHostPrimary) ? smtpHostPrimary : smtpHostLegacy;
            var smtpUser = !string.IsNullOrWhiteSpace(smtpUserPrimary) ? smtpUserPrimary : smtpUserLegacy;
            var smtpPass = !string.IsNullOrWhiteSpace(smtpPassPrimary) ? smtpPassPrimary : smtpPassLegacy;
            var smtpPortStr = !string.IsNullOrWhiteSpace(smtpPortPrimaryStr) ? smtpPortPrimaryStr : smtpPortLegacyStr;
            var smtpUseSslStr = !string.IsNullOrWhiteSpace(smtpUseSslPrimaryStr) ? smtpUseSslPrimaryStr : smtpUseSslLegacyStr;

            if (!int.TryParse(smtpPortStr, out var smtpPort)) smtpPort = 587;
            if (!bool.TryParse(smtpUseSslStr, out var smtpUseSsl)) smtpUseSsl = true;

            // Attempt #1 using preferred credentials
            if (!string.IsNullOrWhiteSpace(smtpHost) && !string.IsNullOrWhiteSpace(smtpUser) && !string.IsNullOrWhiteSpace(smtpPass))
            {
                if (TrySendSmtp(mail, smtpHost, smtpPort, smtpUseSsl, smtpUser, smtpPass, out var err1)) return true;
                System.Diagnostics.Trace.TraceError($"SMTP send failed (primary): host={smtpHost}, port={smtpPort}, ssl={smtpUseSsl}, user={smtpUser} - {err1}");
            }

            // Attempt #2 using legacy credentials (common when Smtp:* keys are present but incorrect)
            if (!string.IsNullOrWhiteSpace(smtpHostLegacy) && !string.IsNullOrWhiteSpace(smtpUserLegacy) && !string.IsNullOrWhiteSpace(smtpPassLegacy)
                && (!string.Equals(smtpUserLegacy, smtpUser, StringComparison.OrdinalIgnoreCase) || !string.Equals(smtpPassLegacy, smtpPass, StringComparison.Ordinal)))
            {
                if (!int.TryParse(smtpPortLegacyStr, out var port2)) port2 = 587;
                if (!bool.TryParse(smtpUseSslLegacyStr, out var ssl2)) ssl2 = true;

                if (TrySendSmtp(mail, smtpHostLegacy, port2, ssl2, smtpUserLegacy, smtpPassLegacy, out var err2)) return true;
                System.Diagnostics.Trace.TraceError($"SMTP send failed (legacy): host={smtpHostLegacy}, port={port2}, ssl={ssl2}, user={smtpUserLegacy} - {err2}");
            }

            return false;
        }

        // Block known spammer patterns (case-insensitive) - reject any containing 'loori'
        private static readonly string[] BannedEmailFragments = new[] { "loori", "bitcoin", "sign in", "get ", "next" };

        private static readonly string[] BannedDomains = new[]
        {
            "@mail.ru",
            "@bk.ru",
            "@list.ru",
            "@outlook.es",
            "@icloud.cim"
        };

        private static readonly string[] BannedEmails = new[]
        {
            "dolnovam@mail.ru",
            "test@spam.ru",
            "margarita.stolyarova.26.05.1995@mail.ru",
            "judyhilly92@gmail.com",
            "dulal_ctg@yaoo.com",
            "ramyl_gilmanov@mail.ru",
            "slayd@t-online.de",
            "jessInjuff7037@gmail.com",
            "sergey-luzan98@mail.ru",
            "tacusol-6816@mail.ru",
            "konstantinrovini@mail.ru",
            "simonenko.borya@list.ru",
            "mamedov.edgar.1992.3.3@inbox.ru",
            "zuyev-vadik@bk.ru",
            "asurering@gmail.com",
            "spacepapes@aol.com",
            "1821977ek@gmail.com",
            "imqxswtz@tacoblastmail.com",
            "slod@unipegasobra.com",
            "tcrepe-tech@laposte.net",
            "fbhdfgh@gmail.com",
            "sggtrfgfg@gmail.com",
            "jacksrenome@gmx.com",
            "dfhfgdffg@gmail.com",
            "paulauskasgintautas@gmail.com",
            "uzibiupz@young-williams.biz",
            "610dv@ro.ru",
            "ozjtforz@allen.com"

        };

        // Pattern to detect suspicious phone number patterns (e.g., +1.8...Bitcoin)
        private static readonly string[] SuspiciousPatterns = new[]
        {
            "+1.8",
            "+1 8",
            "+18",
            "вitсоin", // Cyrillic characters mimicking "bitcoin"
            "bitcoin sign in",
            "bitcoin get",
            "bitcoin next"
        };

        // Unsubscribe endpoint base URL - change to match your domain
        private static readonly string UnsubscribeBaseUrl = "https://ladowebservis.sk/Home/Unsubscribe";

        // Helper to build unsubscribe URL
        private static string GetUnsubscribeUrl(string customerEmail)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return string.Empty;
            var encoded = HttpUtility.UrlEncode(customerEmail);
            return $"{UnsubscribeBaseUrl}?email={encoded}&token=marketing";
        }

        private static string GetOrderNumber(Session session)
        {
            if (session == null) return string.Empty;
            if (session.Metadata != null && session.Metadata.ContainsKey("orderNumber") && !string.IsNullOrWhiteSpace(session.Metadata["orderNumber"]))
            {
                return session.Metadata["orderNumber"].Trim();
            }

            return string.IsNullOrWhiteSpace(session.Id) ? string.Empty : session.Id.Trim();
        }

        private static string BuildOrderNumberHtml(string orderNumber, string label = "Číslo objednávky")
        {
            if (string.IsNullOrWhiteSpace(orderNumber)) return string.Empty;
            return $"<p style='margin:0 0 8px 0;color:#555;'>{HttpUtility.HtmlEncode(label)}: <code>{HttpUtility.HtmlEncode(orderNumber)}</code></p>";
        }

        private static string NormalizeShippingMethod(string shippingMethod)
        {
            return (shippingMethod ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string GetShippingLabel(string shippingMethod)
        {
            var normalizedShippingMethod = NormalizeShippingMethod(shippingMethod);
            if (normalizedShippingMethod == "courier" || normalizedShippingMethod == "kuriér") return "Kuriér na adresu";
            if (normalizedShippingMethod == "gls-courier" || normalizedShippingMethod == "glscourier") return "GLS kuriér";
            if (normalizedShippingMethod == "gls-parcelshop" || normalizedShippingMethod == "glsparcelshop" || normalizedShippingMethod == "gls-pickup") return "GLS ParcelShop";
            if (normalizedShippingMethod == "gls-box" || normalizedShippingMethod == "glsbox") return "GLS Box";
            if (normalizedShippingMethod == "alzabox" || normalizedShippingMethod == "alza-box") return "AlzaBox";
            if (normalizedShippingMethod == "dpd-courier" || normalizedShippingMethod == "dpdcourier" || normalizedShippingMethod == "dpd_kurier") return "DPD kuriér na adresu";
            if (normalizedShippingMethod == "dpd-pickup" || normalizedShippingMethod == "dpdpickup" || normalizedShippingMethod == "dpd-point" || normalizedShippingMethod == "dpdpoint") return "DPD odberné miesto";
            if (normalizedShippingMethod == "zasielkovna" || normalizedShippingMethod == "packeta") return "Zásielkovňa / Packeta";
            if (normalizedShippingMethod == "packeta-zbox" || normalizedShippingMethod == "packetazbox" || normalizedShippingMethod == "z-box" || normalizedShippingMethod == "zbox") return "Packeta Z-BOX";
            return string.IsNullOrWhiteSpace(shippingMethod) ? "Doprava" : shippingMethod.Trim();
        }

        private static bool IsCourierMethod(string shippingMethod)
        {
            var normalizedShippingMethod = NormalizeShippingMethod(shippingMethod);
            return normalizedShippingMethod == "courier"
                || normalizedShippingMethod == "kuriér"
                || normalizedShippingMethod == "gls-courier"
                || normalizedShippingMethod == "glscourier"
                || normalizedShippingMethod == "dpd-courier"
                || normalizedShippingMethod == "dpdcourier"
                || normalizedShippingMethod == "dpd_kurier";
        }

        private static string BuildShippingDetailsHtml(
            string shippingMethod,
            string pickupPointCode,
            string pickupPointName,
            string shippingAddressLine1,
            string shippingAddressLine2,
            string shippingCity,
            string shippingZip,
            string shippingCountry)
        {
            var shippingLabelSafe = HttpUtility.HtmlEncode(GetShippingLabel(shippingMethod));
            var pickupCodeSafe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(pickupPointCode) ? string.Empty : pickupPointCode);
            var pickupNameSafe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(pickupPointName) ? string.Empty : pickupPointName);
            var shipAddr1Safe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(shippingAddressLine1) ? string.Empty : shippingAddressLine1);
            var shipAddr2Safe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(shippingAddressLine2) ? string.Empty : shippingAddressLine2);
            var shipCitySafe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(shippingCity) ? string.Empty : shippingCity);
            var shipZipSafe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(shippingZip) ? string.Empty : shippingZip);
            var shipCountrySafe = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(shippingCountry) ? string.Empty : shippingCountry);
            var normalizedShippingMethod = NormalizeShippingMethod(shippingMethod);

            if (IsCourierMethod(normalizedShippingMethod))
            {
                return $@"<p style='margin:0;color:#555;'>Spôsob: <code>{shippingLabelSafe}</code></p>
           <p style='margin:8px 0 0 0;color:#555;'>Adresa: <strong>{(string.IsNullOrWhiteSpace(shipAddr1Safe) ? "Bude doplnená podľa údajov zákazníka" : shipAddr1Safe)}</strong></p>
           {(string.IsNullOrWhiteSpace(shipAddr2Safe) ? string.Empty : $"<p style='margin:4px 0 0 0;color:#555;'>{shipAddr2Safe}</p>")}
           {(string.IsNullOrWhiteSpace(shipZipSafe) && string.IsNullOrWhiteSpace(shipCitySafe) ? string.Empty : $"<p style='margin:4px 0 0 0;color:#555;'>{shipZipSafe} {shipCitySafe}</p>")}
           {(string.IsNullOrWhiteSpace(shipCountrySafe) ? string.Empty : $"<p style='margin:4px 0 0 0;color:#555;'>{shipCountrySafe}</p>")}";
            }

            if (normalizedShippingMethod == "zasielkovna" || normalizedShippingMethod == "packeta" || normalizedShippingMethod == "dpd-pickup" || normalizedShippingMethod == "dpdpickup" || normalizedShippingMethod == "dpd-point" || normalizedShippingMethod == "dpdpoint" || normalizedShippingMethod == "gls-parcelshop" || normalizedShippingMethod == "glsparcelshop" || normalizedShippingMethod == "gls-pickup" || normalizedShippingMethod == "gls-box" || normalizedShippingMethod == "glsbox" || normalizedShippingMethod == "alzabox" || normalizedShippingMethod == "alza-box" || normalizedShippingMethod == "packeta-zbox" || normalizedShippingMethod == "packetazbox" || normalizedShippingMethod == "z-box" || normalizedShippingMethod == "zbox")
            {
                return $@"<p style='margin:0;color:#555;'>Spôsob: <code>{shippingLabelSafe}</code></p>
           <p style='margin:8px 0 0 0;color:#555;'>Adresa: <strong>{(string.IsNullOrWhiteSpace(pickupNameSafe) ? "Bude doplnená podľa výberu zákazníka" : pickupNameSafe)}</strong></p>
           <p style='margin:4px 0 0 0;color:#777;'>Kód miesta: <code>{(string.IsNullOrWhiteSpace(pickupCodeSafe) ? "neuvedené" : pickupCodeSafe)}</code></p>";
            }

            return $@"<p style='margin:0;color:#555;'>Spôsob: <code>{shippingLabelSafe}</code></p>
           <p style='margin:8px 0 0 0;color:#555;'>{(normalizedShippingMethod == "dpd-pickup" || normalizedShippingMethod == "dpdpickup" || normalizedShippingMethod == "dpd-point" || normalizedShippingMethod == "dpdpoint" ? "DPD odberné miesto" : "Výdajné miesto")}: <strong>{(string.IsNullOrWhiteSpace(pickupNameSafe) ? "Bude doplnené podľa výberu zákazníka" : pickupNameSafe)}</strong></p>
           <p style='margin:4px 0 0 0;color:#777;'>Kód: <code>{(string.IsNullOrWhiteSpace(pickupCodeSafe) ? "neuvedené" : pickupCodeSafe)}</code></p>";
        }

        public bool TrySendBankTransferOrderEmail(PlaceOrderModel model, decimal shippingFee, decimal grandTotal, string orderNumber = null)
        {
            if (model == null) return false;
            return TrySendBankTransferOrderEmail(
                model.Email,
                model.Name,
                model.CartJson,
                model.ShippingMethod,
                model.ZasielkovnaPickupPoint,
                model.ZasielkovnaPickupPointName,
                model.ShippingAddressLine1,
                model.ShippingAddressLine2,
                model.ShippingCity,
                model.ShippingZip,
                model.ShippingCountry,
                shippingFee,
                grandTotal,
                orderNumber);
        }

        public void SendBankTransferOrderEmail(string customerEmail, string customerName, string cartJson, string shippingMethod, string pickupPointCode, string pickupPointName, decimal shippingFee, decimal grandTotal, string orderNumber = null)
        {
            // Backwards-compatible wrapper
            TrySendBankTransferOrderEmail(customerEmail, customerName, cartJson, shippingMethod, pickupPointCode, pickupPointName, null, null, null, null, null, shippingFee, grandTotal, orderNumber);
        }

        private bool TrySendBankTransferOrderEmail(
            string customerEmail,
            string customerName,
            string cartJson,
            string shippingMethod,
            string pickupPointCode,
            string pickupPointName,
            string shippingAddressLine1,
            string shippingAddressLine2,
            string shippingCity,
            string shippingZip,
            string shippingCountry,
            decimal shippingFee,
            decimal grandTotal,
            string orderNumber = null)
        {
            var companyEmail = (System.Configuration.ConfigurationManager.AppSettings["OrderEmail:Company"] ?? "info@ladowebservis.sk").Trim();
            try { if (!string.IsNullOrWhiteSpace(companyEmail)) { var _ = new MailAddress(companyEmail); } } catch { companyEmail = "info@ladowebservis.sk"; }

            var hasValidCustomer = false;
            try { var _ = new MailAddress(customerEmail); hasValidCustomer = true; } catch { hasValidCustomer = false; }
            var hasValidCompany = false;
            try { var _ = new MailAddress(companyEmail); hasValidCompany = true; } catch { hasValidCompany = false; }
            if (!hasValidCustomer && !hasValidCompany) return false;

            try
            {

                var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
                var orderNumberHtml = BuildOrderNumberHtml(orderNumber);

                var cartLines = TryBuildCartLines(cartJson) ?? new List<string>();
                var cartItemsHtml = cartLines.Count == 0
                    ? "<tr><td colspan='2' style='padding:10px 0;color:#666;'>Košík je prázdny.</td></tr>"
                    : string.Join("", cartLines.Select(line => $"<tr><td style='padding:8px 0;'>{HttpUtility.HtmlEncode(line)}</td><td style='padding:8px 0; text-align:right;'></td></tr>"));

                var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);
                var productsUrl = "https://ladowebservis.sk/Home/Produkty";

                // Bank transfer details (configurable)
                var iban = (System.Configuration.ConfigurationManager.AppSettings["BankTransfer:IBAN"] ?? "").Trim();
                var bic = (System.Configuration.ConfigurationManager.AppSettings["BankTransfer:BIC"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(iban)) iban = "SK0211110000001291179008";
                if (string.IsNullOrWhiteSpace(bic)) bic = "UNCRSKBX";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                    if (hasValidCustomer)
                    {
                        mail.To.Add(customerEmail);
                    }
                    else if (hasValidCompany)
                    {
                        // If customer email is invalid, still notify the company so the order isn't lost.
                        mail.To.Add(companyEmail);
                        if (!string.IsNullOrWhiteSpace(customerEmail))
                        {
                            mail.ReplyToList.Add(new MailAddress(companyEmail));
                        }
                    }

                    if (hasValidCompany && hasValidCustomer && !string.Equals(companyEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        mail.Bcc.Add(companyEmail);
                    }

                    mail.Subject = string.IsNullOrWhiteSpace(orderNumber)
                        ? "Potvrdenie objednávky + údaje k platbe (bankový prevod)"
                        : "Potvrdenie objednávky " + orderNumber + " + údaje k platbe (bankový prevod)";
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.BodyEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = true;

                    var shippingDetailsHtml = BuildShippingDetailsHtml(
                        shippingMethod,
                        pickupPointCode,
                        pickupPointName,
                        shippingAddressLine1,
                        shippingAddressLine2,
                        shippingCity,
                        shippingZip,
                        shippingCountry);

                    mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#17a2b8 0%,#138496 100%); color:#fff; padding: 26px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 20px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#138496; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    code {{ background: #fff; border: 1px solid #eee; padding: 3px 8px; border-radius: 8px; font-weight: 800; }}
    table {{ width:100%; border-collapse: collapse; }}
    td {{ border-bottom:1px dashed #eaeaea; font-size:14px; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; color:#777; font-size:12px; line-height:1.6; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
        <h1>🏦 Bankový prevod – údaje k platbe</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň p. {nameSafe},</p>
        <p>Ďakujeme, Vaša objednávka bola prijatá. Pre dokončenie objednávky prosím vykonajte bankový prevod podľa údajov nižšie.Ako variabilný symbol použite číslo objednávky<br /> {orderNumber}.</p>
        {orderNumberHtml}

        <div class='box'>
          <div class='title'>💳 Platobné údaje</div>
          <p style='margin:0;color:#555;'>IBAN: <code>{HttpUtility.HtmlEncode(iban)}</code></p>
          <p style='margin:8px 0 0 0;color:#555;'>BIC: <code>{HttpUtility.HtmlEncode(bic)}</code></p>
          <p style='margin:8px 0 0 0;color:#555;'>Suma na úhradu: <strong>€{grandTotal:0.00}</strong></p>
        </div>

        <div class='box'>
          <div class='title'>✅ Ďalšie kroky</div>
          <p style='margin:0;color:#555;'><strong>1.</strong> Skontrolujte, či sedí súhrn objednávky a doprava.</p>
          <p style='margin:8px 0 0 0;color:#555;'><strong>2.</strong> Vykonajte bankový prevod podľa údajov vyššie (alebo použite rýchly platobný link Stripe,nižšie ak je dostupný).</p>
          <p style='margin:8px 0 0 0;color:#555;'><strong>3.</strong> Po prijatí platby objednávku spracujeme a odošleme.</p>
          <p style='margin:8px 0 0 0;color:#777;'>Ak vám tento e‑mail prišiel omylom alebo máte otázky, odpíšte nám na <strong>info@ladowebservis.sk</strong>.</p>
        </div>

        <div class='box'>
          <div class='title'>🚚 Doprava</div>
          {shippingDetailsHtml}
          <p style='margin:8px 0 0 0;color:#555;'>Doprava: <strong>€{shippingFee:0.00}</strong></p>
        </div>

        <div class='box'>
          <div class='title'>🛒 Položky</div>
          <table role='presentation'>
            <tbody>
              {cartItemsHtml}
            </tbody>
          </table>
          <p style='margin:10px 0 0 0;font-weight:900;font-size:18px;'>Spolu s dopravou a DPH: €{grandTotal:0.00}</p>
        </div>
 

        
      <a class='btn btn-primary' href='{productsUrl}'>📦 Pozrieť aj ďalšie produkty</a>
        <div class='box'>
          <div class='title'>⚡ Zaplatiť môžte aj bezpečne cez Stripe+treba počítať s poplatkami za dopravu</div>
          <p style='margin:0;color:#555;'>Ak si chcete vybrať z našich produktov a zaplatiť ich, použite tento bezpečný link cez Stripe.</p>
          <p style='margin:10px 0 0 0;'><a href='https://buy.stripe.com/bJebJ1as6gng0SM8Sq4wM04?locale=sk' class='btn-stripe' target='_blank' style='display:inline-block;background:#28a745;color:#fff;text-decoration:none;font-weight:900;padding:12px 16px;border-radius:10px;'>Zaplaťte cez Stripe</a></p>
        </div>



        <div class='box'>
          <div class='title'>✨ Chcete členské výhody,vytvorte si účet.</div>
          <p style='margin:0;color:#555;'>Po registrácii získate výhody a zľavové kódy – plus rýchlejší nákup.</p>
          <a class='btn btn-primary' href='https://www.ladowebservis.sk/registracia-uzivatela'>📝 Registrácia nového zákazníka</a>
        </div>

        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 💚</p>
      </div>
      <div class='footer'>
        <div class='muted'>
          Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
          Stále budete dostávať informácie o svojich objednávkach.
      </div>
    </div>
  </div>
</body>
</html>";

                    return TrySendWithConfiguredSmtp(mail);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"TrySendBankTransferOrderEmail failed: {ex}");
                return false;
            }
        }

        public void SendStripePaymentLinkEmail(
            string customerEmail,
            string customerName,
            string stripeSessionUrl,
            string cartJson,
            string shippingMethod,
            string pickupPointCode,
            string pickupPointName,
            string shippingAddressLine1,
            string shippingAddressLine2,
            string shippingCity,
            string shippingZip,
            string shippingCountry,
            decimal shippingFee,
            decimal grandTotal,
            string orderNumber = null)
        {
            if (string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(stripeSessionUrl)) return;
            try { var _ = new MailAddress(customerEmail); } catch { return; }

            try
            {

                var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
                var promoCode = "LETOJETU26";
                var cartUrl = "https://ladowebservis.sk/Home/Kosik";
                var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);
                var orderNumberHtml = BuildOrderNumberHtml(orderNumber);

                var companyEmail = (System.Configuration.ConfigurationManager.AppSettings["OrderEmail:Company"] ?? "info@ladowebservis.sk").Trim();
                try { if (!string.IsNullOrWhiteSpace(companyEmail)) { var _ = new MailAddress(companyEmail); } } catch { companyEmail = "info@ladowebservis.sk"; }

                var cartLines = TryBuildCartLines(cartJson) ?? new List<string>();
                string cartHtml;
                if (cartLines.Count == 0)
                {
                    cartHtml = "<p style='margin:0;color:#666;'>🛒 V košíku momentálne nemáte žiadne položky.</p>";
                }
                else
                {
                    var cartItemsHtml = string.Join("<br/>", cartLines.Select(HttpUtility.HtmlEncode));
                    cartHtml = "<p style='margin:0 0 10px 0;color:#333;font-weight:800;'>🛒 Máte v košíku:</p>" +
                               "<div style='background:#fff;border:1px solid #e0e0e0;border-radius:10px;padding:12px;'" +
                               "<div style='color:#333;font-size:14px;line-height:1.7;'>" + cartItemsHtml + "</div>" +
                               "</div>";
                }

                var shippingDetailsHtml = BuildShippingDetailsHtml(
                    shippingMethod,
                    pickupPointCode,
                    pickupPointName,
                    shippingAddressLine1,
                    shippingAddressLine2,
                    shippingCity,
                    shippingZip,
                    shippingCountry);

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                    mail.To.Add(customerEmail);
                    if (!string.IsNullOrWhiteSpace(companyEmail) && !string.Equals(companyEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        mail.Bcc.Add(companyEmail);
                    }
                    mail.Subject = string.IsNullOrWhiteSpace(orderNumber)
                        ? "💳 Platba objednávky – kliknite na tlačidlo a zaplaťte cez Stripe"
                        : "💳 Platba objednávky " + orderNumber + " – kliknite na tlačidlo a zaplaťte cez Stripe";
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.BodyEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = true;

                    mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#5d33fb 0%,#4a2fb5 100%); color:#fff; padding: 24px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 20px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#5d33fb; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    .btn {{ display:inline-block; text-decoration:none; font-weight: 900; padding: 12px 16px; border-radius: 10px; margin-top: 10px; }}
    .btn-pay {{ background: #28a745; color:#fff !important; }}
    .btn-cart {{ background: #17a2b8; color:#fff !important; margin-left:8px; }}
    code {{ background: #fff; border: 1px solid #eee; padding: 3px 8px; border-radius: 8px; font-weight: 800; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; color:#777; font-size:12px; line-height:1.6; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
        <h1>💳 Dokončite platbu objednávky</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň {nameSafe},</p>
        <p>Pre zaplatenie Vašej objednávky kliknite na tlačidlo nižšie. Platba prebehne bezpečne cez Stripe.</p>
        <div class='box'>
          <div class='title'>🧾 Súhrn</div>
          <p style='margin:0;color:#555;'>Doprava: <strong>€{shippingFee:0.00}</strong></p>
          <p style='margin:8px 0 0 0;font-weight:900;font-size:18px;'>Spolu na úhradu: €{grandTotal:0.00}</p>
          <a class='btn btn-pay' href='{HttpUtility.HtmlAttributeEncode(stripeSessionUrl)}'>✅ Zaplatiť cez Stripe</a>
          <a class='btn btn-cart' href='{cartUrl}'>🛒 Košík</a>
        </div>

        <div class='box'>
          <div class='title'>✅ Ďalšie kroky</div>
          <p style='margin:0;color:#555;'><strong>1.</strong> Kliknite na tlačidlo <strong>„Zaplatiť cez Stripe“</strong> a dokončite platbu.</p>
          <p style='margin:8px 0 0 0;color:#555;'><strong>2.</strong> Po prijatí platby vám pošleme potvrdenie a objednávku spracujeme.</p>
          <p style='margin:8px 0 0 0;color:#777;'>Ak máte otázky, odpíšte na tento e‑mail alebo nás kontaktujte na <strong>info@ladowebservis.sk</strong>.</p>
        </div>

        <div class='box'>
          <div class='title'>🚚 Doprava</div>
          {shippingDetailsHtml}
        </div>

        <div class='box'>
          <div class='title'>🛒 Košík</div>
          {cartHtml}
        </div>

        <div class='box'>
          <div class='title'>🐣 Akcia</div>
          <p style='margin:0;color:#555;'>Pri platbe použite kód <code>{promoCode}</code> a získate 10% zľavu na vybrané produkty.</p>
        </div>

        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 💚</p>
      </div>
      <div class='footer'>
        Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
        Stále budete dostávať informácie o svojich objednávkach.
      </div>
    </div>
  </div>
</body>
</html>";

                    TrySendWithConfiguredSmtp(mail);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"SendStripePaymentLinkEmail failed: {ex}");
            }
        }

        public void SendOrderPaidEmail(Session session, string customerEmail, string customerName, string shippingMethod, string pickupPointCode, string pickupPointName, string shippingAddressLine1, string shippingAddressLine2, string shippingCity, string shippingZip, string shippingCountry, decimal shippingFee, decimal grandTotal)
        {
            if (string.IsNullOrWhiteSpace(customerEmail) || session == null) return;
            try { var _ = new MailAddress(customerEmail); } catch { return; }

            var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
            var orderNumber = GetOrderNumber(session);
            var orderNumberHtml = BuildOrderNumberHtml(orderNumber);
            var shippingDetailsHtml = BuildShippingDetailsHtml(
                shippingMethod,
                pickupPointCode,
                pickupPointName,
                shippingAddressLine1,
                shippingAddressLine2,
                shippingCity,
                shippingZip,
                shippingCountry);

            var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);
            var productsUrl = "https://ladowebservis.sk/Home/Produkty";
            var cartUrl = "https://ladowebservis.sk/Home/Kosik";

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.To.Add(customerEmail);
                mail.Bcc.Add("info@ladowebservis.sk");
                mail.Subject = string.IsNullOrWhiteSpace(orderNumber)
                    ? "✅ Objednávka bola odoslaná (platba prijatá)"
                    : "✅ Objednávka " + orderNumber + " bola odoslaná (platba prijatá)";
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;
                mail.IsBodyHtml = true;

                mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#28a745 0%,#20c997 100%); color:#fff; padding: 26px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 22px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#28a745; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    code {{ background: #fff; border: 1px solid #eee; padding: 3px 8px; border-radius: 8px; font-weight: 800; }}
    .btn {{ display:inline-block; text-decoration:none; font-weight: 800; padding: 12px 16px; border-radius: 10px; margin-right: 10px; margin-top: 10px; }}
    .btn-primary {{ background: #5d33fb; color:#fff !important; }}
    .btn-secondary {{ background: #17a2b8; color:#fff !important; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; color:#777; font-size:12px; line-height:1.6; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
        <h1>✅ Platba prijatá – objednávka odoslaná</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň {nameSafe},</p>
        <p>Ďakujeme – platba bola prijatá a objednávka bola odoslaná na spracovanie.</p>
        {orderNumberHtml}

        <div class='box'>
          <div class='title'>🧾 Platba</div>
          <p style='margin:0; color:#555;'>Stripe session: <code>{HttpUtility.HtmlEncode(session.Id)}</code></p>
          <p style='margin:8px 0 0 0; color:#555;'>Doprava: <strong>€{shippingFee:0.00}</strong></p>
          <p style='margin:8px 0 0 0; font-weight:900; font-size:18px;'>Spolu: €{grandTotal:0.00}</p>
        </div>

        <div class='box'>
          <div class='title'>🚚 Doprava</div>
          {shippingDetailsHtml}
        </div>

        <a class='btn btn-secondary' href='{productsUrl}'>🛍️ Produkty</a>
        <a class='btn btn-primary' href='{cartUrl}'>🛒 Košík</a>

        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 💚</p>
      </div>
      <div class='footer'>
        Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
        Stále budete dostávať informácie o svojich objednávkach.
      </div>
    </div>
  </div>
</body>
</html>";

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                    client.Send(mail);
                }
            }
        }

        // LadowebservisMVC\Util\Mailer.cs

        public void SendOrderConfirmationEmail(string customerEmail, string customerName, string cartJson, string shippingMethod, string pickupPointCode, string pickupPointName, string shippingAddressLine1, string shippingAddressLine2, string shippingCity, string shippingZip, string shippingCountry, decimal shippingFee, decimal grandTotal)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return;
            try { var _ = new MailAddress(customerEmail); } catch { return; }

            var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();

            var cartLines = TryBuildCartLines(cartJson) ?? new List<string>();
            var cartItemsHtml = cartLines.Count == 0
                ? "<tr><td colspan='2' style='padding:10px 0;color:#666;'>Košík je prázdny.</td></tr>"
                : string.Join("", cartLines.Select(line => $"<tr><td style='padding:8px 0;'>{HttpUtility.HtmlEncode(line)}</td><td style='padding:8px 0; text-align:right;'></td></tr>"));

            var shippingDetailsHtml = BuildShippingDetailsHtml(
                shippingMethod,
                pickupPointCode,
                pickupPointName,
                shippingAddressLine1,
                shippingAddressLine2,
                shippingCity,
                shippingZip,
                shippingCountry);
            var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.To.Add(customerEmail);
                mail.Bcc.Add("info@ladowebservis.sk");
                mail.Subject = "✅ Potvrdenie objednávky – doprava";
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;
                mail.IsBodyHtml = true;

                mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#5d33fb 0%,#4a2fb5 100%); color:#fff; padding: 26px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 20px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#5d33fb; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    code {{ background: #fff; border: 1px solid #eee; padding: 3px 8px; border-radius: 8px; font-weight: 800; }}
    table {{ width:100%; border-collapse: collapse; }}
    th {{ text-align:left; padding:8px 0; border-bottom:1px solid #e0e0e0; color:#555; font-size:13px; }}
    td {{ border-bottom:1px dashed #eaeaea; font-size:14px; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; color:#777; font-size:12px; line-height:1.6; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
        <h1>✅ Objednávka prijatá</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň {nameSafe},</p>
        <p>Ďakujeme, Vaša objednávka bola prijatá. Nižšie je zhrnutie zvolenej dopravy.</p>

        <div class='box'>
          <div class='title'>69A Doprava</div>
          {shippingDetailsHtml}
        </div>

        <div class='box'>
          <div class='title'>4E6 Položky</div>
          <table role='presentation'>
            <tbody>
              {cartItemsHtml}
            </tbody>
          </table>
        </div>

        <div class='box'>
          <div class='title'>4B0 Súhrn</div>
          <p style='margin:0;color:#555;'>Doprava: <strong>€{shippingFee:0.00}</strong></p>
          <p style='margin:8px 0 0 0;font-weight:900;font-size:18px;'>Spolu: €{grandTotal:0.00}</p>
        </div>

        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 49A</p>
      </div>
      <div class='footer'>
        Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
        Stále budete dostávať informácie o svojich objednávkach.
      </div>
    </div>
  </div>
</body>
</html>";

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                    client.Send(mail);
                }
            }
        }

        public void SendPaymentConfirmationEmail(Session session, string customerEmail, string customerName = null)
        {
            if (string.IsNullOrWhiteSpace(customerEmail) || session == null) return;
            try { var _ = new MailAddress(customerEmail); } catch { return; }

            var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
            var orderNumber = GetOrderNumber(session);
            var orderNumberHtml = BuildOrderNumberHtml(orderNumber);

            // Build line items list (if expanded)
            var itemsHtml = "";
            try
            {
                if (session.LineItems != null && session.LineItems.Data != null && session.LineItems.Data.Count > 0)
                {
                    var lines = session.LineItems.Data.Select(li =>
                    {
                        var desc = HttpUtility.HtmlEncode(li.Description ?? li.Price?.Product?.Name ?? "Produkt");
                        var qty = li.Quantity ?? 1;
                        var amount = ((decimal)li.AmountTotal) / 100m;
                        return $"<tr><td style='padding:8px 0;'>{desc}</td><td style='padding:8px 0; text-align:center;'>{qty}</td><td style='padding:8px 0; text-align:right;'>€{amount:0.00}</td></tr>";
                    });
                    itemsHtml = string.Join("", lines);
                }
            }
            catch { }

            var totalEur = ((decimal)session.AmountTotal) / 100m;
            var productsUrl = "https://ladowebservis.sk/Home/Produkty";
            var cartUrl = "https://ladowebservis.sk/Home/Kosik";
            var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);
            var shippingMethod = session.Metadata != null && session.Metadata.ContainsKey("shippingMethod") ? session.Metadata["shippingMethod"] : string.Empty;
            var pickupPointCode = session.Metadata != null && session.Metadata.ContainsKey("pickupPoint") ? session.Metadata["pickupPoint"] : string.Empty;
            var pickupPointName = session.Metadata != null && session.Metadata.ContainsKey("pickupPointName") ? session.Metadata["pickupPointName"] : string.Empty;
            var shippingAddressLine1 = session.Metadata != null && session.Metadata.ContainsKey("shippingAddressLine1") ? session.Metadata["shippingAddressLine1"] : string.Empty;
            var shippingAddressLine2 = session.Metadata != null && session.Metadata.ContainsKey("shippingAddressLine2") ? session.Metadata["shippingAddressLine2"] : string.Empty;
            var shippingCity = session.Metadata != null && session.Metadata.ContainsKey("shippingCity") ? session.Metadata["shippingCity"] : string.Empty;
            var shippingZip = session.Metadata != null && session.Metadata.ContainsKey("shippingZip") ? session.Metadata["shippingZip"] : string.Empty;
            var shippingCountry = session.Metadata != null && session.Metadata.ContainsKey("shippingCountry") ? session.Metadata["shippingCountry"] : string.Empty;
            var shippingFeeRaw = session.Metadata != null && session.Metadata.ContainsKey("shippingFee") ? session.Metadata["shippingFee"] : string.Empty;
            var grandTotalRaw = session.Metadata != null && session.Metadata.ContainsKey("grandTotal") ? session.Metadata["grandTotal"] : string.Empty;
            decimal.TryParse((shippingFeeRaw ?? "0").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var shippingFee);
            if (!decimal.TryParse((grandTotalRaw ?? totalEur.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var grandTotal)) grandTotal = totalEur;
            var shippingDetailsHtml = BuildShippingDetailsHtml(shippingMethod, pickupPointCode, pickupPointName, shippingAddressLine1, shippingAddressLine2, shippingCity, shippingZip, shippingCountry);

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.To.Add(customerEmail);
                mail.Bcc.Add("info@ladowebservis.sk");
                mail.Subject = string.IsNullOrWhiteSpace(orderNumber)
                    ? "✅ Potvrdenie platby – ďakujeme za objednávku"
                    : "✅ Potvrdenie platby pre objednávku " + orderNumber;
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;
                mail.IsBodyHtml = true;

                mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#28a745 0%,#20c997 100%); color:#fff; padding: 26px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 22px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#28a745; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    .btn {{ display:inline-block; text-decoration:none; font-weight: 800; padding: 12px 16px; border-radius: 10px; margin-right: 10px; margin-top: 10px; }}
    .btn-primary {{ background: #5d33fb; color:#fff !important; }}
    .btn-secondary {{ background: #17a2b8; color:#fff !important; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; color:#777; font-size:12px; line-height:1.6; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
        <h1>✅ Platba úspešná – ďakujeme!</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň {nameSafe},</p>
        <p>
          Vaša platba bola úspešne prijatá. 
          <strong>Ďakujeme za objednávku</strong> – pripravujeme ju na spracovanie.
        </p>
        {orderNumberHtml}

        <div class='box'>
          <div class='title'>🧾 Súhrn platby</div>
          <p style='margin:0; color:#555;'>ID platby: <code>{HttpUtility.HtmlEncode(session.Id)}</code></p>
          <p style='margin:8px 0 0 0; color:#555;'>Doprava: <strong>€{shippingFee:0.00}</strong></p>
          <p style='margin:8px 0 0 0; font-weight:900; font-size:18px;'>Spolu: €{grandTotal:0.00}</p>
        </div>

        <div class='box'>
          <div class='title'>🚚 Doprava</div>
          {shippingDetailsHtml}
        </div>

        <a class='btn btn-secondary' href='{productsUrl}'>🛍️ Produkty</a>
        <a class='btn btn-primary' href='{cartUrl}'>🛒 Košík</a>

        
      <a class='btn btn-primary' href='{productsUrl}'>📦 Pozrieť aj ďalšie produkty</a>
      

        <div class='box'>
          <div class='title'>💄 Oriflame – kozmetika a katalóg</div>
          <p style='margin:0;color:#555;'>Parfumy, krémy a starostlivosť o pleť. Ak chcete poradiť, odpíšte na tento email.</p>
          <a class='btn btn-secondary' href='https://sk.oriflame.com/products/digital-catalogue-current?store=SK-vladimirksenic'>📖 Otvoriť katalóg</a>
          <a class='btn btn-primary' href='mailto:info@ladowebservis.sk'>✉️ Napísať zoznam produktov</a>
        </div>

        <div class='box'>
          <div class='title'>💻 IT služby (rýchla pomoc)</div>
          <ul>
            <li>🔧 Opravy a údržba PC / notebookov</li>
            <li>⚡ Zrýchlenie a optimalizácia počítača</li>
            <li>🌐 Web / e‑shop – úpravy a správa</li>
            <li>🧩 Inštalácia softvéru, odstránenie vírusov</li>
          </ul>
          <p style='margin:10px 0 0 0;color:#555;'><strong>Kontakt:</strong> <a href='mailto:podpora@ladowebservis.sk'>podpora@ladowebservis.sk</a> alebo +421917952432</p>
        </div>

        

        <div class='box'>
          <div class='title'>✨ Chcete členské výhody?</div>
          <p style='margin:0;color:#555;'>Po registrácii získate výhody a zľavové kódy – plus rýchlejší nákup.</p>
          <a class='btn btn-primary' href='https://ladowebservis.sk/Home/OdoslanieReg'>📝 Registrácia nového zákazníka</a>
        </div>

        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 💚</p>
      </div>
      <div class='footer'>
        <div class='muted'>
          Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
          Stále budete dostávať informácie o svojich objednávkach.
      </div>
    </div>
  </div>
</body>
</html>";

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                    client.Send(mail);
                }
            }
        }

        /// <summary>
        /// Contact form email (no e-book attachment). Sends the user's message to site inbox.
        /// </summary>
        public void OdoslanieKontaktSpravy(ContactModel_Sk model)
        {
            if (model == null) return;

            var email = (model.Email ?? string.Empty).ToLowerInvariant();
            var name = (model.Name ?? string.Empty);

            if (IsEmailBlocked(email, name))
            {
                return;
            }

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.Headers["X-Mailer"] = "https://ladowebservis.sk/kontakt";
                mail.Subject = "Kontaktný formulár – nová správa";
                mail.IsBodyHtml = false;
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;

                // Send to site inbox
                mail.To.Add("info@ladowebservis.sk");

                // Set Reply-To to the customer's email when valid
                if (!string.IsNullOrWhiteSpace(model.Email))
                {
                    try
                    {
                        var _ = new MailAddress(model.Email);
                        mail.ReplyToList.Add(new MailAddress(model.Email));
                    }
                    catch
                    {
                        // ignore invalid reply-to
                    }
                }

                mail.Body = string.Format(
                    "Nová správa z kontaktného formulára:\r\n\r\nMeno: {0}\r\nEmail: {1}\r\n\r\nSpráva:\r\n{2}\r\n",
                    model.Name,
                    model.Email,
                    model.Text);

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                    client.Send(mail);
                }
            }

            // Send a separate, nice Easter promo email to the customer (if they provided an email)
            try
            {
                if (!string.IsNullOrWhiteSpace(model?.Email))
                {
                    SendEasterPromoEmail(model.Email, model.Name, model.CartJson);
                }
            }
            catch
            {
                // ignore promo email errors
            }
        }

        private sealed class CartItem
        {
            public int Quantity { get; set; }
            public string Image { get; set; }
        }

        private static List<string> TryBuildCartLines(string cartJson)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(cartJson)) return result;

            try
            {
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, CartItem>>(cartJson);
                if (dict == null || dict.Count == 0) return result;

                foreach (var kv in dict)
                {
                    var key = kv.Key;
                    var qty = kv.Value != null ? kv.Value.Quantity : 0;
                    if (qty <= 0) continue;

                    // Try to map localStorage key to product name
                    string displayName = key;
                    try
                    {
                        if (ProductCatalog.TryGetById(key, out var p) && p != null)
                        {
                            displayName = p.Name;
                        }
                        else if (ProductCatalog.TryGetByName(key, out var p2) && p2 != null)
                        {
                            displayName = p2.Name;
                        }
                    }
                    catch { }

                    result.Add($"• {displayName} × {qty}");
                }
            }
            catch
            {
                return result;
            }

            return result;
        }

        private void SendEasterPromoEmail(string customerEmail, string customerName, string cartJson)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return;

            // Validate email
            try { var _ = new MailAddress(customerEmail); } catch { return; }

            var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
            var promoCode = "LETOJETU26";
            var memberCode = "REGZAK26";

            var cartLines = TryBuildCartLines(cartJson) ?? new List<string>();
            string cartHtml;
            if (cartLines.Count == 0)
            {
                cartHtml = "<p style='margin:0;color:#666;'>🛒 V košíku momentálne nemáte žiadne položky.</p>";
            }
            else
            {
                var cartItemsHtml = string.Join("<br/>", cartLines.Select(HttpUtility.HtmlEncode));
                cartHtml = "<p style='margin:0 0 10px 0;color:#333;font-weight:800;'>🛒 Máte v košíku:</p>" +
                           "<div style='background:#fff;border:1px solid #e0e0e0;border-radius:10px;padding:12px;'>" +
                           "<div style='color:#333;font-size:14px;line-height:1.7;'>" + cartItemsHtml + "</div>" +
                           "</div>";
            }

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.To.Add(customerEmail);
                mail.Bcc.Add("info@ladowebservis.sk");
                mail.Subject = "💚Hurá leto je tu! Letná ponuka pre zdravie a energiu (kódy " + promoCode + "/" + memberCode + ")";
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;
                mail.IsBodyHtml = true;

                var productsUrl = "https://ladowebservis.sk/Home/Produkty";
                var cartUrl = "https://ladowebservis.sk/Home/Kosik";
                var registerUrl = "https://ladowebservis.sk/Home/Registracia";
                var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);

                mail.Body = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <style>
    body {{ font-family: Arial, sans-serif; background:#f5f7fa; color:#333; margin:0; padding:0; }}
    .wrap {{ padding: 18px; }}
    .container {{ max-width: 650px; margin: 0 auto; background:#fff; border-radius: 12px; overflow:hidden; box-shadow: 0 10px 40px rgba(0,0,0,.10); }}
    .header {{ background: linear-gradient(135deg,#5d33fb 0%,#4a2fb5 100%); color:#fff; padding: 26px 20px; text-align:center; }}
    .header h1 {{ margin:0; font-size: 22px; line-height:1.4; }}
    .content {{ padding: 22px 20px; }}
    .pill {{ display:inline-block; padding:10px 14px; border-radius: 999px; font-weight: 800; background: linear-gradient(135deg,#ffeb3b 0%,#ffc107 100%); color:#2c3e50; }}
    .box {{ background:#f8f9fa; border: 1px solid #e6e6e6; border-radius: 12px; padding: 16px; margin: 16px 0; }}
    .title {{ color:#5d33fb; font-weight: 900; font-size: 16px; margin: 0 0 10px 0; }}
    .btn {{ display:inline-block; text-decoration:none; font-weight: 800; padding: 12px 16px; border-radius: 10px; margin-right: 10px; margin-top: 10px; }}
    .btn-primary {{ background: #5d33fb; color:#fff !important; }}
    .btn-secondary {{ background: #17a2b8; color:#fff !important; }}
    .muted {{ color:#777; font-size: 12px; line-height:1.6; }}
    ul {{ margin: 10px 0 0 18px; padding:0; }}
    li {{ margin: 6px 0; }}
    code {{ background: #fff; border: 1px solid #eee; padding: 3px 8px; border-radius: 8px; font-weight: 800; }}
    .footer {{ padding: 16px 20px; background:#fafafa; border-top:1px solid #eee; }}

    /* Email-friendly product grid */
    .prod-table {{ width:100%; border-collapse:collapse; margin-top: 10px; }}
    .prod-td {{ width:50%; vertical-align:top; padding:10px; }}
    .prod-card {{ background:#fff; border:1px solid #e6e6e6; border-radius:12px; padding:12px; }}
    .prod-row {{ display:block; }}
    .prod-img {{ width:72px; height:auto; border-radius:10px; display:block; }}
    .prod-name {{ font-weight:900; color:#333; margin:8px 0 2px 0; font-size:14px; }}
    .prod-note {{ margin:0; color:#666; font-size:12px; line-height:1.5; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='container'>
      <div class='header'>
<h1>💚 Letná ponuka pre zdravie a energiu</h1>
      </div>
      <div class='content'>
        <p>Dobrý deň {nameSafe},</p>

        <p>Leto je ideálny čas dopriať si viac energie, vitality a pohody. Či už sa chystáte na dovolenku, víkendové výlety alebo si chcete naplno užiť každý bežný deň, správna starostlivosť o telo vám môže pomôcť cítiť sa lepšie a mať viac síl.</p>

        <p>Pripravili sme pre vás letný výber obľúbených produktov, ktoré podporujú imunitu, regeneráciu, zdravú rovnováhu organizmu aj každodenný komfort. Nájdete u nás riešenia pre zdravie, krásu aj praktické služby, ktoré sa vám počas leta môžu hodiť.</p>

        <p>Ak si chcete urobiť radosť alebo vybrať darček pre blízkych, teraz je na to vhodná chvíľa. Pri objednávke môžete využiť aj letný zľavový kód a získať výhodnejší nákup.</p>

        <div style='text-align:center; margin: 14px 0 4px 0;'>
          <span class='pill'>👉 Navštívte náš e-shop a pripravte sa na leto plné energie a pohody.🎁 10% zľava – použite kód <code>{promoCode}</code></span>
        </div>
        <p style='text-align:center; margin: 10px 0 0 0; color:#555;'>
          Registrovaní členovia môžu použiť aj kód <code>{memberCode}</code> (6 mesiacov).
        </p>

        <div class='box'>
          <div class='title'>🌿 Tipy na top produkty a IT služby</div>
          <p style='margin:0 0 10px 0;color:#555;'>Vybrali sme pre vás produkty, ktoré sa hodia na leto a podporia zdravie, energiu aj celkovú pohodu.</p>

          <table class='prod-table' role='presentation'>
            <tr>
              <td class='prod-td'>
                <div class='prod-card'>
                  <img class='prod-img' src='https://ladowebservis.sk/Image/BalanceOil.png' alt='BalanceOil+' />
                  <div class='prod-name'>💊 BalanceOil+</div>
                  <p class='prod-note'>Omega‑3 + vitamín D3 pre srdce, mozog a imunitu.</p>
                </div>
              </td>
              <td class='prod-td'>
                <div class='prod-card'>
                  <img class='prod-img' src='https://ladowebservis.sk/Image/Zinobiotic2025.png' alt='Zinobiotic+' />
                  <div class='prod-name'>🦠 Zinobiotic+</div>
                  <p class='prod-note'>Podpora črevného mikrobiómu a rovnováhy.</p>
                </div>
              </td>
            </tr>
            <tr>
              <td class='prod-td'>
                <div class='prod-card'>
                  <img class='prod-img' src='https://ladowebservis.sk/Image/ZinzinoXtend.png' alt='ZinzinoXtend' />
                  <div class='prod-name'>💪 ZinzinoXtend</div>
                  <p class='prod-note'>23 vitamínov a minerálov pre energiu a imunitu.</p>
                </div>
              </td>
              <td class='prod-td'>
                <div class='prod-card'>
                  <img class='prod-img' src='https://ladowebservis.sk/Image/CollagenBoozt.png' alt='CollagenBoozt' />
                  <div class='prod-name'>✨ CollagenBoozt</div>
                  <p class='prod-note'>Kolagén pre pleť, vlasy a kĺby (10‑dňová rutina).</p>
                </div>
              </td>
            </tr>
          </table>

          <a class='btn btn-primary' href='{productsUrl}'>📦 Pozrieť produkty</a>
        </div>

        <div class='box'>
          <div class='title'>⚡ Stripe Rýchla platba</div>
          <p style='margin:0;color:#555;'>Ak si chcete vybrať z našich produktov a zaplatiť ich, použite jednoducho tento bezpečný link cez Stripe.</p>
          <p style='margin:10px 0 0 0;'><a href='https://buy.stripe.com/bJebJ1as6gng0SM8Sq4wM04?locale=sk' class='btn-stripe' target='_blank' style='display:inline-block;background:#28a745;color:#fff;text-decoration:none;font-weight:900;padding:12px 16px;border-radius:10px;'>Zaplaťte cez Stripe</a></p>
        </div>

        <div class='box'>
          <div class='title'>💄 Oriflame – kozmetika a katalóg</div>
          <p style='margin:0;color:#555;'>Parfumy, krémy a starostlivosť o pleť. Ak chcete poradiť, odpíšte na tento email.</p>
          <a class='btn btn-secondary' href='https://sk.oriflame.com/products/digital-catalogue-current?store=SK-vladimirksenic'>📖 Otvoriť katalóg</a>
          <a class='btn btn-primary' href='mailto:info@ladowebservis.sk'>✉️ Napísať zoznam produktov</a>
        </div>

        <div class='box'>
          <div class='title'>💻 IT služby (rýchla pomoc)</div>
          <ul>
            <li>🔧 Opravy a údržba PC / notebookov</li>
            <li>⚡ Zrýchlenie a optimalizácia počítača</li>
            <li>🌐 Web / e‑shop – úpravy a správa</li>
            <li>🧩 Inštalácia softvéru, odstránenie vírusov</li>
          </ul>
          <p style='margin:10px 0 0 0;color:#555;'><strong>Kontakt:</strong> <a href='mailto:podpora@ladowebservis.sk'>podpora@ladowebservis.sk</a> alebo +421917952432</p>
        </div>

        <div class='box'>
          <div class='title'>🛒 Pripomienka košíka</div>
          {cartHtml}
          <a class='btn btn-secondary' href='{cartUrl}'>👀 Skontrolovať košík</a>
        </div>

        <div class='box'>
          <div class='title'>✨ Chcete členské výhody?</div>
          <p style='margin:0;color:#555;'>Po registrácii získate výhody a zľavové kódy – plus rýchlejší nákup.</p>
          <a class='btn btn-primary' href='{registerUrl}'>📝 Registrácia</a>
        </div>

        <p style='margin: 18px 0 0 0; color:#555;'>Ak potrebujete s výberom poradiť, stačí odpovedať na tento email. Radi vám odporučíme vhodné produkty alebo služby podľa vašich potrieb.</p>
        <p style='margin: 18px 0 0 0; font-weight: 800;'>S pozdravom,<br/>Tím ladowebservis.sk 💚</p>
      </div>
      <div class='footer'>
        <div class='muted'>
          Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{unsubscribeUrl}'>kliknite sem</a>.<br/>
          Stále budete dostávať informácie o svojich objednávkach.
        </div>
      </div>
    </div>
  </div>
</body>
</html>";

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                    client.Send(mail);
                }
            }
        }

        /// <summary>
        /// Check if email or name contains suspicious patterns
        /// </summary>
        private static bool ContainsSuspiciousPatterns(string email, string name)
        {
            var emailLower = (email ?? string.Empty).ToLowerInvariant();
            var nameLower = (name ?? string.Empty).ToLowerInvariant();

            // Check email and name for suspicious patterns
            return SuspiciousPatterns.Any(pattern =>
                (!string.IsNullOrEmpty(emailLower) && emailLower.IndexOf(pattern.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(nameLower) && nameLower.IndexOf(pattern.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>
        /// Check if email is in blocked list
        /// </summary>
        private static bool IsEmailBlocked(string email, string name)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            var emailLower = email.ToLowerInvariant();
            var nameLower = (name ?? string.Empty).ToLowerInvariant();

            // Check exact email match
            if (BannedEmails.Any(b => emailLower.Equals(b, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check banned domains
            if (BannedDomains.Any(d => emailLower.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check banned fragments in email or name
            if (BannedEmailFragments.Any(b =>
                (!string.IsNullOrEmpty(emailLower) && emailLower.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(nameLower) && nameLower.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)))
                return true;

            // Check suspicious patterns (Bitcoin, phone number spam, etc.)
            if (ContainsSuspiciousPatterns(emailLower, nameLower))
                return true;

            return false;
        }

        // Accepts optional attachment (HttpPostedFileBase) from MVC form file input
        public void OdoslanieSpravy(ContactModel_Sk model, HttpPostedFileBase attachment = null)
        {
            // Validate model
            if (model == null)
                return;

            var email = (model.Email ?? string.Empty).ToLowerInvariant();
            var name = (model.Name ?? string.Empty);

            // Check if email is blocked (comprehensive check)
            if (IsEmailBlocked(email, name))
            {
                // Silently ignore/send no mail for blocked senders
                return;
            }

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.Headers["X-Mailer"] = "https://ladowebservis.sk/zdravie";
                mail.Subject = "Ďakujeme! Nech sa páči E-book je v prílohe";
                mail.IsBodyHtml = false;
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;

                // determine filename to display in body
                string attachedFileName = string.Empty;
                if (attachment != null && attachment.ContentLength > 0)
                {
                    attachedFileName = attachment.FileName;
                }
                else if (model.File != null && model.File.ContentLength > 0)
                {
                    attachedFileName = model.File.FileName;
                }

                // Build body with correct placeholders (admin notification)
                mail.Body = string.Format(
                    "\r\n Ahoj,\r\n ďakujem, že si sa prihlásil medzi ľudí, ktorým záleží na zdraví a energii.\r\nTu je tvoj e-book zdarma:\r\n Vaše meno: {0} ,\r\n Váš email: {1} ,\r\n Potvrdenie hesla: {2}," +
                    "\r\n Priložený súbor:Päť_prirodzených_ciest_k_väčšej_energii od_Zinzina {4}" +
                    "\r\n Vaša správa: {5}," +
                    "\r\n Prajem ti, aby si v ňom našiel inšpiráciu pre zdravší a pokojnejší deň.\r\n Prečítaj si ho prosím a dozvieš sa viac ako dostať svoje telo späť do rovnováhy zdravia.\r\n\r\nS pozdravom,\nTím ladowebservis.sk" +
                    "\r\n\r\n---\r\n📧 SPRÁVA O EMAILOCH:\r\nAk si neželáte dostávať ďalšie ponuky a novinky, kliknite sem: {6}\r\n\r\nStále budete dostávať informácie o svojich objednávkach.",
                    model.Name,
                    model.Email,
                    model.Password,
                    model.File,
                    attachedFileName,
                    model.Text,
                    GetUnsubscribeUrl(model.Email));
                mail.BodyEncoding = Encoding.UTF8;
                if (!string.IsNullOrWhiteSpace(model.Email))
                {
                    try
                    {
                        var addr = new MailAddress(model.Email);
                        mail.To.Add(model.Email);
                    }
                    catch
                    {
                        // invalid email, do not attempt send to user
                        mail.To.Add("info@ladowebservis.sk");
                    }
                }
                else
                {
                    mail.To.Add("info@ladowebservis.sk");
                }

                mail.Bcc.Add("info@ladowebservis.sk");

                // Attach file from parameter if provided, otherwise from model.File if present
                if (attachment != null && attachment.ContentLength > 0)
                {
                    var mailAttachment = new Attachment(attachment.InputStream, attachment.FileName, attachment.ContentType);
                    mail.Attachments.Add(mailAttachment);
                }
                else if (model.File != null && model.File.ContentLength > 0)
                {
                    var mailAttachment = new Attachment(model.File.InputStream, model.File.FileName, model.File.ContentType);
                    mail.Attachments.Add(mailAttachment);
                }

                // Also attach a bundled PDF (App_Data/MailAttachment.pdf) if it exists
                try
                {
                    var context = HttpContext.Current;
                    if (context != null)
                    {
                        var pdfPath = context.Server.MapPath("~/App_Data/MailAttachment.pdf");
                        if (System.IO.File.Exists(pdfPath))
                        {
                            var pdfAttachment = new Attachment(pdfPath);
                            mail.Attachments.Add(pdfAttachment);
                        }
                    }
                }
                catch
                {
                    // ignore failures attaching the bundled PDF
                }

                using (var client = new SmtpClient("smtp.websupport.cz"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");


                    client.Send(mail);
                }
            }

            // Send follow-up email with registration invitation and benefits
            try
            {
                if (!string.IsNullOrWhiteSpace(model?.Email))
                {
                    SendContactFollowUpEmail(model.Email, model.Name);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Send a follow-up email after contact form submission with registration invitation,
        /// promo code, cart reminder, and membership benefits
        /// </summary>
        private void SendContactFollowUpEmail(string customerEmail, string customerName = null)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return;

            try
            {
                // Validate email
                try { var _ = new MailAddress(customerEmail); } catch { return; }

                var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
                // Current promo code used across the site
                var promoCode = "LETOJETU26";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                    mail.To.Add(customerEmail);
                    mail.Bcc.Add("info@ladowebservis.sk");
                    mail.Subject = "💚 Odporúčané produkty pre zdravie a imunitu (kód " + promoCode + ")";
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.BodyEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = true;

                    // Build HTML body with product images
                    var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; background-color: #f5f7fa; }}
        .container {{ max-width: 600px; margin: 0 auto; background-color: #fff; padding: 20px; border-radius: 8px; }}
        .header {{ background: linear-gradient(135deg, #5d33fb 0%, #764ba2 100%); color: #fff; padding: 20px; text-align: center; border-radius: 8px; }}
        .header h1 {{ margin: 0; font-size: 24px; }}
        .product-section {{ margin: 30px 0; padding: 20px; background: #f9f9f9; border-radius: 8px; border-left: 4px solid #5d33fb; }}
        .product-card {{ background: #fff; padding: 15px; margin: 15px 0; border-radius: 8px; border: 1px solid #e0e0e0; }}
        .product-image {{ max-width: 150px; height: auto; border-radius: 6px; margin: 10px 0; }}
        .product-title {{ color: #5d33fb; font-weight: bold; font-size: 16px; margin: 10px 0; }}
        .price {{ color: #28a745; font-weight: bold; font-size: 18px; margin: 10px 0; }}
        .features {{ color: #666; font-size: 13px; margin: 10px 0; line-height: 1.6; }}
        .features li {{ margin: 5px 0; }}
        .promo-badge {{ background: #ffcc00; color: #2c3e50; padding: 10px 20px; border-radius: 8px; font-weight: bold; display: inline-block; margin: 15px 0; }}
        .button {{ background: #28a745; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 6px; display: inline-block; margin: 10px 0; }}
        .button:hover {{ background: #218838; }}
        .footer {{ color: #999; font-size: 12px; margin-top: 30px; padding-top: 20px; border-top: 1px solid #e0e0e0; }}
        .section-title {{ color: #5d33fb; font-weight: bold; font-size: 18px; margin: 20px 0 15px 0; border-bottom: 2px solid #5d33fb; padding-bottom: 10px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>💚 Odporúčané produkty pre zdravie a imunitu</h1>
        </div>

        <p>Dobrý deň {nameSafe},</p>
        <p>Ďakujeme, že ste s nami. Posielame Vám krátky výber odporúčaných produktov pre zdravie a imunitu.</p>
        <p>Pri objednávke môžete použiť promokód: <strong>{promoCode}</strong></p>

        <!-- Featured Products -->
        <div class='product-section'>
            <div class='section-title'>🌟 Odporúčané produkty</div>

            <!-- Balance Oil -->
            <div class='product-card'>
                <div style='text-align: center;'>
                    <img src='https://ladowebservis.sk/Image/BalanceOil.png' alt='Balance Oil' class='product-image'>
                </div>
                <div class='product-title'>💊 Balance Oil - Omega 3</div>
                <p>Prírodný Omega-3 olej pre zdravé srdce a mozog. Esenciálne mastné kyseliny pre vaše zdravie.</p>
                <p><strong>Výhody:</strong> Podporuje srdcovocievne zdravie, zlepšuje mozgovú funkciu.Obsahuje vysoký obsah Omega3 mastných kyselín a vitamínu D3.Podporuje normálne hladiny triglyceridov v krvi, normálny krvný tlak a normálne hladiny vápnika v krvi.</p>
                <a href='https://ladowebservis.sk/Home/Produkty?q=balance' class='button'>Pozrieť →</a>
            </div>

            <!-- Zinobiotic -->
            <div class='product-card'>
                <div style='text-align: center;'>
                    <img src='https://ladowebservis.sk/Image/Zinobiotic2025.png' alt='Zinobiotic' class='product-image'>
                </div>
                <div class='product-title'>🔬 Zinobiotic - Prémiové probiotiká</div>
                <p>Moderné probiotiká s klinicky študovanými bakteriálnymi kmeňmi pre zdravý tráviaci systém.</p>
                <p><strong>Výhody:</strong> Zdravé trávenie, silná imunita, pomáha udržiavať optimálnu hladinu cholesterolu vo vašom tele.</p>
                <a href='https://ladowebservis.sk/Home/Produkty?q=zinobiotic' class='button'>Pozrieť →</a>
            </div>

            <!-- Collagen Boozt -->
            <div class='product-card'>
                <div style='text-align: center;'>
                    <img src='https://ladowebservis.sk/Image/CollagenBoozt.png' alt='CollagenBoozt' class='product-image'>
                </div>
                <div class='product-title'>💎 Collagen Boozt - Prírodný kolagén</div>
                <p>Prémiový kolagénový nápoj s vitamínmi pre zdravú pleť, silné vlasy a pružné kĺby.</p>
                <p><strong>Výhody:</strong> Krása pokožky, silné vlasy a nechty, zdravé kĺby</p>
                <a href='https://ladowebservis.sk/Home/Produkty?q=collagen' class='button'>Pozrieť →</a>
            </div>

            <!-- ZinzinoXtend (supporting product for immunity) -->
            <div class='product-card'>
                <div style='text-align: center;'>
                    <img src='https://ladowebservis.sk/Image/ZinzinoXtend.png' alt='ZinzinoXtend' class='product-image'>
                </div>
                <div class='product-title'>💪 ZinzinoXtend – podpora imunity po zime</div>
                <p>
                    Kompletný imunitný a výživový doplnok s <strong>23 vitamínmi a minerálmi</strong>.
                    Ideálny ako podpora počas jarného obdobia po zimnej sezóne.
                </p>
                <p><strong>Tip:</strong> Skombinujte s BalanceOil pre komplexnú podporu.</p>
                <a href='https://ladowebservis.sk/Home/Produkty?q=xtend' class='button'>Pozrieť →</a>
            </div>
        </div>

        <div class='product-section' style='background: #fff3cd; border-left-color: #ffc107;'>
            <div class='section-title' style='color: #ff9800; border-bottom-color: #ffc107;'>❓ Máte otázky?</div>
            <p><strong>📧 Email:</strong> <a href='mailto:info@ladowebservis.sk'>info@ladowebservis.sk</a></p>
            <p><strong>📞 Telefón:</strong> +421907151293 alebo +421917952432</p>
            <p><strong>🌐 Web:</strong> <a href='https://ladowebservis.sk/Home/Kontakt'>ladowebservis.sk/Home/Kontakt</a></p>
            <p>Ak chcete poradiť s výberom, odpíšte na tento email.</p>
        </div>

        <p style='text-align: center; margin-top: 30px; font-weight: bold;'>S pozdravom,<br>Tím ladowebservis.sk 💚</p>

        <div class='footer'>
            <p><strong>📧 SPRÁVA O EMAILOCH:</strong></p>
            <p>Ak si neželáte dostávať ďalšie ponuky a novinky, <a href='{GetUnsubscribeUrl(customerEmail)}'>kliknite sem</a>.</p>
            <p>Stále budete dostávať informácie o svojich objednávkach.</p>
        </div>
    </div>
</body>
</html>";

                    mail.Body = htmlBody;

                    using (var client = new SmtpClient("email.active24.com"))
                    {
                        client.EnableSsl = true;
                        client.Port = 587;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                        client.Send(mail);
                    }
                }
            }
            catch
            {
                // ignore errors
            }
        }

        // Accepts optional attachment (HttpPostedFileBase) from MVC form file input
        public void OdoslanieEmailu(RegisterModel model, HttpPostedFileBase attachment = null)
        {
            // Block known spammer patterns (case-insensitive) - reject any containing 'loori'
            if (model != null)
            {
                var email = (model.Email ?? string.Empty).ToLowerInvariant();
                var name = (model.Name ?? string.Empty);

                // Check banned emails list
                if (BannedEmails.Any(b => email.Equals(b, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                // Check banned domains (e.g., @mail.ru, @bk.ru, @list.ru)
                if (BannedDomains.Any(d => email.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                // Check banned fragments
                if (BannedEmailFragments.Any(b =>
                        (!string.IsNullOrEmpty(email) && email.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(name) && name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    return;
                }
            }

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                mail.Headers["X-Mailer"] = "ladowebservis.sk";
                mail.Subject = "Ďakujeme za Vašu registráciu.";
                mail.IsBodyHtml = false;
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;

                mail.Body = string.Format("\r\n Ďakujeme, že ste sa u nás zaregistrovali.Ako zaregistrovaný zákazník okrem iného získavate následovné výhody:" +
                    "\r\n Rýchlejší nákup vďaka vlastnému zoznamu obľúbených položiek." +
                    "\r\n Možnosť byť informovaný o novinkách a akciách." +
                    "\r\n Prístup do členskej sekcie a zľavového programu." + "\r\n\r\n" +
                    "\r\n Váš email: {0}" +
                    "\r\n Vaše meno: {1}" +
                    "\r\n Priezvisko: {2}," +
                    "\r\n Telefón: {3}" +
                    "\r\n Adresa: {4}" +
                    "\r\n Vaša správa: {5}," +
                    "\r\n Vaše heslo: {6}" +
                    "\r\n Váš text:\r\n {7}" +
                    "\r\n\r\n Prajeme príjemný deň." +
                    "\r\n\r\n---\r\n📧 SPRÁVA O EMAILOCH:\r\nAk si neželáte dostávať ďalšie ponuky a novinky, kliknite sem: {8}\r\n\r\nStále budete dostávať informácie o svojich objednávkach." +
                    "\r\n\r\n S pozdravom ladowebservis.sk",
                    model.Email,
                    model.Name,
                    model.Priezvisko,
                    model.Phone,
                    model.Adresa,
                    model.Text,
                    GetUnsubscribeUrl(model.Email));

                mail.To.Add(model.Email);
                mail.Bcc.Add("info@ladowebservis.sk");

                if (attachment != null && attachment.ContentLength > 0)
                {
                    var mailAttachment = new Attachment(attachment.InputStream, attachment.FileName, attachment.ContentType);
                    mail.Attachments.Add(mailAttachment);
                }

                using (var client = new SmtpClient("email.active24.com"))
                {
                    client.EnableSsl = true;
                    client.Port = 587;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");


                    client.Send(mail);
                }
            }

            // Extra: send a short promo / new products email after registration
            try
            {
                if (!string.IsNullOrWhiteSpace(model?.Email))
                {
                    SendNewProductsPromoEmail(model.Email, model.Name);
                }
            }
            catch
            {
                // ignore
            }
        }

        // Send a follow-up email containing product links to the specified recipient.

        public void SendProductsEmail(string customerEmail, string customerName = null, IEnumerable<string> cartProductIds = null)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return;
            try
            {
                // validate email
                try { var _ = new MailAddress(customerEmail); } catch { return; }

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                    mail.To.Add(customerEmail);
                    mail.Bcc.Add("info@ladowebservis.sk");
                    mail.Subject = "Odporúčané produkty od Ladowebservis";
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = false; // send plain-text only

                    var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();

                    // Build plain-text body
                    var text = new StringBuilder();
                    text.AppendLine($"Dobrý deň {nameSafe},");
                    text.AppendLine();
                    text.AppendLine("Posielame výber odporúčaných produktov, ktoré by Vás mohli zaujímať:");
                    text.AppendLine();

                    try
                    {
                        var prodList = ProductCatalog.GetAll() ?? Enumerable.Empty<object>();

                        // If cartProductIds provided, list those first with more detail
                        if (cartProductIds != null && cartProductIds.Any())
                        {
                            text.AppendLine("Produkty, ktoré zostali vo Vašom košíku:");
                            foreach (var id in cartProductIds)
                            {
                                if (string.IsNullOrWhiteSpace(id)) continue;
                                dynamic found = null;
                                foreach (dynamic p in prodList)
                                {
                                    try
                                    {
                                        var pidTry = (p?.Id ?? string.Empty).ToString();
                                        if (pidTry.Equals(id, StringComparison.OrdinalIgnoreCase)) { found = p; break; }
                                    }
                                    catch { }
                                }
                                if (found != null)
                                {
                                    var pname = (found.Name ?? string.Empty).ToString();
                                    decimal pprice = 0m;
                                    try { pprice = Convert.ToDecimal(found.Price); } catch { decimal.TryParse((found.Price ?? "0").ToString(), out pprice); }
                                    string pdesc = string.Empty;
                                    try { pdesc = (found.Description ?? found.Text ?? found.Info ?? found.ShortDescription ?? string.Empty).ToString(); } catch { pdesc = string.Empty; }
                                    var productUrl = UrlForProduct((found.Id ?? string.Empty).ToString());

                                    text.AppendLine($"- {pname} — €{pprice:0.00}");
                                    if (!string.IsNullOrWhiteSpace(pdesc))
                                    {
                                        var shortDesc = pdesc.Length > 300 ? pdesc.Substring(0, 300) + "..." : pdesc;
                                        text.AppendLine("  Popis: " + shortDesc);
                                    }
                                    text.AppendLine("  Stránka produktu: " + productUrl);
                                    text.AppendLine();
                                }
                                else
                                {
                                    // fallback: include id and link
                                    text.AppendLine($"- Produkt ID: {id} (zobraziť: {UrlForProduct(id)})");
                                    text.AppendLine();
                                }
                            }

                            text.AppendLine();
                            text.AppendLine("Nižšie sú ďalšie odporúčené produkty:");
                            text.AppendLine();
                        }

                        // General product list (brief)
                        foreach (dynamic p in prodList)
                        {
                            if (p == null) continue;
                            var pid = (p.Id ?? string.Empty).ToString();
                            var pname = (p.Name ?? string.Empty).ToString();
                            decimal pprice = 0m;
                            try { pprice = Convert.ToDecimal(p.Price); } catch { decimal.TryParse((p.Price ?? "0").ToString(), out pprice); }
                            var link = UrlForProduct(pid);
                            text.AppendLine($"- {pname} — €{pprice:0.00} — {link}");
                        }
                    }
                    catch
                    {
                        text.AppendLine("Zobraziť naše produkty: https://ladowebservis.sk/Home/Produkty");
                    }

                    text.AppendLine();
                    text.AppendLine("Ak chcete, môžeme Vám produkty zaslať neskôr znova. Stačí odpovedať na tento e‑mail.");
                    text.AppendLine();
                    text.AppendLine("S pozdravom,\nTím Ladowebservis");
                    text.AppendLine();
                    text.AppendLine("---");
                    text.AppendLine("📧 SPRÁVA O EMAILOCH:");
                    text.AppendLine("Ak si neželáte dostávať ďalšie ponuky a novinky, kliknite sem: " + GetUnsubscribeUrl(customerEmail));
                    text.AppendLine();
                    text.AppendLine("Stále budete dostávať informácie o svojich objednávkach.");

                    mail.Body = text.ToString();
                    mail.BodyEncoding = Encoding.UTF8;

                    // do not add HTML alternate view; send plain text only

                    using (var client = new SmtpClient("email.active24.com"))
                    {
                        client.EnableSsl = true;
                        client.Port = 587;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                        client.Send(mail);
                    }
                }
            }
            catch
            {
                // ignore send errors
            }
        }

        public bool TrySendSubscriptionEmail(string customerEmail)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return false;

            try
            {
                try { var _ = new MailAddress(customerEmail); } catch { return false; }

                using (var customerMail = new MailMessage())
                {
                    customerMail.From = new MailAddress("info@ladowebservis.sk", "Ladowebservis");
                    customerMail.To.Add(customerEmail);
                    customerMail.Bcc.Add("info@ladowebservis.sk");
                    customerMail.Subject = "Potvrdenie odberu noviniek - Ladowebservis";
                    customerMail.SubjectEncoding = Encoding.UTF8;
                    customerMail.BodyEncoding = Encoding.UTF8;
                    customerMail.IsBodyHtml = true;

                    var unsubscribeUrl = GetUnsubscribeUrl(customerEmail);
                    var customerEmailSafe = HttpUtility.HtmlEncode(customerEmail);
                    customerMail.Body = $@"
<div style='font-family:Arial,sans-serif;color:#24324a;line-height:1.6;'>
    <h2 style='color:#5d33fb;'>Dakujeme za prihlasenie k odberu noviniek</h2>
    <p>Email <strong>{customerEmailSafe}</strong> sme zaradili do zoznamu noviniek Ladowebservis.</p>
    <p>Budeme vam posielat novinky o produktoch, doprave, platbach, akciach a odporucaniach pre zdravie aj web/IT sluzby.</p>
    <p>Ak si budete zelat odber zrusit, pouzite <a href='{unsubscribeUrl}'>odhlasovaci odkaz</a>.</p>
    <p style='margin-top:24px;'>S pozdravom,<br><strong>Tím Ladowebservis</strong></p>
</div>";

                    if (!TrySendWithConfiguredSmtp(customerMail)) return false;
                }

                using (var adminMail = new MailMessage())
                {
                    adminMail.From = new MailAddress("info@ladowebservis.sk", "Ladowebservis");
                    adminMail.To.Add("info@ladowebservis.sk");
                    adminMail.Subject = "Novy odber noviniek na webe";
                    adminMail.SubjectEncoding = Encoding.UTF8;
                    adminMail.BodyEncoding = Encoding.UTF8;
                    adminMail.IsBodyHtml = false;
                    adminMail.Body = "Na webe sa prihlasil novy odberatel noviniek:\r\n\r\n" + customerEmail + "\r\n\r\nDatum: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm");

                    return TrySendWithConfiguredSmtp(adminMail);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"TrySendSubscriptionEmail failed: {ex}");
                return false;
            }
        }

        // Send a short promo email with new/featured products and promo code.
        // Used as a follow-up after registration confirmation.
        private void SendNewProductsPromoEmail(string customerEmail, string customerName = null)
        {
            if (string.IsNullOrWhiteSpace(customerEmail)) return;

            try
            {
                try { var _ = new MailAddress(customerEmail); } catch { return; }

                var nameSafe = string.IsNullOrWhiteSpace(customerName) ? "" : HttpUtility.HtmlEncode(customerName).Trim();
                var promoCode = "REGZAK26";

                // Use Produkty page with query as a simple way to highlight modern products (e.g., balance)
                var productsUrl = "https://ladowebservis.sk/Home/Produkty";
                var balzmUrl = "https://ladowebservis.sk/Home/Produkty?q=" + HttpUtility.UrlEncode("balzam");
                var omegaUrl = "https://ladowebservis.sk/Home/Produkty?q=" + HttpUtility.UrlEncode("balance");
                var probioticUrl = "https://ladowebservis.sk/Home/Produkty?q=" + HttpUtility.UrlEncode("zinobiotic");

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("info@ladowebservis.sk", "ladowebservis.sk");
                    mail.To.Add(customerEmail);
                    mail.Bcc.Add("info@ladowebservis.sk");
                    mail.Subject = "Novinky a špeciálna zľava 10% – použite nový kód " + promoCode;
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.BodyEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = false;

                    var sb = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(nameSafe))
                    {
                        sb.AppendLine("Dobrý deň " + nameSafe + ",");
                    }
                    else
                    {
                        sb.AppendLine("Dobrý deň,");
                    }

                    sb.AppendLine();
                    sb.AppendLine("Ďakujeme, že ste s nami. Máme pre Vás niečo extra – nové moderné produkty pre Vaše zdravie a špeciálny bonus do košíka.");
                    sb.AppendLine("Tento špeciálny bonus platí len pre zaregistrovaných zákazníkov na produkty už od 50€ po dobu 6 mesiacov!");
                    sb.AppendLine();
                    sb.AppendLine("PROMO KÓD: " + promoCode);
                    sb.AppendLine("Zadajte ho v košíku a získate 10% zľavu");
                    sb.AppendLine();
                    sb.AppendLine("Tipy na novinky / obľúbené produkty:");
                    sb.AppendLine("• Balzamy: " + balzmUrl);
                    sb.AppendLine("• Omega 3 a zdravie: " + omegaUrl);
                    sb.AppendLine("• Probiotiká (Zinobiotic): " + probioticUrl);
                    sb.AppendLine();
                    sb.AppendLine("Pozrieť všetky produkty:");
                    sb.AppendLine(productsUrl);
                    sb.AppendLine();
                    sb.AppendLine("Ak chcete poradiť s výberom, odpíšte na tento e‑mail – radi pomôžeme.");
                    sb.AppendLine();
                    sb.AppendLine("S pozdravom,");
                    sb.AppendLine("Tím ladowebservis.sk");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine("📧 SPRÁVA O EMAILOCH:");
                    sb.AppendLine("Ak si neželáte dostávať ďalšie ponuky a novinky, kliknite sem: " + GetUnsubscribeUrl(customerEmail));
                    sb.AppendLine();
                    sb.AppendLine("Stále budete dostávať informácie o svojich objednávkach.");

                    mail.Body = sb.ToString();

                    using (var client = new SmtpClient("email.active24.com"))
                    {
                        client.EnableSsl = true;
                        client.Port = 587;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential("info@ladowebservis.sk", "a98HdiBMYNRH");
                        client.Send(mail);
                    }
                }
            }
            catch
            {
                // ignore send errors
            }
        }

        // helper to build product URL (simple fallback)
        private static string UrlForProduct(String productId)
        {
            if (String.IsNullOrWhiteSpace(productId)) return "https://ladowebservis.sk/Home/Produkty";
            // attempt to link to a product details route
            return "https://ladowebservis.sk/Home/Produkt?id=" + HttpUtility.UrlEncode(productId);
        }
    }
}