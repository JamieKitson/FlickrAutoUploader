using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using FlickrAutoUploader.Resources;
using FlickrNet;
using System.IO.IsolatedStorage;
using Microsoft.Xna.Framework.Media;
using Microsoft.Phone.Scheduler;
using PhoneClassLibrary1;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

namespace FlickrAutoUploader
{
    public partial class MainPage : PhoneApplicationPage
    {
        //Flickr f;
        string callBack = "http://kitten-x.com";
        ResourceIntensiveTask resourceIntensiveTask;
        string resourceIntensiveTaskName = "resourceIntensiveTaskName";
        private OAuthRequestToken requestToken = null;
        //private FlickrResult<string> UploadResult;
        // setting keys
/*        const string ALBUMS = "albums";
        const string TOKEN = "token";
        const string SECRET = "secret";
        const string UPLOAD_ALL = "uploadAll";


        private Flickr getFlickr()
        {
            Flickr f = new Flickr("xxx", "xxx");
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            if (s.Contains(TOKEN) && s.Contains(SECRET))
            {
                f.OAuthAccessToken = (string)s[TOKEN];
                f.OAuthAccessTokenSecret = (string)s[SECRET];
            }
            return f;
        }
        */
        public MainPage()
        {
            InitializeComponent();
            // f = new Flickr("74f994b1e804a06dff42e8031dd56527", "b8d8a02b3374b94d");
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            /*
            if (s.Contains(TOKEN) && s.Contains(SECRET))
            {
                f.OAuthAccessToken = (string)s[TOKEN];
                f.OAuthAccessTokenSecret = (string)s[SECRET];
                f.PhotosSearchAsync(new PhotoSearchOptions("me", "jamiekitson"), (res) =>
                    {
                        if (res.HasError)
                            TextBox1.Text = res.Error.Message;
                        else
                        {
                            TextBox1.Text = res.Result.Total.ToString();
                        }
                    });
                
                f.TestLoginAsync((res) =>
                {
                    if (res.HasError)
                        TextBox1.Text = res.Error.Message;
                    else
                    {
                        TextBox1.Text = res.Result.UserId;
                    }
                }); 
                
            }
            cbUploadAll.IsChecked = s.Contains(MyFlickr.UPLOAD_ALL) && (bool)s[MyFlickr.UPLOAD_ALL];
            cbUploadAll.Checked += RadioButton_Checked;
            cbUploadNew.Checked += RadioButton_Checked;
            */
            if (ScheduledActionService.Find(resourceIntensiveTaskName) != null)
            {
                ToggleSwitch1.IsChecked = true;
                //ScheduledActionService.Remove(resourceIntensiveTaskName);
            }
            /*
            resourceIntensiveTask = new ResourceIntensiveTask(resourceIntensiveTaskName);
            resourceIntensiveTask.Description = "This demonstrates a resource-intensive task.";
            ScheduledActionService.Add(resourceIntensiveTask); */
            //ScheduledActionService.LaunchForTest(resourceIntensiveTaskName, TimeSpan.FromSeconds(10));
            LoadFolders();
            //TextBlock1.Text = Settings.StartFrom.ToString();
            DatePicker1.Value = Settings.StartFrom;
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {

            Flickr f = MyFlickr.getFlickr();

            /*
            if (Settings.TokensSet())
            {
                f.TestLoginAsync((res) =>
                {
                    if (res.HasError)
                        TextBox1.Text = res.Error.Message;
                    else
                        TextBox1.Text = res.Result.UserId;
                });

                return;
            }
            */

            f.OAuthGetRequestTokenAsync(callBack, (tok) => 
            {
                if (tok.HasError)
                    TextBox1.Text = tok.Error.Message;
                else
                {
                    requestToken = tok.Result;
                    string url = f.OAuthCalculateAuthorizationUrl(requestToken.Token, AuthLevel.Write);
                    WebBrowser1.Navigate(new Uri(url));
                    WebBrowser1.Visibility = Visibility.Visible;
                }
            });
        }

        private void WebBrowser1_Navigating(object sender, NavigatingEventArgs e)
        {
            const string OAUTH_VERIFIER = "oauth_verifier";
            const string OAUTH_TOKEN = "oauth_token";
            string q = e.Uri.Query;
            //WebBrowser1.GetCookies()
            if (e.Uri.AbsoluteUri.StartsWith(callBack) || (q.Contains(OAUTH_VERIFIER) && q.Contains(OAUTH_TOKEN)))
            //if (!e.Uri.AbsoluteUri.StartsWith(callBack))
            //    return;
            {
                e.Cancel = true;
                WebBrowser1.Visibility = Visibility.Collapsed;
                Dictionary<string, string> ps = new Dictionary<string, string>();
                foreach (string s in q.Substring(1).Split('&')) // substr(1) - don't want the leading question mark
                {
                    string[] p = s.Split('=');
                    if (p.Count() == 2)
                        ps.Add(p[0], p[1]);
                }
                Flickr f = MyFlickr.getFlickr();
                f.OAuthGetAccessTokenAsync(requestToken, ps[OAUTH_VERIFIER], (tok) =>
                {
                    if (tok.HasError)
                        TextBox1.Text = tok.Error.Message;
                    else
                    {
                        Settings.OAuthAccessToken = tok.Result.Token;
                        Settings.OAuthAccessTokenSecret = tok.Result.TokenSecret;
                        /*
                        IsolatedStorageSettings settings = IsolatedStorageSettings.ApplicationSettings;
                        settings[MyFlickr.TOKEN] = tok.Result.Token;
                        settings[MyFlickr.SECRET] = tok.Result.TokenSecret;
                        settings.Save(); */
                        TextBox1.Text = tok.Result.UserId;                        
                    }
                }); 
            }
        }

        private List<string> getCheckedAlbums()
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

        private void LoadFolders()
        {
            //const int CB_HEIGHT = 50;
            List<string> checkedAlbums = getCheckedAlbums();
            foreach (MediaSource source in MediaSource.GetAvailableMediaSources())
            {
                if (source.MediaSourceType == MediaSourceType.LocalDevice)
                {
                    MediaLibrary medLib = new MediaLibrary(source);
                    PictureAlbumCollection allAlbums = medLib.RootPictureAlbum.Albums;
                    double t = 0;
                    foreach (PictureAlbum album in allAlbums)
                    {
                        CheckBox cb = new CheckBox();
                        cb.Content = album.Name;
                        cb.IsChecked = checkedAlbums.Contains(album.Name);
                        cb.Checked += CheckBox_Checked;
                        cb.Unchecked += CheckBox_Unchecked;
                        Grid1.Children.Add(cb);
                        cb.Margin = new Thickness(0, t, 0, 0); // Grid1.Height - t - CB_HEIGHT);
                        cb.Height = 72;
                        t += 60;
                    }
                }
            } 

        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            string c = (string)((CheckBox)sender).Content;
            if (!s.Contains(MyFlickr.ALBUMS))
            {
                s.Add(MyFlickr.ALBUMS, new List<string>());
            }
            ((List<string>)s[MyFlickr.ALBUMS]).Add(c);
            s.Save();
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            IsolatedStorageSettings s = IsolatedStorageSettings.ApplicationSettings;
            if (s.Contains(MyFlickr.ALBUMS))
            {
                string c = (string)((CheckBox)sender).Content;
                ((List<string>)s[MyFlickr.ALBUMS]).Remove(c);
                s.Save();
            }

        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            await MyFlickr.Upload();
        }

        private void ToggleSwitch_Checked(object sender, RoutedEventArgs e)
        {
            if (ScheduledActionService.Find(resourceIntensiveTaskName) != null)
            {
                ScheduledActionService.Remove(resourceIntensiveTaskName);
            }
            resourceIntensiveTask = new ResourceIntensiveTask(resourceIntensiveTaskName);
            resourceIntensiveTask.Description = "This demonstrates a resource-intensive task.";
            ScheduledActionService.Add(resourceIntensiveTask);
        }
    }
}