using System;

namespace MsdnChannel9VideoLister
{
  // Our main MSDN CH9 Video/Episode meta data class that get's written
  // to the JSON file, this is an amalgamation of all the data we grab
  // from a page

  public class MsdnVideo
  {
    public Guid VideoId { get; set; }
    public string DataApi { get; set; }
    public string VideoPageLink { get; set; }
    public string VideoPageThumb { get; set; }
    public string ShowName { get; set; }
    public TimeSpan VideoLength { get; set; }
    public string VideoTitle { get; set; }
    public string Author { get; set; }
    public DateTime UtcDateTimePublished { get; set; }
    public string ActualVideoFileLink { get; set; }
    public string Description { get; set; }
  }
}
