using FlickrNet;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Phone.Shell;
using Windows.Storage;
using Windows.Storage.Streams;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Text;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Phone;

namespace PhoneClassLibrary1
{
    public class MyFlickr
    {

        public const string RIT_NAME = "FlickrAutoUploader";

        public static Flickr getFlickr()
        {
            Flickr f = new Flickr(Secrets.apiKey, Secrets.apiSecret);
            f.OAuthAccessToken = Settings.OAuthAccessToken;
            f.OAuthAccessTokenSecret = Settings.OAuthAccessTokenSecret;
            return f;
        }

        public static Exception lastError;

        public static async Task<bool> Test()
        {
            lastError = null;
            if (!Settings.TokensSet())
                return false;
            try
            {
                Flickr f = getFlickr();
                FoundUser testResult = await f.TestLoginAsync();
                return (testResult != null);
            }
            catch (Exception ex)
            {
                lastError = ex;
                return false;
            }
        }

        public static async Task Upload()
        {
            Settings.ClearLog();
            // Delete any lingering temporary files
            Directory.GetFiles(Path.GetDirectoryName(Path.GetTempFileName())).ToList().ForEach(file =>
            {
                try { File.Delete(file); }
                catch { Settings.DebugLog("Failed to delete temporary file " + file); }
            });

            Flickr f = MyFlickr.getFlickr();
            IList<string> checkedAlbums = Settings.SelectedPhoneAlbums;
            Settings.DebugLog("Checked albums: " + string.Join(", ", checkedAlbums));
            const string HIGHRES = "__highres";
            const string UPLOADABLE_IMAGE_EXTS = ".jpg .png .gif .tiff";

            // Cache setting values
            DateTime StartFrom = Settings.StartFrom;
            bool UploadVideos = Settings.UploadVideos;
            bool UploadHiRes = Settings.UploadHiRes;
            Settings.DebugLog("Uploading videos: " + UploadVideos + ", uploading high res: " + UploadHiRes);
            Photoset FlickrAlbum = Settings.FlickrAlbum;
            Settings.DebugLog("Flickr album to upload to: " + (FlickrAlbum == null ? "null" : FlickrAlbum.Title + " " + FlickrAlbum.PhotosetId));

            List<StorageFile> pics = new List<StorageFile>();
            IReadOnlyList<StorageFolder> albums = await KnownFolders.PicturesLibrary.GetFoldersAsync();
            foreach (StorageFolder album in albums.Where(folder => checkedAlbums.Contains(folder.Name)))
            {
                Settings.DebugLog("Found album: " + album.Name);
                IReadOnlyList<StorageFile> files = await album.GetFilesAsync();
                pics.AddRange(files
                    .Where(file =>
                        {
                            string ext = Path.GetExtension(file.Name).ToLower();
                            // Get files more recent than the last uploaded, don't get DNG files, don't get videos unless we're uploading videos
                            return (file.DateCreated > StartFrom) && (UPLOADABLE_IMAGE_EXTS.Contains(ext) || (ext == ".mp4" && UploadVideos));
                        })
                    .GroupBy(
                    // Group high/low res twins together. We need to group by name in case some photos are missing one of the hi/low res pair
                        file => file.Name.Replace(HIGHRES, string.Empty))
                    .Select(
                    // If there's only one file always use that one, otherwise select correct resolution dending on setting
                        group => group.Where(file => (group.Count() == 1) || (file.Name.Contains(HIGHRES) == UploadHiRes)).ToList()[0]
                    ));
            }

            Settings.DebugLog(pics.Count() + " pics taken since " + Settings.StartFrom);
            foreach (StorageFile p in pics.OrderBy(file => file.DateCreated))
            {
                Settings.DebugLog("Found picture " + p.Name);
                Stream stream = await p.OpenStreamForReadAsync();
                // MD5Managed hash = new MD5Managed();
                SHA1Managed hash = new SHA1Managed();
                hash.ComputeHash(stream);
                // string hashTag = "file:md5sum=" + 
                string hashTag = "file:sha1sig=" + BitConverter.ToString(hash.Hash).Replace("-", string.Empty);
                string filenameTag = "file:name=" + p.Name;
                PhotoSearchOptions so = new PhotoSearchOptions("me", hashTag);
                PhotoCollection searchResult = await f.PhotosSearchAsync(so);
                string PhotoID;
                if (searchResult.Count > 0)
                {
                    PhotoID = searchResult[0].PhotoId;
                    Settings.DebugLog("Already uploaded, skipping. PhotoID: " + PhotoID);
                }
                else
                {
                    Settings.DebugLog("Uploading...");
                    bool isPublic = Settings.Privacy == Settings.ePrivacy.Public;
                    bool isFriends = (Settings.Privacy & Settings.ePrivacy.Friends) > 0;
                    bool isFamily = (Settings.Privacy & Settings.ePrivacy.Family) > 0;
                    string album = Path.GetFileName(Path.GetDirectoryName(p.Path));
                    string tags = string.Join(", ", new string[] { filenameTag, hashTag, Settings.Tags, "\"" + album + "\"" });
                    ContentType ct = album == "Screenshots" ? ContentType.Screenshot : ContentType.Photo;
                    stream.Seek(0, 0);
                    PhotoID = await UploadPictureAsync(stream, p.Name, p.Name, string.Empty, tags, isPublic, isFamily, isFriends, ct, SafetyLevel.Safe, HiddenFromSearch.Visible);
                    Settings.LogInfo("Uploaded: " + p.Name + " FlickrID: " + PhotoID);
                }
                if (FlickrAlbum != null)
                {
                    Settings.DebugLog("Adding to Flickr album " + FlickrAlbum.Title);
                    try
                    {
                        await f.PhotosetsAddPhotoAsync(FlickrAlbum.PhotosetId, PhotoID);
                    }
                    catch (FlickrApiException ex)
                    {
                        if (ex.Code != 3) // Photo already in set
                            throw;
                    }
                }
                Settings.StartFrom = p.DateCreated.DateTime;
                Settings.UploadsFailed = 0;
            }
        }

