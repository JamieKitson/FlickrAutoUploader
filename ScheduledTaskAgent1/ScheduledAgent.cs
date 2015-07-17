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

            // string toastMessage = "";

            // If your application uses both PeriodicTask and ResourceIntensiveTask
            // you can branch your application code here. Otherwise, you don't need to.
            if (task is PeriodicTask)
            {
                // Execute periodic task actions here.
                // toastMessage = "Periodic task running.";
            }
            else
            {
                Settings.DebugLog("Schedule started, Enabled: " + Settings.Enabled);
                // Execute resource-intensive task actions here.
                // toastMessage = "Resource-intensive task running.";
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
                            Settings.Log("Flickr login failed, please re-enable app to re-authenticate with Flickr.");
                        }
                        else
                            Settings.DebugLog("Not uploading, test failed.");
                    }
                }
            }


            // Launch a toast to show that the agent is running.
            // The toast will not be shown if the foreground application is running.
            /*

            ShellToast toast = new ShellToast();
            toast.Title = "Background Agent Sample";
            toast.Content = toastMessage;
            toast.Show();
            */
            // If debugging is enabled, launch the agent again in one minute.
//#if DEBUG_AGENT
  //ScheduledActionService.LaunchForTest(task.Name, TimeSpan.FromSeconds(10));
//#endif
            NotifyComplete();
        }



    }
}