using System.Diagnostics;
using System.Windows;
using Microsoft.Phone.Scheduler;
using Microsoft.Phone.Shell;
using System;
//using FlickrNet;
using System.IO.IsolatedStorage;
using PhoneClassLibrary1;

namespace ScheduledTaskAgent1
{
    public class ScheduledAgent : ScheduledTaskAgent
    {
        /// <remarks>
        /// ScheduledAgent constructor, initializes the UnhandledException handler
        /// </remarks>
        static ScheduledAgent()
        {
            // Subscribe to the managed exception handler
            Deployment.Current.Dispatcher.BeginInvoke(delegate
            {
                Application.Current.UnhandledException += UnhandledException;
            });
        }

        /// Code to execute on Unhandled Exceptions
        private static void UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                // An unhandled exception has occurred; break into the debugger
                Debugger.Break();
            }
        }

        /// <summary>
        /// Agent that runs a scheduled task
        /// </summary>
        /// <param name="task">
        /// The invoked task
        /// </param>
        /// <remarks>
        /// This method is called when a periodic or resource intensive task is invoked
        /// </remarks>
        protected override async void OnInvoke(ScheduledTask task)
        {
            Settings.DebugLog("Schedule started, Enabled: " + Settings.Enabled);

            if (Settings.Enabled)
            {
                if (await MyFlickr.Test())
                {
                    Settings.TestsFailed = 0;
                    Settings.DebugLog("Test succeeded, starting upload.");
                    await MyFlickr.Upload();
                }
                else
                {
                    if (Settings.TestsFailed++ > 5)
                    {
                        Settings.Enabled = false;
                        Settings.TestsFailed = 0;
                        Settings.ErrorLog("Flickr login failed, please re-enable app to re-authenticate with Flickr.");
                    }
                    else
                    {
                        string err = "Returned null";
                        if (MyFlickr.testResult != null)
                        {
                            if (!string.IsNullOrEmpty(MyFlickr.testResult.ErrorMessage))
                                err = MyFlickr.testResult.ErrorMessage;
                            else if (MyFlickr.testResult.Error != null)
                                err = MyFlickr.testResult.Error.Message;
                        }
                        Settings.DebugLog("Not uploading, test failed " + Settings.TestsFailed + " times. " + err);
                    }
                }
            }
            NotifyComplete();
        }



    }
}