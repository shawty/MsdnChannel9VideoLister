using System;

namespace MsdnChannel9VideoLister
{
  // Class describing a known MSDN CH9 show or series, used to 
  // build the main SHows List prior to grabbing the video names

  public class MsdnShow
  {
    public string DataApi { get; set; }
    public string DisplayName { get; set; }
    public string Href { get; set; }
    public string ThumbHref { get; set; }
    public Guid ShowId { get; set; }
    public string VideosJsonFile { get; set; }
  }
}
