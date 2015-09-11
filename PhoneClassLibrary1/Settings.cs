using FlickrNet;
using Microsoft.Phone.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace PhoneClassLibrary1
{
    public class Settings
    {

        private static T GetSetting<T>(string name, T defVal)
        {
            Mutex mutexFile = new Mutex(false, name);
            mutexFile.WaitOne();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (store.FileExists(name))
                    {
                        XmlSerializer x = new XmlSerializer(typeof(T));
                        using (var file = store.OpenFile(name, FileMode.Open))
                            return (T)x.Deserialize(file);
                    }
                }
            }
            finally
            {
                mutexFile.ReleaseMutex();
            }
            return defVal;
        }

        private static void SetSetting<T>(string name, T val)
        {
            Mutex mutexFile = new Mutex(false, name);
            mutexFile.WaitOne();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    XmlSerializer x = new XmlSerializer(typeof(T));
                    using (var file = store.OpenFile(name, FileMode.OpenOrCreate))
                    {
                        file.SetLength(0);
                        x.Serialize(file, val);
                    }
                }
            }
            finally
            {
                mutexFile.ReleaseMutex();
            }
        }

        private static IList<string> GetSettingList(string name, string defVal)
        {
            var ol = GetSetting(name, new ObservableCollection<string>(new string[] { defVal }));
            ol.CollectionChanged += delegate(object sender, NotifyCollectionChangedEventArgs e) { SetSetting(name, ol); };
            return ol;
        }

        private const string TOKEN = "token";
        public static string OAuthAccessToken
        {
            get { return GetSetting(TOKEN, ""); }
            set { SetSetting(TOKEN, value); }
        }

        private const string SECRET = "secret";
        public static string OAuthAccessTokenSecret
        {
            get
            {
                try
                {
                    byte[] ProtectedSecretByte = GetSetting<byte[]>(SECRET, null);
                    if (ProtectedSecretByte == null) // This means it's never been set
                        return string.Empty;
                    byte[] SecretByte = ProtectedData.Unprotect(ProtectedSecretByte, null);
                    return Encoding.UTF8.GetString(SecretByte, 0, SecretByte.Length);
                }
                catch // Assume this exception means that we have a previously saved unprotected secret
                {
                    string s = GetSetting(SECRET, "");
                    OAuthAccessTokenSecret = s; // Encrypt the secret
                    return s;
                }
            }
            set
            {
                byte[] SecretByte = Encoding.UTF8.GetBytes(value);
                byte[] ProtectedSecretByte = ProtectedData.Protect(SecretByte, null);
                SetSetting(SECRET, ProtectedSecretByte);
            }
        }

        public static bool TokensSet()
        {
            return !string.IsNullOrEmpty(OAuthAccessToken + OAuthAccessTokenSecret);
        }

        public static void UnsetTokens()
        {
            OAuthAccessToken = "";
            OAuthAccessTokenSecret = "";
        }

        private const string ALBUMS = "albums";
        public static IList<string> SelectedPhoneAlbums
        {
            get { return GetSettingList(ALBUMS, "Camera Roll"); }
            set { SetSetting(ALBUMS, value); }
        }

        private const string START_FROM = "startfrom";
        public static DateTime StartFrom
        {
            get { return GetSetting(START_FROM, DateTime.Now.Date); }
            set { SetSetting(START_FROM, value); }
        }

        private static void DoLog(string msg, int level)
        {
            LogLine(DateTime.Now.ToString("s") + " " + level + " " + msg);
            if (level <= LogLevel)
                ToastMessage(msg);
        }

        public static void ErrorLog(string msg)
        {
            DoLog(msg, 0);
        }

        public static void LogInfo(string msg)
        {
            DoLog(msg, 1);
        }

        public static void DebugLog(string msg)
        {
            DoLog(msg, 2);
        }

        private static void ToastMessage(string msg)
        {
            ShellToast toast = new ShellToast();
            toast.Title = "Flickr Auto Uploader";
            toast.Content = msg;
            toast.Show();
        }

        public enum ePrivacy { Private, Friends, Family, FriendsFamily, Public };
        const string PRIVACY = "privacy";
        public static ePrivacy Privacy
        {
            get { return GetSetting(PRIVACY, ePrivacy.Private); }
            set { SetSetting(PRIVACY, value); }
        }

        private const string TAGS = "tags";
        public static string Tags
        {
            get { return GetSetting(TAGS, "wpautouploader"); }
            set { SetSetting(TAGS, value); }
        }

        private const string TESTS_FAILED = "testsfailed";
        public static int TestsFailed
        {
            get { return GetSetting(TESTS_FAILED, 0); }
            set { SetSetting(TESTS_FAILED, value); }
        }

        private const string LOG_LEVEL = "loglevel";
        public static double LogLevel
        {
            get { return GetSetting<double>(LOG_LEVEL, 0); }
            set { SetSetting(LOG_LEVEL, value); }
        }

        private const string LOG = "log";

        private static void LogLine(string msg)
        {
            Mutex mutexFile = new Mutex(false, LOG);
            mutexFile.WaitOne();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                using (var file = store.OpenFile(LOG, FileMode.Append))
                using (StreamWriter writer = new StreamWriter(file))
                    writer.WriteLine(msg);
            }
            finally
            {
                mutexFile.ReleaseMutex();
            }
        }

        public static void ClearLog()
        {
            Mutex mutexFile = new Mutex(false, LOG);
            mutexFile.WaitOne();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    string log;
                    using (var file = store.OpenFile(LOG, FileMode.OpenOrCreate))
                    using (var reader = new System.IO.StreamReader(file))
                        log = reader.ReadToEnd();

                    List<string> lines = log.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    bool removed = false;
                    while (lines.Count > 0)
                    {
                        string[] ss = lines[0].Split(new char[] { ' ' }, 2);
                        if (ss.Count() == 2)
                        {
                            DateTime dt;
                            if (DateTime.TryParse(ss[0], out dt) && (DateTime.Now - dt < new TimeSpan(24, 0, 0)))
                                break;
                        }
                        lines.RemoveAt(0);
                        removed = true;
                    }
                    if (removed)
                    {
                        using (var file = store.OpenFile(LOG, FileMode.OpenOrCreate))
                        {
                            file.SetLength(0);
                            using (StreamWriter writer = new StreamWriter(file))
                                writer.Write(string.Join(Environment.NewLine, lines));
                        }
                    }
                }
            }
            finally
            {
                mutexFile.ReleaseMutex();
            }
        }

        public static string GetLog()
        {
            Mutex mutexFile = new Mutex(false, LOG);
            mutexFile.WaitOne();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                using (var file = store.OpenFile(LOG, FileMode.OpenOrCreate))
                using (var reader = new System.IO.StreamReader(file))
                    return reader.ReadToEnd();
            }
            finally
            {
                mutexFile.ReleaseMutex();
            }
        }

        private const string FLICKR_ALBUM = "flickralbum";
        public static Photoset FlickrAlbum
        {
            get { return GetSetting<Photoset>(FLICKR_ALBUM, null); }
            set { SetSetting(FLICKR_ALBUM, value); }
        }

        private const string UPLOADS_FAILED = "uploadsfailed";
        public static int UploadsFailed
        {
            get { return GetSetting(UPLOADS_FAILED, 0); }
            set { SetSetting(UPLOADS_FAILED, value); }
        }

        private const string UPLOAD_VIDEOS = "uploadvideos";
        public static bool UploadVideos
        {
            get { return GetSetting(UPLOAD_VIDEOS, true); }
            set { SetSetting(UPLOAD_VIDEOS, value); }
        }

        private const string UPLOAD_HI_RES = "uploadhires";
        public static bool UploadHiRes
        {
            get { return GetSetting(UPLOAD_HI_RES, true); }
            set { SetSetting(UPLOAD_HI_RES, value); }
        }

        private const string LAST_SUCCESSFUL_RUN = "lastsuccessfulrun";
        public static DateTime LastSuccessfulRun
        {
            get { return GetSetting(LAST_SUCCESSFUL_RUN, new DateTime(0)); }
            set { SetSetting(LAST_SUCCESSFUL_RUN, value); }
        }
    }
}
