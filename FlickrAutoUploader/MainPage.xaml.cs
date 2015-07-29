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
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Net.NetworkInformation;
using Microsoft.Phone.Tasks;
using System.Diagnostics;
using Microsoft.Phone.Globalization;
using System.Globalization;

namespace FlickrAutoUploader
{
    public partial class MainPage : PhoneApplicationPage
    {
        const string CALL_BACK = "http://kitten-x.com";
        const string RIT_NAME = "FlickrAutoUploader";
        OAuthRequestToken requestToken;
        int AuthAttempts;

        public MainPage()
        {
            InitializeComponent();
            LoadFolders();
            PopulatePrivacy();
            FixPrivacyItmemsBackground();
            dpUploadFrom.Value = Settings.StartFrom;
            tbTags.Text = Settings.Tags;
            slLogLevel.Value = Settings.LogLevel;
            if (Debugger.IsAttached)
                DebugPanel.Visibility = Visibility.Visible;
        }

        private void PhoneApplicationPage_Loaded(object sender, RoutedEventArgs e)
        {
            SetToggleCheck();
            Photoset fa = Settings.FlickrAlbum;
            if (fa != null)
                ShowFlickrAlbums.Content = fa.Title;
        }

        private void PopulatePrivacy()
        {
            PrivacyPicker.Items.Clear();
            Enum.GetValues(typeof(Settings.ePrivacy)).Cast<Settings.ePrivacy>().ToList().ForEach(v =>
            {
                ListPickerItem lpi = new ListPickerItem();
                lpi.Content = v.ToString().Replace("FriendsFamily", "Friends & Family");
                PrivacyPicker.Items.Add(lpi);
            });
            PrivacyPicker.SelectedIndex = (int)Settings.Privacy;
            PrivacyPicker.SelectionChanged += PrivacyPicker_SelectionChanged;
        }

        private void FixPrivacyItmemsBackground()
        {
            try
            {
                // Fix transparent ListPicker background in light theme
                SolidColorBrush bg = (SolidColorBrush)Application.Current.Resources["PhoneBackgroundBrush"];
                if (bg.Color == Colors.White)
                    PrivacyPicker.Items.ToList().ForEach(i => ((ListPickerItem)i).Background = bg);
            }
            catch { } // Don't really care if it fails
        }

        private void SetToggleCheck()
        {
            if (Settings.Enabled && (ScheduledActionService.Find(RIT_NAME) != null))
                tgEnabled.IsChecked = true;
            if (NetworkInterface.GetIsNetworkAvailable() && Settings.TokensSet())
            {
                MyFlickr.getFlickr().TestLoginAsync((ret) =>
                    {
                        if (ret.HasError)
                            tgEnabled.IsChecked = false;
                        else
                            LoadDestAlbums();
                    });
            }
            tgEnabled.Checked += tgEnabled_Checked;
        }

        private void Auth_Click(object sender, RoutedEventArgs e)
        {
            AuthAttempts = 0;
            StartAuthProcess();
        }

        private void WebBrowser1_Navigating(object sender, NavigatingEventArgs e)
        {
            TextBox1.Text = e.Uri.AbsoluteUri;
            const string OAUTH_VERIFIER = "oauth_verifier";
            const string OAUTH_TOKEN = "oauth_token";
            string q = e.Uri.Query;
            // Sometimes we end up here instead of the authorisation page
            if ((e.Uri.AbsoluteUri == "https://m.flickr.com/#/home") && (AuthAttempts++ < 3))
                StartAuthProcess();
            if (e.Uri.AbsoluteUri.StartsWith(CALL_BACK) || (q.Contains(OAUTH_VERIFIER) && q.Contains(OAUTH_TOKEN)))
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
                if (ps.ContainsKey(OAUTH_VERIFIER))
                {
                    f.OAuthGetAccessTokenAsync(requestToken, ps[OAUTH_VERIFIER], (tok) =>
                    {
                        if (tok.HasError)
                        {
                            tgEnabled.IsChecked = false;
                            MessageBox.Show(tok.Error.Message);
                        }
                        else
                        {
                            Settings.OAuthAccessToken = tok.Result.Token;
                            Settings.OAuthAccessTokenSecret = tok.Result.TokenSecret;
                            TextBox1.Text = tok.Result.UserId;
                            tgEnabled.IsChecked = true;
                            LoadDestAlbums();
                        }
                    });
                }
                else
                    MessageBox.Show("Authorisation failed.");
            }
        }

