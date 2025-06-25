#pragma warning disable 1591, SYSLIB0014, CA1002, CS0162
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Scripts.ENTITIES;
using Jellyfin.Plugin.Newsletters.Scripts.SCRAPER;
using Jellyfin.Plugin.Newsletters.Shared.DATA;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
// using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.Scanner.NLImageHandler;

public class PosterImageHandler
{
    // Global Vars
    // Readonly
    private readonly PluginConfiguration config;
    private Logger logger;
    private SQLiteDatabase db;

    // Non-readonly
    private List<JsonFileObj> archiveSeriesList;
    // private List<string> fileList;

    public PosterImageHandler()
    {
        logger = new Logger();
        db = new SQLiteDatabase();
        config = Plugin.Instance!.Configuration;

        archiveSeriesList = new List<JsonFileObj>();
    }

    public string FetchImagePoster(JsonFileObj item)
    {
        // Check which config option for posters are selected
        logger.Debug($"HOSTING TYPE: {config.PHType}");
        switch (config.PHType)
        {
            case "Imgur":
                return UploadToImgur(item.PosterPath);
            case "JfHosting":
                return $"{config.Hostname}/Items/{item.ItemID}/Images/Primary";
            case "Embedded":
                // For embedded images, we return the local path
                // The actual embedding happens in the email builder
                return item.PosterPath;
            default:
                logger.Warn("Unknown poster hosting type, defaulting to embedded");
                return item.PosterPath;
        }
    }

    private string UploadToImgur(string posterFilePath)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            logger.Error("Imgur API key is not configured!");
            return "ERR_NO_API_KEY";
        }

        if (!File.Exists(posterFilePath))
        {
            logger.Error($"Poster file not found: {posterFilePath}");
            return "ERR_FILE_NOT_FOUND";
        }

        try
        {
            using (WebClient wc = new WebClient())
            {
                NameValueCollection values = new NameValueCollection
                {
                    { "image", Convert.ToBase64String(File.ReadAllBytes(posterFilePath)) }
                };

                wc.Headers.Add("Authorization", "Client-ID " + config.ApiKey);

                byte[] response = wc.UploadValues("https://api.imgur.com/3/upload.xml", values);
                string res = System.Text.Encoding.Default.GetString(response);

                logger.Debug("Imgur Response: " + res);

                if (res.Contains("<link>") && res.Contains("</link>"))
                {
                    string imageUrl = res.Split("<link>")[1].Split("</link>")[0];
                    logger.Info("Imgur Uploaded! Link: " + imageUrl);
                    return imageUrl;
                }
                else
                {
                    logger.Error("Unexpected Imgur response format");
                    return "ERR_INVALID_RESPONSE";
                }
            }
        }
        catch (WebException e)
        {
            logger.Error("Imgur upload failed: " + e.Message);
            
            if (e.Response is HttpWebResponse response)
            {
                logger.Debug($"HTTP Status: {response.StatusCode}");
                
                using (var reader = new StreamReader(e.Response.GetResponseStream()))
                {
                    string errorResponse = reader.ReadToEnd();
                    logger.Debug($"Error response: {errorResponse}");
                }

                // Handle rate limiting
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return "429";
                }
            }

            return "ERR";
        }
        catch (Exception ex)
        {
            logger.Error("Error during Imgur upload: " + ex.Message);
            return "ERR";
        }
    }
}