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

namespace PhoneClassLibrary1
{
    public class MyFlickr
    {

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
            if (!Settings.TokensSet())
                return false;
            try
            {
                Flickr f = getFlickr();
                FoundUser testResult = await f.TestLoginAsync();
                return (testResult != null); // && (!testResult.HasError);
            }
            catch (Exception ex)
            {
                lastError = ex;
                return false;
            }
        }

        const string HIGHRES = "__highres";
        public static async Task Upload()
        {
            Settings.ClearLog();
            Photoset FlickrAlbum = Settings.FlickrAlbum;
            try
            {
                Flickr f = MyFlickr.getFlickr();
                IList<string> checkedAlbums = Settings.SelectedPhoneAlbums;

                List<StorageFile> pics = new List<StorageFile>();
                IReadOnlyList<StorageFolder> albums = await KnownFolders.PicturesLibrary.GetFoldersAsync();
                foreach (StorageFolder album in albums.Where(folder => checkedAlbums.Contains(folder.Name)))
                {
                    IReadOnlyList<StorageFile> files = await album.GetFilesAsync();
                    pics.AddRange(files
                        .Where(file => file.DateCreated > Settings.StartFrom)
                        .GroupBy(file => file.Name.Replace(HIGHRES, string.Empty))
                        .Select(
                            group => group.Where(file => file.Name.Contains(HIGHRES) || group.Count() == 1).ToList()[0]
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
                    if (FlickrAlbum == null)
                    {
                        Settings.DebugLog("No Flickr album set, not adding to album.");
                    }
                    else
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
                }
                Settings.UploadsFailed = 0;
            }
            catch (Exception ex)
            {
                if (Settings.UploadsFailed++ > 5)
                {
                    Settings.UploadsFailed = 0;
                    Settings.Enabled = false;
                    Settings.ErrorLog("Error uploading: " + ex.Message);
                }
                else
                    Settings.DebugLog("Error uploading: " + ex.Message);
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
                //var response = await FlickrResponder.UploadDataAsync(stream, title, new Uri(UploadUrl), parameters);
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
            /*
            var sorted = new SortedList<string, string>();
            foreach (var pair in parameters)
            {
                sorted.Add(pair.Key, pair.Value);
            }
            */ // *
            
            var sb = new StringBuilder();
            foreach (var pair in parameters.OrderBy(p => p.Key))
            {
                sb.Append(pair.Key);
                sb.Append("=");
                sb.Append(UtilityMethods.EscapeOAuthString(pair.Value));
                sb.Append("&");
            }

            sb.Remove(sb.Length - 1, 1);

            var baseString = method + "&" + UtilityMethods.EscapeOAuthString(url) + "&" +
                                UtilityMethods.EscapeOAuthString(sb.ToString());

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
            //public static byte[] CreateUploadData(Stream imageStream, string filename, IDictionary<string, string> parameters, string boundary)
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
                //using (var stream = new MemoryStream())
                //{
                    body.WriteTo(stream);
                    stream.Position = 0;
                    return stream; // .ToArray();
                    //return stream.ToArray();
                //}
            }

            public static string OAuthCalculateAuthHeader(IDictionary<string, string> parameters)
            {
                // Silverlight < 5 doesn't support modification of the Authorization header, so all data must be sent in post body.
//#if SILVERLIGHT
//                return "";
//#else
            var sb = new StringBuilder("OAuth ");
            foreach (var pair in parameters)
            {
                if (pair.Key.StartsWith("oauth"))
                {
                    sb.Append(pair.Key + "=\"" + Uri.EscapeDataString(pair.Value) + "\",");
                }
            }
            return sb.Remove(sb.Length - 1, 1).ToString();
//#endif
            }


            internal static async Task<string> UploadDataHttpWebRequestAsync(string url, Stream dataBuffer, string contentTypeHeader, string authHeader)
            //internal static async Task<string> UploadDataAsync(Stream imageStream, string fileName, Uri uploadUri, Dictionary<string, string> parameters)
            {
                /*
                var boundary = "FLICKR_MIME_" + DateTime.Now.ToString("yyyyMMddhhmmss", System.Globalization.DateTimeFormatInfo.InvariantInfo);

                var authHeader = FlickrResponder.OAuthCalculateAuthHeader(parameters);
                var dataBuffer = CreateUploadData(imageStream, fileName, parameters, boundary);
                */

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                //if (Proxy != null) 
                  //  req.Proxy = Proxy;
                //req.Timeout = HttpTimeout;                
                //req.ContentType = "multipart/form-data; boundary=" + boundary;
                req.ContentType = contentTypeHeader;
                req.AllowWriteStreamBuffering = false;

                if (!string.IsNullOrEmpty(authHeader))
                {
                    req.Headers["Authorization"] = authHeader;
                }

                req.ContentLength = dataBuffer.Length;

                using (var reqStream = await req.GetRequestStreamAsync())
                //using (var reqStream = req.GetRequestStream())
                {
                    var bufferSize = 32 * 1024;
                    if (dataBuffer.Length / 100 > bufferSize) 
                        bufferSize = bufferSize * 2;
                    // dataBuffer.UploadProgress += (o, e) => { if (OnUploadProgress != null) OnUploadProgress(this, e); };
                    dataBuffer.CopyTo(reqStream, bufferSize);
                    reqStream.Flush();
                }

                //var res = (HttpWebResponse)req.GetResponse();
                var res = (HttpWebResponse) await req.GetResponseAsync();
                var stream = res.GetResponseStream();
                if (stream == null) throw new FlickrWebException("Unable to retrieve stream from web response.");

                var sr = new StreamReader(stream);
                var s = sr.ReadToEnd();
                sr.Close();
                return s;
            }

            internal static async Task<string> UploadDataHttpClientAsync(string url, Stream data, string contentTypeHeader, string oauthHeader)
            //internal static async Task<string> UploadDataAsync(string url, byte[] data, string contentTypeHeader, string oauthHeader)
            {
                var client = new HttpClient();

                if (!String.IsNullOrEmpty(oauthHeader)) 
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", oauthHeader.Replace("OAuth ", ""));

                //var content = new ByteArrayContent(data);
                //data.Position = 0;
                var content = new StreamContent(data);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentTypeHeader);
                var response = await client.PostAsync(new Uri(url), content);
                return await response.Content.ReadAsStringAsync();
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
            //public byte[] Content { get; private set; }
            public Stream Content { get; private set; }

            public void LoadContent(Stream stream)
            {
                //Content = new byte[stream.Length];
                //stream.Read(Content, 0, Content.Length);
                //Content = new FileStream(Path.GetTempFileName(), FileMode.Create);
                //stream.Position = 0;
                //stream.CopyTo(Content);
                Content = stream;
            }

            public override void WriteTo(Stream stream)
            {
                var sw = new StreamWriter(stream);
                sw.WriteLine("Content-Disposition: form-data; name=\"" + Name + "\"; filename=\"" + Filename + "\"");
                sw.WriteLine("Content-Type: " + ContentType);
                sw.WriteLine();
                sw.Flush();

                //stream.Write(Content, 0, Content.Length);
                Content.Position = 0;
                Content.CopyTo(stream);

                sw.WriteLine();
                sw.Flush();
            }
        }
// */
    }

}
