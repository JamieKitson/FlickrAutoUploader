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

        private const string ALBUMS = "albums";
        private const string TOKEN = "token";
        private const string SECRET = "secret";
        private const string START_FROM = "startfrom";
        private const string ENABLED = "enabled";

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

        public static string OAuthAccessToken
        {
            get { return GetSetting(TOKEN, ""); }
            set { SetSetting(TOKEN, value); }
        }

        public static string OAuthAccessTokenSecret
        {
            get { return GetSetting(SECRET, ""); }
            set { SetSetting(SECRET, value); }
        }

        public static bool TokensSet()
        {
            return !string.IsNullOrEmpty(OAuthAccessToken + OAuthAccessTokenSecret);
        }

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

        public static DateTime StartFrom
        {
            get { return GetSetting(START_FROM, DateTime.Now); }
            set { SetSetting(START_FROM, value); }
        }

        public static bool Enabled
        {
            get { return GetSetting(ENABLED, false); }
            set { SetSetting(ENABLED, value); }
        }
    }
}
