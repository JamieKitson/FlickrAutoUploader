﻿using FlickrNet;
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
                foreach(StorageFolder album in albums.Where(folder => checkedAlbums.Contains(folder.Name)))
                {
                    IReadOnlyList<StorageFile> files = await album.GetFilesAsync();
                    pics.AddRange(files.Where(file => file.DateCreated > Settings.StartFrom));
                }

                Settings.DebugLog(pics.Count() + " pics taken since " + Settings.StartFrom);
                foreach (StorageFile p in pics.OrderBy(file => file.DateCreated))
                {
                    Settings.DebugLog("Found picture " + p.Name);
                    // MD5Managed hash = new MD5Managed();
                    SHA1Managed hash = new SHA1Managed();
                    MemoryStream stream = new MemoryStream();
                    await RandomAccessStream.CopyAsync(await p.OpenSequentialReadAsync(), stream.AsOutputStream());
                    stream.Seek(0, 0);
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
                        PhotoID = await f.UploadPictureAsync(stream, p.Name, p.Name, string.Empty, tags, isPublic, isFamily, isFriends, ct, SafetyLevel.Safe, HiddenFromSearch.Visible);
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

    }


}
