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

        public static FlickrResult<FoundUser> testResult;

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
            return (testResult != null) && (!testResult.HasError);
        }

        private static FlickrResult<string> uploadResult;
        private static FlickrResult<PhotoCollection> searchResult;
        private static bool flickrReturned;

        public static async Task Upload()
        {
            Settings.ClearLog();
            Photoset FlickrAlbum = Settings.FlickrAlbum;
            try
            {
                Flickr f = MyFlickr.getFlickr();
                IList<string> checkedAlbums = Settings.SelectedPhoneAlbums;
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
                    // MD5Managed hash = new MD5Managed();
                    SHA1Managed hash = new SHA1Managed();
                    hash.ComputeHash(p.GetImage());
                    // string hashTag = "file:md5sum=" + 
                    string hashTag = "file:sha1sig=" + BitConverter.ToString(hash.Hash).Replace("-", string.Empty);
                    string filenameTag = "file:name=" + p.Name;
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

                    bool isPublic = Settings.Privacy == Settings.ePrivacy.Public;
                    bool isFriends = (Settings.Privacy & Settings.ePrivacy.Friends) > 0;
                    bool isFamily = (Settings.Privacy & Settings.ePrivacy.Family) > 0;
                    string album = p.Album.Name;
                    string tags = string.Join(", ", new string[] { filenameTag, hashTag, Settings.Tags, "\"" + album + "\"" });
                    ContentType ct = album == "Screenshots" ? ContentType.Screenshot : ContentType.Photo;
                    flickrReturned = false;
                    f.UploadPictureAsync(p.GetImage(), p.Name, p.Name, string.Empty, tags, isPublic, isFamily, isFriends, ct, SafetyLevel.Safe, HiddenFromSearch.Visible, (ret) =>
                        {
                            uploadResult = ret;
                            flickrReturned = true;
                        });
                    await waitForFlickrResult();
                    if (uploadResult.HasError)
                        throw new Exception(uploadResult.ErrorMessage);
                    if (FlickrAlbum != null)
                    {
                        Settings.DebugLog("Uploading to Flickr album " + FlickrAlbum.Title);
                        flickrReturned = false;
                        f.PhotosetsAddPhotoAsync(FlickrAlbum.PhotosetId, uploadResult.Result, ret =>
                            {
                                flickrReturned = true;
                            });
                        await waitForFlickrResult();
                    }
                    else
                        Settings.DebugLog("No Flickr album set, not adding to album.");
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

        private static FlickrResult<PhotosetCollection> AlbumListResult;
        public static async Task<Dictionary<string, string>> GetAlbums()
        {
            Flickr f = MyFlickr.getFlickr();
            flickrReturned = false;
            AlbumListResult = null;
            f.PhotosetsGetListAsync((ret) =>
            {
                flickrReturned = true;
                AlbumListResult = ret;
            });
            await waitForFlickrResult();
            Dictionary<string, string> res = new Dictionary<string, string>();
            if (AlbumListResult.HasError)
                return res;
            AlbumListResult.Result.ToList().ForEach(album => res.Add(album.PhotosetId, album.Title));
            return res;
        }

    }


}
