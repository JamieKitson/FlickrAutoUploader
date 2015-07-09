using FlickrNet;
using System;
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
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            if (s.Contains(TOKEN) && s.Contains(SECRET))
            {
                f.OAuthAccessToken = (string)s[TOKEN];
                f.OAuthAccessTokenSecret = (string)s[SECRET];
            }
            return f;
        }


    }
}
