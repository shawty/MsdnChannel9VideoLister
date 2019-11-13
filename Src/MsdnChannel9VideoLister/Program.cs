using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace MsdnChannel9VideoLister
{
  class Program
  {
    private const string siteRoot = "https://channel9.msdn.com";
    private const string outputFolder = "jsonfiles";
    private static string showsJson = $"{outputFolder}\\showslist.json";

    private static WebClient client = new WebClient();
    private static List<MsdnShow> shows = new List<MsdnShow>();

    private static int GetMaxPageNumber(IHtmlDocument parentPage)
    {
      int max = 0;

      var navUl = parentPage
        .All
        .Where(
          el => el.LocalName == "ul" &&
          el.HasAttribute("aria-labelledby") &&
          el.GetAttribute("aria-labelledby") == "pagingLabel")
        .FirstOrDefault();

      // If we are on the actual page looking for a nav, but don't find one, we know we have at least one page
      if (navUl == null) return 1;

      var elipsLi = navUl.Children.Where(el => el.InnerHtml.Contains("<span class=\"ellip\">")).FirstOrDefault();
      if (elipsLi == null) return 1;

      var valText = elipsLi.Children.Where(el => el.LocalName == "a").First().TextContent.Trim();

      if (!int.TryParse(valText, out max)) return 1;

      return max;
    }

    private static void GetShows()
    {
      string rootPageUrl = $"{siteRoot}/Browse/AllShows";

      string rootPage = client.DownloadString(rootPageUrl);

      HtmlParser parser = new HtmlParser();
      IHtmlDocument document = parser.ParseDocument(rootPage);

      var maxPage = GetMaxPageNumber(document); // Find maximum page number
      shows.AddRange(GetShowsFromPage(document)); // Get shows off first page

      for (int pageNumber = 2; pageNumber < maxPage + 1; pageNumber++)
      {
        string nextUrl = $"{rootPageUrl}?page={pageNumber}";
        rootPage = client.DownloadString(nextUrl);
        document = parser.ParseDocument(rootPage);
        shows.AddRange(GetShowsFromPage(document));
      }

    }

    private static List<MsdnShow> GetShowsFromPage(IHtmlDocument parentPage)
    {
      List<MsdnShow> results = new List<MsdnShow>();

      var showArticles = parentPage.All.Where(el => el.LocalName == "article" && el.ClassList.Contains("noVideo"));
      foreach (var showArticle in showArticles)
      {
        var aTag = showArticle.QuerySelector("header h3 a");
        var imgTag = showArticle.QuerySelector("a img");

        string displayName = imgTag.Attributes["alt"].Value.Trim();
        string videosFileName = $"{RemoveBadNameChars(displayName)}.json";
        videosFileName = ConvertStringToPureAscii(videosFileName).Trim();

        results.Add(new MsdnShow()
        {
          DataApi = showArticle.Attributes["data-api"].Value,
          DisplayName = displayName,
          Href = aTag.Attributes["href"].Value,
          ThumbHref = imgTag.Attributes["src"].Value,
          ShowId = new Guid(showArticle.Attributes["data-api"].Value.Replace("/Areas(guid'", "").Replace("')/", "")),
          VideosJsonFile = videosFileName
        });
      }

      return results;
    }

    private static void WriteShowsJson()
    {
      var settings = new JsonSerializerSettings
      {
        Formatting = Formatting.Indented,
      };
      File.WriteAllText(showsJson, JsonConvert.SerializeObject(shows, settings));
    }

    private static void LoadShowsJson()
    {
      shows = JsonConvert.DeserializeObject<List<MsdnShow>>(File.ReadAllText(showsJson));
    }

    private static List<MsdnVideo> GetVideosForShow(MsdnShow show, int pageNumber, out int maxPage)
    {
      List<MsdnVideo> results = new List<MsdnVideo>();

      string pageLink = $"{siteRoot}{show.Href}?page={pageNumber}";
      string sectionPage = client.DownloadString(pageLink);

      HtmlParser parser = new HtmlParser();
      IHtmlDocument document = parser.ParseDocument(sectionPage);

      maxPage = GetMaxPageNumber(document);

      var videoEntrys = document.All.Where(el => el.LocalName == "article" && el.ClassList.Contains("abstract") && el.ClassList.Contains("small"));

      foreach (var entry in videoEntrys)
      {
        MsdnVideo newVideo = new MsdnVideo();

        newVideo.DataApi = entry.Attributes["data-api"].Value;
        newVideo.VideoId = new Guid(newVideo.DataApi.Replace("/Entries(guid'", "").Replace("')/", ""));

        var testDiv = entry.FirstElementChild.FirstElementChild;
        if(testDiv.ClassList.Contains("liveFuture"))
        {
          // Video is a future live stream and does not yet have a video page, so we skip it
          continue;
        }

        newVideo.VideoPageLink = entry.FirstElementChild.FirstElementChild.FirstElementChild.Attributes["href"].Value;

        string videoPageLink = $"{siteRoot}{newVideo.VideoPageLink}";
        string videoPage = client.DownloadString(videoPageLink);
        IHtmlDocument videoPageDocument = parser.ParseDocument(videoPage);

        string descriptionTemp = "";
        var metaDescription = videoPageDocument.All.FirstOrDefault(el =>
          el.LocalName == "meta"
          && el.HasAttribute("name")
          && el.Attributes["name"].Value == "description"
        );
        if (metaDescription != null)
        {
          descriptionTemp = metaDescription.Attributes["content"].Value;
        }

        string authorTemp = "";
        var authorsDiv = videoPageDocument.All.FirstOrDefault(el =>
          el.LocalName == "div"
          && el.ClassList.Contains("authors")
        );

        if (authorsDiv != null)
        {
          var authorName = authorsDiv.FirstElementChild.TextContent;
          if (!string.IsNullOrEmpty("authorName"))
          {
            authorTemp = authorName;
          }
        }

        var jsonDescription = videoPageDocument.All.FirstOrDefault(
          el => el.LocalName == "script" &&
          el.HasAttribute("type") &&
          el.Attributes["type"].Value == "application/ld+json"
        );

        if (jsonDescription != null)
        {
          var jsonContent = jsonDescription.TextContent;
          var videoDescription = JsonConvert.DeserializeObject<VideoDescription>(jsonContent);

          newVideo.VideoPageThumb = videoDescription.ThumbnailUrl;
          newVideo.ShowName = show.DisplayName;
          newVideo.VideoLength = TimeSpan.Parse(videoDescription.Duration.Replace("PT", "").Replace("H", ":").Replace("M", ":").Replace("S", ""));
          newVideo.VideoTitle = videoDescription.Name;
          newVideo.Author = string.IsNullOrEmpty(authorTemp) ? "UNKNOWN" : authorTemp;
          newVideo.UtcDateTimePublished = DateTime.Parse(videoDescription.UploadDate).ToUniversalTime();
          newVideo.ActualVideoFileLink = videoDescription.ContentUrl;
          newVideo.Description = string.IsNullOrEmpty(descriptionTemp) ? videoDescription.Description : descriptionTemp;

          // To reduce the likely hood of getting JSON with no usable details entry's in the list, we ONLY save
          // the entry IF we actually got a json description tag
          results.Add(newVideo);
        }
      }

      return results;
    }

    private static List<MsdnVideo> GetAllVideosForShow(MsdnShow show)
    {
      var results = new List<MsdnVideo>();

      int maxPage;
      results.AddRange(GetVideosForShow(show, 1, out maxPage));
      if (maxPage == 1)
      {
        return results;
      }

      for (int count = 2; count <= maxPage; count++)
      {
        int notUsed;
        results.AddRange(GetVideosForShow(show, count, out notUsed));
      }

      return results;
    }

    // Remove any standard chars that will cause a file system meltdown if trying to save a name with them present
    private static string RemoveBadNameChars(string inputName)
    {
      string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());

      foreach (char c in invalid)
      {
        inputName = inputName.Replace(c.ToString(), "");
      }

      return inputName;
    }

    // Remove any Unicode chars that will cause a file system meltdown if trying to save a name with them present
    // NOTE: This WILL remove Kanjii, Crylic, Arabic and all sorts of other craziness, think of it as normalization
    // for file names.
    private static string ConvertStringToPureAscii(string inputString)
    {
      return Encoding.ASCII.GetString(
      Encoding.Convert(
          Encoding.UTF8,
          Encoding.GetEncoding(
              Encoding.ASCII.EncodingName,
              new EncoderReplacementFallback(string.Empty),
              new DecoderExceptionFallback()
              ),
          Encoding.UTF8.GetBytes(inputString)
        )
      );
    }

    private static void WriteVideosJson(MsdnShow show, List<MsdnVideo> videos)
    {
      string fileName = $"{outputFolder}\\{RemoveBadNameChars(show.DisplayName)}.json";
      fileName = ConvertStringToPureAscii(fileName).Trim();
      var settings = new JsonSerializerSettings
      {
        Formatting = Formatting.Indented,
      };
      File.WriteAllText(fileName, JsonConvert.SerializeObject(videos, settings));
    }

    private static void GetVideos()
    {
      foreach (var show in shows)
      {
        Console.WriteLine($"Processing show : {show.DisplayName}");
        var vids = GetAllVideosForShow(show);
        WriteVideosJson(show, vids);
      }
    }

    static void Main(string[] args)
    {
      // Pretend we are Google bot doing a web crawl.
      client.Headers.Add("user-agent", "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");

      // Make sure our output folder exists
      if(!Directory.Exists(outputFolder))
      {
        Directory.CreateDirectory(outputFolder);
      }

      // To create the initial shows list JSON file uncomment the following two lines
      GetShows();
      WriteShowsJson();

      // To load in an already created JSON Shows file uncomment the next line
      //LoadShowsJson();

      // Quick fix 7-7-2019 to update the "Videos Json File" in the shows list already produced
      // if your building new files from scratch you don't need this :-)
      //foreach(var show in shows)
      //{
      //  string fileName = $"{RemoveBadNameChars(show.DisplayName)}.json";
      //  fileName = ConvertStringToPureAscii(fileName).Trim();
      //  show.VideosJsonFile = fileName;
      //}
      //WriteShowsJson();

      // Get videos iterates through the current shows list, and get's the videos for them
      // NOTE: It does NOT get the headline videos, just the regular video list from the main page content
      GetVideos();

    }

  }
}
