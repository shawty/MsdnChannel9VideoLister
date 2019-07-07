namespace MsdnChannel9VideoLister
{
  
  public class VideoDescription
  {
    // Most video pages on MSDN CH9 include a script tag with the mime type of "application/ld+json"
    // this class models that JSON string so we can grab the video details

    public string @Context { get; set; }
    public string @Type { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ThumbnailUrl { get; set; }
    public string UploadDate { get; set; }
    public string Duration { get; set; }
    public string ContentUrl { get; set; }
    public string EmbedUrl { get; set; }

  }
}
