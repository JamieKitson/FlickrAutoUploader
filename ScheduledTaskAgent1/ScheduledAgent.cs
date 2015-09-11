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
            if ((DateTime.Now - Settings.LastSuccessfulRun) < new TimeSpan(1, 0, 0))
            {
                Settings.DebugLog("Already run in the last hour (at " + Settings.LastSuccessfulRun + "), not running.");
            }
            else if (await MyFlickr.Test())
            {
                Settings.TestsFailed = 0;
                Settings.DebugLog("Test succeeded, starting upload.");
                try
                {
                    await MyFlickr.Upload();
                    Settings.DebugLog("Finished!");
                    Settings.LastSuccessfulRun = DateTime.Now;
                }
                catch (Exception ex)
                {
                    string msg = "Error uploading: " + ex.Message;
                    if (Settings.UploadsFailed++ > 5)
                    {
                        Settings.ErrorLog(msg);
                        Settings.UploadsFailed = 0;
                        Abort();
                    }
                    else
                        Settings.DebugLog(msg);
                }
            }
            else
            {
                if (Settings.TestsFailed++ > 5)
                {
                    Settings.TestsFailed = 0;
                    Settings.ErrorLog("Flickr login failed, please re-enable app to re-authenticate with Flickr.");
                    Abort();
                }
                else
                {
                    string err = "Returned null";
                    if (MyFlickr.lastError != null)
                    {
                        err = MyFlickr.lastError.Message;
                    }
                    Settings.DebugLog("Not uploading, test failed " + Settings.TestsFailed + " times. " + err);
                }
            }
            NotifyComplete();
        }



    }
}