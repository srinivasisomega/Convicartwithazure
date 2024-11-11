using ConvicartWebApp.DataAccessLayer.Models;

namespace ConvicartWebApp.PresentationLayer.ViewModels
{
    public class ForYouPageViewModel
    {
        public Customer Customer { get; set; }  // Customer information
        public List<VideoPostViewModel> VideoPosts { get; set; }  // List of video posts to be displayed
        public int PageIndex { get; set; }  // The current page
        public int TotalPages { get; set; }  // Total pages available
    }

    public class VideoPostViewModel
    {
        public string VideoPath { get; set; }  // Path to the video file
        public string FileName { get; set; }  // The name of the file
    }
}
