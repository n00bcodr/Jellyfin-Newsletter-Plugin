#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Emails.HTMLBuilder;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Scripts.ENTITIES;
using Jellyfin.Plugin.Newsletters.Shared.DATA;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Controllers;

[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Newsletter")]
public class NewsletterController : ControllerBase
{
    private readonly PluginConfiguration config;
    private SQLiteDatabase db;
    private Logger logger;
    private JsonFileObj jsonHelper;

    public NewsletterController()
    {
        db = new SQLiteDatabase();
        logger = new Logger();
        jsonHelper = new JsonFileObj();
        config = Plugin.Instance!.Configuration;
    }

    [HttpGet("GetItems")]
    public ActionResult<List<JsonFileObj>> GetNewsletterItems()
    {
        try
        {
            db.CreateConnection();
            var items = new List<JsonFileObj>();

            foreach (var row in db.Query("SELECT * FROM CurrNewsletterData ORDER BY Title, Season, Episode;"))
            {
                if (row is not null)
                {
                    var item = jsonHelper.ConvertToObj(row);
                    items.Add(item);
                }
            }

            return Ok(items);
        }
        catch (Exception e)
        {
            logger.Error("An error occurred while getting newsletter items: " + e);
            return StatusCode(500, "Failed to retrieve newsletter items");
        }
        finally
        {
            db.CloseConnection();
        }
    }

    [HttpPost("RemoveItem")]
    public ActionResult RemoveNewsletterItem([FromBody] RemoveItemRequest request)
    {
        try
        {
            db.CreateConnection();

            string sanitizedFilename = request.Filename.Replace("'", string.Empty, StringComparison.Ordinal);
            db.ExecuteSQL($"DELETE FROM CurrNewsletterData WHERE Filename='{sanitizedFilename}';");

            logger.Info($"Removed item from newsletter: {request.Filename}");
            return Ok();
        }
        catch (Exception e)
        {
            logger.Error("An error occurred while removing newsletter item: " + e);
            return StatusCode(500, "Failed to remove newsletter item");
        }
        finally
        {
            db.CloseConnection();
        }
    }

    [HttpGet("Preview")]
    public ActionResult<string> PreviewNewsletter()
    {
        try
        {
            db.CreateConnection();

            if (!NewsletterDbIsPopulated())
            {
                return Ok("<html><body><h2>No newsletter data available</h2><p>Run the library scanner first to generate newsletter content.</p></body></html>");
            }

            HtmlBuilder hb = new HtmlBuilder();
            string body = hb.GetDefaultHTMLBody();
            string builtString = hb.BuildDataHtmlStringFromNewsletterData();
            builtString = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", config.Hostname);
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            builtString = builtString.Replace("{Date}", currDate, StringComparison.Ordinal);

            // Final cleanup of any remaining template variables
            builtString = Regex.Replace(builtString, "{[A-za-z]*}", " ");

            return Ok(builtString);
        }
        catch (Exception e)
        {
            logger.Error("An error occurred while generating newsletter preview: " + e);
            return StatusCode(500, "Failed to generate newsletter preview");
        }
        finally
        {
            db.CloseConnection();
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
                    return true;
                }
            }
        }

        return false;
    }

    public class RemoveItemRequest
    {
        public string Filename { get; set; } = string.Empty;
    }
}