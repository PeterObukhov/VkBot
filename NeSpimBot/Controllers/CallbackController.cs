using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using VkNet.Abstractions;
using VkNet.Model;
using VkNet.Model.Attachments;
using VkNet.Model.RequestParams;
using VkNet.Utils;

namespace NeSpimBot.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CallbackController : ControllerBase
    {
        private readonly IConfiguration configuration;
        private readonly IVkApi vkApi;
        private readonly long groupId;
        private readonly long albumId;

        public CallbackController(IConfiguration configuration, IVkApi vkApi)
        {
            this.configuration = configuration;
            this.vkApi = vkApi;
            groupId = long.Parse(configuration["Config:GroupId"]);
            albumId = long.Parse(configuration["Config:AlbumId"]);
        }

        
        [HttpPost]
        public async Task<IActionResult> Callback([FromBody] Updates updates)
        {
            switch (updates.Type)
            {
                case "confirmation":
                    return Ok(configuration["Config:Confirmation"]);

                case "message_new":
                    var msg = Message.FromJson(new VkResponse(updates.Object));

                    if (msg.Text.Contains(groupId.ToString()) || msg.PeerId.Value != 2000000001)
                    {
                        var wallUploadServer = vkApi.Photo.GetWallUploadServer(groupId);
                        var uploadServer = vkApi.Photo.GetUploadServer(albumId, groupId);
                        List<MediaAttachment> attachments = new List<MediaAttachment>();
                        foreach (string url in GetUrls(msg))
                        {
                            string response = await UploadFile(wallUploadServer.UploadUrl, url, "jpg");
                            var wallPhoto = vkApi.Photo.SaveWallPhoto(response, null, (ulong)groupId);
                            attachments.Add(wallPhoto[0]);

                            var albPhoto = await UploadFile(uploadServer.UploadUrl, url, "jpg");
                            vkApi.Photo.Save(new PhotoSaveParams
                            {
                                SaveFileResponse = albPhoto,
                                GroupId = groupId,
                                AlbumId = albumId
                            });
                        }

                        vkApi.Wall.Post(new WallPostParams()
                        {
                            OwnerId = -groupId,
                            FromGroup = true,
                            Attachments = attachments
                        });
                    }
                    break;
            }
            return Ok("ok");
        }
        private async Task<string> UploadFile(string serverUrl, string file, string fileExtension)
        {
            var data = GetBytes(file);

            using (var client = new HttpClient())
            {
                var requestContent = new MultipartFormDataContent();
                var content = new ByteArrayContent(data);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                requestContent.Add(content, "file", $"file.{fileExtension}");

                var response = client.PostAsync(serverUrl, requestContent).Result;
                return Encoding.Default.GetString(await response.Content.ReadAsByteArrayAsync());
            }
        }
        private byte[] GetBytes(string fileUrl)
        {
            using (var webClient = new WebClient())
            {
                return webClient.DownloadData(fileUrl);
            }
        }
        private List<string> GetUrls(Message msg)
        {
            List<string> urls = new List<string>();
            for(int i = 0; i < msg.Attachments.Count; i++)
            {
                if (msg.Attachments[i].Type.Name == "Photo")
                {
                    Photo img = (Photo)msg.Attachments[i].Instance;
                    urls.Add(img.Sizes[img.Sizes.Count - 1].Url.ToString());
                }
            }
            return urls;
        }
    }
}