        // The following code is taken from versions 3 and 4 of FlickrNet in mid 2015 and adapted for the low memory limit of WP background tasks to use FileStreams and HttpWebRequest.AllowWriteStreamBuffering = false
        
        private const string UploadUrl = "https://up.flickr.com/services/upload/";
        public static async Task<string> UploadPictureAsync(Stream stream, string filename, string title, string description, string tags, bool isPublic, bool isFamily, bool isFriend, ContentType contentType, SafetyLevel safetyLevel, HiddenFromSearch hiddenFromSearch)
        {
            var parameters = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(title))
            {
                parameters.Add("title", title);
            }
            if (!string.IsNullOrEmpty(description))
            {
                parameters.Add("description", description);
            }
            if (!string.IsNullOrEmpty(tags))
            {
                parameters.Add("tags", tags);
            }

            parameters.Add("is_public", isPublic ? "1" : "0");
            parameters.Add("is_friend", isFriend ? "1" : "0");
            parameters.Add("is_family", isFamily ? "1" : "0");

            if (safetyLevel != SafetyLevel.None)
            {
                parameters.Add("safety_level", safetyLevel.ToString("D"));
            }
            if (contentType != ContentType.None)
            {
                parameters.Add("content_type", contentType.ToString("D"));
            }
            if (hiddenFromSearch != HiddenFromSearch.None)
            {
                parameters.Add("hidden", hiddenFromSearch.ToString("D"));
            }

            FlickrResponder.OAuthGetBasicParameters(parameters);
            parameters.Add("oauth_consumer_key", Secrets.apiKey);
            parameters.Add("oauth_token", Settings.OAuthAccessToken);
            parameters.Add("oauth_signature", OAuthCalculateSignature("POST", UploadUrl, parameters, Settings.OAuthAccessTokenSecret));

            string boundary = FlickrResponder.CreateBoundary();

            string oauthHeader = FlickrResponder.OAuthCalculateAuthHeader(parameters);
            string contentTypeHeader = "multipart/form-data; boundary=" + boundary;

            string response;
            FileStream data = FlickrResponder.CreateUploadData(stream, filename, parameters, boundary);
            try
            {
                response = await FlickrResponder.UploadDataHttpWebRequestAsync(UploadUrl, data, contentTypeHeader, oauthHeader);
            }
            finally
            {
                data.Close();
                File.Delete(data.Name);
                data.Dispose();
            }

            Match match = Regex.Match(response, "<photoid>(\\d+)</photoid>");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            using (var reader = XmlReader.Create(new StringReader(response), new XmlReaderSettings { IgnoreWhitespace = true }))
            {
                if (!reader.ReadToDescendant("rsp"))
                {
                    throw new XmlException("Unable to find response element 'rsp' in Flickr response");
                }
                while (reader.MoveToNextAttribute())
                {
                    if (reader.LocalName == "stat" && reader.Value == "fail")
                        throw ExceptionHandler.CreateResponseException(reader);
                }
            }
            throw new FlickrException("Unable to determine photo id from upload response: " + response);
        }

        internal static string OAuthCalculateSignature(string method, string url, IDictionary<string, string> parameters, string tokenSecret)
        {
            var key = Secrets.apiSecret + "&" + tokenSecret;
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var sb = new StringBuilder();
            foreach (var pair in parameters.OrderBy(p => p.Key))
            {
                sb.Append(pair.Key);
                sb.Append("=");
                sb.Append(UtilityMethods.EscapeOAuthString(pair.Value));
                sb.Append("&");
            }
            sb.Remove(sb.Length - 1, 1);
            var baseString = method + "&" + UtilityMethods.EscapeOAuthString(url) + "&" + UtilityMethods.EscapeOAuthString(sb.ToString());
            var hash = Sha1Hash(keyBytes, baseString);
            return hash;
        }

        internal static string Sha1Hash(byte[] key, string basestring)
        {
            var sha1 = new System.Security.Cryptography.HMACSHA1(key);

            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(basestring));

