﻿using System;
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

namespace FlickrAutoUploader
{
    public partial class MainPage : PhoneApplicationPage
    {
        const string CALL_BACK = "http://kitten-x.com";
        const string RIT_NAME = "FlickrAutoUploader";
        OAuthRequestToken requestToken;

        public MainPage()
        {
            InitializeComponent();
            LoadFolders();
            PopulatePrivacy();
            FixPrivacyItmemsBackground();
            dpUploadFrom.Value = Settings.StartFrom;
            tbTags.Text = Settings.Tags;
            slLogLevel.Value = Settings.LogLevel;
        }

        private void PhoneApplicationPage_Loaded(object sender, RoutedEventArgs e)
        {
            SetToggleCheck();
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
            {
                tgEnabled.IsChecked = true;
                if (NetworkInterface.GetIsNetworkAvailable())
                {
                    MyFlickr.getFlickr().TestLoginAsync((ret) =>
                        {
                            if (ret.HasError)
                                tgEnabled.IsChecked = false;
                        });
                }
            }
            tgEnabled.Checked += tgEnabled_Checked;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            StartAuthProcess();
        }

        private void WebBrowser1_Navigating(object sender, NavigatingEventArgs e)
        {
            TextBox1.Text = e.Uri.AbsoluteUri;
            const string OAUTH_VERIFIER = "oauth_verifier";
            const string OAUTH_TOKEN = "oauth_token";
            string q = e.Uri.Query;
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
                        }
                    });
                }
                else
                    MessageBox.Show("Authorisation failed.");
            }
        }

        private void LoadFolders()
        {
            Grid1.Children.Clear();
            //const int CB_HEIGHT = 50;
            double t = 0;
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
                        Grid1.Children.Add(cb);
                        cb.Margin = new Thickness(0, t, 0, 0); // Grid1.Height - t - CB_HEIGHT);
                        //cb.Height = 72;
                        t += 60;
                    }
                }
            }
            Grid1.Height = t + 60;
        }

        private void Album_Checked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedAlbums.Add((string)((CheckBox)sender).Content);
        }

        private void Album_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.SelectedAlbums.Remove((string)((CheckBox)sender).Content);
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
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
                    //WebBrowser1.Margin = new Thickness(0, 0, 0, 0);
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
            if (WebBrowser1.Visibility == Visibility.Visible)
            {
                e.Cancel = true;
                WebBrowser1.Visibility = Visibility.Collapsed;
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            /*
            ShellToast toast = new ShellToast();
            toast.Title = "Flickr Auto Uploader";
            toast.Content = "one";
            toast.Show();

            Settings.Log("two");

            MessageBox.Show("Three");
            */
            //#if DEBUG_AGENT
            ScheduledActionService.LaunchForTest(RIT_NAME, TimeSpan.FromMilliseconds(2000));
            //#endif
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

    }
}