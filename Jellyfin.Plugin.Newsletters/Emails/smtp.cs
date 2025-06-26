#pragma warning disable 1591
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Emails.HTMLBuilder;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Shared.DATA;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Emails.EMAIL;

/// <summary>
/// Interaction logic for SendMail.xaml.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Smtp")]
public class Smtp : ControllerBase
{
    private readonly PluginConfiguration config;
    private SQLiteDatabase db;
    private Logger logger;

    public Smtp()
    {
        db = new SQLiteDatabase();
        logger = new Logger();
        config = Plugin.Instance!.Configuration;
    }

    [HttpPost("SendTestMail")]
    public void SendTestMail()
    {
        MailMessage mail;
        SmtpClient smtp;

        try
        {
            logger.Debug("Sending out test mail!");
            mail = new MailMessage();

            mail.From = new MailAddress(config.FromAddr);
            mail.To.Clear();
            mail.Subject = "Jellyfin Newsletters - Test";
            mail.Body = "Success! You have properly configured your email notification settings";
            mail.IsBodyHtml = false;

            foreach (string email in config.ToAddr.Split(','))
            {
                mail.Bcc.Add(email.Trim());
            }

            smtp = new SmtpClient(config.SMTPServer, config.SMTPPort);
            smtp.Credentials = new NetworkCredential(config.SMTPUser, config.SMTPPass);
            smtp.EnableSsl = true;
            smtp.Send(mail);
        }
        catch (Exception e)
        {
            logger.Error("An error has occured: " + e);
        }
    }

    [HttpPost("SendSmtp")]
    public void SendEmail()
    {
        try
        {
            db.CreateConnection();

            if (NewsletterDbIsPopulated())
            {
                logger.Debug("Sending out mail!");
                MailMessage mail = new MailMessage();
                string smtpAddress = config.SMTPServer;
                int portNumber = config.SMTPPort;
                bool enableSSL = true;
                string emailFromAddress = config.FromAddr;
                string username = config.SMTPUser;
                string password = config.SMTPPass;
                string emailToAddress = config.ToAddr;
                string subject = config.Subject;

                HtmlBuilder hb = new HtmlBuilder();

                string body = hb.GetDefaultHTMLBody();
                string builtString = hb.BuildDataHtmlStringFromNewsletterData();
                builtString = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", config.Hostname);
                string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                builtString = builtString.Replace("{Date}", currDate, StringComparison.Ordinal);

                mail.From = new MailAddress(emailFromAddress, emailFromAddress);
                mail.To.Clear();
                mail.Subject = subject;
                mail.Body = Regex.Replace(builtString, "{[A-za-z]*}", " "); // Final cleanup
                mail.IsBodyHtml = true;

                // Handle embedded images if enabled
                if (config.EmbedImages && config.PHType == "Embedded")
                {
                    ProcessEmbeddedImages(mail, builtString);
                }

                foreach (string email in emailToAddress.Split(','))
                {
                    mail.Bcc.Add(email.Trim());
                }

                SmtpClient smtp = new SmtpClient(smtpAddress, portNumber);
                smtp.Credentials = new NetworkCredential(username, password);
                smtp.EnableSsl = enableSSL;
                smtp.Send(mail);

                hb.CleanUp(builtString);
            }
            else
            {
                logger.Info("There is no Newsletter data.. Have I scanned or sent out a newsletter recently?");
            }
        }
        catch (Exception e)
        {
            logger.Error("An error has occured: " + e);
        }
        finally
        {
            db.CloseConnection();
        }
    }

    private void ProcessEmbeddedImages(MailMessage mail, string htmlBody)
    {
        try
        {
            // Find all data: image URLs in the HTML
            var dataImageRegex = new Regex(@"data:image/([^;]+);base64,([^""'>\s]+)", RegexOptions.IgnoreCase);
            var matches = dataImageRegex.Matches(htmlBody);

            int imageIndex = 0;
            foreach (Match match in matches)
            {
                string mimeType = match.Groups[1].Value;
                string base64Data = match.Groups[2].Value;
                string contentId = $"image{imageIndex}";

                try
                {
                    byte[] imageBytes = Convert.FromBase64String(base64Data);
                    using (var stream = new MemoryStream(imageBytes))
                    {
                        var attachment = new Attachment(stream, contentId, $"image/{mimeType}");
                        attachment.ContentDisposition.Inline = true;
                        attachment.ContentId = contentId;
                        mail.Attachments.Add(attachment);
                    }

                    // Replace the data URL with a cid reference
                    string originalDataUrl = match.Value;
                    string cidReference = $"cid:{contentId}";
                    mail.Body = mail.Body.Replace(originalDataUrl, cidReference);
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to process embedded image {imageIndex}: {ex}");
                }

                imageIndex++;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to process embedded images: {ex}");
        }
    }

    private bool NewsletterDbIsPopulated()
    {
        foreach (var row in db.Query("SELECT COUNT(*) FROM CurrNewsletterData;"))
        {
            if (row is not null)
            {
                if (int.Parse(row[0].ToString(), CultureInfo.CurrentCulture) > 0)
                {
                    db.CloseConnection();
                    return true;
                }
            }
        }

        db.CloseConnection();
        return false;
    }
}