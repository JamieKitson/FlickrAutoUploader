using System;
using System.Collections.Generic;
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

        private static T GetSetting<T>(string name, T defVal)
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            if (s.Contains(name))
                return (T)s[name];
            else
                return defVal;
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

        public static List<string> SelectedAlbums
        {
            get { 
                List<string> sa = GetSetting(ALBUMS, new List<string>( new string[] {"Camera Roll"})); // Default to selecting camera roll
                
                return sa;
            }
            set { SetSetting(ALBUMS, value); } 
        }

        public static DateTime StartFrom
        {
            get { return GetSetting(START_FROM, new DateTime(2015, 6, 22)); } // CHANGEME to DateTime.Now
            set { SetSetting(START_FROM, value); }
        }
    }
}
