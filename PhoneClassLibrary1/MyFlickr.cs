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
            f.TestLoginAsync((ret) => 
                { 
                    testResult = ret;
                    flickrReturned = true;
                });
            await waitForFlickrResult();
            if ((testResult == null) || (testResult.HasError))
                return false;
            return true;
        }

        private static FlickrResult<string> uploadResult;
        private static FlickrResult<PhotoCollection> searchResult;
        private static bool flickrReturned;

        public static async Task Upload()
        {
            try
            {
                Flickr f = MyFlickr.getFlickr();
                IList<string> checkedAlbums = Settings.SelectedAlbums;
                // Apparently even without the where clause GetAvailableMediaSources will only ever return a single media source on Windows Phone
                var sources = MediaSource.GetAvailableMediaSources().Where(s => s.MediaSourceType == MediaSourceType.LocalDevice);
                if (sources.Count() != 1)
                    throw new Exception(sources.Count() + " media sources found.");
                MediaLibrary medLib = new MediaLibrary(sources.First());
                IEnumerable<Picture> pics = medLib.RootPictureAlbum.Albums
                    .Where(a => checkedAlbums.Contains(a.Name)) // Get selected albums
                    .SelectMany(a => a.Pictures)                // Get all pictures from selected albums
                    .Where(p => p.Date > Settings.StartFrom)    // Only pictures more recent than the last upload
                    .OrderBy(p => p.Date);                      // Order by date taken so that we upload the oldest first
                Settings.DebugLog(pics.Count() + " pics taken since " + Settings.StartFrom);
                foreach (Picture p in pics)
                {
                    Settings.DebugLog("Found picture " + p.Name);

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
                    await waitForFlickrResult();
                    if (searchResult.Result.Count > 0)
                    {
                        Settings.DebugLog("Already uploaded, skipping.");
                        continue;
                    }

                    flickrReturned = false;
                    bool isPublic = Settings.Privacy == Settings.ePrivacy.Public;
                    bool isFriends = (Settings.Privacy & Settings.ePrivacy.Friends) > 0;
                    bool isFamily = (Settings.Privacy & Settings.ePrivacy.Family) > 0;
                    string tags = p.Name + ", " + hashTag + ", " + Settings.Tags;
                    f.UploadPictureAsync(p.GetImage(), p.Name, p.Name, "", tags, isPublic, isFamily, isFriends, ContentType.Photo, SafetyLevel.Safe, HiddenFromSearch.Visible, (ret) =>
                        {
                            uploadResult = ret;
                            flickrReturned = true;
                        });
                    await waitForFlickrResult();
                    Settings.StartFrom = p.Date;
                    Settings.LogInfo("Uploaded: " + p.Name);
                }
            }
            catch (Exception ex)
            {
                Settings.DebugLog("Error uploading: " + ex.Message);
            }
        }

        private static async Task waitForFlickrResult()
        {
            const int DELAY_MS = 100;
            int i = 0;
            while (!flickrReturned && (i++ < 60 * 1000 / DELAY_MS)) // time out after 1 minute
                await Task.Delay(DELAY_MS);
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

    }


}
