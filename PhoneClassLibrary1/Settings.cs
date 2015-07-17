using Microsoft.Phone.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhoneClassLibrary1
{
    public class Settings
    {

        private static T GetSetting<T>(string name, T defVal)
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            if (s.Contains(name))
            {
                return (T)s[name];
            }
            else
            {
                SetSetting(name, defVal);
                return defVal;
            }
        }

        private static void SetSetting<T>(string name, T val)
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            s[name] = val;
            s.Save();
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
            get { return GetSetting(SECRET, ""); }
            set { SetSetting(SECRET, value); }
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
        public static IList<string> SelectedAlbums
        {
            get 
            { 
                var ol = GetSetting(ALBUMS, new ObservableCollection<string>(new string[] { "Camera Roll" }));
                ol.CollectionChanged += delegate(object sender, NotifyCollectionChangedEventArgs e) { SetSetting(ALBUMS, ol); };
                return ol;
            }
            set { SetSetting(ALBUMS, value); }
        }

        private const string START_FROM = "startfrom";
        public static DateTime StartFrom
        {
            get { return GetSetting(START_FROM, DateTime.Now); }
            set { SetSetting(START_FROM, value); }
        }

        private const string ENABLED = "enabled";
        public static bool Enabled
        {
            get { return GetSetting(ENABLED, false); }
            set { SetSetting(ENABLED, value); }
        }

        public static void Log(string msg)
        {
            ToastMessage(msg);
        }

        public static void DebugLog(string msg)
        {
            if (Settings.Debug)
                ToastMessage(msg);
        }

        public static void ErrorLog(string msg)
        {
            ToastMessage("Error: " + msg);
        }

        public static void ToastMessage(string msg)
        {
            if (Settings.Toast)
            {
                ShellToast toast = new ShellToast();
                toast.Title = "Flickr Auto Uploader";
                toast.Content = msg;
                toast.Show();
            }
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
            get { return GetSetting(TAGS, "wp8flickrautouploader"); }
            set { SetSetting(TAGS, value); }
        }

        private const string TOAST = "toast";
        public static bool Toast
        {
            get { return GetSetting(TOAST, true); }
            set { SetSetting(TOAST, value); }
        }

        private const string DEBUG = "debug";
        public static bool Debug
        {
            get { return GetSetting(DEBUG, true); }
            set { SetSetting(DEBUG, value); }
        }

        private const string TESTS_FAILED = "testsfailed";
        public static int TestsFailed
        {
            get { return GetSetting(TESTS_FAILED, 0); }
            set { SetSetting(TESTS_FAILED, value); }
        }

    }
}