            return Convert.ToBase64String(hashBytes);
        }

        internal static partial class FlickrResponder
        {
            public static void OAuthGetBasicParameters(IDictionary<string, string> parameters)
            {
                var oAuthParameters = OAuthGetBasicParameters();
                foreach (var k in oAuthParameters)
                {
                    parameters.Add(k.Key, k.Value);
                }
            }

            private static IEnumerable<KeyValuePair<string, string>> OAuthGetBasicParameters()
            {
                var oauthtimestamp = UtilityMethods.DateToUnixTimestamp(DateTime.UtcNow);
                var oauthnonce = Guid.NewGuid().ToString("N");

                var parameters = new Dictionary<string, string>
                                 {
                                     {"oauth_nonce", oauthnonce},
                                     {"oauth_timestamp", oauthtimestamp},
                                     {"oauth_version", "1.0"},
                                     {"oauth_signature_method", "HMAC-SHA1"}
                                 };
                return parameters;
            }

            public static string CreateBoundary()
            {
                return "----FLICKR_MIME_" + DateTime.Now.ToString("yyyyMMddhhmmss", DateTimeFormatInfo.InvariantInfo) + "--";
            }

            public static FileStream CreateUploadData(Stream imageStream, string filename, IDictionary<string, string> parameters, string boundary)
            {
                var body = new MimeBody
                {
                    Boundary = boundary,
                    MimeParts = parameters
                    .Where(p => !p.Key.StartsWith("oauth_"))
                    .Select(p => (MimePart)new FormDataPart { Name = p.Key, Value = p.Value }).ToList()
                };

                var binaryPart = new BinaryPart
                {
                    Name = "photo",
                    ContentType = "image/jpeg",
                    Filename = filename
                };
                binaryPart.LoadContent(imageStream);
                body.MimeParts.Add(binaryPart);

                var stream = new FileStream(Path.GetTempFileName(), FileMode.Create);
                body.WriteTo(stream);
                stream.Position = 0;
                return stream;
            }

            public static string OAuthCalculateAuthHeader(IDictionary<string, string> parameters)
            {
                var sb = new StringBuilder("OAuth ");
                foreach (var pair in parameters)
                {
                    if (pair.Key.StartsWith("oauth"))
                    {
                        sb.Append(pair.Key + "=\"" + Uri.EscapeDataString(pair.Value) + "\",");
                    }
                }
                return sb.Remove(sb.Length - 1, 1).ToString();
            }


            internal static async Task<string> UploadDataHttpWebRequestAsync(string url, Stream dataBuffer, string contentTypeHeader, string authHeader)
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = contentTypeHeader;
                req.AllowWriteStreamBuffering = false;
                req.ContentLength = dataBuffer.Length;

                if (!string.IsNullOrEmpty(authHeader))
                    req.Headers["Authorization"] = authHeader;

                using (var reqStream = await req.GetRequestStreamAsync())
                {
                    var bufferSize = 32 * 1024;
                    if (dataBuffer.Length / 100 > bufferSize) 
                        bufferSize = bufferSize * 2;
                    dataBuffer.CopyTo(reqStream, bufferSize);
                    reqStream.Flush();
                }

                var res = (HttpWebResponse) await req.GetResponseAsync();
                var stream = res.GetResponseStream();
                if (stream == null) 
                    throw new FlickrWebException("Unable to retrieve stream from web response.");

                var sr = new StreamReader(stream);
                var s = sr.ReadToEnd();
                sr.Close();
                return s;
            }
        }

        internal class MimeBody : MimePart
        {
            public string Boundary { get; set; }
            public List<MimePart> MimeParts { get; set; }

            public override void WriteTo(Stream stream)
            {
                var boundaryBytes = Encoding.UTF8.GetBytes("--" + Boundary + "\r\n");

                foreach (var part in MimeParts)
                {
                    stream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    part.WriteTo(stream);
                }

                var endBoundaryBytes = Encoding.UTF8.GetBytes("--" + Boundary + "--\r\n");
                stream.Write(endBoundaryBytes, 0, endBoundaryBytes.Length);
            }
        }

        internal abstract class MimePart
        {
            public abstract void WriteTo(Stream stream);
        }

        internal class FormDataPart : MimePart
        {
            public string Name { get; set; }
            public string Value { get; set; }

            public override void WriteTo(Stream stream)
            {
                var sw = new StreamWriter(stream);
                sw.WriteLine("Content-Disposition: form-data; name=\"" + Name + "\"");
                sw.WriteLine();
                sw.WriteLine(Value);
                sw.Flush();
            }
        }

        internal class BinaryPart : MimePart
        {
            public string Name { get; set; }
            public string Filename { get; set; }
            public string ContentType { get; set; }
            public Stream Content { get; private set; }

            public void LoadContent(Stream stream)
            {
                Content = stream;
            }

            public override void WriteTo(Stream stream)
            {
                var sw = new StreamWriter(stream);
                sw.WriteLine("Content-Disposition: form-data; name=\"" + Name + "\"; filename=\"" + Filename + "\"");
                sw.WriteLine("Content-Type: " + ContentType);
                sw.WriteLine();
                sw.Flush();

                Content.Position = 0;
                Content.CopyTo(stream);

                sw.WriteLine();
                sw.Flush();
            }
        }
    }

}