        private void LoadFolders()
        {
            Folders.Children.Clear();
            IList<string> checkedAlbums = Settings.SelectedAlbums;
            foreach (MediaSource source in MediaSource.GetAvailableMediaSources())
            {
                if (source.MediaSourceType == MediaSourceType.LocalDevice)
                {
                    MediaLibrary medLib = new MediaLibrary(source);
                    PictureAlbumCollection allAlbums = medLib.RootPictureAlbum.Albums;
                    foreach (PictureAlbum album in allAlbums)
                    {
                        CheckBox cb = new CheckBox();
                        cb.Content = album.Name;
                        cb.IsChecked = checkedAlbums.Contains(album.Name);
                        cb.Checked += Album_Checked;
                        cb.Unchecked += Album_Unchecked;
                        cb.Margin = new Thickness(0, 0, 0, -10);
                        Folders.Children.Add(cb);
                    }
                }
            }
        }

        private void Album_Checked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedAlbums.Add((string)((CheckBox)sender).Content);
        }

        private void Album_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedAlbums.Remove((string)((CheckBox)sender).Content);
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            await MyFlickr.Upload();
        }

        private void AddScheduledTask()
        {
            RemoveSchedule();
            ResourceIntensiveTask resourceIntensiveTask = new ResourceIntensiveTask(RIT_NAME);
            resourceIntensiveTask.Description = "This demonstrates a resource-intensive task.";
            ScheduledActionService.Add(resourceIntensiveTask);
            Settings.Enabled = true;
        }

        private async void tgEnabled_Checked(object sender, RoutedEventArgs e)
        {
            //MessageBox.Show("checked");
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                MessageBox.Show("Please re-try enabling when you have an internet connection available.");
                tgEnabled.IsChecked = false;
            }
            else if (await MyFlickr.Test())
            {
                AddScheduledTask();
            }
            else
            {
                Settings.UnsetTokens();
                tgEnabled.IsChecked = false;
                AuthAttempts = 0;
                StartAuthProcess();
            }
        }

        private void StartAuthProcess()
        {
            Flickr f = MyFlickr.getFlickr();
            f.OAuthGetRequestTokenAsync(CALL_BACK, (tok) =>
            {
                if (tok.HasError)
                    TextBox1.Text = tok.Error.Message;
                else
                {
                    requestToken = tok.Result;
                    string url = f.OAuthCalculateAuthorizationUrl(requestToken.Token, AuthLevel.Write);
                    WebBrowser1.Visibility = Visibility.Visible;
                    WebBrowser1.Navigate(new Uri(url));
                }
            });
        }

        private void RemoveSchedule()
        {
            if (ScheduledActionService.Find(RIT_NAME) != null)
            {
                ScheduledActionService.Remove(RIT_NAME);
            }
        }

        private void tgEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            //MessageBox.Show("unchecked");
            RemoveSchedule();
            Settings.Enabled = false;
        }

        private void dpUploadFrom_ValueChanged(object sender, DateTimeValueChangedEventArgs e)
        {
            Settings.StartFrom = (DateTime)dpUploadFrom.Value;
        }

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            base.OnBackKeyPress(e);
            if ((WebBrowser1.Visibility == Visibility.Visible) || (LongListSelector1.Visibility == Visibility.Visible))
            {
                e.Cancel = true;
                WebBrowser1.Visibility = Visibility.Collapsed;
                LongListSelector1.Visibility = Visibility.Collapsed;
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (Debugger.IsAttached)
                ScheduledActionService.LaunchForTest(RIT_NAME, TimeSpan.FromMilliseconds(2000));
        }

        private void PrivacyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Settings.Privacy = (Settings.ePrivacy)PrivacyPicker.SelectedIndex;
        }

        private void tbTags_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.Tags = tbTags.Text;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            slLogLevel.Value = Math.Round(slLogLevel.Value);
            Settings.LogLevel = slLogLevel.Value;
        }

        private void EmailLog_Click(object sender, RoutedEventArgs e)
        {
            EmailComposeTask emailComposeTask = new EmailComposeTask();
            emailComposeTask.Subject = "Flickr Auto Uploader Log";
            emailComposeTask.Body = Settings.GetLog();
            emailComposeTask.To = "jamie@kitten-x.com";
            emailComposeTask.Show();
        }

        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            WebBrowser1.Visibility = Visibility.Visible;
            WebBrowser1.NavigateToString("<pre>" + Settings.GetLog() + "</pre>");
        }

        private void LoadDestAlbums()
        {
            if (LongListSelector1.ItemsSource != null)
                return;
            if (Settings.FlickrAlbum == null)
                ShowFlickrAlbums.Content = "Loading Albums...";
            Flickr f = MyFlickr.getFlickr();
            f.PhotosetsGetListAsync((ret) => 
            {
                LongListSelector1.SelectionChanged -= LongListSelector1_SelectionChanged;
                ret.Result.Insert(0, new Photoset() { PhotosetId = string.Empty, Title = "-- None --" });
                LongListSelector1.ItemsSource = AlphaKeyGroup<Photoset>.CreateGroups(ret.Result, Thread.CurrentThread.CurrentUICulture, (Photoset p) => { return p.Title; }, true);
                ShowFlickrAlbums.IsEnabled = true;
                if (Settings.FlickrAlbum == null)
                    ShowFlickrAlbums.Content = "Choose Album";
                LongListSelector1.SelectionChanged += LongListSelector1_SelectionChanged;
            });
        }

        private void LongListSelector1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LongListSelector1.Visibility = Visibility.Collapsed;
            Photoset sel = (Photoset)LongListSelector1.SelectedItem;
            ShowFlickrAlbums.Content = sel.Title;
            Settings.FlickrAlbum = string.IsNullOrEmpty(sel.PhotosetId) ? null : sel;
        }

        public class AlphaKeyGroup<T> : List<T>
        {
            /// <summary>
            /// The delegate that is used to get the key information.
            /// </summary>
            /// <param name="item">An object of type T</param>
            /// <returns>The key value to use for this object</returns>
            public delegate string GetKeyDelegate(T item);

            /// <summary>
            /// The Key of this group.
            /// </summary>
            public string Key { get; private set; }

            /// <summary>
            /// Public constructor.
            /// </summary>
            /// <param name="key">The key for this group.</param>
            public AlphaKeyGroup(string key)
            {
                Key = key;
            }

            /// <summary>
            /// Create a list of AlphaGroup<T> with keys set by a SortedLocaleGrouping.
            /// </summary>
            /// <param name="slg">The </param>
            /// <returns>Theitems source for a LongListSelector</returns>
            private static List<AlphaKeyGroup<T>> CreateGroups(SortedLocaleGrouping slg)
            {
                List<AlphaKeyGroup<T>> list = new List<AlphaKeyGroup<T>>();

                foreach (string key in slg.GroupDisplayNames)
                {
                    list.Add(new AlphaKeyGroup<T>(key));
                }

                return list;
            }

            /// <summary>
            /// Create a list of AlphaGroup<T> with keys set by a SortedLocaleGrouping.
            /// </summary>
            /// <param name="items">The items to place in the groups.</param>
            /// <param name="ci">The CultureInfo to group and sort by.</param>
            /// <param name="getKey">A delegate to get the key from an item.</param>
            /// <param name="sort">Will sort the data if true.</param>
            /// <returns>An items source for a LongListSelector</returns>
            public static List<AlphaKeyGroup<T>> CreateGroups(IEnumerable<T> items, CultureInfo ci, GetKeyDelegate getKey, bool sort)
            {
                SortedLocaleGrouping slg = new SortedLocaleGrouping(ci);
                List<AlphaKeyGroup<T>> list = CreateGroups(slg);

                foreach (T item in items)
                {
                    int index = 0;
                    if (slg.SupportsPhonetics)
                    {
                        //check if your database has yomi string for item
                        //if it does not, then do you want to generate Yomi or ask the user for this item.
                        //index = slg.GetGroupIndex(getKey(Yomiof(item)));
                    }
                    else
                    {
                        index = slg.GetGroupIndex(getKey(item));
                    }
                    if (index >= 0 && index < list.Count)
                    {
                        list[index].Add(item);
                    }
                }

                if (sort)
                {
                    foreach (AlphaKeyGroup<T> group in list)
                    {
                        group.Sort((c0, c1) => { return ci.CompareInfo.Compare(getKey(c0), getKey(c1)); });
                    }
                }

                return list;
            }

        }

        private void ShowFlickrAlbums_Click(object sender, RoutedEventArgs e)
        {
            LongListSelector1.Visibility = Visibility.Visible;
        }

   }
}