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
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.FileProperties;
using Microsoft.Phone.Info;
using System.Text.RegularExpressions;
using System.IO;

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
            LoadPhoneAlbums();
            PopulatePrivacy();
            FixPrivacyItmemsBackground();
            dpUploadFrom.Value = Settings.StartFrom;
            dpUploadFrom.ValueChanged += dpUploadFrom_ValueChanged;
            tbTags.Text = Settings.Tags;
            slLogLevel.Value = Settings.LogLevel;
            if (Debugger.IsAttached)
                DebugPanel.Visibility = Visibility.Visible;
            // AFAIK high res images are only available on a Lumia 1020
            UploadHiRes.Visibility = Settings.PhoneModelName == "Lumia 1020" ? Visibility.Visible : Visibility.Collapsed;
            SetToggleCheck();
            Photoset fa = Settings.FlickrAlbum;
            if (fa != null)
                ShowFlickrAlbums.Content = fa.Title;
            UploadHiRes.IsChecked = Settings.UploadHiRes;
            UploadVideos.IsChecked = Settings.UploadVideos;
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

        private async void SetToggleCheck()
        {
            ScheduledAction task = ScheduledActionService.Find(RIT_NAME);
            if (task != null)
            {
                if (task.IsScheduled)
                    tgEnabled.IsChecked = true;
                else
                {
                    Settings.DebugLog("Schedule was disabled.");
                    RemoveSchedule();
                }
            }
            tgEnabled.Checked += tgEnabled_Checked;
            if (NetworkInterface.GetIsNetworkAvailable() && Settings.TokensSet())
            {
                if (await MyFlickr.Test())
                    LoadFlickrAlbums();
                else
                    tgEnabled.IsChecked = false;
            }
        }

        private void Auth_Click(object sender, RoutedEventArgs e)
        {
            AuthAttempts = 0;
            StartAuthProcess();
        }

        private async void WebBrowser1_Navigating(object sender, NavigatingEventArgs e)
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
                if (ps.ContainsKey(OAUTH_VERIFIER))
                {
                    Flickr f = MyFlickr.getFlickr();
                    try
                    {
                        OAuthAccessToken tok = await f.OAuthAccessTokenAsync(requestToken.Token, requestToken.TokenSecret, ps[OAUTH_VERIFIER]);
                        if (tok != null)
                        {
                            Settings.OAuthAccessToken = tok.Token;
                            Settings.OAuthAccessTokenSecret = tok.TokenSecret;
                            TextBox1.Text = tok.UserId;
                            AddScheduledTask();
                            tgEnabled.IsChecked = true;
                            LoadFlickrAlbums();
                        }
                    }
                    catch (Exception ex)
                    {
                        tgEnabled.IsChecked = false;
                        MessageBox.Show("Error: " + ex.Message);
                    }
                }
                else
                    MessageBox.Show("Authorisation failed.");
            }
        }

        private async void LoadPhoneAlbums()
        {
            PhoneAlbumList.Children.Clear();
            IList<string> checkedPhoneAlbums = Settings.SelectedPhoneAlbums;
            IReadOnlyList<StorageFolder> allAlbums = await KnownFolders.PicturesLibrary.GetFoldersAsync();
            foreach (StorageFolder album in allAlbums)
            {
                CheckBox cb = new CheckBox();
                cb.Content = album.Name;
                cb.IsChecked = checkedPhoneAlbums.Contains(album.Name);
                cb.Checked += PhoneAlbum_Checked;
                cb.Unchecked += PhoneAlbum_Unchecked;
                cb.Margin = new Thickness(0, 0, 0, -10);
                PhoneAlbumList.Children.Add(cb);
            }
        }

        private void PhoneAlbum_Checked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedPhoneAlbums.Add((string)((CheckBox)sender).Content);
        }

        private void PhoneAlbum_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedPhoneAlbums.Remove((string)((CheckBox)sender).Content);
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            await MyFlickr.Upload();
        }

        private void AddScheduledTask()
        {
            RemoveSchedule();
            ResourceIntensiveTask resourceIntensiveTask = new ResourceIntensiveTask(RIT_NAME);
            resourceIntensiveTask.Description = "Flickr Auto Uploader";
            ScheduledActionService.Add(resourceIntensiveTask);
        }

        private async void tgEnabled_Checked(object sender, RoutedEventArgs e)
        {
            ScheduledAction task = ScheduledActionService.Find(RIT_NAME);
            if ((task != null) && (task.IsScheduled))
            {
                // Already enabled, nothing to do
            }
            else if (!NetworkInterface.GetIsNetworkAvailable())
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
                // Don't bother unsetting tokens, Flickr might be down
                tgEnabled.IsChecked = false;
                AuthAttempts = 0;
                StartAuthProcess();
            }
        }

        private async void StartAuthProcess()
        {
            try
            {
                Flickr f = MyFlickr.getFlickr();
                requestToken = await f.OAuthRequestTokenAsync(CALL_BACK);
                if (requestToken != null)
                {
                    string url = f.OAuthCalculateAuthorizationUrl(requestToken.Token, AuthLevel.Write);
                    WebBrowser1.Visibility = Visibility.Visible;
                    WebBrowser1.Navigate(new Uri(url));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
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
            RemoveSchedule();
        }

        private void dpUploadFrom_ValueChanged(object sender, DateTimeValueChangedEventArgs e)
        {
            Settings.StartFrom = (DateTime)dpUploadFrom.Value.Value.Date;
        }

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            base.OnBackKeyPress(e);
            if ((WebBrowser1.Visibility == Visibility.Visible) || (FlickrAlbumList.Visibility == Visibility.Visible))
            {
                e.Cancel = true;
                WebBrowser1.Visibility = Visibility.Collapsed;
                FlickrAlbumList.Visibility = Visibility.Collapsed;
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (Debugger.IsAttached)
                ScheduledActionService.LaunchForTest(RIT_NAME, TimeSpan.FromMilliseconds(5000));
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

        private async void LoadFlickrAlbums()
        {
            if (FlickrAlbumList.ItemsSource != null)
                return;
            if (Settings.FlickrAlbum == null)
                ShowFlickrAlbums.Content = "Loading Albums...";
            Flickr f = MyFlickr.getFlickr();
            try
            {
                PhotosetCollection ret = await f.PhotosetsGetListAsync();
                if (ret != null)
                {
                    FlickrAlbumList.SelectionChanged -= FlickrAlbumList_SelectionChanged;
                    ret.Add(new Photoset() { PhotosetId = string.Empty, Title = "-- None --" });
                    FlickrAlbumList.ItemsSource = AlphaKeyGroup<Photoset>.CreateGroups(ret, Thread.CurrentThread.CurrentUICulture, (Photoset p) => { return p.Title; }, true);
                    ShowFlickrAlbums.IsEnabled = true;
                    if (Settings.FlickrAlbum == null)
                        ShowFlickrAlbums.Content = "Choose Album";
                    FlickrAlbumList.SelectionChanged += FlickrAlbumList_SelectionChanged;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void FlickrAlbumList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FlickrAlbumList.Visibility = Visibility.Collapsed;
            Photoset sel = (Photoset)FlickrAlbumList.SelectedItem;
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
            FlickrAlbumList.Visibility = Visibility.Visible;
        }

        private void Go_Click(object sender, RoutedEventArgs e)
        {
            string s = Path.GetTempFileName();
            //MessageBox.Show(s);
            string[] files = Directory.GetFiles(Path.GetDirectoryName(s));
            MessageBox.Show(string.Join(Environment.NewLine, files));
            IEnumerable<long> sizes = files.ToList().Select(f => 
            {
                if (!File.Exists(f))
                    return 0;
                FileInfo fi = new FileInfo(f); 
                return fi.Length; 
            });
            MessageBox.Show(string.Join(", ", sizes));
        }

        private void UploadVideos_Checked(object sender, RoutedEventArgs e)
        {
            Settings.UploadVideos = UploadVideos.IsChecked.HasValue && UploadVideos.IsChecked.Value;
        }

        private void UploadHiRes_Checked(object sender, RoutedEventArgs e)
        {
            Settings.UploadHiRes = UploadHiRes.IsChecked.HasValue && UploadHiRes.IsChecked.Value;
        }

   }
}