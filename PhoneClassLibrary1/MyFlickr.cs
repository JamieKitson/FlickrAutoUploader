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
using System.Windows.Shapes;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Phone.Shell;

namespace PhoneClassLibrary1
{
    public class MyFlickr
    {

        public const string ALBUMS = "albums";
        public const string TOKEN = "token";
        public const string SECRET = "secret";
        public const string UPLOAD_ALL = "uploadAll";
        
        public static Flickr getFlickr()
        {
            Flickr f = new Flickr(Secrets.apiKey, Secrets.apiSecret);
            f.OAuthAccessToken = Settings.OAuthAccessToken;
            f.OAuthAccessTokenSecret = Settings.OAuthAccessTokenSecret;
            return f;
        }

        private static FlickrResult<FoundUser> testResult;

        public static async Task<bool> Test()
        {
            if (!Settings.TokensSet())
                return false;
            Flickr f = getFlickr();
            flickrReturned = false;
            testResult = null;
            f.TestLoginAsync((ret) => { 
                testResult = ret;
                flickrReturned = true;
            });
            await waitForFlickrResult();
            if ((testResult == null) || (testResult.HasError))
            {
                Settings.OAuthAccessToken = "";
                Settings.OAuthAccessTokenSecret = "";
                Settings.Enabled = false;
                return false;
            }
            return true;
        }

        public static async Task Upload()
        {
            try
            {
                Flickr f = MyFlickr.getFlickr();
                List<string> checkedAlbums = getCheckedAlbums();

                var x = from s in MediaSource.GetAvailableMediaSources() where (s.MediaSourceType == MediaSourceType.LocalDevice) select s;

                foreach (MediaSource source in x)
                {
                    MediaLibrary medLib = new MediaLibrary(source);
                    var albums = from a in medLib.RootPictureAlbum.Albums where checkedAlbums.Contains(a.Name) select a;
                    foreach (PictureAlbum album in albums)
                    {
                        var pics = from p in album.Pictures where p.Date > Settings.StartFrom orderby p.Date ascending select p;
                        foreach (Picture p in pics)
                        {
                            //HMACSHA1 hash = new HMACSHA1();
                            SHA1Managed hash = new SHA1Managed();
                            hash.ComputeHash(p.GetImage());
                            string hashTag = "checksum:sha1=" + BitConverter.ToString(hash.Hash).Replace("-", "");

                            PhotoSearchOptions so = new PhotoSearchOptions("me", hashTag);
                            flickrReturned = false;
                            f.PhotosSearchAsync(so, (ret) =>
                            {
                                searchResult = ret;
                                flickrReturned = true;
                            });
                            await waitForFlickrResult(/*ResultTypes.rtSearch*/);
                            if (searchResult.Result.Count > 0)
                                continue;

                            //FlickrResult<string> UploadResult = null;
                            flickrReturned = false;
                            f.UploadPictureAsync(p.GetImage(), p.Name, p.Name, "", p.Name + ",wp8flickrautouploader," + hashTag, false, false, false, ContentType.Photo, SafetyLevel.Safe, HiddenFromSearch.Visible, (ret) =>
                            {
                                uploadResult = ret;
                                flickrReturned = true;
                            });
                            await waitForFlickrResult(/*ResultTypes.rtUpload*/);
                            Settings.StartFrom = p.Date;

                            ShellToast toast = new ShellToast();
                            toast.Title = "Flickr Auto Uploader";
                            toast.Content = "Uploaded: " + p.Name;
                            toast.Show();

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //TextBox1.Text = ex.Message;
            }
        }

        private static FlickrResult<string> uploadResult;
        private static FlickrResult<PhotoCollection> searchResult;
        //FlickrResult<object>[] flickrResults = new FlickrResult<object>[2];
        private static bool flickrReturned;

        private static async Task waitForFlickrResult(/*ResultTypes rt*/)
        {
            const int DELAY_MS = 100;
            int i = 0;
            while (!flickrReturned && (i++ < 60 * 1000 / DELAY_MS)) // time out after 1 minute
                await Task.Delay(DELAY_MS);
            //Thread.Sleep(500);
            if (!flickrReturned)
                throw new Exception("Timeedout");
        }

        private static void checkResult<T>(FlickrResult<T> res)
        {
            if (res.HasError)
                throw new Exception("Error: " + res.ErrorMessage);
            //else
                //TextBox1.Text = "Success";
        }

        private static List<string> getCheckedAlbums()
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            List<string> checkedAlbums;
            if (s.Contains(MyFlickr.ALBUMS))
            {
                checkedAlbums = (List<string>)s[MyFlickr.ALBUMS];
            }
            else
            {
                checkedAlbums = new List<string>();
                checkedAlbums.Add("Camera Roll");
            }
            return checkedAlbums;
        }

    }


}
