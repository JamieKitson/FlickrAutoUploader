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
                    FlickrResult<PhotoCollection> searchResult = null;
                    f.PhotosSearchAsync(so, (ret) =>
                        {
                            searchResult = ret;
                            flickrReturned = true;
                        });
                    await waitForFlickrResult();
                    checkResult(searchResult);
                    string PhotoID;
                    if (searchResult.Result.Count > 0)
                    {
                        PhotoID = searchResult.Result[0].PhotoId;
                        Settings.DebugLog("Already uploaded, skipping.");
                    }
                    else
                    {
                        Settings.DebugLog("Uploading...");
                        bool isPublic = Settings.Privacy == Settings.ePrivacy.Public;
                        bool isFriends = (Settings.Privacy & Settings.ePrivacy.Friends) > 0;
                        bool isFamily = (Settings.Privacy & Settings.ePrivacy.Family) > 0;
                        string album = p.Album.Name;
                        string tags = string.Join(", ", new string[] { filenameTag, hashTag, Settings.Tags, "\"" + album + "\"" });
                        ContentType ct = album == "Screenshots" ? ContentType.Screenshot : ContentType.Photo;
                        flickrReturned = false;
                        FlickrResult<string> uploadResult = null;
                        f.UploadPictureAsync(p.GetImage(), p.Name, p.Name, string.Empty, tags, isPublic, isFamily, isFriends, ct, SafetyLevel.Safe, HiddenFromSearch.Visible, (ret) =>
                            {
                                uploadResult = ret;
                                flickrReturned = true;
                            });
                        await waitForFlickrResult();
                        checkResult(uploadResult);
                        if (uploadResult.HasError)
                            throw new Exception(uploadResult.ErrorMessage);
                        PhotoID = uploadResult.Result;
                        Settings.LogInfo("Uploaded: " + p.Name);
                    }
                    if (FlickrAlbum == null)
                    {
                        Settings.DebugLog("No Flickr album set, not adding to album.");
                    }
                    else
                    {
                        Settings.DebugLog("Adding to Flickr album " + FlickrAlbum.Title);
                        flickrReturned = false;
                        FlickrResult<NoResponse> AddToSetResult = null;
                        f.PhotosetsAddPhotoAsync(FlickrAlbum.PhotosetId, PhotoID, ret =>
                            {
                                AddToSetResult = ret;
                                flickrReturned = true;
                            });
                        await waitForFlickrResult();
                        checkResult(AddToSetResult);
                    }
                    Settings.StartFrom = p.Date;
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

        private static async Task waitForFlickrResult()
        {
            const int DELAY_MS = 100;
            int i = 0;
            while (!flickrReturned && (i++ < 60 * 1000 / DELAY_MS)) // time out after 1 minute
                await Task.Delay(DELAY_MS);
            if (!flickrReturned)
                throw new Exception("Timedout");
        }

        private static void checkResult<T>(FlickrResult<T> res)
        {
            if (res == null)
                throw new Exception("Flickr call returned null.");
            if (res.HasError)
            {
                if ((res.ErrorCode == 3) && (res.ErrorMessage == "Photo already in set"))
                    return;
                if (!string.IsNullOrEmpty(res.ErrorMessage))
                    throw new Exception(res.ErrorMessage);
                else if (res.Error != null)
                    throw new Exception(res.Error.Message);
            }
        }

    }


}
